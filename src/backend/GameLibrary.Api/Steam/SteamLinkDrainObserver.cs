namespace GameLibrary.Api.Steam;

// Observation only: counts whole v2 HTTP pipelines, including denied requests.
// Zero is NOT proof that database work, another replica or a legacy writer drained.
// No request/context/identity is retained and there is no public diagnostics route.
internal sealed class SteamLinkDrainObserver
{
    private readonly object _sync = new();
    private long _started;
    private long _completed;

    internal (long Started, long Completed, long Active) Snapshot()
    {
        lock (_sync) return (_started, _completed, _started - _completed);
    }

    internal IDisposable Enter()
    {
        lock (_sync) _started++;
        return new Lease(this);
    }

    private sealed class Lease(SteamLinkDrainObserver owner) : IDisposable
    {
        private SteamLinkDrainObserver? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null) lock (current._sync) current._completed++;
        }
    }

    internal static async Task ObserveAsync(HttpContext context, RequestDelegate next)
    {
        if (!SteamLinkHttp.IsV2(context)) { await next(context); return; }
        var observer = context.RequestServices.GetRequiredService<SteamLinkDrainObserver>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(SteamLogPrivacy.Category);
        var lease = observer.Enter();
        try { Log(); await next(context); }
        finally { lease.Dispose(); Log(); }

        void Log()
        {
            var snapshot = observer.Snapshot();
            logger.LogInformation(new EventId(1102, "SteamLinkPipelineObserved"),
                "Steam v2 HTTP pipelines: started {Started}, completed {Completed}, active {Active}.",
                snapshot.Started, snapshot.Completed, snapshot.Active);
        }
    }
}
