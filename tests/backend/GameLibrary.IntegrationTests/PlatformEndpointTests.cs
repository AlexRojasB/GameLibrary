using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Real-PostgreSQL integration tests for the Platforms feature. Each test uses a
/// fresh, unique <c>sub</c> so no cleanup between tests is required; migrations
/// are applied idempotently before every test.
/// </summary>
[Collection("Database")]
public class PlatformEndpointTests : IClassFixture<PlatformTestFactory>, IAsyncLifetime
{
    private readonly PlatformTestFactory _factory;

    public PlatformEndpointTests(PlatformTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .Options;
        await using var db = new GameLibraryDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("GET", "/platforms")]
    [InlineData("POST", "/platforms")]
    [InlineData("PUT", "/platforms/{id}")]
    [InlineData("DELETE", "/platforms/{id}")]
    public async Task Platforms_WithoutToken_ReturnsUnauthorized(string method, string path)
    {
        var client = _factory.CreateClient();
        var url = path.Replace("{id}", Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { name = "Steam" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Platforms_WithTokenLackingSub_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/platforms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(subject: null));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidName_Returns201_StoredTrimmed_NoLocation()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/platforms", new { name = "  Steam  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<PlatformResponse>();
        Assert.NotNull(body);
        Assert.Equal("Steam", body!.Name);

        var list = await client.GetFromJsonAsync<PlatformResponse[]>("/platforms");
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("Steam", list![0].Name);
    }

    [Fact]
    public async Task Create_NameExactly100Chars_Returns201()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/platforms", new { name = new string('x', 100) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyOrWhitespaceName_Returns400(string name)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/platforms", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NullName_Returns400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/platforms", new { name = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/platforms", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Name101Chars_Returns400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/platforms", new { name = new string('x', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyCallingUsersPlatforms_OrderedByName()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());

        await PostPlatform(userA, "XBox");
        await PostPlatform(userA, "steam");
        await PostPlatform(userB, "PC");

        var listA = await userA.GetFromJsonAsync<PlatformResponse[]>("/platforms");
        var listB = await userB.GetFromJsonAsync<PlatformResponse[]>("/platforms");

        Assert.Equal(new[] { "steam", "XBox" }, listA!.Select(p => p.Name));
        Assert.Equal(new[] { "PC" }, listB!.Select(p => p.Name));
    }

    [Fact]
    public async Task List_UserWithNoLibrary_ReturnsEmpty()
    {
        var client = ClientFor(NewSub());

        var list = await client.GetFromJsonAsync<PlatformResponse[]>("/platforms");

        Assert.Empty(list!);
    }

    [Fact]
    public async Task OtherUsersPlatform_UpdateAndDelete_Return404_SameNameAllowed()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());

        var aPlatform = await PostPlatform(userA, "Steam");

        var sameName = await userB.PostAsJsonAsync("/platforms", new { name = "Steam" });
        Assert.Equal(HttpStatusCode.Created, sameName.StatusCode);

        var update = await userB.PutAsJsonAsync($"/platforms/{aPlatform.Id}", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await userB.DeleteAsync($"/platforms/{aPlatform.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var listA = await userA.GetFromJsonAsync<PlatformResponse[]>("/platforms");
        Assert.Single(listA!);
        Assert.Equal("Steam", listA![0].Name);
    }

    [Fact]
    public async Task Rename_Returns200_NewNameReflectedInList()
    {
        var client = ClientFor(NewSub());
        var created = await PostPlatform(client, "Steam");

        var response = await client.PutAsJsonAsync($"/platforms/{created.Id}", new { name = "Nintendo Switch" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PlatformResponse>();
        Assert.NotNull(body);
        Assert.Equal("Nintendo Switch", body!.Name);

        var list = await client.GetFromJsonAsync<PlatformResponse[]>("/platforms");
        Assert.Equal("Nintendo Switch", list![0].Name);
    }

    [Fact]
    public async Task Rename_InvalidNames_Return400()
    {
        var client = ClientFor(NewSub());
        var created = await PostPlatform(client, "Steam");

        var whitespace = await client.PutAsJsonAsync($"/platforms/{created.Id}", new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, whitespace.StatusCode);

        var tooLong = await client.PutAsJsonAsync($"/platforms/{created.Id}", new { name = new string('x', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameCaseInsensitive_Returns409()
    {
        var client = ClientFor(NewSub());
        await PostPlatform(client, "Steam");

        var response = await client.PostAsJsonAsync("/platforms", new { name = "steam" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rename_ToDuplicateOfAnotherPlatform_Returns409()
    {
        var client = ClientFor(NewSub());
        await PostPlatform(client, "Steam");
        var second = await PostPlatform(client, "XBox");

        var response = await client.PutAsJsonAsync($"/platforms/{second.Id}", new { name = "  STEAM " });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rename_ToOwnCurrentNameDifferentCasing_Returns200()
    {
        var client = ClientFor(NewSub());
        var created = await PostPlatform(client, "Steam");

        var response = await client.PutAsJsonAsync($"/platforms/{created.Id}", new { name = "steam" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PlatformResponse>();
        Assert.NotNull(body);
        Assert.Equal("steam", body!.Name);
    }

    [Fact]
    public async Task Delete_Returns204_RemovesFromList_SecondDeleteReturns404()
    {
        var client = ClientFor(NewSub());
        var created = await PostPlatform(client, "Steam");

        var delete = await client.DeleteAsync($"/platforms/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await client.GetFromJsonAsync<PlatformResponse[]>("/platforms");
        Assert.Empty(list!);

        var again = await client.DeleteAsync($"/platforms/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task FirstCreate_CreatesExactlyOneLibraryRow_SecondReusesIt()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        await PostPlatform(client, "Steam");
        await PostPlatform(client, "XBox");

        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .Options;
        await using var db = new GameLibraryDbContext(options);
        var libraryCount = await db.Libraries.CountAsync(l => l.UserId == sub);

        Assert.Equal(1, libraryCount);
    }

    private static string NewSub() => Guid.NewGuid().ToString();

    private HttpClient ClientFor(string sub)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub));
        return client;
    }

    private static async Task<PlatformResponse> PostPlatform(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/platforms", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlatformResponse>())!;
    }

    private sealed record PlatformResponse(Guid Id, string Name);
}
