using System.Text.Json.Nodes;

namespace NeonQa;

interface IOperatorSession
{
    Task<JsonObject> ReadState(); // Explicit repeatable-read READ ONLY, then ROLLBACK.
    Task<bool> TryLock(); // Session lock; never transaction lock or blocking wait.
    Task Command(string sql);
    Task VerifyApplied(string sql);
    Task Cleanup();
}

sealed class Engine
{
    public string Phase { get; private set; } = "offline-gates";
    public int AcknowledgedCommits { get; private set; }
    public bool MutationDispatched { get; private set; }
    public string? PreflightHash { get; private set; }

    public async Task Run(IOperatorSession session, string mode, string sql, string expectedPreflight, Action gate)
    {
        try
        {
            gate();
            Phase = "read-only-preflight";
            var state = await session.ReadState();
            Policy.EmptyDatabase(state);
            PreflightHash = Policy.Hash(state.ToJsonString());
            if (mode == "preflight") { Phase = "read-only-preflight-passed"; return; }
            Policy.Require(mode == "execute" && PreflightHash == expectedPreflight, "preflight-fingerprint-drift");
            gate();
            Phase = "nonblocking-session-lock";
            Policy.Require(await session.TryLock(), "concurrent-runner");
            Phase = "post-lock-read-only-revalidation";
            // New transaction/snapshot AFTER lock acquisition. Never reuse a stale
            // repeatable-read snapshot from before waiting for a coordinator.
            var locked = await session.ReadState();
            Policy.EmptyDatabase(locked);
            Policy.Require(Policy.Hash(locked.ToJsonString()) == PreflightHash, "post-lock-drift");
            gate();
            Phase = "enable-approved-session-writes";
            await session.Command("SET default_transaction_read_only = off;");
            var statements = Policy.SplitSql(sql);
            Policy.ValidateStatements(statements);
            for (var i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];
                if (i == 0 || statement.Trim() == "START TRANSACTION;") gate();
                Phase = i == 0 ? "history-initialization-dispatch" : statement.Trim() == "COMMIT;"
                    ? $"commit-dispatch-{AcknowledgedCommits + 1}" : $"migration-command-{AcknowledgedCommits + 1}";
                // History CREATE is autocommit. A thrown command, including COMMIT,
                // may already have committed. Never infer rollback from exceptions.
                MutationDispatched = true;
                await session.Command(statement);
                if (statement.Trim() == "COMMIT;") AcknowledgedCommits++;
            }
            Phase = "read-only-postverification";
            await session.Command("SET default_transaction_read_only = on;");
            await session.VerifyApplied(sql);
            Phase = "applied-and-minimal-postchecks-passed";
        }
        finally
        {
            // Cleanup failure must remain a failure, even after acknowledged commits.
            // This releases only our own lock; no retry/reconnect/reset/Down exists.
            await session.Cleanup();
        }
    }
}
