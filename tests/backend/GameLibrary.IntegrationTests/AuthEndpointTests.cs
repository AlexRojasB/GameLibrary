using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GameLibrary.IntegrationTests;

public class AuthEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public AuthEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_AuthMe_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Get_AuthMe_WithValidToken_ReturnsSubAsUserId()
    {
        var client = _factory.CreateClient();
        const string sub = "6e3f10b4-1c7d-4f0a-9e6a-9b3c5f6a7d21";
        var token = TestTokens.CreateToken(sub);

        var response = await client.SendAsync(AuthorizedRequest(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);
        Assert.Equal(sub, body!.UserId);
    }

    [Fact]
    public async Task Get_AuthMe_WithWrongSignature_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var token = TestTokens.CreateToken("some-user-id", signingKey: TestTokens.OtherSigningKey);

        var response = await client.SendAsync(AuthorizedRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuthMe_WithExpiredToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var token = TestTokens.CreateToken("some-user-id", expires: DateTime.UtcNow.AddHours(-1));

        var response = await client.SendAsync(AuthorizedRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuthMe_WithTokenLackingSub_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var token = TestTokens.CreateToken(subject: null);

        var response = await client.SendAsync(AuthorizedRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_RemainsAnonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage AuthorizedRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record MeResponse(string UserId);
}