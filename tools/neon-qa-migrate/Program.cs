using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NeonQa;
using Npgsql;

var engine = new Engine();
try
{
    Policy.Require(!OperatingSystem.IsWindows(), "unix-operator-required");
    Policy.Require(args.Length >= 3 && args[0] is "offline" or "self-test" or "preflight" or "execute", "arguments");
    var mode = args[0]; var root = args[1]; var sqlPath = args[2];
    Policy.Require(args.Length == (mode is "offline" or "self-test" ? 3 : 7), "arguments");
    Policy.Require(typeof(NpgsqlConnection).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0] == "10.0.3", "driver-version");
    var artifacts = Policy.Artifacts(root, sqlPath);
    Policy.GitInputs(root);
    if (mode == "self-test") return await OfflineTests.Run(root, sqlPath, artifacts.Sql);
    if (mode == "offline")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { status = "offline-artifacts-passed", executionEnabled = false,
            sqlSha256 = Policy.SqlHash, frozenManifestSha256 = Policy.FrozenHash, runnerInventorySha256 = artifacts.InventoryHash,
            migrationCount = 11, frozenFiles = 407, lockKey = Policy.LockKey, networkAttempted = false }));
        return 0;
    }
    // Future network gates: do not read credentials until every public artifact,
    // owner authorization, control-plane attestation and explicit input passes.
    var authPath = args[3]; var metadataPath = args[4]; var passfile = args[5]; var ca = args[6];
    foreach (var path in new[] { authPath, metadataPath, passfile })
    {
        Policy.Require(Path.IsPathFullyQualified(path) && !Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && new FileInfo(path).LinkTarget is null && new FileInfo(path).Length is > 0 and <= 65536, "private-input-location");
        if (!OperatingSystem.IsWindows())
            Policy.Require(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "private-input-mode");
    }
    var authBytes = File.ReadAllBytes(authPath); var metadataBytes = File.ReadAllBytes(metadataPath);
    var auth = JsonNode.Parse(authBytes)!.AsObject(); var metadata = JsonNode.Parse(metadataBytes)!.AsObject();
    void Gate()
    {
        Policy.Require(Policy.Hash(File.ReadAllBytes(authPath)) == Policy.Hash(authBytes)
            && Policy.Hash(File.ReadAllBytes(metadataPath)) == Policy.Hash(metadataBytes), "evidence-file-changed");
        var freshArtifacts = Policy.Artifacts(root, sqlPath);
        Policy.GitInputs(root);
        Policy.Require(freshArtifacts.InventoryHash == artifacts.InventoryHash, "inventory-changed");
        Policy.Evidence(auth, metadata, Policy.Hash(metadataBytes), artifacts.InventoryHash, mode, DateTimeOffset.UtcNow);
    }
    Gate();
    Policy.Require(Policy.ValidPassfile(File.ReadAllText(passfile)), "passfile-target");
    using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    await using var session = await SqlSession.Open(passfile, ca, budget.Token);
    await engine.Run(session, mode, artifacts.Sql, Policy.Text(auth, "expectedPreflightSha256"), Gate);
    Console.WriteLine(JsonSerializer.Serialize(new { status = engine.Phase, preflightSha256 = engine.PreflightHash,
        acknowledgedMigrationCommits = engine.AcknowledgedCommits, apiStartupApproved = false,
        releaseVerificationComplete = false }));
    return 0;
}
catch (Exception error)
{
    // Never echo raw SQL, connection strings, credentials, driver exceptions,
    // stack traces, server diagnostics or names taken from unexpected objects.
    Console.WriteLine(JsonSerializer.Serialize(new { status = "stopped", phase = engine.Phase,
        code = error is GuardFailure guard ? guard.Code : "input-driver-or-cleanup-failure",
        acknowledgedMigrationCommits = engine.AcknowledgedCommits,
        reconciliationRequired = engine.MutationDispatched,
        outcome = engine.MutationDispatched ? "possibly-partially-committed-do-not-retry" : "no-migration-command-dispatched" }));
    return 1;
}
