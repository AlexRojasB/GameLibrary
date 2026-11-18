using System.Text.Json.Nodes;

namespace NeonQa;

static class OfflineTests
{
    static int passed;
    static async Task Check(string name, Func<Task> test)
    {
        await test(); passed++; Console.WriteLine("PASS " + name);
    }
    static Task Sync(Action action) { action(); return Task.CompletedTask; }
    static void Assert(bool value) => Policy.Require(value, "offline-test-assertion");
    static async Task Reject(Func<Task> action, string? code = null)
    {
        try { await action(); }
        catch (GuardFailure error) { Assert(code is null || error.Code == code); return; }
        throw new GuardFailure("offline-test-expected-rejection");
    }

    internal static JsonObject CleanState() => new()
    {
        ["database"] = Policy.Database, ["currentUser"] = Policy.Role, ["sessionUser"] = Policy.Role,
        ["version"] = 180007, ["recovery"] = false, ["readOnly"] = "on", ["defaultReadOnly"] = "on",
        ["isolation"] = "repeatable read", ["searchPath"] = "public", ["databaseOwner"] = Policy.Role,
        ["publicOwner"] = "pg_database_owner", ["schemaUsage"] = true, ["schemaCreate"] = true,
        ["databaseCreate"] = true, ["canLogin"] = true, ["inherits"] = true, ["createDb"] = true,
        ["createRole"] = true, ["bypassRls"] = true, ["superuser"] = false,
        ["schemas"] = new JsonArray("public"), ["extensions"] = new JsonArray("plpgsql:1.0:pg_catalog"),
        ["relations"] = 0L, ["routines"] = 0L, ["types"] = 0L, ["otherObjects"] = 0L,
        ["globalObjects"] = 0L, ["supabaseRoles"] = 0L,
        ["defaultAcls"] = new JsonArray("cloud_admin:public:S:{neon_superuser=r*w*U*/cloud_admin}",
            "cloud_admin:public:r:{neon_superuser=a*r*w*d*D*m*/cloud_admin}"),
        ["roleMemberships"] = new JsonArray(), ["backendSsl"] = false
    };

    internal static (JsonObject Auth, JsonObject Metadata) Approved(DateTimeOffset now)
    {
        // Synthetic, in-memory fixtures only. No real authorization artifact is created.
        var metadata = new JsonObject
        {
            ["verified"] = true, ["observedAt"] = now.ToString("O"),
            ["source"] = "authenticated-neon-control-plane", ["target"] = Policy.Target(),
            ["neonAuthEnabled"] = false, ["dataApiEnabled"] = false, ["otherBrowserDataSurfaceEnabled"] = false
        };
        var auth = new JsonObject
        {
            ["approved"] = true, ["action"] = "execute", ["authorizationId"] = Guid.NewGuid().ToString(),
            ["issuedAt"] = now.ToString("O"), ["expiresAt"] = now.AddMinutes(20).ToString("O"),
            ["target"] = Policy.Target(), ["sqlSha256"] = Policy.SqlHash, ["frozenManifestSha256"] = Policy.FrozenHash,
            ["runnerInventorySha256"] = "fixture-inventory", ["metadataSha256"] = Policy.Hash(metadata.ToJsonString()),
            ["expectedPreflightSha256"] = Policy.Hash(CleanState().ToJsonString()),
            ["actualConfigurationAccepted"] = true, ["sourceReleaseApproved"] = true, ["localPostgresGatesPassed"] = true,
            ["exposurePolicyVerified"] = true, ["transportBoundaryAccepted"] = true, ["exclusiveMaintenanceConfirmed"] = true
        };
        return (auth, metadata);
    }

    public static async Task<int> Run(string root, string sqlPath, string sql)
    {
        var now = DateTimeOffset.Parse("2026-11-15T20:00:00Z");
        var expected = Policy.Hash(CleanState().ToJsonString());
        await Check("clean-preflight-has-no-lock-and-no-mutations", async () =>
        {
            var s = new ModelSession(new LeaseModel()); var e = new Engine();
            await e.Run(s, "preflight", sql, "", () => { });
            Assert(e.PreflightHash == expected && s.LockAttempts == 0 && s.Commands.Count == 0 && s.Cleaned);
        });
        await Check("11-commits-same-session-lock-spans-all-commands", async () =>
        {
            var s = new ModelSession(new LeaseModel()); var e = new Engine();
            await e.Run(s, "execute", sql, expected, () => { });
            Assert(e.AcknowledgedCommits == 11 && s.Reads == 2 && s.Verified && s.Cleaned
                && !s.Model.Held && s.Model.Commits == 11);
            Assert(s.Commands.Skip(1).Take(s.Commands.Count - 2).SequenceEqual(Policy.SplitSql(sql)));
        });
        foreach (var (key, value) in new (string, JsonNode)[] {
            ("database", JsonValue.Create("other")!), ("currentUser", JsonValue.Create("runtime")!),
            ("sessionUser", JsonValue.Create("runtime")!), ("version", JsonValue.Create(180008)!),
            ("recovery", JsonValue.Create(true)!), ("readOnly", JsonValue.Create("off")!),
            ("defaultReadOnly", JsonValue.Create("off")!), ("isolation", JsonValue.Create("read committed")!),
            ("searchPath", JsonValue.Create("\"$user\", public")!), ("databaseOwner", JsonValue.Create("other")!),
            ("publicOwner", JsonValue.Create("other")!), ("superuser", JsonValue.Create(true)!),
            ("schemas", new JsonArray("public", "private")), ("extensions", new JsonArray("plpgsql:1.0:pg_catalog", "pgcrypto:1.3:public")),
            ("defaultAcls", new JsonArray()), ("relations", JsonValue.Create(1L)!), ("routines", JsonValue.Create(1L)!),
            ("types", JsonValue.Create(1L)!), ("otherObjects", JsonValue.Create(1L)!),
            ("globalObjects", JsonValue.Create(1L)!), ("supabaseRoles", JsonValue.Create(1L)!) })
        {
            await Check("reject-database-" + key, async () =>
            {
                var state = CleanState(); state[key] = value;
                var s = new ModelSession(new LeaseModel()) { State = state };
                await Reject(() => new Engine().Run(s, "execute", sql, expected, () => { }));
                Assert(s.Commands.Count == 0 && s.LockAttempts == 0);
            });
        }
        foreach (var key in new[] { "schemaUsage", "schemaCreate", "databaseCreate", "canLogin", "inherits", "createDb", "createRole", "bypassRls" })
            await Check("reject-missing-privilege-or-role-drift-" + key, () =>
            {
                var state = CleanState(); state[key] = false;
                return Reject(() => Sync(() => Policy.EmptyDatabase(state)));
            });
        await Check("post-lock-schema-drift-rejected-before-history-create", async () =>
        {
            var s = new ModelSession(new LeaseModel()) { DriftOnSecondRead = true };
            await Reject(() => new Engine().Run(s, "execute", sql, expected, () => { }), "nonempty-relations");
            Assert(s.Commands.Count == 0 && s.Cleaned && !s.Model.Held);
        });
        await Check("approved-preflight-fingerprint-drift", async () =>
        {
            var s = new ModelSession(new LeaseModel()); s.State["roleMemberships"] = new JsonArray("new-membership");
            await Reject(() => new Engine().Run(s, "execute", sql, expected, () => { }), "preflight-fingerprint-drift");
            Assert(s.LockAttempts == 0);
        });
        await Check("expired-post-lock-gate-stops-before-mutation", async () =>
        {
            var calls = 0; var s = new ModelSession(new LeaseModel());
            await Reject(() => new Engine().Run(s, "execute", sql, expected, () => Policy.Require(++calls < 3, "expired")), "expired");
            Assert(s.Commands.Count == 0 && !s.Model.Held);
        });
        await Check("modeled-concurrent-runner-nonblocking-rejection-and-duplicate-rejection", async () =>
        {
            var lease = new LeaseModel();
            var first = new ModelSession(lease) { PauseAfterAcquire = true };
            var a = new Engine().Run(first, "execute", sql, expected, () => { });
            await first.Acquired.Task;
            var second = new ModelSession(lease);
            await Reject(() => new Engine().Run(second, "execute", sql, expected, () => { }), "concurrent-runner");
            Assert(second.Commands.Count == 0 && lease.Held);
            first.Continue.SetResult(); await a;
            var duplicate = new ModelSession(lease);
            await Reject(() => new Engine().Run(duplicate, "execute", sql, expected, () => { }), "nonempty-relations");
            Assert(duplicate.LockAttempts == 0 && duplicate.Commands.Count == 0 && !lease.Held);
        });
        await Check("unknown-commit-no-replay-no-false-rollback-claim", async () =>
        {
            var s = new ModelSession(new LeaseModel()) { FailCommitAck = 3 }; var e = new Engine();
            await Reject(() => e.Run(s, "execute", sql, expected, () => { }), "simulated-lost-ack");
            Assert(s.Model.Commits == 3 && e.AcknowledgedCommits == 2 && e.MutationDispatched
                && s.Cleaned && !s.Model.Held && !s.Verified);
            Assert(s.Commands.Count(x => x.Trim() == "COMMIT;") == 3);
        });
        await Check("history-autocommit-failure-is-unknown-and-does-not-retry", async () =>
        {
            var s = new ModelSession(new LeaseModel()) { FailHistoryAck = true }; var e = new Engine();
            await Reject(() => e.Run(s, "execute", sql, expected, () => { }), "simulated-history-ack");
            Assert(e.MutationDispatched && e.AcknowledgedCommits == 0 && s.Model.Dirty && s.Commands.Count == 2);
        });
        await Check("sql-roundtrip-preserves-bytes-outer-dollar-blocks-and-nested-privacy", () => Sync(() =>
        {
            var statements = Policy.SplitSql(sql);
            Assert(string.Concat(statements) + sql[string.Concat(statements).Length..] == sql);
            Assert(statements.Count(s => s.Contains("DO $privacy$", StringComparison.Ordinal)) == 1);
            Assert(statements.Count(s => s.Trim() == "START TRANSACTION;") == 11
                && statements.Count(s => s.Trim() == "COMMIT;") == 11);
            Policy.ValidateStatements(statements);
        }));
        foreach (var key in Policy.Target().Select(x => x.Key).ToArray())
            await Check("reject-control-plane-target-drift-" + key, () =>
            {
                var (auth, metadata) = Approved(now); metadata["target"]![key] = null;
                auth["metadataSha256"] = Policy.Hash(metadata.ToJsonString());
                return Reject(() => Sync(() => Policy.Evidence(auth, metadata, Policy.Hash(metadata.ToJsonString()), "fixture-inventory", "execute", now)), "target-mismatch");
            });
        foreach (var field in new[] { "approved", "actualConfigurationAccepted", "sourceReleaseApproved", "localPostgresGatesPassed",
            "exposurePolicyVerified", "transportBoundaryAccepted", "exclusiveMaintenanceConfirmed" })
            await Check("reject-missing-future-approval-" + field, () =>
            {
                var (auth, metadata) = Approved(now); auth[field] = false;
                return Reject(() => Sync(() => Policy.Evidence(auth, metadata, Policy.Hash(metadata.ToJsonString()), "fixture-inventory", "execute", now)), field);
            });
        foreach (var field in new[] { "sqlSha256", "frozenManifestSha256", "runnerInventorySha256", "metadataSha256" })
            await Check("reject-authorization-hash-" + field, () =>
            {
                var (auth, metadata) = Approved(now); auth[field] = new string('0', 64);
                return Reject(() => Sync(() => Policy.Evidence(auth, metadata, Policy.Hash(metadata.ToJsonString()), "fixture-inventory", "execute", now)), "approval-artifact-hash");
            });
        await Check("future-authorization-and-metadata-valid-synthetic-only", () => Sync(() =>
        {
            var (auth, metadata) = Approved(now);
            Policy.Evidence(auth, metadata, Policy.Hash(metadata.ToJsonString()), "fixture-inventory", "execute", now);
        }));
        foreach (var scenario in new[] { "expired", "future-issued", "stale-metadata", "exposed", "unverified", "missing-fingerprint", "wrong-action", "extra-field" })
            await Check("reject-evidence-" + scenario, () =>
            {
                var (auth, metadata) = Approved(now);
                switch (scenario)
                {
                    case "expired": auth["expiresAt"] = now.ToString("O"); break;
                    case "future-issued": auth["issuedAt"] = now.AddMinutes(1).ToString("O"); break;
                    case "stale-metadata": metadata["observedAt"] = now.AddMinutes(-6).ToString("O"); break;
                    case "exposed": metadata["dataApiEnabled"] = true; break;
                    case "unverified": metadata["verified"] = false; break;
                    case "missing-fingerprint": auth["expectedPreflightSha256"] = null; break;
                    case "wrong-action": auth["action"] = "preflight"; break;
                    case "extra-field": auth["unexpected"] = true; break;
                }
                auth["metadataSha256"] = Policy.Hash(metadata.ToJsonString());
                return Reject(() => Sync(() => Policy.Evidence(auth, metadata, Policy.Hash(metadata.ToJsonString()), "fixture-inventory", "execute", now)));
            });
        await Check("altered-sql-hash-rejected-offline", async () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql");
            try { File.WriteAllText(temp, sql + " "); await Reject(() => Sync(() => Policy.Artifacts(root, temp)), "reviewed-sql-hash"); }
            finally { File.Delete(temp); }
        });
        await Check("exact-target-passfile-parser-synthetic-only", () => Sync(() =>
        {
            var valid = $"{Policy.Host}:5432:{Policy.Database}:{Policy.Role}:synthetic\\:fixture\\\\only";
            Assert(Policy.ValidPassfile(valid));
            foreach (var invalid in new[] { valid.Replace(Policy.Host, "*"), valid.Replace(Policy.Database, "*"),
                valid.Replace(Policy.Role, "runtime"), valid.Replace(Policy.Host, Policy.Host.Replace(".c-9", "-pooler.c-9")),
                valid + "\n" + valid, valid + "\\", $"{Policy.Host}:5432:{Policy.Database}:{Policy.Role}:" })
                Assert(!Policy.ValidPassfile(invalid));
        }));
        // Temporary copies only: never alter the frozen worktree to test corruption.
        var fixture = Path.Combine(Path.GetTempPath(), "neon-offline-source-" + Guid.NewGuid());
        Directory.CreateDirectory(fixture);
        try
        {
            var inventory = Policy.ReadJson(Path.Combine(root, Policy.InventoryPath));
            foreach (var entry in inventory["entries"]!.AsArray())
            {
                var relative = entry!["path"]!.GetValue<string>(); var destination = Path.Combine(fixture, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(root, relative), destination);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(Path.Combine(root, relative)));
            }
            var inventoryDestination = Path.Combine(fixture, Policy.InventoryPath);
            File.Copy(Path.Combine(root, Policy.InventoryPath), inventoryDestination);
            var migration = Path.Combine(fixture, "src/backend/GameLibrary.Core/Data/Migrations/" + Policy.Migrations[0] + ".cs");
            var original = File.ReadAllBytes(migration);
            await Check("altered-migration-source-rejected", async () =>
            {
                File.AppendAllText(migration, " ");
                try { await Reject(() => Sync(() => Policy.Artifacts(fixture, sqlPath)), "source-hash"); }
                finally { File.WriteAllBytes(migration, original); }
            });
            await Check("extra-migration-file-rejected", async () =>
            {
                var extra = Path.Combine(Path.GetDirectoryName(migration)!, "Unexpected.cs"); File.WriteAllText(extra, "fixture");
                try { await Reject(() => Sync(() => Policy.Artifacts(fixture, sqlPath)), "migration-file-set"); }
                finally { File.Delete(extra); }
            });
            await Check("altered-frozen-manifest-rejected", async () =>
            {
                File.AppendAllText(Path.Combine(fixture, Policy.FrozenPath), " ");
                await Reject(() => Sync(() => Policy.Artifacts(fixture, sqlPath)), "frozen-manifest-hash");
            });
        }
        finally { Directory.Delete(fixture, recursive: true); } // Only this test's UUID-owned temporary fixture.
        Console.WriteLine($"{passed} offline checks passed. No network/private input/API. Modeled concurrency is NOT PostgreSQL proof.");
        return 0;
    }

    sealed class LeaseModel
    {
        public bool Held; public bool Dirty; public int Commits;
        public bool TryAcquire() { lock (this) { if (Held) return false; Held = true; return true; } }
        public void Release() { lock (this) Held = false; }
    }

    sealed class ModelSession(LeaseModel model) : IOperatorSession
    {
        public LeaseModel Model { get; } = model;
        public JsonObject State { get; set; } = CleanState();
        public bool DriftOnSecondRead; public bool PauseAfterAcquire; public bool FailHistoryAck;
        public int FailCommitAck; public int Reads; public int LockAttempts;
        public bool Verified; public bool Cleaned; bool ownsLease;
        public List<string> Commands { get; } = [];
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonObject> ReadState()
        {
            Reads++; var state = (JsonObject)State.DeepClone();
            if (Model.Dirty || (DriftOnSecondRead && Reads == 2)) state["relations"] = 1L;
            return Task.FromResult(state);
        }
        public async Task<bool> TryLock()
        {
            LockAttempts++; ownsLease = Model.TryAcquire();
            if (ownsLease && PauseAfterAcquire) { Acquired.SetResult(); await Continue.Task; }
            return ownsLease;
        }
        public Task Command(string sql)
        {
            Assert(ownsLease && Model.Held); Commands.Add(sql);
            if (sql.Contains("CREATE TABLE IF NOT EXISTS", StringComparison.Ordinal))
            {
                Model.Dirty = true;
                if (FailHistoryAck) throw new GuardFailure("simulated-history-ack");
            }
            if (sql.Trim() == "COMMIT;")
            {
                Model.Commits++;
                if (FailCommitAck == Model.Commits) throw new GuardFailure("simulated-lost-ack");
            }
            return Task.CompletedTask;
        }
        public Task VerifyApplied(string sql) { Assert(ownsLease && Model.Commits == 11); Verified = true; return Task.CompletedTask; }
        public Task Cleanup() { if (ownsLease) Model.Release(); Cleaned = true; return Task.CompletedTask; }
    }
}
