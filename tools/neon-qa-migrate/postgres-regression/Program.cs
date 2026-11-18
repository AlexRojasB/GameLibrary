using System.Text.Json;
using System.Text.Json.Nodes;
using NeonQa;
using Npgsql;

// Test executable only: the production project excludes this directory. No
// production Open(), hostname argument, TCP listener or credential input exists.
try
{
    Policy.Require(args.Length == 1 && !OperatingSystem.IsWindows(), "local-test-arguments");
    var socket = args[0];
    Policy.Require(Path.IsPathFullyQualified(socket) && new DirectoryInfo(socket).LinkTarget is null
        && File.ReadAllText(Path.Combine(socket, "regression.marker")) == "isolated-postgres-regression\n", "owned-test-marker");
    if (!OperatingSystem.IsWindows())
        Policy.Require(File.GetUnixFileMode(socket) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "private-test-directory");
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var settings = new NpgsqlConnectionStringBuilder
    {
        Host = socket, Port = 65439, Database = "postgres", Username = "regression_admin",
        SslMode = SslMode.Disable, GssEncryptionMode = GssEncryptionMode.Disable,
        Pooling = false, Multiplexing = false, Timeout = 5, CommandTimeout = 10,
        IncludeErrorDetail = false, ApplicationName = "neon-runner-local-query-regression"
    };
    await using var admin = new NpgsqlConnection(settings.ConnectionString);
    await admin.OpenAsync(budget.Token);
    Policy.Require(await Scalar(admin, "SELECT current_setting('server_version_num')::int=180007 AND inet_server_addr() IS NULL") is true, "isolated-postgres-18.7");
    await Scalar(admin, "CREATE ROLE neondb_owner LOGIN INHERIT CREATEDB CREATEROLE BYPASSRLS NOSUPERUSER");
    await Scalar(admin, "CREATE ROLE cloud_admin NOLOGIN");
    await Scalar(admin, "CREATE ROLE neon_superuser NOLOGIN");
    await Scalar(admin, "CREATE DATABASE neondb OWNER neondb_owner TEMPLATE template0");
    settings.Database = "neondb";
    await using (var setup = new NpgsqlConnection(settings.ConnectionString))
    {
        await setup.OpenAsync(budget.Token);
        await Scalar(setup, "ALTER DEFAULT PRIVILEGES FOR ROLE cloud_admin IN SCHEMA public GRANT SELECT,INSERT,UPDATE,DELETE,TRUNCATE,MAINTAIN ON TABLES TO neon_superuser WITH GRANT OPTION");
        await Scalar(setup, "ALTER DEFAULT PRIVILEGES FOR ROLE cloud_admin IN SCHEMA public GRANT SELECT,UPDATE,USAGE ON SEQUENCES TO neon_superuser WITH GRANT OPTION");
    }
    settings.Username = Policy.Role;
    settings.Options = "-c search_path=public -c default_transaction_read_only=on";
    await using var owner = new NpgsqlConnection(settings.ConnectionString);
    await owner.OpenAsync(budget.Token);
    await Scalar(owner, "BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY");
    try
    {
        // Execute the ACTUAL linked production query, not a copied/fake snapshot.
        var state = JsonNode.Parse((string)(await Scalar(owner, SqlSession.StateSql))!)!.AsObject();
        Policy.EmptyDatabase(state);
        Policy.Require(state["defaultAcls"]!.AsArray().Count == 2, "nonempty-default-acl-fixture");
    }
    finally { await Scalar(owner, "ROLLBACK"); }
    Console.WriteLine("PASS actual-production-StateSql-nonsuperuser-readonly-default-acls");

    // Negative control proves this fixture detects the original 42725 defect.
    const string fixedExpression = "||defaclobjtype::text||";
    Policy.Require(SqlSession.StateSql.Contains(fixedExpression, StringComparison.Ordinal), "regression-control-expression");
    var legacy = SqlSession.StateSql.Replace(fixedExpression, "||defaclobjtype||", StringComparison.Ordinal);
    var rejected = false;
    await Scalar(owner, "BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY");
    try { await Scalar(owner, legacy); }
    catch (PostgresException error) when (error.SqlState == "42725") { rejected = true; }
    finally { await Scalar(owner, "ROLLBACK"); }
    Policy.Require(rejected, "legacy-query-must-fail-42725");
    Console.WriteLine("PASS legacy-expression-negative-control-42725");
    Console.WriteLine(JsonSerializer.Serialize(new { passed = 2, failed = 0, remoteConnectionAttempted = false }));
    return 0;

    async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(budget.Token);
    }
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "regression-failed",
        code = error is GuardFailure guard ? guard.Code : "local-test-input-or-driver-failure",
        sqlState = error is PostgresException postgres ? postgres.SqlState : null }));
    return 1;
}
