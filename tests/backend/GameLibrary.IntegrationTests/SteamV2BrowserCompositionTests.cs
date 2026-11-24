using GameLibrary.Api.Steam;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.TestHost;

namespace GameLibrary.IntegrationTests;

public sealed class SteamV2BrowserCompositionTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Production_and_ordinary_Staging_reject_explicit_E2E_admission_even_with_all_flags(string environment) =>
        Assert.Throws<InvalidOperationException>(() => E2eSteamLinking.Validate(Config(), new Host(environment)));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Real_composition_cannot_start_with_test_linking_admission_in_ordinary_hosts(string environment)
    {
        using var factory = new SteamV2Factory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1", environment);
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("E2E:SteamLinking:Enabled", "true"));
        Assert.Throws<InvalidOperationException>(() => host.CreateClient());
    }

    [Theory]
    [InlineData("E2E:Auth:Enabled", "false")]
    [InlineData("E2E:SteamIntegration:Enabled", "false")]
    [InlineData("SteamIntegration:AllowLocalLinkingHttp", "false")]
    [InlineData("SteamIntegration:PublicApiBaseUrl", "http://evil.example")]
    [InlineData("SteamIntegration:FrontendBaseUrl", "http://localhost:4200")]
    [InlineData("ConnectionStrings:E2E", "Host=remote.example;Database=game_library_e2e")]
    [InlineData("ConnectionStrings:E2E", "Host=127.0.0.1;Database=production")]
    public void Unsafe_composition_fails_before_any_database_provider_IO(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => E2eSteamLinking.Validate(Config(key, value), new Host("Test")));

    [Fact]
    public void Explicit_isolated_composition_still_requires_a_literal_loopback_HTTP_request()
    {
        Assert.True(E2eSteamLinking.Validate(Config(), new Host("Test")));
        var context = new DefaultHttpContext();
        var readiness = new E2eSteamLinking(new HttpContextAccessor { HttpContext = context });
        Assert.False(readiness.Ready);
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Host = new HostString("127.0.0.1", 5218);
        Assert.True(readiness.Ready);
        context.Request.Host = new HostString("evil.example"); Assert.False(readiness.Ready);
        context.Request.Host = new HostString("127.0.0.1", 5218);
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1"); Assert.False(readiness.Ready);
    }

    [Fact]
    public async Task Controlled_provider_verifies_exact_signed_fields_and_spends_nonce_once()
    {
        var fields = new Dictionary<string, string?> { ["openid.response_nonce"] = "synthetic-nonce", ["openid.identity"] = "synthetic-identity" };
        fields["openid.sig"] = E2eSteamClient.Sign(fields);
        var provider = new E2eSteamClient();
        var tampered = new Dictionary<string, string?>(fields) { ["openid.identity"] = "changed" };
        Assert.False(await provider.VerifyOpenIdAsync(tampered, default));
        Assert.True(await provider.VerifyOpenIdAsync(fields, default));
        Assert.False(await provider.VerifyOpenIdAsync(fields, default));
    }

    private static IConfiguration Config(string? key = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["E2E:SteamLinking:Enabled"] = "true", ["E2E:Auth:Enabled"] = "true", ["E2E:SteamIntegration:Enabled"] = "true",
            ["SteamIntegration:PublicApiBaseUrl"] = "http://127.0.0.1:5218", ["SteamIntegration:FrontendBaseUrl"] = "http://127.0.0.1:4200",
            ["SteamIntegration:AllowLocalLinkingHttp"] = "true", ["ConnectionStrings:E2E"] = "Host=127.0.0.1;Database=game_library_e2e",
        };
        if (key is not null) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private sealed class Host(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
