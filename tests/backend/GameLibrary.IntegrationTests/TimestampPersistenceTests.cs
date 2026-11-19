using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GameLibrary.IntegrationTests;

[Collection("Cover lifecycle")]
public sealed class TimestampPersistenceTests(CoverHttpDatabaseFixture database)
    : IClassFixture<CoverHttpDatabaseFixture>
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task Npgsql_truncates_submicrosecond_ticks_but_JSON_and_the_tracked_entity_do_not(int extraTicks)
    {
        // Fixed expected values, independent of the platform's UtcNow resolution.
        var expected = new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero).AddTicks(1234560);
        var supplied = expected.AddTicks(extraTicks);
        Assert.Equal(0, expected.Ticks % TimeSpan.TicksPerMicrosecond);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT @value", connection);
            command.Parameters.AddWithValue("value", NpgsqlDbType.TimestampTz, supplied);
            var decoded = Assert.IsType<DateTime>(await command.ExecuteScalarAsync());
            Assert.Equal(DateTimeKind.Utc, decoded.Kind);
            Assert.Equal(expected.UtcDateTime, decoded);
        }

        await using var factory = new CoverHttpTestFactory(database.ConnectionString, new CoverHttpStorage(), new CoverLifecycleClock());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(Guid.NewGuid().ToString()));
        using var created = await client.PostAsJsonAsync("/board-games", new
        {
            name = "Timestamp precision fixture", acquisitionStatus = "Owned", minimumPlayers = 1, maximumPlayers = 4,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var trackedDb = database.CreateDb())
        {
            var game = await trackedDb.Games.SingleAsync(g => g.Id == id);
            Assert.Equal("timestamp with time zone", trackedDb.Model.FindEntityType(game.GetType())!
                .FindProperty(nameof(game.CreatedAt))!.GetColumnType());
            game.CreatedAt = supplied;
            await trackedDb.SaveChangesAsync();
            // SaveChanges does not reload this client-assigned property from PostgreSQL.
            Assert.Equal(supplied, game.CreatedAt);
            var json = JsonSerializer.SerializeToElement(new { game.CreatedAt }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(supplied, json.GetProperty("createdAt").GetDateTimeOffset());
        }

        await using var beforeDb = database.CreateDb();
        var before = await beforeDb.Games.AsNoTracking().Where(g => g.Id == id).Select(g => g.CreatedAt).SingleAsync();
        Assert.Equal(expected, before);
        Assert.Equal(extraTicks, supplied.Ticks - before.Ticks);
        var read = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/board-games"))!);
        Assert.Equal(expected, read.GetProperty("createdAt").GetDateTimeOffset());

        using var rejected = await client.PutAsJsonAsync($"/games/{id}/cover", new { coverUpdate = new { intent = "remove" } });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); // Missing optimistic precondition.
        await using var afterDb = database.CreateDb();
        var after = await afterDb.Games.AsNoTracking().Where(g => g.Id == id).Select(g => g.CreatedAt).SingleAsync();
        Assert.Equal(before, after); // Exact persisted equality; no rounding/tolerance here.
        var afterRead = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/board-games"))!);
        Assert.Equal(read.GetRawText(), afterRead.GetRawText());
    }
}
