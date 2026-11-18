using GameLibrary.Core.CoverUploads;
using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Api.CoverUploads;

public sealed class CoverCleanupService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<CoverCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CoverUploadService>().CleanupAsync(stoppingToken);
                var oldest = await scope.ServiceProvider.GetRequiredService<GameLibraryDbContext>().CoverUploads
                    .Where(u => u.CleanupAfter != null).MinAsync(u => u.CleanupAfter, stoppingToken);
                if (oldest is { } due && due < clock.GetUtcNow())
                    logger.LogWarning("Cover cleanup has overdue work; oldest due age is {Seconds} seconds.",
                        (long)(clock.GetUtcNow() - due).TotalSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Do not log provider/connection exceptions, secrets or object locators.
                logger.LogWarning("Cover cleanup pass deferred; database or private Storage unavailable.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
