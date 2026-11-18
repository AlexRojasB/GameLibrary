using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace NeonQa;

sealed class GuardFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}

static class Policy
{
    public const string SqlHash = "90d01c192dd653205e06a3ef1faa9aa376ab4da1459a53d7d0df34352bf1be27";
    public const string FrozenHash = "73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb";
    public const string FrozenPath = "docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-manifest.json";
    public const string InventoryPath = "docs/deployment/neon-qa-source-manifest-runner-preflight-fix.json";
    public const string Host = "ep-wispy-cherry-beqkq4cc.c-9.us-east-2.aws.neon.tech";
    public const string Role = "neondb_owner";
    public const string Database = "neondb";
    // SHA256(target namespace), first eight bytes, signed big endian; deterministic,
    // target-specific, session-level and shared by every runner invocation.
    public const string LockNamespace = "GameLibraryQa/super-fire-30188358/br-muddy-base-be5m73fj/neondb/ef-migrations";
    public static readonly long LockKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(LockNamespace)));
    public static readonly string[] Migrations = [
        "20260816220833_InitialCreate", "20260817034441_AddPlatformManagement",
        "20260818012723_AddVideoGameManagement", "20260818035247_AddBoardGameManagement",
        "20260819183609_RelaxVideoGamePlayerCountConstraints", "20260825025118_AddPlayLogEntries",
        "20260825034331_AddPlayLogDurationMinutes", "20260826034524_AddSteamIntegration",
        "20260827154518_AddSteamPlaytimeTracking", "20261028185426_AddFamilyFriendly",
        "20261029134813_AddCoverUploads"];
    public static readonly string[] Tables = ["__EFMigrationsHistory", "cover_uploads", "game_genres",
        "game_platforms", "games", "genres", "libraries", "library_entries", "platforms",
        "play_log_entries", "steam_accounts", "steam_link_requests"];
    public static JsonObject Target() => new()
    {
        ["projectName"] = "GameLibraryQa", ["projectId"] = "super-fire-30188358",
        ["organizationId"] = "org-falling-fog-81506005", ["branchName"] = "production",
        ["branchId"] = "br-muddy-base-be5m73fj", ["computeId"] = "ep-wispy-cherry-beqkq4cc",
        ["database"] = Database, ["role"] = Role, ["databaseMetadataId"] = 612895,
        ["host"] = Host, ["port"] = 5432, ["postgresVersionNum"] = 180007,
        ["region"] = "aws-us-east-2", ["endpointType"] = "read_write",
        ["minCu"] = 0.25, ["maxCu"] = 2, ["suspendTimeoutSecondsRaw"] = 0,
        ["organizationPlan"] = "free", ["ownerSubscriptionType"] = "free_v3"
    };
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static void Require(bool valid, string code) { if (!valid) throw new GuardFailure(code); }
    public static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    public static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    public static void True(JsonObject obj, string key) => Require(obj[key]?.GetValue<bool>() == true, key);
    public static void ExactKeys(JsonObject obj, params string[] keys) =>
        Require(obj.Select(x => x.Key).Order().SequenceEqual(keys.Order()), "unexpected-or-missing-fields");

    public static (string Sql, string InventoryHash) Artifacts(string root, string sqlPath)
    {
        var sqlBytes = File.ReadAllBytes(sqlPath);
        Require(sqlBytes.Length == 24737 && Hash(sqlBytes) == SqlHash, "reviewed-sql-hash");
        var manifestBytes = File.ReadAllBytes(Path.Combine(root, FrozenPath));
        Require(Hash(manifestBytes) == FrozenHash, "frozen-manifest-hash");
        var frozen = JsonNode.Parse(manifestBytes)!["entries"]!.AsArray();
        Require(frozen.Count == 407, "frozen-file-count");
        VerifyEntries(root, frozen);
        var dir = Path.Combine(root, "src/backend/GameLibrary.Core/Data/Migrations");
        var expected = Migrations.SelectMany(m => new[] { m + ".cs", m + ".Designer.cs" })
            .Append("GameLibraryDbContextModelSnapshot.cs").Order();
        Require(Directory.GetFiles(dir).Select(Path.GetFileName).Order().SequenceEqual(expected), "migration-file-set");
        var inventory = File.ReadAllBytes(Path.Combine(root, InventoryPath));
        var node = JsonNode.Parse(inventory)!.AsObject();
        Require(Text(node, "frozenManifestSha256") == FrozenHash && node["executionApproved"]!.GetValue<bool>() == false,
            "inventory-policy");
        VerifyEntries(root, node["entries"]!.AsArray());
        // EF's reviewed file starts with a UTF-8 signature. Hash the complete
        // original bytes, then decode the signature as encoding metadata, not SQL.
        Require(sqlBytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "sql-encoding");
        var sql = Encoding.UTF8.GetString(sqlBytes.AsSpan(3));
        ValidateStatements(SplitSql(sql));
        return (sql, Hash(inventory));
    }

    public static void GitInputs(string root)
    {
        var inventory = ReadJson(Path.Combine(root, InventoryPath));
        var expected = inventory["entries"]!.AsArray().Select(x => x!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        expected.UnionWith(new[] { InventoryPath, "opencode.json", "render.yaml" });
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard" }) start.ArgumentList.Add(argument);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000)) { process.Kill(); throw new GuardFailure("git-inventory-timeout"); }
        Task.WaitAll(output, errors);
        Require(process.ExitCode == 0 && expected.SetEquals(output.Result.Split('\0', StringSplitOptions.RemoveEmptyEntries)), "unreviewed-source-path-set");
    }

    static void VerifyEntries(string root, JsonArray entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var relative = entry!["path"]!.GetValue<string>();
            Require(!Path.IsPathRooted(relative) && !relative.Split('/').Contains("..") && seen.Add(relative), "unsafe-source-path");
            var file = Path.Combine(root, relative);
            var cursor = new FileInfo(file) as FileSystemInfo;
            while (cursor is not null && cursor.FullName != root)
            {
                Require(cursor.LinkTarget is null, "source-symlink");
                cursor = cursor is FileInfo f ? f.Directory : ((DirectoryInfo)cursor).Parent;
            }
            var bytes = File.ReadAllBytes(file);
            Require(Hash(bytes) == entry["sha256"]!.GetValue<string>() && bytes.Length == entry["bytes"]!.GetValue<int>(), "source-hash");
            if (!OperatingSystem.IsWindows())
            {
                var executable = (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
                Require(entry["mode"]!.GetValue<string>() == (executable ? "100755" : "100644"), "source-mode");
            }
        }
    }

    public static void Evidence(JsonObject auth, JsonObject metadata, string metadataHash, string inventoryHash,
        string mode, DateTimeOffset now)
    {
        ExactKeys(auth, "approved", "action", "authorizationId", "issuedAt", "expiresAt", "target", "sqlSha256",
            "frozenManifestSha256", "runnerInventorySha256", "metadataSha256", "expectedPreflightSha256",
            "actualConfigurationAccepted", "sourceReleaseApproved", "localPostgresGatesPassed",
            "exposurePolicyVerified", "transportBoundaryAccepted", "exclusiveMaintenanceConfirmed");
        True(auth, "approved");
        Require(Text(auth, "action") == mode && mode is "execute" or "preflight", "authorization-action");
        Require(Guid.TryParse(Text(auth, "authorizationId"), out var id) && id != Guid.Empty, "authorization-id");
        var issued = DateTimeOffset.Parse(Text(auth, "issuedAt"));
        var expires = DateTimeOffset.Parse(Text(auth, "expiresAt"));
        Require(issued <= now && expires > now && expires > issued && expires - issued <= TimeSpan.FromMinutes(30), "authorization-expired");
        Require(JsonNode.DeepEquals(auth["target"], Target()) && JsonNode.DeepEquals(metadata["target"], Target()), "target-mismatch");
        Require(Text(auth, "sqlSha256") == SqlHash && Text(auth, "frozenManifestSha256") == FrozenHash
            && Text(auth, "runnerInventorySha256") == inventoryHash && Text(auth, "metadataSha256") == metadataHash,
            "approval-artifact-hash");
        ExactKeys(metadata, "verified", "observedAt", "source", "target", "neonAuthEnabled", "dataApiEnabled", "otherBrowserDataSurfaceEnabled");
        True(metadata, "verified");
        Require(Text(metadata, "source") == "authenticated-neon-control-plane", "metadata-source");
        var observed = DateTimeOffset.Parse(Text(metadata, "observedAt"));
        Require(observed <= now && now - observed <= TimeSpan.FromMinutes(5), "metadata-stale");
        foreach (var key in new[] { "neonAuthEnabled", "dataApiEnabled", "otherBrowserDataSurfaceEnabled" })
            Require(metadata[key]?.GetValue<bool>() == false, "browser-data-exposure");
        if (mode == "execute")
        {
            foreach (var key in new[] { "actualConfigurationAccepted", "sourceReleaseApproved", "localPostgresGatesPassed",
                "exposurePolicyVerified", "transportBoundaryAccepted", "exclusiveMaintenanceConfirmed" }) True(auth, key);
            Require(System.Text.RegularExpressions.Regex.IsMatch(Text(auth, "expectedPreflightSha256"), "^[a-f0-9]{64}$"), "approved-preflight-missing");
        }
    }

    // Small PostgreSQL lexical splitter, not a migration rewriter. Every emitted
    // command is an unchanged substring of the hash-pinned EF script (incl. ';').
    public static List<string> SplitSql(string sql)
    {
        var result = new List<string>();
        var start = 0;
        string? dollar = null;
        var single = false; var quoted = false;
        for (var i = 0; i < sql.Length; i++)
        {
            if (dollar is not null)
            {
                if (sql.AsSpan(i).StartsWith(dollar, StringComparison.Ordinal)) { i += dollar.Length - 1; dollar = null; }
                continue;
            }
            if (single || quoted)
            {
                var delimiter = single ? '\'' : '"';
                if (sql[i] == delimiter)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == delimiter) i++;
                    else { single = false; quoted = false; }
                }
                continue;
            }
            if (sql[i] == '\'') { single = true; continue; }
            if (sql[i] == '"') { quoted = true; continue; }
            if (sql[i] == '$')
            {
                var match = System.Text.RegularExpressions.Regex.Match(sql[i..], @"^\$[A-Za-z_][A-Za-z_0-9]*\$|^\$\$");
                if (match.Success) { dollar = match.Value; i += dollar.Length - 1; continue; }
            }
            // Reviewed artifact contains no comments or E-string escapes. Do not
            // generalize this tool to new SQL without a new artifact review.
            Require(!(i + 1 < sql.Length && (sql.AsSpan(i, 2).SequenceEqual("--") || sql.AsSpan(i, 2).SequenceEqual("/*"))), "unsupported-sql-comment");
            if (sql[i] == ';') { result.Add(sql[start..(i + 1)]); start = i + 1; }
        }
        Require(dollar is null && !single && !quoted && string.IsNullOrWhiteSpace(sql[start..]), "sql-lexical-state");
        return result;
    }

    public static void ValidateStatements(List<string> statements)
    {
        Require(statements[0].TrimStart().StartsWith("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (", StringComparison.Ordinal), "history-initialization");
        var active = false; var migration = 0;
        foreach (var raw in statements.Skip(1))
        {
            var sql = raw.Trim();
            if (sql == "START TRANSACTION;") { Require(!active && migration < 11, "transaction-start"); active = true; }
            else if (sql == "COMMIT;") { Require(active, "transaction-commit"); active = false; migration++; }
            else
            {
                Require(active && sql.StartsWith("DO $EF$", StringComparison.Ordinal) && sql.EndsWith("END $EF$;", StringComparison.Ordinal), "sql-command-kind");
                Require(sql.Contains("'" + Migrations[migration] + "'", StringComparison.Ordinal), "sql-migration-order");
            }
        }
        Require(!active && migration == 11, "transaction-count");
        var inserts = statements.Where(s => s.Contains("INSERT INTO \"__EFMigrationsHistory\"", StringComparison.Ordinal)).ToArray();
        Require(inserts.Length == 11 && inserts.Select((s, i) => s.Contains($"VALUES ('{Migrations[i]}', '10.0.11')", StringComparison.Ordinal)).All(x => x), "history-id-set");
    }

    public static void EmptyDatabase(JsonObject state)
    {
        Require(Text(state, "database") == Database && Text(state, "currentUser") == Role && Text(state, "sessionUser") == Role,
            "database-identity");
        Require(state["version"]!.GetValue<int>() == 180007 && state["recovery"]!.GetValue<bool>() == false, "version-or-primary");
        Require(Text(state, "readOnly") == "on" && Text(state, "defaultReadOnly") == "on"
            && Text(state, "isolation") == "repeatable read" && Text(state, "searchPath") == "public", "session-configuration");
        Require(Text(state, "databaseOwner") == Role && Text(state, "publicOwner") == "pg_database_owner", "ownership");
        foreach (var key in new[] { "schemaUsage", "schemaCreate", "databaseCreate", "canLogin", "inherits", "createDb", "createRole", "bypassRls" }) True(state, key);
        Require(state["superuser"]!.GetValue<bool>() == false, "role-drift");
        Require(JsonNode.DeepEquals(state["schemas"], new JsonArray("public")), "unexpected-schema");
        Require(JsonNode.DeepEquals(state["extensions"], new JsonArray("plpgsql:1.0:pg_catalog")), "extension-drift");
        foreach (var key in new[] { "relations", "routines", "types", "otherObjects", "globalObjects", "supabaseRoles" })
            Require(state[key]!.GetValue<long>() == 0, "nonempty-" + key);
        Require(JsonNode.DeepEquals(state["defaultAcls"], new JsonArray(
            "cloud_admin:public:S:{neon_superuser=r*w*U*/cloud_admin}",
            "cloud_admin:public:r:{neon_superuser=a*r*w*d*D*m*/cloud_admin}")), "default-acl-drift");
    }

    public static bool ValidPassfile(string text)
    {
        var lines = text.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0 && !x.StartsWith('#')).ToArray();
        if (lines.Length != 1) return false;
        var fields = new List<string>(); var field = new StringBuilder(); var escaped = false;
        foreach (var c in lines[0])
        {
            if (escaped) { field.Append(c); escaped = false; }
            else if (c == '\\') escaped = true;
            else if (c == ':') { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        if (escaped) return false;
        fields.Add(field.ToString());
        return fields.Count == 5 && fields[0] == Host && fields[1] == "5432"
            && fields[2] == Database && fields[3] == Role && fields[4].Length > 0;
    }
}
