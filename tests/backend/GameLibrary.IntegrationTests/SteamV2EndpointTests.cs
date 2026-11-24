using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Data;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

public sealed class SteamV2TestReadiness : ISteamLinkReadiness { public bool Ready => true; }

public sealed class SteamV2Provider : ISteamClient
{
    private readonly ConcurrentDictionary<string, byte> _spent = new();
    private int _calls;
    public int Calls => _calls;
    public bool Valid = true;
    public Func<CancellationToken, Task>? BeforeVerify;
    public async Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        if (BeforeVerify is not null) await BeforeVerify(ct);
        return Valid && _spent.TryAdd(parameters["openid.response_nonce"]!, 0);
    }
    public Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SteamOwnedGame>>([]);
    public void Spend(string nonce) => _spent.TryAdd(nonce, 0);
}

public sealed class SteamV2Factory(string connection, string environment = "Test", bool ready = true,
    string api = "https://api.example.test", string frontend = "https://app.example.test", params IInterceptor[] interceptors)
    : WebApplicationFactory<Program>
{
    public SteamV2Provider Provider { get; } = new();
    internal SteamPrivacyCapture Capture { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", connection);
        builder.UseSetting("E2E:Auth:Enabled", "false");
        // This protocol fixture never exercises cover cleanup or real Storage.
        builder.UseSetting("PrivateQa:DisableCoverCleanup", "true");
        builder.UseSetting("CoverUploads:Enabled", "false");
        builder.UseSetting("CoverUploads:StorageBaseUrl", "");
        builder.UseSetting("CoverUploads:StorageCredential", "");
        builder.UseSetting("SteamIntegration:Enabled", "true");
        builder.UseSetting("SteamIntegration:SecureLinkingEnabled", "true");
        builder.UseSetting("SteamIntegration:PublicApiBaseUrl", api);
        builder.UseSetting("SteamIntegration:FrontendBaseUrl", frontend);
        builder.UseSetting("SteamIntegration:AllowLocalLinkingHttp", "true");
        builder.UseSetting("AllowedOrigins:0", frontend);
        builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Capture));
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = null!; options.MetadataAddress = null!;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = TestTokens.Issuer, ValidateAudience = true,
                    ValidAudience = TestTokens.Audience, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(TestTokens.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromMinutes(1),
                };
            });
            services.RemoveAll<ISteamClient>(); services.AddSingleton<ISteamClient>(Provider);
            if (ready) { services.RemoveAll<ISteamLinkReadiness>(); services.AddSingleton<ISteamLinkReadiness, SteamV2TestReadiness>(); }
            if (interceptors.Length != 0) services.AddDbContext<GameLibraryDbContext>(o => o.AddInterceptors(interceptors));
        });
    }
    public HttpClient Browser(string? sub = null, string? origin = null)
    {
        var client = CreateClient(new() { BaseAddress = new Uri(api), AllowAutoRedirect = false, HandleCookies = false });
        if (sub is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub));
        client.DefaultRequestHeaders.Add("Origin", origin ?? new Uri(frontend).GetLeftPart(UriPartial.Authority));
        return client;
    }
}

[Collection("Steam secure Core")]
public sealed class SteamV2EndpointTests(SteamSecureFixture fixture) : IClassFixture<SteamSecureFixture>
{
    private const string Root = "/steam/link/v2/";
    private static string User() => "v2-" + Guid.NewGuid().ToString("N");
    private sealed record Flow(Guid Id, string State, string Proof, string Cookie, DateTimeOffset ExpiresAt)
    { public override string ToString() => nameof(Flow); }

    private static async Task<Flow> Create(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(Root + "requests", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Private(response);
        Assert.Null(response.Headers.Location);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("protocolVersion").GetInt32());
        var authorization = new Uri(result.GetProperty("authorizationUrl").GetString()!);
        Assert.Equal("https://steamcommunity.com/openid/login", authorization.GetLeftPart(UriPartial.Path));
        var parameters = QueryHelpers.ParseQuery(authorization.Query);
        var state = QueryHelpers.ParseQuery(new Uri(parameters["openid.return_to"].ToString()).Query)["state"].ToString();
        var set = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var cookie = set.Split(';')[0]; var proof = cookie[(cookie.IndexOf('=') + 1)..];
        Assert.True(SteamSecureLinkRules.Decode(proof) is not null && proof != state, "Independent proof must be canonical.");
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new(result.GetProperty("transactionId").GetGuid(), state, proof, cookie, result.GetProperty("expiresAt").GetDateTimeOffset());
    }
    private static Dictionary<string, string?> Assertion(Flow flow, string id = "76561198000000031", string api = "https://api.example.test") => new()
    {
        ["state"] = flow.State, ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace, ["openid.mode"] = "id_res",
        ["openid.op_endpoint"] = SteamLinkConfiguration.Provider, ["openid.identity"] = SteamIntegrationRules.ClaimedIdPrefix + id,
        ["openid.claimed_id"] = SteamIntegrationRules.ClaimedIdPrefix + id,
        ["openid.return_to"] = api + Root + "callback?state=" + flow.State,
        ["openid.realm"] = api + "/", ["openid.response_nonce"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N"),
        ["openid.assoc_handle"] = "synthetic-association", ["openid.signed"] = SteamOpenIdProtocol.RequiredSigned,
        ["openid.sig"] = "synthetic-v2-signature",
    };
    private static string Callback(Dictionary<string, string?> values) => QueryHelpers.AddQueryString(Root + "callback", values);
    private static void Private(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private == true && response.Headers.CacheControl.NoStore);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
    private static async Task Code(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode); Private(response);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
    private static async Task ReviewState(HttpClient client, Flow flow, string state)
    {
        using var response = await client.PostAsJsonAsync(Root + "review", new { transactionId = flow.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Private(response);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(state, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Full_protocol_stages_only_then_explicitly_confirms_current_receipt_without_changing_cookie()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client);
        await ReviewState(client, flow, "Pending");
        var assertion = Assertion(flow, Identity());
        using var callback = await client.GetAsync(Callback(assertion));
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Private(callback);
        Assert.Equal("https://app.example.test/manage/steam?steamLink=review", callback.Headers.Location!.ToString());
        Assert.False(callback.Headers.Contains("Set-Cookie"));
        await using var db = fixture.Db();
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        Assert.Null(row.ConsumedAt); Assert.Null(row.LinkedAccountId);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
        await ReviewState(client, flow, "AwaitingConfirmation");
        using var wrong = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = "1" });
        await Code(wrong, 409, "REVIEW_REQUIRED");
        foreach (var attempt in Enumerable.Range(0, 2))
        {
            using var confirm = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = row.VerifiedSteamId64 });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode); Private(confirm); Assert.False(confirm.Headers.Contains("Set-Cookie"));
        }
        await ReviewState(client, flow, "Linked");
        using var replay = await client.GetAsync(Callback(assertion));
        Assert.Equal("https://app.example.test/manage/steam?steamLink=failure", replay.Headers.Location!.ToString());
        Assert.Equal(1, host.Provider.Calls);
        using var cancel = await client.PostAsJsonAsync(Root + "cancel", new { transactionId = flow.Id });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode); Assert.False(cancel.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, await db.SteamAccounts.CountAsync(a => a.LibraryId == row.LibraryId));
        using var unlink = await client.DeleteAsync("/steam/link"); Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        using var stale = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = row.VerifiedSteamId64 });
        await Code(stale, 409, "LINK_NOT_CURRENT");
    }

    [Theory]
    [InlineData("state")]
    [InlineData("hash")]
    [InlineData("double-hash")]
    [InlineData("verifier")]
    [InlineData("random")]
    [InlineData("padding")]
    [InlineData("percent")]
    [InlineData("duplicate")]
    [InlineData("duplicate-header")]
    [InlineData("legacy")]
    [InlineData("missing")]
    public async Task Leaked_state_and_forged_or_ambiguous_cookie_cannot_verify_stage_or_confirm(string kind)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var assertion = Assertion(flow);
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        var value = kind switch
        {
            "state" => flow.State, "hash" => row.StateHash, "double-hash" => SteamIntegrationRules.HashState(row.StateHash),
            "verifier" => SteamSecureLinkRules.Encode(row.BrowserBindingHash!), "padding" => flow.Proof + "=",
            "percent" => "%" + ((int)flow.Proof[0]).ToString("X2") + flow.Proof[1..],
            _ => SteamSecureLinkRules.NewSecret(),
        };
        client.DefaultRequestHeaders.Remove("Cookie");
        if (kind is "duplicate" or "duplicate-header")
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", kind == "duplicate"
                ? [flow.Cookie + "; " + flow.Cookie] : new[] { flow.Cookie, flow.Cookie });
        else if (kind != "missing") client.DefaultRequestHeaders.Add("Cookie", kind == "legacy"
            ? "__Host-GameLibrary.SteamLink=" + flow.Proof : "__Host-GameLibrary.SteamLink.v2=" + value);
        using var callback = await client.GetAsync(Callback(assertion));
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Assert.Equal(0, host.Provider.Calls);
        Assert.False(callback.Headers.Contains("Set-Cookie"));
        foreach (var operation in new[] { "review", "confirm", "cancel" })
        {
            using var response = await client.PostAsJsonAsync(Root + operation, operation == "confirm"
                ? (object)new { transactionId = flow.Id, expectedSteamId64 = "76561198000000031" } : new { transactionId = flow.Id });
            await Code(response, 404, "LINK_REQUEST_NOT_FOUND");
        }
        Assert.Null((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id)).AssertionAcceptedAt);
    }

    [Fact]
    public async Task Cookie_alone_wrong_JWT_cross_transaction_and_stolen_JWT_existing_flow_fail_but_new_flow_gets_fresh_proof()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); var sub = User();
        using var owner = host.Browser(sub); var flow = await Create(owner);
        using var other = host.Browser(User()); other.DefaultRequestHeaders.Add("Cookie", flow.Cookie);
        using var wrong = await other.PostAsJsonAsync(Root + "review", new { transactionId = flow.Id });
        await Code(wrong, 404, "LINK_REQUEST_NOT_FOUND");
        using var anonymous = host.Browser(); anonymous.DefaultRequestHeaders.Add("Cookie", flow.Cookie);
        using var noJwt = await anonymous.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = "1" });
        Assert.Equal(HttpStatusCode.Unauthorized, noJwt.StatusCode);
        using var stolen = host.Browser(sub);
        using var existing = await stolen.PostAsJsonAsync(Root + "review", new { transactionId = flow.Id });
        await Code(existing, 404, "LINK_REQUEST_NOT_FOUND");
        var newFlow = await Create(stolen); Assert.True(newFlow.Proof != flow.Proof);
        using var old = await owner.GetAsync(Callback(Assertion(flow))); Assert.Equal(HttpStatusCode.SeeOther, old.StatusCode);
        Assert.Equal(0, host.Provider.Calls);
        // Legitimate bearer authority can initiate its own flow: B is not MFA.
        await ReviewState(stolen, newFlow, "Pending");
        stolen.DefaultRequestHeaders.Remove("Cookie"); stolen.DefaultRequestHeaders.Add("Cookie", flow.Cookie);
        using var transferred = await stolen.GetAsync(Callback(Assertion(newFlow))); Assert.Equal(0, host.Provider.Calls);
    }

    [Theory]
    [InlineData("openid.ns", "wrong")]
    [InlineData("openid.mode", "cancel")]
    [InlineData("openid.mode", "setup_needed")]
    [InlineData("openid.mode", "error")]
    [InlineData("openid.op_endpoint", "https://evil.example/openid")]
    [InlineData("openid.claimed_id", "https://steamcommunity.com/openid/id/01")]
    [InlineData("openid.identity", "https://steamcommunity.com/openid/id/2")]
    [InlineData("openid.return_to", "https://evil.example/return")]
    [InlineData("openid.realm", "https://evil.example/")]
    [InlineData("openid.signed", "identity,claimed_id")]
    [InlineData("openid.signed", "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle,identity")]
    [InlineData("openid.response_nonce", "not-a-nonce")]
    [InlineData("openid.sig", "")]
    [InlineData("openid.assoc_handle", "")]
    [InlineData("openid.ext1", "untrusted")]
    public async Task Invalid_assertion_fields_never_reach_provider_or_mutate(string field, string value)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var assertion = Assertion(flow); assertion[field] = value;
        using var callback = await client.GetAsync(Callback(assertion));
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Assert.Equal(0, host.Provider.Calls);
        Assert.False(callback.Headers.Contains("Set-Cookie")); await ReviewState(client, flow, "Pending");
    }

    [Theory]
    [InlineData("state")]
    [InlineData("openid.sig")]
    [InlineData("encoded-key")]
    [InlineData("bad-utf8")]
    [InlineData("value-cap")]
    [InlineData("query-cap")]
    [InlineData("count-cap")]
    [InlineData("key-cap")]
    public async Task Duplicate_decoded_keys_and_query_resource_envelopes_fail_closed(string kind)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var url = Callback(Assertion(flow));
        url += kind switch
        {
            "state" => "&state=" + flow.State, "openid.sig" => "&openid.sig=second",
            "encoded-key" => "&%6fpenid.sig=second", "bad-utf8" => "&openid.invalidate_handle=%FF",
            "value-cap" => "&openid.invalidate_handle=" + new string('a', 8193),
            "query-cap" => "&openid.invalidate_handle=" + new string('a', 16384),
            "key-cap" => "&" + new string('a', 129) + "=v", _ => string.Concat(Enumerable.Range(0, 33).Select(i => "&x" + i + "=v")),
        };
        using var callback = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Assert.Equal(0, host.Provider.Calls);
        await ReviewState(client, flow, "Pending");
    }

    [Theory]
    [InlineData(-601)]
    [InlineData(-61)]
    [InlineData(61)]
    public async Task Nonce_freshness_uses_database_creation_and_now_before_provider(int seconds)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var assertion = Assertion(flow);
        assertion["openid.response_nonce"] = DateTimeOffset.UtcNow.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "synthetic";
        using var response = await client.GetAsync(Callback(assertion)); Assert.Equal(0, host.Provider.Calls);
        await ReviewState(client, flow, "Pending");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("https://evil.example")]
    [InlineData("https://app.example.test/")]
    public async Task Json_operations_require_exact_Origin_even_with_valid_JWT_and_cookie(string origin)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); client.DefaultRequestHeaders.Remove("Origin");
        if (origin != "missing") client.DefaultRequestHeaders.Add("Origin", origin);
        foreach (var operation in new[] { "requests", "review", "confirm", "cancel" })
        {
            using var response = await client.PostAsJsonAsync(Root + operation, new { transactionId = flow.Id });
            await Code(response, 403, "LINK_ORIGIN_REJECTED");
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        }
    }

    [Theory]
    [InlineData("{\"ownerId\":\"forged\"}", 400)]
    [InlineData("{\"callbackUrl\":\"https://evil.example\"}", 400)]
    [InlineData("{\"x\":1,\"x\":2}", 400)]
    [InlineData("null", 400)]
    [InlineData("[]", 400)]
    [InlineData("{", 400)]
    [InlineData("oversize", 413)]
    public async Task Json_envelope_unknown_duplicate_types_and_chunked_size_reject_before_creation(string body, int status)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "requests")
        { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body == "oversize" ? new string(' ', 4097) : body))) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.SendAsync(request); await Code(response, status, "LINK_INVALID_REQUEST");
        Assert.Equal(0, host.Provider.Calls);
    }

    [Fact]
    public async Task Review_and_confirmation_reject_decoded_duplicate_properties_and_non_UUID_locators()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client);
        foreach (var body in new[]
        {
            "{\"transactionId\":\"" + flow.Id + "\",\"transaction\\u0049d\":\"" + flow.Id + "\"}",
            "{\"transactionId\":\"not-a-uuid\"}", "{\"transactionId\":3}",
        })
        {
            using var response = await client.PostAsync(Root + "review", new StringContent(body, Encoding.UTF8, "application/json"));
            await Code(response, 400, "LINK_INVALID_REQUEST");
        }
        using var noJson = await client.PostAsync(Root + "requests", new StringContent("{}")); await Code(noJson, 415, "LINK_INVALID_REQUEST");
    }

    [Fact]
    public async Task Preflight_is_unauthenticated_empty_exact_and_does_no_protocol_work()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser();
        foreach (var kind in new[] { "good", "origin", "headers", "method", "body", "query" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, Root + "requests" + (kind == "query" ? "?state=synthetic" : ""));
            request.Headers.Add("Origin", kind == "origin" ? "https://evil.example" : "https://app.example.test");
            request.Headers.Add("Access-Control-Request-Method", kind == "method" ? "GET" : "POST");
            request.Headers.Add("Access-Control-Request-Headers", kind == "headers" ? "authorization,content-type,x-extra" : "authorization,content-type");
            if (kind == "body") request.Content = new StringContent("nonempty");
            using var response = await client.SendAsync(request); Private(response);
            Assert.Equal(kind == "good" ? 204 : 403, (int)response.StatusCode);
            if (kind == "good") Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
            else Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        Assert.Equal(0, host.Provider.Calls);
    }

    [Fact]
    public async Task Callback_wait_holds_no_SQL_locks_and_replacement_during_provider_wait_cannot_stage_old_row()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Provider.BeforeVerify = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        var callback = client.GetAsync(Callback(Assertion(flow)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            // Creation takes the same Library lock and completes while provider I/O waits.
            var replacement = await Create(client);
            Assert.True(replacement.Id != flow.Id && replacement.Proof != flow.Proof);
        }
        finally { release.SetResult(); }
        using var response = await callback;
        Assert.Equal("https://app.example.test/manage/steam?steamLink=failure", response.Headers.Location!.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        Assert.NotNull(row.CancelledAt); Assert.Null(row.AssertionAcceptedAt);
    }

    [Fact]
    public async Task Unlink_invalidates_pending_or_staged_rows_while_gate_disabled_and_cannot_resurrect_receipt()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client);
        using var callback = await client.GetAsync(Callback(Assertion(flow)));
        var monitor = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<SteamOptions>>();
        monitor.CurrentValue.SecureLinkingEnabled = false;
        using var unlink = await client.DeleteAsync("/steam/link"); Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        Assert.NotNull(row.CancelledAt); Assert.Null(row.ConsumedAt);
        monitor.CurrentValue.SecureLinkingEnabled = true; // Test-host DI only; never shipped admission.
        using var confirm = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = row.VerifiedSteamId64 });
        await Code(confirm, 409, "LINK_REQUEST_NOT_ACTIVE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_creation_commit_never_issues_cookie_or_assumed_authorization_URL(bool committed)
    {
        var fault = new SteamCommitFault(committed) { Armed = true };
        using var host = new SteamV2Factory(fixture.ConnectionString, interceptors: [fault]); using var client = host.Browser(User());
        using var response = await client.PostAsJsonAsync(Root + "requests", new { });
        await Code(response, 503, "LINK_OUTCOME_UNKNOWN");
        Assert.False((await response.Content.ReadAsStringAsync()).Contains("authorizationUrl", StringComparison.Ordinal));
        Assert.Equal(1, fault.Fired);
    }

    [Fact]
    public async Task Provider_nonce_spent_before_API_or_lost_verification_cannot_be_replayed_as_success()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var assertion = Assertion(flow); host.Provider.Spend(assertion["openid.response_nonce"]!);
        using var failed = await client.GetAsync(Callback(assertion));
        await ReviewState(client, flow, "Pending");
        Assert.Equal(1, host.Provider.Calls); Assert.False(failed.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Application_logs_scopes_and_errors_exclude_protocol_canaries_on_success_and_failure()
    {
        using var activities = new ProtocolActivityCapture();
        using var host = new SteamV2Factory(fixture.ConnectionString); var sub = User(); using var client = host.Browser(sub);
        var flow = await Create(client); var assertion = Assertion(flow, Identity());
        using var staged = await client.GetAsync(Callback(assertion));
        await ReviewState(client, flow, "AwaitingConfirmation");
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        host.Provider.BeforeVerify = _ => throw new HttpRequestException("Synthetic failure " + flow.Proof + assertion["openid.sig"]);
        var second = await Create(client); using var failed = await client.GetAsync(Callback(Assertion(second)));
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        host.Capture.AssertPrivate(flow.State, flow.Proof, flow.Id.ToString(), token, row.StateHash,
            SteamSecureLinkRules.Encode(row.BrowserBindingHash!), assertion["openid.sig"]!, assertion["openid.claimed_id"]!, sub);
        activities.AssertPrivate(flow.State, flow.Proof, flow.Id.ToString(), token, row.StateHash,
            SteamSecureLinkRules.Encode(row.BrowserBindingHash!), assertion["openid.sig"]!);
        Assert.False(failed.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Https_cookie_is_host_only_bounded_and_incoming_proof_or_forwarded_Host_is_never_adopted()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var chosen = SteamSecureLinkRules.NewSecret();
        client.DefaultRequestHeaders.Add("Cookie", "__Host-GameLibrary.SteamLink.v2=" + chosen);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "evil.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        client.DefaultRequestHeaders.Host = "evil.example";
        using var response = await client.PostAsJsonAsync(Root + "requests", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Equal("__Host-GameLibrary.SteamLink.v2", cookie.Name.ToString());
        Assert.True(cookie.Secure && cookie.HttpOnly && cookie.SameSite == Microsoft.Net.Http.Headers.SameSiteMode.None);
        Assert.Equal("/", cookie.Path.ToString()); Assert.False(cookie.Domain.HasValue);
        Assert.True(cookie.Value.ToString() != chosen && cookie.MaxAge > TimeSpan.Zero && cookie.MaxAge <= TimeSpan.FromMinutes(10));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(body.GetProperty("expiresAt").GetDateTimeOffset().ToUnixTimeSeconds(), cookie.Expires!.Value.ToUnixTimeSeconds());
        var url = new Uri(body.GetProperty("authorizationUrl").GetString()!);
        var parameters = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("https://api.example.test/", parameters["openid.realm"].ToString());
        var trusted = parameters["openid.return_to"].ToString().StartsWith("https://api.example.test/steam/link/v2/callback?state=", StringComparison.Ordinal);
        Assert.True(trusted, "Return target must use configured API, never forwarded Host/scheme.");
    }

    [Fact]
    public async Task Explicit_local_exception_uses_one_literal_host_and_never_falls_back_from_secure_cookie()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString, api: "http://127.0.0.1:5218", frontend: "http://127.0.0.1:4200");
        using var client = host.Browser(User());
        using var response = await client.PostAsJsonAsync(Root + "requests", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Equal("GameLibrary.SteamLink.v2", cookie.Name.ToString());
        Assert.True(cookie.HttpOnly && !cookie.Secure && cookie.SameSite == Microsoft.Net.Http.Headers.SameSiteMode.Lax);
        Assert.False(cookie.Domain.HasValue);
        Assert.Equal("http://127.0.0.1:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Cross_transaction_assertion_replay_fails_exact_return_to_before_provider()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var first = await Create(client); var assertion = Assertion(first);
        var second = await Create(client); assertion["state"] = second.State;
        using var replay = await client.GetAsync(Callback(assertion)); Assert.Equal(0, host.Provider.Calls);
        Assert.False(replay.Headers.Contains("Set-Cookie")); await ReviewState(client, second, "Pending");
    }

    [Fact]
    public async Task Same_browser_injected_identity_or_user_switch_never_links_until_correct_owner_explicitly_confirms()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); var sub = User(); using var client = host.Browser(sub);
        var flow = await Create(client); var id = Identity();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(User()));
        using var callback = await client.GetAsync(Callback(Assertion(flow, id)));
        Assert.Equal("https://app.example.test/manage/steam?steamLink=review", callback.Headers.Location!.ToString());
        using var wrongUser = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = id });
        await Code(wrongUser, 404, "LINK_REQUEST_NOT_FOUND");
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub));
        // No prior review call/token: the server checks U/B/T/staged identity, not human UI history.
        using var explicitConfirm = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = id });
        Assert.Equal(HttpStatusCode.OK, explicitConfirm.StatusCode);
    }

    [Fact]
    public async Task Signed_expiry_is_rechecked_despite_middleware_clock_skew_allowance()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); var sub = User(); using var client = host.Browser(sub);
        var flow = await Create(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub, expires: DateTime.UtcNow.AddSeconds(-2)));
        foreach (var operation in new[] { "review", "cancel", "confirm" })
        {
            using var response = await client.PostAsJsonAsync(Root + operation, operation == "confirm"
                ? (object)new { transactionId = flow.Id, expectedSteamId64 = "1" } : new { transactionId = flow.Id });
            await Code(response, 401, "LINK_UNAUTHORIZED");
        }
        using var create = await client.PostAsJsonAsync(Root + "requests", new { }); await Code(create, 401, "LINK_UNAUTHORIZED");
        Assert.Equal(0, host.Provider.Calls);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("configuration")]
    [InlineData("pause")]
    public async Task Changes_while_provider_waits_obey_final_eligibility_and_admission_is_not_revocation(string change)
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Provider.BeforeVerify = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        var callback = client.GetAsync(Callback(Assertion(flow)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<SteamOptions>>().CurrentValue;
        try
        {
            if (change == "expiry")
            {
                await using var db = fixture.Db();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET expires_at = clock_timestamp() WHERE id = {flow.Id}");
            }
            if (change == "configuration") options.OpenIdVerifyUrl = "https://evil.example/";
            if (change == "pause") options.SecureLinkingEnabled = false;
        }
        finally { release.SetResult(); }
        using var response = await callback; Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var inspect = fixture.Db(); var row = await inspect.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        Assert.Equal(change == "pause", row.AssertionAcceptedAt is not null);
        Assert.Null(row.ConsumedAt); // A flag flip doesn't revoke admitted work; no callback links.
        if (change == "pause")
        {
            using var confirm = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = row.VerifiedSteamId64 });
            await Code(confirm, 503, "LINK_UNAVAILABLE");
        }
    }

    [Fact]
    public async Task Initiation_and_owner_rate_limits_are_separate_per_subject_and_keep_retry_guidance()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        Flow? flow = null; for (var i = 0; i < 5; i++) flow = await Create(client);
        using var sixth = await client.PostAsJsonAsync(Root + "requests", new { }); await Code(sixth, 429, "LINK_RATE_LIMITED");
        Assert.True(sixth.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        for (var i = 0; i < 30; i++) await ReviewState(client, flow!, "Pending");
        using var thirtyFirst = await client.PostAsJsonAsync(Root + "review", new { transactionId = flow!.Id });
        await Code(thirtyFirst, 429, "LINK_RATE_LIMITED");
        using var other = host.Browser(User()); await Create(other);
    }

    [Fact]
    public async Task Callback_fifth_inflight_request_is_rejected_before_verification_with_retry_after_one()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString);
        var clients = Enumerable.Range(0, 5).Select(_ => host.Browser(User())).ToArray();
        try
        {
            var flows = new List<Flow>(); foreach (var client in clients) flows.Add(await Create(client));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var count = 0;
            host.Provider.BeforeVerify = async ct => { if (Interlocked.Increment(ref count) == 4) entered.SetResult(); await release.Task.WaitAsync(ct); };
            var callbacks = Enumerable.Range(0, 4).Select(i => clients[i].GetAsync(Callback(Assertion(flows[i])))).ToArray();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                using var rejected = await clients[4].GetAsync(Callback(Assertion(flows[4])));
                await Code(rejected, 429, "LINK_RATE_LIMITED");
                Assert.Equal(TimeSpan.FromSeconds(1), rejected.Headers.RetryAfter!.Delta);
                Assert.Equal(4, host.Provider.Calls);
            }
            finally { release.SetResult(); }
            foreach (var response in await Task.WhenAll(callbacks)) { Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode); response.Dispose(); }
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    [Theory]
    [InlineData("stage", false)]
    [InlineData("stage", true)]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    public async Task Unknown_commit_is_not_rollback_and_review_reads_durable_outcome_without_replay(string operation, bool committed)
    {
        var fault = new SteamCommitFault(committed);
        using var host = new SteamV2Factory(fixture.ConnectionString, interceptors: [fault]); using var client = host.Browser(User());
        var flow = await Create(client); var identity = Identity(); var assertion = Assertion(flow, identity);
        if (operation == "confirm") { using var stage = await client.GetAsync(Callback(assertion)); }
        fault.Armed = true;
        using var response = operation == "stage" ? await client.GetAsync(Callback(assertion))
            : await client.PostAsJsonAsync(Root + operation, operation == "confirm"
                ? (object)new { transactionId = flow.Id, expectedSteamId64 = identity } : new { transactionId = flow.Id });
        Assert.False(response.Headers.Contains("Set-Cookie")); Assert.Equal(1, fault.Fired);
        if (operation == "stage") Assert.Equal("https://app.example.test/manage/steam?steamLink=failure", response.Headers.Location!.ToString());
        else await Code(response, 503, "LINK_OUTCOME_UNKNOWN");
        if (operation == "cancel" && committed)
        {
            using var review = await client.PostAsJsonAsync(Root + "review", new { transactionId = flow.Id });
            await Code(review, 409, "LINK_REQUEST_NOT_ACTIVE");
        }
        else await ReviewState(client, flow, operation == "confirm" ? committed ? "Linked" : "AwaitingConfirmation"
            : operation == "stage" && committed ? "AwaitingConfirmation" : "Pending");
        Assert.Equal(operation is "stage" or "confirm" ? 1 : 0, host.Provider.Calls);
    }

    [Fact]
    public async Task Parallel_distinct_verified_assertions_stage_once_and_parallel_confirmations_return_one_receipt()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var client = host.Browser(User());
        var flow = await Create(client); var count = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Provider.BeforeVerify = async ct => { if (Interlocked.Increment(ref count) == 2) both.SetResult(); await both.Task.WaitAsync(ct); };
        var responses = await Task.WhenAll(client.GetAsync(Callback(Assertion(flow, Identity()))), client.GetAsync(Callback(Assertion(flow, Identity()))));
        try
        {
            Assert.Single(responses, r => r.Headers.Location!.ToString() == "https://app.example.test/manage/steam?steamLink=review");
            Assert.Single(responses, r => r.Headers.Location!.ToString() == "https://app.example.test/manage/steam?steamLink=failure");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        var confirms = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Root + "confirm",
            new { transactionId = flow.Id, expectedSteamId64 = row.VerifiedSteamId64 })));
        try { Assert.All(confirms, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); }
        finally { foreach (var response in confirms) response.Dispose(); }
        Assert.Equal(1, await db.SteamAccounts.CountAsync(a => a.LibraryId == row.LibraryId));
    }

    [Fact]
    public async Task Competing_Libraries_get_one_link_and_one_controlled_conflict_without_partial_consumption()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); using var a = host.Browser(User()); using var b = host.Browser(User());
        var first = await Create(a); var second = await Create(b); var identity = Identity();
        using var firstCallback = await a.GetAsync(Callback(Assertion(first, identity)));
        using var secondCallback = await b.GetAsync(Callback(Assertion(second, identity)));
        var result = await Task.WhenAll(a.PostAsJsonAsync(Root + "confirm", new { transactionId = first.Id, expectedSteamId64 = identity }),
            b.PostAsJsonAsync(Root + "confirm", new { transactionId = second.Id, expectedSteamId64 = identity }));
        try
        {
            Assert.Single(result, r => r.StatusCode == HttpStatusCode.OK);
            await Code(Assert.Single(result, r => r.StatusCode == HttpStatusCode.Conflict), 409, "LINK_CONFLICT");
        }
        finally { foreach (var response in result) response.Dispose(); }
        await using var db = fixture.Db(); var ids = new[] { first.Id, second.Id };
        var rows = await db.SteamLinkRequests.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
        Assert.Single(rows, r => r.ConsumedAt is not null && r.LinkedAccountId is not null);
        Assert.Single(rows, r => r.ConsumedAt is null && r.LinkedAccountId is null && r.VerifiedSteamId64 == identity);
    }

    [Fact]
    public async Task Unlink_and_confirm_race_under_shared_Library_barrier_never_relinks_after_unlink()
    {
        var barrier = new LibraryBarrier();
        using var host = new SteamV2Factory(fixture.ConnectionString, interceptors: [barrier]); using var client = host.Browser(User());
        var flow = await Create(client); var identity = Identity(); using var callback = await client.GetAsync(Callback(Assertion(flow, identity)));
        barrier.Armed = true;
        var confirmation = client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = identity });
        var unlinking = client.DeleteAsync("/steam/link");
        try { await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { barrier.Release.TrySetResult(); }
        using var confirm = await confirmation; using var unlink = await unlinking;
        Assert.Contains(confirm.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        Assert.True(row.CancelledAt is not null ^ row.ConsumedAt is not null);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
        using var retry = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = identity });
        await Code(retry, 409, row.ConsumedAt is null ? "LINK_REQUEST_NOT_ACTIVE" : "LINK_NOT_CURRENT");
    }

    [Fact]
    public async Task Schema_mismatch_disables_only_linking_and_unlink_still_invalidates_rows_and_preserves_catalog()
    {
        using var host = new SteamV2Factory(fixture.ConnectionString); var sub = User(); using var client = host.Browser(sub);
        var flow = await Create(client);
        await using var db = fixture.Db(); var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id);
        db.SteamAccounts.Add(new SteamAccount { Id = Guid.NewGuid(), LibraryId = row.LibraryId, SteamId64 = Identity(), LinkedAt = DateTimeOffset.UtcNow });
        var game = new GameLibrary.Core.Games.Game { Id = Guid.NewGuid(), Name = "Preserved catalog", GameType = GameLibrary.Core.Games.GameType.BoardGame,
            MinimumPlayers = 1, MaximumPlayers = 4, CreatedAt = DateTimeOffset.UtcNow };
        db.Games.Add(game);
        db.LibraryEntries.Add(new GameLibrary.Core.Games.LibraryEntry { Id = Guid.NewGuid(), LibraryId = row.LibraryId, GameId = game.Id,
            AcquisitionStatus = GameLibrary.Core.Games.AcquisitionStatus.Owned, Notes = "Private notes unchanged" });
        await db.SaveChangesAsync();
        var definition = await db.Database.SqlQuery<string>($"""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
            WHERE conrelid = to_regclass('public.steam_link_requests') AND conname = 'ck_steam_link_requests_v2_binding'
            """).SingleAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE steam_link_requests DROP CONSTRAINT ck_steam_link_requests_v2_binding");
        try
        {
            using var unavailable = await client.PostAsJsonAsync(Root + "review", new { transactionId = flow.Id });
            await Code(unavailable, 503, "LINK_UNAVAILABLE");
            using var status = await client.GetAsync("/steam/status"); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var body = await status.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("linked").GetBoolean()); Assert.False(body.GetProperty("linkAvailable").GetBoolean());
            using var unlink = await client.DeleteAsync("/steam/link"); Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
            Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
            Assert.NotNull((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.Id)).CancelledAt);
            Assert.Equal("Preserved catalog", (await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.Id)).Name);
            Assert.Equal("Private notes unchanged", (await db.LibraryEntries.AsNoTracking().SingleAsync(e => e.GameId == game.Id)).Notes);
        }
        finally
        {
            // Trusted server-generated constraint DDL, not caller data. Avoid EF's
            // format-string interpretation of the regex's literal {64} braces.
            await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand("ALTER TABLE steam_link_requests ADD CONSTRAINT ck_steam_link_requests_v2_binding " + definition, connection);
            await command.ExecuteNonQueryAsync();
        }
        using var replay = await client.PostAsJsonAsync(Root + "confirm", new { transactionId = flow.Id, expectedSteamId64 = "1" });
        await Code(replay, 409, "LINK_REQUEST_NOT_ACTIVE");
    }

    private sealed class LibraryBarrier : DbCommandInterceptor
    {
        public bool Armed; private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("FROM libraries", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _calls) == 2) Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class ProtocolActivityCapture : IDisposable
    {
        private readonly System.Diagnostics.ActivityListener _listener;
        private readonly ConcurrentQueue<string> _values = new();
        internal ProtocolActivityCapture()
        {
            _listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || source.Name == "System.Net.Http" || source.Name == "SteamV2TestControl",
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref System.Diagnostics.ActivityCreationOptions<string> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _values.Enqueue(activity.DisplayName + string.Join(";", activity.TagObjects.Select(t => t.Key + "=" + t.Value))),
            };
            System.Diagnostics.ActivitySource.AddActivityListener(_listener);
            using var source = new System.Diagnostics.ActivitySource("SteamV2TestControl");
            using var control = source.StartActivity("probe-enabled");
        }
        internal void AssertPrivate(params string[] canaries)
        {
            Assert.Contains(_values, v => v.Contains("probe-enabled", StringComparison.Ordinal));
            foreach (var value in _values)
            foreach (var canary in canaries)
                Assert.False(value.Contains(canary, StringComparison.Ordinal) || value.Contains(Uri.EscapeDataString(canary), StringComparison.Ordinal),
                    "Protocol canary reached Activity metadata; payload withheld.");
        }
        public void Dispose() => _listener.Dispose();
    }

    private static string Identity() => Random.Shared.NextInt64(100000000, long.MaxValue).ToString(CultureInfo.InvariantCulture);
}
