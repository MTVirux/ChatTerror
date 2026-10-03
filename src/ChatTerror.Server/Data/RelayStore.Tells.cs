using ChatTerror.Protocol;

namespace ChatTerror.Server.Data;

public sealed partial class RelayStore
{
    private const string TellSchema = """
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

    public string? InstallPublicKey(string installId) =>
        QuerySingle("SELECT public_key FROM installs WHERE id = $id", reader => reader.GetString(0), ("$id", installId));

    public void SetTellBundle(string installId, SignedTellBundle bundle) =>
        Execute("""
            INSERT INTO tell_bundles(install_id, bundle, signature) VALUES($install, $bundle, $signature)
            ON CONFLICT(install_id) DO UPDATE SET bundle = excluded.bundle, signature = excluded.signature
            """,
            ("$install", installId), ("$bundle", bundle.Bundle), ("$signature", bundle.Signature));

    public void DeleteTellBundle(string installId) =>
        Execute("DELETE FROM tell_bundles WHERE install_id = $install", ("$install", installId));

    public SignedTellBundle? FindTellBundle(string installId) =>
        QuerySingle("SELECT bundle, signature FROM tell_bundles WHERE install_id = $install",
            reader => new SignedTellBundle(reader.GetString(0), reader.GetString(1)), ("$install", installId));

    // The sender's own install, or a friend of it.
    private const string FromFriendOrSelf = """
        (sender = install_id OR EXISTS(SELECT 1 FROM friends
            WHERE (install_a = sender AND install_b = install_id) OR (install_a = install_id AND install_b = sender)))
        """;

    // Returns false, queuing nothing, when the sender is no longer a friend. The lock keeps an unfriend from landing
    // between the check and the insert, which would leave the tell queued.
    public bool EnqueueTell(string id, string installId, string target, string sender, string senderKey, string envelope)
    {
        lock (friendLock)
        {
            if (sender != installId && !AreFriends(sender, installId))
                return false;
            QueueTell(id, installId, target, sender, senderKey, envelope);
            return true;
        }
    }

    // Only the newest copies per target are kept, so a flood can't grow the queue without bound, and each sender only
    // gets a share of them, so one sender can't push out everyone else's tells.
    private void QueueTell(string id, string installId, string target, string sender, string senderKey, string envelope)
    {
        Execute("""
            INSERT OR IGNORE INTO tell_queue(id, install_id, target, sender, sender_key, envelope, created)
            VALUES($id, $install, $target, $sender, $senderKey, $envelope, $now)
            """,
            ("$id", id), ("$install", installId), ("$target", target), ("$sender", sender), ("$senderKey", senderKey),
            ("$envelope", envelope), ("$now", Now));
        Execute("""
            DELETE FROM tell_queue WHERE install_id = $install AND target = $target AND sender = $sender AND rowid NOT IN (
                SELECT rowid FROM tell_queue WHERE install_id = $install AND target = $target AND sender = $sender
                ORDER BY created DESC, rowid DESC LIMIT $max)
            """,
            ("$install", installId), ("$target", target), ("$sender", sender), ("$max", maxQueuedTellsPerSender));
        Execute("""
            DELETE FROM tell_queue WHERE install_id = $install AND target = $target AND rowid NOT IN (
                SELECT rowid FROM tell_queue WHERE install_id = $install AND target = $target ORDER BY created DESC, rowid DESC LIMIT $max)
            """,
            ("$install", installId), ("$target", target), ("$max", Limits.MaxQueuedTells));
    }

    // Tells from senders who are no longer friends are dropped rather than delivered.
    public List<TellFrame> PendingTells(string installId, string target)
    {
        Execute($"DELETE FROM tell_queue WHERE install_id = $install AND target = $target AND NOT {FromFriendOrSelf}",
            ("$install", installId), ("$target", target));
        return Query("SELECT id, sender, envelope, sender_key FROM tell_queue WHERE install_id = $install AND target = $target ORDER BY created, rowid",
            reader => new TellFrame(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)),
            ("$install", installId), ("$target", target));
    }

    public void AckTells(string installId, string target, IEnumerable<string> ids)
    {
        foreach (var id in ids.Distinct())
            Execute("DELETE FROM tell_queue WHERE id = $id AND install_id = $install AND target = $target",
                ("$id", id), ("$install", installId), ("$target", target));
    }

    public void DeleteQueuedTellsBetween(string a, string b) =>
        Execute("DELETE FROM tell_queue WHERE (install_id = $a AND sender = $b) OR (install_id = $b AND sender = $a)", ("$a", a), ("$b", b));

    public int DeleteExpiredTells() =>
        Execute("DELETE FROM tell_queue WHERE created <= $cutoff", ("$cutoff", Now - (long)Limits.TellTtl.TotalMilliseconds));

    private void DeleteTellData(string installId)
    {
        DeleteTellBundle(installId);
        Execute("DELETE FROM tell_queue WHERE install_id = $install", ("$install", installId));
    }
}
