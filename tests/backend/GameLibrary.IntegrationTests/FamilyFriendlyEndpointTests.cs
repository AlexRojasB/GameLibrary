using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public class FamilyFriendlyEndpointTests : IClassFixture<LibraryTestFactory>, IAsyncLifetime
{
    private readonly LibraryTestFactory _factory;

    public FamilyFriendlyEndpointTests(LibraryTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("VideoGame", "omitted", false)]
    [InlineData("VideoGame", "null", false)]
    [InlineData("VideoGame", "false", false)]
    [InlineData("VideoGame", "true", true)]
    [InlineData("BoardGame", "omitted", false)]
    [InlineData("BoardGame", "null", false)]
    [InlineData("BoardGame", "false", false)]
    [InlineData("BoardGame", "true", true)]
    public async Task Create_normalizes_classification_and_returns_it_on_read(string type, string value, bool expected)
    {
        var sub = Guid.NewGuid().ToString();
        var client = ClientFor(sub);
        var body = await GameBody(client, type, value);

        var created = await Create(client, type, body);

        Assert.Equal(expected, created.GetProperty("isFamilyFriendly").GetBoolean());
        await AssertPersistedAndRead(client, sub, type, created.GetProperty("id").GetGuid(), expected);
    }

    [Theory]
    [InlineData("VideoGame", "omitted", false)]
    [InlineData("VideoGame", "null", false)]
    [InlineData("VideoGame", "false", false)]
    [InlineData("VideoGame", "true", true)]
    [InlineData("BoardGame", "omitted", false)]
    [InlineData("BoardGame", "null", false)]
    [InlineData("BoardGame", "false", false)]
    [InlineData("BoardGame", "true", true)]
    public async Task Update_replaces_classification_in_both_directions_including_defaults(string type, string value, bool expected)
    {
        var sub = Guid.NewGuid().ToString();
        var client = ClientFor(sub);
        var body = await GameBody(client, type, expected ? "false" : "true");
        var created = await Create(client, type, body);
        var id = created.GetProperty("id").GetGuid();
        SetClassification(body, value);

        var response = await client.PutAsJsonAsync($"{Route(type)}/{id}", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, updated.GetProperty("isFamilyFriendly").GetBoolean());
        Assert.Equal(created.GetProperty("createdAt").GetDateTimeOffset(), updated.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(4, updated.GetProperty("rating").GetInt32());
        Assert.Equal("Keep notes", updated.GetProperty("notes").GetString());
        await AssertPersistedAndRead(client, sub, type, id, expected);
    }

    [Theory]
    [InlineData("VideoGame", "true")]
    [InlineData("VideoGame", 1)]
    [InlineData("BoardGame", "false")]
    [InlineData("BoardGame", 0)]
    public async Task Game_classification_requires_a_json_boolean_on_create_and_update(string type, object invalidValue)
    {
        var client = ClientFor(Guid.NewGuid().ToString());
        var body = await GameBody(client, type, "true");
        var created = await Create(client, type, body);
        body["isFamilyFriendly"] = invalidValue;

        var create = await client.PostAsJsonAsync(Route(type), body);
        var update = await client.PutAsJsonAsync($"{Route(type)}/{created.GetProperty("id").GetGuid()}", body);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var stored = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>(Route(type)))!);
        Assert.True(stored.GetProperty("isFamilyFriendly").GetBoolean());
    }

    [Theory]
    [InlineData("VideoGame")]
    [InlineData("BoardGame")]
    public async Task Classification_create_update_read_filter_and_pick_are_user_scoped(string type)
    {
        var subA = Guid.NewGuid().ToString();
        var subB = Guid.NewGuid().ToString();
        var clientA = ClientFor(subA);
        var clientB = ClientFor(subB);
        var body = await GameBody(clientA, type, "true");
        body["userId"] = subB;
        body["ownerId"] = subB;
        var created = await Create(clientA, type, body);
        var id = created.GetProperty("id").GetGuid();

        Assert.Empty(await clientB.GetFromJsonAsync<JsonElement[]>(Route(type)) ?? []);
        Assert.Empty(await clientB.GetFromJsonAsync<JsonElement[]>("/library?familyFriendlyOnly=true") ?? []);
        var pick = await Pick(clientB, new { mode = "All", familyFriendlyOnly = true });
        Assert.Equal("NO_CANDIDATES", pick.GetProperty("state").GetString());

        var foreignEdit = await GameBody(clientB, type, "false");
        var response = await clientB.PutAsJsonAsync($"{Route(type)}/{id}", foreignEdit);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertPersistedAndRead(clientA, subA, type, id, true);

        var own = await Create(clientB, type, foreignEdit);
        Assert.False(own.GetProperty("isFamilyFriendly").GetBoolean());
        Assert.Empty(await clientB.GetFromJsonAsync<JsonElement[]>("/library?familyFriendlyOnly=true") ?? []);
        var ownPick = await Pick(clientA, new { mode = "All", familyFriendlyOnly = true });
        Assert.Equal(id, ownPick.GetProperty("result").GetProperty("gameId").GetGuid());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("VideoGame")]
    [InlineData("BoardGame")]
    public async Task Library_family_friendly_filter_applies_across_types_and_combines_using_and(string? gameType)
    {
        var client = ClientFor(Guid.NewGuid().ToString());
        foreach (var type in new[] { "VideoGame", "BoardGame" })
        {
            var match = await GameBody(client, type, "true");
            match["name"] = "Family Match " + type;
            await Create(client, type, match);
            var decoy = new Dictionary<string, object?>(match) { ["isFamilyFriendly"] = false };
            await Create(client, type, decoy);
            decoy = new(match) { ["name"] = "Unrelated" };
            await Create(client, type, decoy);
            decoy = new(match) { ["acquisitionStatus"] = "Wishlist", ["gameStatus"] = null };
            await Create(client, type, decoy);
            decoy = new(match) { ["rating"] = 3 };
            await Create(client, type, decoy);
            decoy = new(match) { ["rating"] = null };
            await Create(client, type, decoy);
        }

        var typeFilter = gameType is null ? "" : "&gameType=" + gameType;
        var items = (await client.GetFromJsonAsync<JsonElement[]>(
            "/library?familyFriendlyOnly=true&search=fAMILY%20mATCH&acquisitionStatuses=Owned&ratingMin=4" + typeFilter))!;

        Assert.Equal(gameType is null ? 2 : 1, items.Length);
        Assert.All(items, item =>
        {
            Assert.True(item.GetProperty("isFamilyFriendly").GetBoolean());
            Assert.Equal("Owned", item.GetProperty("acquisitionStatus").GetString());
            Assert.Equal(4, item.GetProperty("rating").GetInt32());
            if (gameType is not null) Assert.Equal(gameType, item.GetProperty("gameType").GetString());
        });

        var unfiltered = (await client.GetFromJsonAsync<JsonElement[]>("/library"))!;
        Assert.Equal(12, unfiltered.Length);
        Assert.Equal(12, (await client.GetFromJsonAsync<JsonElement[]>("/library?familyFriendlyOnly=false"))!.Length);
        Assert.Equal(12, (await client.GetFromJsonAsync<JsonElement[]>("/library?familyFriendlyOnly="))!.Length);
        Assert.Contains(unfiltered, item => !item.GetProperty("isFamilyFriendly").GetBoolean());
    }

    [Theory]
    [InlineData("VideoGame")]
    [InlineData("BoardGame")]
    public async Task Library_family_friendly_combines_with_type_specific_filters_using_and(string type)
    {
        var client = ClientFor(Guid.NewGuid().ToString());
        var body = await GameBody(client, type, "true");
        var match = await Create(client, type, body);
        await Create(client, type, new Dictionary<string, object?>(body) { ["isFamilyFriendly"] = false });

        string typeFilters;
        if (type == "VideoGame")
        {
            var platformId = ((Guid[])body["platformIds"]!)[0];
            var genreId = ((Guid[])body["genreIds"]!)[0];
            typeFilters = $"&platformIds={platformId}&genreIds={genreId}&gameStatuses=Playing";
            var otherPlatform = await client.PostAsJsonAsync("/platforms", new { name = "Other" });
            otherPlatform.EnsureSuccessStatusCode();
            var otherId = (await otherPlatform.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await Create(client, type, new Dictionary<string, object?>(body) { ["platformIds"] = new[] { otherId } });
            await Create(client, type, new Dictionary<string, object?>(body) { ["genreIds"] = Array.Empty<Guid>() });
            await Create(client, type, new Dictionary<string, object?>(body) { ["gameStatus"] = null });
        }
        else
        {
            typeFilters = "&playerCount=3&interactionTypes=Cooperative";
            await Create(client, type, new Dictionary<string, object?>(body) { ["maximumPlayers"] = 2 });
            await Create(client, type, new Dictionary<string, object?>(body) { ["interactionType"] = null });
        }

        var items = (await client.GetFromJsonAsync<JsonElement[]>(
            $"/library?familyFriendlyOnly=true&gameType={type}{typeFilters}"))!;

        var result = Assert.Single(items);
        Assert.Equal(match.GetProperty("id").GetGuid(), result.GetProperty("id").GetGuid());
        Assert.True(result.GetProperty("isFamilyFriendly").GetBoolean());
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("null")]
    public async Task Library_invalid_boolean_uses_invalid_query_problem_details(string value)
    {
        var response = await ClientFor(Guid.NewGuid().ToString()).GetAsync("/library?familyFriendlyOnly=" + value);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid library query", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("VideoGames")]
    [InlineData("BoardGames")]
    [InlineData("All")]
    public async Task Picker_filters_owned_candidates_with_and_and_preserves_distinct_states(string mode)
    {
        var client = ClientFor(Guid.NewGuid().ToString());
        var type = mode == "VideoGames" ? "VideoGame" : "BoardGame";
        var body = await GameBody(client, type, "true");
        var match = await Create(client, type, body);
        await Create(client, type, new Dictionary<string, object?>(body) { ["isFamilyFriendly"] = false });
        await Create(client, type, new Dictionary<string, object?>(body) { ["acquisitionStatus"] = "Wishlist", ["gameStatus"] = null });
        await Create(client, type, new Dictionary<string, object?>(body) { ["minimumPlayers"] = 1, ["maximumPlayers"] = 2 });

        var filters = new Dictionary<string, object?>
        {
            ["mode"] = mode, ["familyFriendlyOnly"] = true, ["playerCount"] = 3,
        };
        if (mode == "VideoGames")
        {
            filters["platformIds"] = body["platformIds"];
            filters["genreIds"] = body["genreIds"];
            filters["gameStatuses"] = new[] { "Playing" };
            await Create(client, type, new Dictionary<string, object?>(body) { ["genreIds"] = Array.Empty<Guid>() });
            await Create(client, type, new Dictionary<string, object?>(body) { ["gameStatus"] = null });
            var otherPlatform = await client.PostAsJsonAsync("/platforms", new { name = "Other" });
            var otherId = (await otherPlatform.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await Create(client, type, new Dictionary<string, object?>(body) { ["platformIds"] = new[] { otherId } });
        }
        else if (mode == "BoardGames")
        {
            filters["availableDuration"] = 60;
            filters["interactionTypes"] = new[] { "Cooperative" };
            await Create(client, type, new Dictionary<string, object?>(body) { ["approximateDuration"] = 90 });
            await Create(client, type, new Dictionary<string, object?>(body) { ["interactionType"] = "Competitive" });
        }
        else
        {
            filters["ratingMin"] = 4;
            await Create(client, type, new Dictionary<string, object?>(body) { ["rating"] = 3 });
            var video = await GameBody(client, "VideoGame", "true");
            await Create(client, "VideoGame", video);
            await Create(client, "VideoGame", new Dictionary<string, object?>(video) { ["isFamilyFriendly"] = false });
        }

        var shown = new List<Guid>();
        var pickedTypes = new List<string>();
        for (var i = 0; i < (mode == "All" ? 2 : 1); i++)
        {
            filters["shownLibraryEntryIds"] = shown;
            var picked = await Pick(client, filters);
            Assert.Equal("SUCCESS", picked.GetProperty("state").GetString());
            var result = picked.GetProperty("result");
            Assert.True(result.GetProperty("isFamilyFriendly").GetBoolean());
            shown.Add(result.GetProperty("libraryEntryId").GetGuid());
            pickedTypes.Add(result.GetProperty("gameType").GetString()!);
            if (mode != "All") Assert.Equal(match.GetProperty("id").GetGuid(), result.GetProperty("gameId").GetGuid());
        }
        if (mode == "All") Assert.Equal(new[] { "BoardGame", "VideoGame" }, pickedTypes.OrderBy(t => t));

        var allShown = await Pick(client, filters);
        Assert.Equal("ALL_ALREADY_SHOWN", allShown.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, allShown.GetProperty("result").ValueKind);
        filters["playerCount"] = 100;
        var noCandidates = await Pick(client, filters);
        Assert.Equal("NO_CANDIDATES", noCandidates.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, noCandidates.GetProperty("result").ValueKind);
    }

    [Theory]
    [InlineData("VideoGames", "VideoGame")]
    [InlineData("BoardGames", "BoardGame")]
    [InlineData("All", "BoardGame")]
    public async Task Picker_missing_null_or_false_does_not_require_family_friendly(string mode, string type)
    {
        var client = ClientFor(Guid.NewGuid().ToString());
        var created = await Create(client, type, await GameBody(client, type, "false"));
        foreach (var value in new[] { "omitted", "null", "false" })
        {
            var body = new Dictionary<string, object?> { ["mode"] = mode };
            if (value != "omitted") body["familyFriendlyOnly"] = value == "null" ? null : false;
            var picked = await Pick(client, body);
            Assert.Equal("SUCCESS", picked.GetProperty("state").GetString());
            Assert.Equal(created.GetProperty("id").GetGuid(), picked.GetProperty("result").GetProperty("gameId").GetGuid());
            Assert.False(picked.GetProperty("result").GetProperty("isFamilyFriendly").GetBoolean());
        }
    }

    [Theory]
    [InlineData("VideoGames")]
    [InlineData("BoardGames")]
    [InlineData("All")]
    public async Task Picker_family_friendly_requires_a_json_boolean(string mode)
    {
        var client = ClientFor(Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/random-picker/pick", new { mode, familyFriendlyOnly = "true" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Dictionary<string, object?>> GameBody(HttpClient client, string type, string value)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Family Game", ["acquisitionStatus"] = "Owned", ["rating"] = 4,
            ["notes"] = "Keep notes", ["minimumPlayers"] = 2, ["maximumPlayers"] = 4,
        };
        if (type == "VideoGame")
        {
            var platforms = (await client.GetFromJsonAsync<JsonElement[]>("/platforms"))!;
            if (platforms.Length == 0)
            {
                var response = await client.PostAsJsonAsync("/platforms", new { name = "Steam" });
                response.EnsureSuccessStatusCode();
                platforms = [await response.Content.ReadFromJsonAsync<JsonElement>()];
            }
            body["platformIds"] = new[] { platforms[0].GetProperty("id").GetGuid() };
            body["genreIds"] = new[] { GenresCatalog.All.Single(g => g.Name == "Party").Id };
            body["gameStatus"] = "Playing";
        }
        else
        {
            body["interactionType"] = "Cooperative";
            body["approximateDuration"] = 45;
        }
        SetClassification(body, value);
        return body;
    }

    private static void SetClassification(Dictionary<string, object?> body, string value)
    {
        body.Remove("isFamilyFriendly");
        if (value != "omitted") body["isFamilyFriendly"] = value == "null" ? null : value == "true";
    }

    private async Task AssertPersistedAndRead(HttpClient client, string sub, string type, Guid id, bool expected)
    {
        var games = (await client.GetFromJsonAsync<JsonElement[]>(Route(type)))!;
        Assert.Equal(expected, Assert.Single(games).GetProperty("isFamilyFriendly").GetBoolean());
        var library = (await client.GetFromJsonAsync<JsonElement[]>("/library"))!;
        Assert.Equal(expected, Assert.Single(library).GetProperty("isFamilyFriendly").GetBoolean());
        await using var db = CreateDbContext();
        var game = await db.Games.Include(g => g.LibraryEntry.Library).SingleAsync(g => g.Id == id);
        Assert.Equal(expected, game.IsFamilyFriendly);
        Assert.Equal(sub, game.LibraryEntry.Library.UserId);
    }

    private static string Route(string type) => type == "VideoGame" ? "/video-games" : "/board-games";

    private static async Task<JsonElement> Create(HttpClient client, string type, object body)
    {
        var response = await client.PostAsJsonAsync(Route(type), body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Pick(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/random-picker/pick", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpClient ClientFor(string sub)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub));
        return client;
    }

    private GameLibraryDbContext CreateDbContext() => new(new DbContextOptionsBuilder<GameLibraryDbContext>()
        .UseNpgsql(_factory.ConnectionString).Options);
}
