using ChatTerror.Protocol;
using ChatTerror.Server.Auth;
using ChatTerror.Server.Push;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Data;

public static class DeviceStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
}

public sealed record InstallRecord(string Id, string PublicKey);

public sealed record PairingRecord(string Code, string InstallId, string PluginPublicKey, long ExpiresAt);

public sealed record DeviceRecord(string Id, string InstallId, string Name, string PublicKey, string Status, long Created, long LastSeen, PushSubscriptionRecord? Push);

public enum ClaimStatus
{
    Ok,
    NotFound,
    TooManyDevices,
}

public sealed record ClaimResult(ClaimStatus Status, DeviceRecord? Device = null, string? Token = null);

public sealed partial class RelayStore
{
    private const string Schema = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS installs(
            id TEXT PRIMARY KEY,
            token_hash BLOB NOT NULL,
            public_key TEXT NOT NULL,
            created INTEGER NOT NULL,
            last_seen INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS devices(
            id TEXT PRIMARY KEY,
            install_id TEXT NOT NULL,
            token_hash BLOB NOT NULL,
            name TEXT NOT NULL,
            public_key TEXT NOT NULL,
            status TEXT NOT NULL,
            created INTEGER NOT NULL,
            last_seen INTEGER NOT NULL,
            push_endpoint TEXT,
            push_p256dh TEXT,
            push_auth TEXT);
        CREATE INDEX IF NOT EXISTS devices_install ON devices(install_id);
        CREATE TABLE IF NOT EXISTS pairings(
            code TEXT PRIMARY KEY,
            install_id TEXT NOT NULL,
            expires INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS pairings_install ON pairings(install_id);
        CREATE INDEX IF NOT EXISTS devices_last_seen ON devices(last_seen);
        CREATE INDEX IF NOT EXISTS installs_created ON installs(created);
        """;

    private const string InstallIndexes = "CREATE INDEX IF NOT EXISTS installs_last_seen ON installs(last_seen)";

    // An install is stale when it has not connected within InstallTtl and has no devices, or within InactiveInstallTtl at all.
    private const string StaleInstall = """
        (installs.last_seen <= $emptyCutoff AND NOT EXISTS(SELECT 1 FROM devices WHERE devices.install_id = installs.id))
        OR installs.last_seen <= $inactiveCutoff
        """;

    private const string DeviceColumns = "id, install_id, name, public_key, status, created, last_seen, push_endpoint, push_p256dh, push_auth";

    private readonly string connectionString;
    private readonly TimeProvider time;
    private readonly TimeSpan pendingDeviceTtl;
    private readonly TimeSpan installTtl;
    private readonly TimeSpan inactiveInstallTtl;
    private readonly TimeSpan inactiveDeviceTtl;
    private readonly int maxInstallsPerDay;
    private readonly Lock claimLock = new();
    private readonly Lock installLock = new();

    public RelayStore(IOptions<RelayOptions> options, TimeProvider time)
    {
        this.time = time;
        pendingDeviceTtl = options.Value.PendingDeviceTtl;
        installTtl = options.Value.InstallTtl;
        inactiveInstallTtl = options.Value.InactiveInstallTtl;
        inactiveDeviceTtl = options.Value.InactiveDeviceTtl;
        maxInstallsPerDay = options.Value.MaxInstallsPerDay;

        var path = Path.GetFullPath(options.Value.DbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        Execute(Schema);
        Execute(TellSchema);
        AddInstallLastSeen();
        Execute(InstallIndexes);
    }

    // Databases created before installs tracked last_seen.
    private void AddInstallLastSeen()
    {
        var columns = Query("PRAGMA table_info(installs)", reader => reader.GetString(1));
        if (columns.Contains("last_seen"))
            return;
        Execute("ALTER TABLE installs ADD COLUMN last_seen INTEGER NOT NULL DEFAULT 0");
        Execute("UPDATE installs SET last_seen = created");
    }

    private long Now => time.GetUtcNow().ToUnixTimeMilliseconds();

    // Returns null once MaxInstallsPerDay installs were created in the last 24 hours.
    public (string Id, string Token)? CreateInstall(string publicKey)
    {
        lock (installLock)
        {
            var now = Now;
            var since = now - (long)TimeSpan.FromDays(1).TotalMilliseconds;
            var recent = QuerySingle("SELECT COUNT(*) FROM installs WHERE created > $since", reader => reader.GetInt32(0), ("$since", since));
            if (recent >= maxInstallsPerDay)
                return null;

            var id = Tokens.NewId();
            var (token, hash) = Tokens.Create(Tokens.InstallPrefix, id);
            Execute("INSERT INTO installs(id, token_hash, public_key, created, last_seen) VALUES($id, $hash, $key, $now, $now)",
                ("$id", id), ("$hash", hash), ("$key", publicKey), ("$now", now));
            return (id, token);
        }
    }

    public InstallRecord? FindInstallByToken(string token)
    {
        if (!Tokens.TryParse(token, Tokens.InstallPrefix, out var id, out var secret))
            return null;

        var found = QuerySingle<(byte[] Hash, string PublicKey)?>("SELECT token_hash, public_key FROM installs WHERE id = $id",
            reader => ((byte[])reader[0], reader.GetString(1)), ("$id", id));
        return found is { } install && Tokens.Matches(install.Hash, secret) ? new InstallRecord(id, install.PublicKey) : null;
    }

    public void TouchInstall(string id) =>
        Execute("UPDATE installs SET last_seen = $now WHERE id = $id", ("$id", id), ("$now", Now));

    // Replaces any earlier code, so an install has at most one live code.
    public (string Code, long ExpiresAt) CreatePairing(string installId)
    {
        var expires = time.GetUtcNow().Add(Limits.PairingTtl).ToUnixTimeMilliseconds();
        Execute("DELETE FROM pairings WHERE install_id = $install", ("$install", installId));
        while (true)
        {
            var code = PairingCodes.Generate();
            var inserted = Execute("INSERT OR IGNORE INTO pairings(code, install_id, expires) VALUES($code, $install, $expires)",
                ("$code", code), ("$install", installId), ("$expires", expires));
            if (inserted == 1)
                return (code, expires);
        }
    }

    public PairingRecord? FindPairing(string code) =>
        QuerySingle("""
            SELECT p.code, p.install_id, i.public_key, p.expires FROM pairings p
            JOIN installs i ON i.id = p.install_id
            WHERE p.code = $code AND p.expires > $now
            """,
            reader => new PairingRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)),
            ("$code", code), ("$now", Now));

    public bool ConsumePairing(string code) =>
        Execute("DELETE FROM pairings WHERE code = $code AND expires > $now", ("$code", code), ("$now", Now)) == 1;

    public ClaimResult ClaimPairing(string code, string devicePublicKey, string deviceName)
    {
        lock (claimLock)
        {
            var pairing = FindPairing(code);
            if (pairing == null)
                return new ClaimResult(ClaimStatus.NotFound);
            if (CountDevices(pairing.InstallId) >= Limits.MaxDevices)
                return new ClaimResult(ClaimStatus.TooManyDevices);
            if (!ConsumePairing(code))
                return new ClaimResult(ClaimStatus.NotFound);

            var (device, token) = CreateDevice(pairing.InstallId, devicePublicKey, deviceName);
            return new ClaimResult(ClaimStatus.Ok, device, token);
        }
    }

    public int CountDevices(string installId) =>
        QuerySingle("SELECT COUNT(*) FROM devices WHERE install_id = $install", reader => reader.GetInt32(0), ("$install", installId));

    public int CountActiveDevices(string installId) =>
        QuerySingle("SELECT COUNT(*) FROM devices WHERE install_id = $install AND status = $active",
            reader => reader.GetInt32(0), ("$install", installId), ("$active", DeviceStatus.Active));

    public (DeviceRecord Device, string Token) CreateDevice(string installId, string publicKey, string name)
    {
        var id = Tokens.NewId();
        var (token, hash) = Tokens.Create(Tokens.DevicePrefix, id);
        var now = Now;
        Execute("""
            INSERT INTO devices(id, install_id, token_hash, name, public_key, status, created, last_seen)
            VALUES($id, $install, $hash, $name, $key, $status, $now, $now)
            """,
            ("$id", id), ("$install", installId), ("$hash", hash), ("$name", name), ("$key", publicKey),
            ("$status", DeviceStatus.Pending), ("$now", now));
        return (new DeviceRecord(id, installId, name, publicKey, DeviceStatus.Pending, now, now, null), token);
    }

    public DeviceRecord? FindDeviceByToken(string token)
    {
        if (!Tokens.TryParse(token, Tokens.DevicePrefix, out var id, out var secret))
            return null;

        var found = QuerySingle<(byte[] Hash, DeviceRecord Device)?>($"SELECT token_hash, {DeviceColumns} FROM devices WHERE id = $id",
            reader => ((byte[])reader[0], ReadDevice(reader, 1)), ("$id", id));
        return found is { } device && Tokens.Matches(device.Hash, secret) ? device.Device : null;
    }

    public DeviceRecord? FindDevice(string id) =>
        QuerySingle($"SELECT {DeviceColumns} FROM devices WHERE id = $id", reader => ReadDevice(reader, 0), ("$id", id));

    public List<DeviceRecord> ListDevices(string installId) =>
        Query($"SELECT {DeviceColumns} FROM devices WHERE install_id = $install ORDER BY created",
            reader => ReadDevice(reader, 0), ("$install", installId));

    public void SetDeviceStatus(string id, string status) =>
        Execute("UPDATE devices SET status = $status WHERE id = $id", ("$id", id), ("$status", status));

    public bool DeleteDevice(string id)
    {
        Execute("DELETE FROM tell_queue WHERE target = $id", ("$id", id));
        return Execute("DELETE FROM devices WHERE id = $id", ("$id", id)) == 1;
    }

    public void SetPush(string id, PushSubscriptionRecord push) =>
        Execute("UPDATE devices SET push_endpoint = $endpoint, push_p256dh = $p256dh, push_auth = $auth WHERE id = $id",
            ("$id", id), ("$endpoint", push.Endpoint), ("$p256dh", push.P256dh), ("$auth", push.Auth));

    // With an endpoint, only clears if it is still the stored one, so a fresh subscription survives a late failure.
    public void ClearPush(string id, string? endpoint = null) =>
        Execute("""
            UPDATE devices SET push_endpoint = NULL, push_p256dh = NULL, push_auth = NULL
            WHERE id = $id AND ($endpoint IS NULL OR push_endpoint = $endpoint)
            """,
            ("$id", id), ("$endpoint", endpoint));

    public void TouchLastSeen(string id) =>
        Execute("UPDATE devices SET last_seen = $now WHERE id = $id", ("$id", id), ("$now", Now));

    // Returns the pending devices that were removed, so their sockets and plugins can be told.
    public List<(string DeviceId, string InstallId)> DeleteExpired()
    {
        var now = Now;
        Execute("DELETE FROM pairings WHERE expires <= $now", ("$now", now));

        var cutoff = now - (long)pendingDeviceTtl.TotalMilliseconds;
        var candidates = Query("SELECT id, install_id FROM devices WHERE status = $pending AND created <= $cutoff",
            reader => (DeviceId: reader.GetString(0), InstallId: reader.GetString(1)),
            ("$pending", DeviceStatus.Pending), ("$cutoff", cutoff));
        return candidates
            .Where(device => Execute("DELETE FROM devices WHERE id = $id AND status = $pending",
                ("$id", device.DeviceId), ("$pending", DeviceStatus.Pending)) == 1)
            .ToList();
    }

    // Deletes devices that have not connected within the TTL and are not connected now.
    public List<(string DeviceId, string InstallId)> DeleteInactiveDevices(Func<string, bool> isConnected)
    {
        var cutoff = Now - (long)inactiveDeviceTtl.TotalMilliseconds;
        var candidates = Query("SELECT id, install_id FROM devices WHERE last_seen <= $cutoff",
            reader => (DeviceId: reader.GetString(0), InstallId: reader.GetString(1)), ("$cutoff", cutoff));
        return candidates
            .Where(device => !isConnected(device.DeviceId))
            .Where(device => Execute("DELETE FROM devices WHERE id = $id AND last_seen <= $cutoff",
                ("$id", device.DeviceId), ("$cutoff", cutoff)) == 1)
            .ToList();
    }

    // Deletes stale installs that are not connected now, with their devices and pairings.
    // Returns each deleted install with its devices, so their sockets can be closed.
    public List<(string InstallId, List<string> DeviceIds)> DeleteStaleInstalls(Func<string, bool> isConnected)
    {
        var now = Now;
        (string, object?)[] cutoffs =
        [
            ("$emptyCutoff", now - (long)installTtl.TotalMilliseconds),
            ("$inactiveCutoff", now - (long)inactiveInstallTtl.TotalMilliseconds),
        ];
        var candidates = Query($"SELECT id FROM installs WHERE {StaleInstall}", reader => reader.GetString(0), cutoffs);

        var deleted = new List<(string, List<string>)>();
        foreach (var id in candidates.Where(id => !isConnected(id)))
        {
            lock (claimLock)
            {
                if (Execute($"DELETE FROM installs WHERE id = $id AND ({StaleInstall})", [("$id", id), .. cutoffs]) == 0)
                    continue;
                var devices = Query("SELECT id FROM devices WHERE install_id = $id", reader => reader.GetString(0), ("$id", id));
                Execute("DELETE FROM devices WHERE install_id = $id", ("$id", id));
                Execute("DELETE FROM pairings WHERE install_id = $id", ("$id", id));
                DeleteTellData(id);
                deleted.Add((id, devices));
            }
        }
        return deleted;
    }

    private static DeviceRecord ReadDevice(SqliteDataReader reader, int offset)
    {
        var push = reader.IsDBNull(offset + 7)
            ? null
            : new PushSubscriptionRecord(reader.GetString(offset + 7), reader.GetString(offset + 8), reader.GetString(offset + 9));
        return new DeviceRecord(
            reader.GetString(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3),
            reader.GetString(offset + 4),
            reader.GetInt64(offset + 5),
            reader.GetInt64(offset + 6),
            push);
    }

    private SqliteCommand Command(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }

    private T? QuerySingle<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        return reader.Read() ? map(reader) : default;
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
            results.Add(map(reader));
        return results;
    }
}
