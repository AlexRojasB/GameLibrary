using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;

namespace NeonQa;

sealed class SqlSession : IOperatorSession, IAsyncDisposable
{
    readonly NpgsqlConnection connection;
    readonly CancellationToken ct;
    bool locked;
    readonly int backendPid;

    SqlSession(NpgsqlConnection connection, CancellationToken ct)
    {
        this.connection = connection;
        this.ct = ct;
        backendPid = connection.ProcessID;
    }

    public static async Task<SqlSession> Open(string passfile, string ca, CancellationToken ct)
    {
        // Explicit settings only. No connection string from app config/environment,
        // no pooling/multiplexing, password fallback, host failover or retries.
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = Policy.Host, Port = 5432, Database = Policy.Database, Username = Policy.Role,
            Passfile = passfile, RootCertificate = ca, SslMode = SslMode.VerifyFull,
            GssEncryptionMode = GssEncryptionMode.Disable, Pooling = false, Multiplexing = false,
            Timeout = 15, CommandTimeout = 30, IncludeErrorDetail = false, PersistSecurityInfo = false,
            ApplicationName = "game-library-neon-qa-migration-operator",
            Options = "-c search_path=public -c default_transaction_read_only=on -c statement_timeout=30000 -c lock_timeout=2000 -c idle_in_transaction_session_timeout=30000"
        };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try { await connection.OpenAsync(ct); return new SqlSession(connection, ct); }
        catch { await connection.DisposeAsync(); throw; }
    }

    void SameSession() => Policy.Require(connection.State == System.Data.ConnectionState.Open
        && connection.ProcessID == backendPid, "physical-session-changed");

    public async Task Command(string sql)
    {
        SameSession();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    async Task<object?> Scalar(string sql, CancellationToken token)
    {
        SameSession();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(token);
    }

    public async Task<bool> TryLock()
    {
        SameSession();
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", Policy.LockKey);
        locked = (bool)(await command.ExecuteScalarAsync(ct))!;
        return locked;
    }

    async Task<T> ReadOnly<T>(Func<Task<T>> query)
    {
        await Command("BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;");
        try { return await query(); }
        finally
        {
            using var rollbackBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Scalar("ROLLBACK;", rollbackBudget.Token);
        }
    }

    public Task<JsonObject> ReadState() => ReadOnly(async () => JsonNode.Parse((string)(await Scalar(StateSql, ct))!)!.AsObject());

    public async Task VerifyApplied(string sql)
    {
        await ReadOnly(async () =>
        {
            var state = JsonNode.Parse((string)(await Scalar(StateSql, ct))!)!.AsObject();
            // Compare all original identity/security fields, allowing only the
            // expected new relations and their automatically created row types.
            Policy.Require(state["types"]!.GetValue<long>() == Policy.Tables.Length * 2L, "post-row-type-count");
            state["relations"] = 0L; state["types"] = 0L;
            Policy.EmptyDatabase(state);
            var tables = JsonNode.Parse((string)(await Scalar("""
                SELECT COALESCE(json_agg(relname ORDER BY relname), '[]')::text
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind='r'
                """, ct))!)!.AsArray().Select(x => x!.GetValue<string>());
            Policy.Require(tables.SequenceEqual(Policy.Tables.Order()), "post-table-set");
            var unexpected = (long)(await Scalar("""
                SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND (c.relkind NOT IN ('r','i') OR pg_get_userbyid(c.relowner) <> current_user)
                """, ct))!;
            Policy.Require(unexpected == 0, "post-object-kind-or-owner");
            var history = JsonNode.Parse((string)(await Scalar("""
                SELECT COALESCE(json_agg("MigrationId" || ':' || "ProductVersion" ORDER BY "MigrationId"), '[]')::text
                FROM public."__EFMigrationsHistory"
                """, ct))!)!.AsArray().Select(x => x!.GetValue<string>());
            Policy.Require(history.SequenceEqual(Policy.Migrations.Select(m => m + ":10.0.11")), "post-history");
            var genres = JsonNode.Parse((string)(await Scalar("SELECT COALESCE(json_agg(id::text || ':' || name ORDER BY id), '[]')::text FROM public.genres", ct))!)!
                .AsArray().Select(x => x!.GetValue<string>());
            var expectedGenres = Regex.Matches(sql, @"INSERT INTO genres \(id, name\)\s+VALUES \('([^']+)', '([^']+)'\);")
                .Select(m => m.Groups[1].Value + ":" + m.Groups[2].Value).Order();
            Policy.Require(expectedGenres.Count() == 15 && genres.SequenceEqual(expectedGenres), "post-genre-seeds");
            foreach (var table in Policy.Tables.Except(new[] { "genres", "__EFMigrationsHistory" }))
                Policy.Require(!(bool)(await Scalar($"SELECT EXISTS (SELECT 1 FROM public.\"{table}\" LIMIT 1)", ct))!, "post-unexpected-application-data");
            Policy.Require(!(bool)(await Scalar("""
                SELECT EXISTS (SELECT 1 FROM pg_class c, LATERAL aclexplode(COALESCE(c.relacl, acldefault('r', c.relowner))) a
                WHERE c.oid='public.cover_uploads'::regclass AND a.grantee=0)
                """, ct))!, "post-cover-public-privileges");
            return true;
        });
    }

    public async Task Cleanup()
    {
        if (connection.State != System.Data.ConnectionState.Open) return; // Dispose closes broken sessions too.
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // ROLLBACK is best-effort cleanup, NEVER proof that a previous COMMIT failed.
        await Scalar("ROLLBACK;", budget.Token);
        if (locked)
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            command.Parameters.AddWithValue("key", Policy.LockKey);
            Policy.Require((bool)(await command.ExecuteScalarAsync(budget.Token))!, "session-unlock-failed");
            locked = false;
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    // Catalog-only snapshot. Counts, not arbitrary object names/row contents, are
    // exposed for drift rejection; no server files, password catalogs or OS SQL.
    internal const string StateSql = """
        WITH user_ns AS (
          SELECT oid,nspname,nspowner FROM pg_namespace
          WHERE nspname !~ '^pg_' AND nspname <> 'information_schema'
        ), me AS (SELECT * FROM pg_roles WHERE rolname=current_user)
        SELECT json_build_object(
          'database',current_database(), 'currentUser',current_user, 'sessionUser',session_user,
          'version',current_setting('server_version_num')::int, 'recovery',pg_is_in_recovery(),
          'readOnly',current_setting('transaction_read_only'), 'defaultReadOnly',current_setting('default_transaction_read_only'),
          'isolation',current_setting('transaction_isolation'), 'searchPath',current_setting('search_path'),
          'databaseOwner',(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),
          'publicOwner',(SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname='public'),
          'schemaUsage',has_schema_privilege(current_user,'public','USAGE'),
          'schemaCreate',has_schema_privilege(current_user,'public','CREATE'),
          'databaseCreate',has_database_privilege(current_user,current_database(),'CREATE'),
          'canLogin',me.rolcanlogin,'inherits',me.rolinherit,'createDb',me.rolcreatedb,
          'createRole',me.rolcreaterole,'bypassRls',me.rolbypassrls,'superuser',me.rolsuper,
          'schemas',(SELECT COALESCE(json_agg(nspname ORDER BY nspname),'[]') FROM user_ns),
          'extensions',(SELECT COALESCE(json_agg(extname||':'||extversion||':'||nspname ORDER BY extname),'[]')
                        FROM pg_extension JOIN pg_namespace ON extnamespace=pg_namespace.oid),
          'relations',(SELECT count(*) FROM pg_class WHERE relnamespace IN (SELECT oid FROM user_ns)),
          'routines',(SELECT count(*) FROM pg_proc WHERE pronamespace IN (SELECT oid FROM user_ns)),
          'types',(SELECT count(*) FROM pg_type WHERE typnamespace IN (SELECT oid FROM user_ns)),
          'otherObjects',(
            (SELECT count(*) FROM pg_collation WHERE collnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_conversion WHERE connamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_operator WHERE oprnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_opclass WHERE opcnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_opfamily WHERE opfnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_ts_config WHERE cfgnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_ts_dict WHERE dictnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_ts_parser WHERE prsnamespace IN (SELECT oid FROM user_ns)) +
            (SELECT count(*) FROM pg_ts_template WHERE tmplnamespace IN (SELECT oid FROM user_ns))),
          'globalObjects',(
            (SELECT count(*) FROM pg_event_trigger) + (SELECT count(*) FROM pg_foreign_server) +
            (SELECT count(*) FROM pg_foreign_data_wrapper) + (SELECT count(*) FROM pg_publication) +
            (SELECT count(*) FROM pg_subscription WHERE subdbid=(SELECT oid FROM pg_database WHERE datname=current_database())) +
            (SELECT count(*) FROM pg_largeobject_metadata)),
          'supabaseRoles',(SELECT count(*) FROM pg_roles WHERE rolname IN ('anon','authenticated','service_role')),
          'defaultAcls',(SELECT COALESCE(json_agg(pg_get_userbyid(defaclrole)||':'||COALESCE(nspname,'(global)')||':'||defaclobjtype::text||':'||defaclacl::text
                              ORDER BY pg_get_userbyid(defaclrole),nspname,defaclobjtype),'[]')
                         FROM pg_default_acl LEFT JOIN pg_namespace ON defaclnamespace=pg_namespace.oid),
          'roleMemberships',(SELECT COALESCE(json_agg(pg_get_userbyid(roleid)||':'||pg_get_userbyid(member)||':'||admin_option::text||':'||inherit_option::text||':'||set_option::text
                                  ORDER BY roleid,member),'[]') FROM pg_auth_members
                             WHERE member=(SELECT oid FROM me)),
          'backendSsl',(SELECT ssl FROM pg_stat_ssl WHERE pid=pg_backend_pid())
        )::text FROM me
        """;
}
