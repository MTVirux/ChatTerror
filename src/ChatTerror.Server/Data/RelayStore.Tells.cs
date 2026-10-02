using System.Text.Json;
using ChatTerror.Protocol;

namespace ChatTerror.Server.Data;

public sealed partial class RelayStore
{
    private const string TellSchema = """
        CREATE TABLE IF NOT EXISTS tell_characters(
            hash TEXT PRIMARY KEY,
            install_id TEXT NOT NULL,
            friends TEXT NOT NULL,
            updated INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS tell_characters_install ON tell_characters(install_id);
        CREATE TABLE IF NOT EXISTS tell_bundles(
            install_id TEXT PRIMARY KEY,
            bundle TEXT NOT NULL,
            signature TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS tell_queue(
            id TEXT NOT NULL,
            install_id TEXT NOT NULL,
            target TEXT NOT NULL,
            sender TEXT NOT NULL,
            sender_key TEXT NOT NULL,
            envelope TEXT NOT NULL,
            created INTEGER NOT NULL,
            PRIMARY KEY(id, install_id, target));
        CREATE INDEX IF NOT EXISTS tell_queue_target ON tell_queue(install_id, target, created);
        """;

    // The relay can't prove who is logged in as a character, so a character only moves to another install once its
    // current install has been gone for TellOwnerTtl. Returns false when it belongs to an active install.
    public bool SetTellCharacter(string installId, string hash, IReadOnlyCollection<string> friends)
    {
        lock (tellOwnerLock)
        {
            var owner = TellCharacterOwner(hash);
            if (owner != null && owner != installId)
            {
                var ownerSeen = QuerySingle<long?>("SELECT last_seen FROM installs WHERE id = $id", reader => reader.GetInt64(0), ("$id", owner));
                if (ownerSeen > Now - (long)Limits.TellOwnerTtl.TotalMilliseconds)
                    return false;
            }

            Execute("""
                INSERT INTO tell_characters(hash, install_id, friends, updated) VALUES($hash, $install, $friends, $now)
                ON CONFLICT(hash) DO UPDATE SET install_id = excluded.install_id, friends = excluded.friends, updated = excluded.updated
                """,
                ("$hash", hash), ("$install", installId), ("$friends", JsonSerializer.Serialize(friends)), ("$now", Now));
            return true;
        }
    }

    public string? InstallPublicKey(string installId) =>
        QuerySingle("SELECT public_key FROM installs WHERE id = $id", reader => reader.GetString(0), ("$id", installId));

    public int CountTellCharacters(string installId) =>
        QuerySingle("SELECT COUNT(*) FROM tell_characters WHERE install_id = $install", reader => reader.GetInt32(0), ("$install", installId));

    // Bundles are only handed out for the install's own characters and their friends, so the relay can't be used to probe content ids.
    public bool CanSeeTellCharacter(string installId, string hash)
    {
        if (TellCharacterOwner(hash) == installId)
            return true;
        var lists = Query("SELECT friends FROM tell_characters WHERE install_id = $install", reader => reader.GetString(0), ("$install", installId));
        return lists.Any(friends => (JsonSerializer.Deserialize<List<string>>(friends) ?? []).Contains(hash));
    }

    public void DeleteTellCharacters(string installId) =>
        Execute("DELETE FROM tell_characters WHERE install_id = $install", ("$install", installId));

    public string? TellCharacterOwner(string hash) =>
        QuerySingle("SELECT install_id FROM tell_characters WHERE hash = $hash", reader => reader.GetString(0), ("$hash", hash));

    public bool IsTellFriend(string hash, string friendHash)
    {
        var friends = QuerySingle("SELECT friends FROM tell_characters WHERE hash = $hash", reader => reader.GetString(0), ("$hash", hash));
        return friends != null && (JsonSerializer.Deserialize<List<string>>(friends) ?? []).Contains(friendHash);
    }

    public List<string> RegisteredTellCharacters(IEnumerable<string> hashes) =>
        hashes.Distinct().Where(hash => TellCharacterOwner(hash) != null).ToList();

    public void SetTellBundle(string installId, SignedTellBundle bundle) =>
        Execute("""
            INSERT INTO tell_bundles(install_id, bundle, signature) VALUES($install, $bundle, $signature)
            ON CONFLICT(install_id) DO UPDATE SET bundle = excluded.bundle, signature = excluded.signature
            """,
            ("$install", installId), ("$bundle", bundle.Bundle), ("$signature", bundle.Signature));

    public SignedTellBundle? FindTellBundle(string installId) =>
        QuerySingle("SELECT bundle, signature FROM tell_bundles WHERE install_id = $install",
            reader => new SignedTellBundle(reader.GetString(0), reader.GetString(1)), ("$install", installId));

    // Only the newest copies per target are kept, so a flood can't grow the queue without bound.
    public void EnqueueTell(string id, string installId, string target, string sender, string senderKey, string envelope)
    {
        Execute("""
            INSERT OR IGNORE INTO tell_queue(id, install_id, target, sender, sender_key, envelope, created)
            VALUES($id, $install, $target, $sender, $senderKey, $envelope, $now)
            """,
            ("$id", id), ("$install", installId), ("$target", target), ("$sender", sender), ("$senderKey", senderKey),
            ("$envelope", envelope), ("$now", Now));
        Execute("""
            DELETE FROM tell_queue WHERE install_id = $install AND target = $target AND rowid NOT IN (
                SELECT rowid FROM tell_queue WHERE install_id = $install AND target = $target ORDER BY created DESC, rowid DESC LIMIT $max)
            """,
            ("$install", installId), ("$target", target), ("$max", Limits.MaxQueuedTells));
    }

    public List<TellFrame> PendingTells(string installId, string target) =>
        Query("SELECT id, sender, envelope, sender_key FROM tell_queue WHERE install_id = $install AND target = $target ORDER BY created, rowid",
            reader => new TellFrame(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)),
            ("$install", installId), ("$target", target));

    public void AckTells(string installId, string target, IEnumerable<string> ids)
    {
        foreach (var id in ids.Distinct())
            Execute("DELETE FROM tell_queue WHERE id = $id AND install_id = $install AND target = $target",
                ("$id", id), ("$install", installId), ("$target", target));
    }

    public int DeleteExpiredTells() =>
        Execute("DELETE FROM tell_queue WHERE created <= $cutoff", ("$cutoff", Now - (long)Limits.TellTtl.TotalMilliseconds));

    private void DeleteTellData(string installId)
    {
        DeleteTellCharacters(installId);
        Execute("DELETE FROM tell_bundles WHERE install_id = $install", ("$install", installId));
        Execute("DELETE FROM tell_queue WHERE install_id = $install", ("$install", installId));
    }
}
