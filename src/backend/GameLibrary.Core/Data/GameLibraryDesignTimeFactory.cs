using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameLibrary.Core.Data;

// Schema tooling must not depend on optional API providers or hosted cleanup startup.
public sealed class GameLibraryDesignTimeFactory : IDesignTimeDbContextFactory<GameLibraryDbContext>
{
    public GameLibraryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Test")
            ?? "Host=localhost;Database=game_library_design";
        return new(new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(connection).Options);
    }
}
