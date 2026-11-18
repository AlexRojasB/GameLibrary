using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoverApiVerification;

// A loopback HTTP protocol fixture, NOT a Supabase permissions emulator. Only
// the provider is simulated; the API's normal typed adapter/transport stays active.
internal sealed class StorageFixture : IAsyncDisposable
{
    internal const string Credential = "sb_secret_localverificationonly";
    internal readonly ConcurrentDictionary<string, byte[]> Objects = new();
    internal TaskCompletionSource? WritesBlocked;
    internal TaskCompletionSource? ReadsBlocked;
    internal bool FailReads;
    internal bool FailDeletes;
    internal bool Public;
    internal int ActiveWrites, ActiveReads, ActiveDeletes;
    internal int Writes, Reads, Deletes, Cancellations, RejectedCredentials;
    private WebApplication _host = null!;
    internal string BaseUrl { get; private set; } = null!;

    internal async Task Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        _host = builder.Build();
        _host.Use(async (context, next) =>
        {
            if (context.Request.Headers["apikey"] != Credential || context.Request.Headers.ContainsKey("Authorization"))
            {
                Interlocked.Increment(ref RejectedCredentials);
                context.Response.StatusCode = 401;
                return;
            }
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                Interlocked.Increment(ref Cancellations);
            }
        });
        _host.MapGet("/storage/v1/bucket/game-covers", () => Results.Json(new
        {
            id = "game-covers",
            name = "game-covers",
            @public = Public,
            file_size_limit = 2097152,
            allowed_mime_types = new[] { "image/jpeg" },
        }));
        _host.MapPost("/storage/v1/object/game-covers/{**key}", async (string key, HttpContext context) =>
        {
            Interlocked.Increment(ref Writes);
            Interlocked.Increment(ref ActiveWrites);
            try
            {
                using var bytes = new MemoryStream();
                await context.Request.Body.CopyToAsync(bytes, context.RequestAborted);
                if (bytes.Length > 2097152 || context.Request.Headers["x-upsert"] != "false"
                    || context.Request.ContentType != "image/jpeg") return Results.StatusCode(400);
                if (WritesBlocked is { } block) await block.Task.WaitAsync(context.RequestAborted);
                if (!Objects.TryAdd(key, bytes.ToArray())) return Results.StatusCode(409);
                return Results.Json(new Dictionary<string, string> { ["Key"] = "game-covers/" + key }, statusCode: 201);
            }
            finally { Interlocked.Decrement(ref ActiveWrites); }
        });
        _host.MapGet("/storage/v1/object/authenticated/game-covers/{**key}", async (string key, HttpContext context) =>
        {
            Interlocked.Increment(ref Reads);
            Interlocked.Increment(ref ActiveReads);
            try
            {
                if (FailReads || !Objects.TryGetValue(key, out var bytes)) return Results.StatusCode(503);
                if (ReadsBlocked is { } block)
                {
                    // Headers and one byte admit the real adapter into its bounded
                    // body buffer before uploads decode, not merely a headers wait.
                    context.Response.ContentType = "image/jpeg";
                    context.Response.ContentLength = bytes.Length;
                    await context.Response.Body.WriteAsync(bytes.AsMemory(0, 1), context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    await block.Task.WaitAsync(context.RequestAborted);
                    await context.Response.Body.WriteAsync(bytes.AsMemory(1), context.RequestAborted);
                    return Results.Empty;
                }
                return Results.Bytes(bytes, "image/jpeg");
            }
            finally { Interlocked.Decrement(ref ActiveReads); }
        });
        _host.MapDelete("/storage/v1/object/game-covers/{**key}", async (string key, HttpContext context) =>
        {
            Interlocked.Increment(ref Deletes);
            Interlocked.Increment(ref ActiveDeletes);
            try
            {
                if (FailDeletes) return Results.StatusCode(503);
                Objects.TryRemove(key, out _);
                return Results.NoContent();
            }
            finally { Interlocked.Decrement(ref ActiveDeletes); }
        });
        await _host.StartAsync();
        BaseUrl = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/storage/v1/";
    }

    internal static TaskCompletionSource Block() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();
}
