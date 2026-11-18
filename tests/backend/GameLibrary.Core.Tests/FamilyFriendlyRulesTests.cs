using GameLibrary.Core.Games;
using GameLibrary.Core.RandomPicker;

namespace GameLibrary.Core.Tests;

public class FamilyFriendlyRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Game_input_values_and_omitted_defaults_are_normalized_by_helper(bool? value, bool expected)
    {
        var video = new VideoGameInput("Video", null, "Wishlist", null, null, null, null, null, null, null, null, value);
        var board = new BoardGameInput("Board", 1, 4, null, null, "Owned", null, null, null, value);

        Assert.Equal(expected, GameRules.NormalizeFamilyFriendly(video.IsFamilyFriendly));
        Assert.Equal(expected, GameRules.NormalizeFamilyFriendly(board.IsFamilyFriendly));
        Assert.False(GameRules.NormalizeFamilyFriendly(new VideoGameInput("Video", null, null, null, null, null, null, null, null, null, null).IsFamilyFriendly));
        Assert.False(GameRules.NormalizeFamilyFriendly(new BoardGameInput("Board", 1, 4, null, null, null, null, null, null).IsFamilyFriendly));
    }

    [Theory]
    [InlineData("VideoGames", null)]
    [InlineData("VideoGames", false)]
    [InlineData("VideoGames", true)]
    [InlineData("BoardGames", null)]
    [InlineData("BoardGames", false)]
    [InlineData("BoardGames", true)]
    [InlineData("All", null)]
    [InlineData("All", false)]
    [InlineData("All", true)]
    public void Picker_accepts_family_friendly_in_every_mode(string mode, bool? value)
    {
        var query = RandomPickerRules.Parse(new RandomPickerRequest(mode, null, null, null, null, null, null, null, null, value));

        Assert.Equal(value == true, query.FamilyFriendlyOnly);
        Assert.False(RandomPickerRules.Parse(new RandomPickerRequest(mode, null, null, null, null, null, null, null, null)).FamilyFriendlyOnly);
    }

    [Theory]
    [InlineData("VideoGames")]
    [InlineData("BoardGames")]
    [InlineData("All")]
    public void Picker_family_friendly_combines_with_every_available_filter_using_and(string mode)
    {
        var platform = Guid.NewGuid();
        var genre = Guid.NewGuid();
        var query = RandomPickerRules.Parse(new RandomPickerRequest(
            mode, null,
            mode == "VideoGames" ? [platform] : null,
            mode == "VideoGames" ? [genre] : null,
            mode == "VideoGames" ? ["Playing"] : null,
            3,
            mode == "BoardGames" ? 60 : null,
            mode == "BoardGames" ? ["Cooperative"] : null,
            mode == "All" ? 4 : null,
            true));

        Game Candidate() => new()
        {
            Id = Guid.NewGuid(),
            GameType = mode == "VideoGames" ? GameType.VideoGame : GameType.BoardGame,
            IsFamilyFriendly = true,
            MinimumPlayers = 2,
            MaximumPlayers = 4,
            ApproximateDuration = 45,
            InteractionType = InteractionType.Cooperative,
            LibraryEntry = new LibraryEntry
            {
                Rating = 4,
                GameStatus = GameStatus.Playing,
                GamePlatforms = [new GamePlatform { PlatformId = platform }],
            },
            GameGenres = [new GameGenre { GenreId = genre }],
        };

        var match = Candidate();
        var games = new List<Game> { match };
        var exclusions = new List<Action<Game>>
        {
            g => g.IsFamilyFriendly = false,
            g => g.MaximumPlayers = 2,
            g => { g.MinimumPlayers = null; g.MaximumPlayers = null; },
        };
        if (mode == "VideoGames")
        {
            exclusions.Add(g => g.LibraryEntry.GamePlatforms.Clear());
            exclusions.Add(g => g.GameGenres.Clear());
            exclusions.Add(g => g.LibraryEntry.GameStatus = null);
        }
        else if (mode == "BoardGames")
        {
            exclusions.Add(g => g.ApproximateDuration = 61);
            exclusions.Add(g => g.ApproximateDuration = null);
            exclusions.Add(g => g.InteractionType = InteractionType.Competitive);
            exclusions.Add(g => g.InteractionType = null);
        }
        else
        {
            exclusions.Add(g => g.LibraryEntry.Rating = 3);
            exclusions.Add(g => g.LibraryEntry.Rating = null);
        }

        foreach (var exclude in exclusions)
        {
            var decoy = Candidate();
            exclude(decoy);
            games.Add(decoy);
        }

        Assert.Equal(match.Id, Assert.Single(RandomPickerService.ApplyFilters(games.AsQueryable(), query)).Id);
    }
}
