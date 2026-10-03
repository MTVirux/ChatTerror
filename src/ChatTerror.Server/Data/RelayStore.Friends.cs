using ChatTerror.Protocol;
using ChatTerror.Server.Auth;
using Microsoft.Data.Sqlite;

namespace ChatTerror.Server.Data;

public sealed record FriendInviteRecord(
    string Id, string InstallId, string InstallPublicKey, string Scope, string Tag, long ExpiresAt,
    string? ClaimInstall, string? ClaimPublicKey, string? ClaimSealed);

public enum FriendInviteResult
{
    Ok,
    NotFound,
    AlreadyClaimed,
    SelfInvite,
    AlreadyFriends,
    TooManyFriends,
}

public sealed partial class RelayStore
{
    private const string FriendSchema = """
        DROP TABLE IF EXISTS tell_characters;
        CREATE TABLE IF NOT EXISTS friend_invites(id TEXT PRIMARY KEY, install_id TEXT NOT NULL, scope TEXT NOT NULL, tag TEXT NOT NULL,
            created INTEGER NOT NULL, claim_install TEXT, claim_sealed TEXT);
        CREATE INDEX IF NOT EXISTS friend_invites_install ON friend_invites(install_id);
        CREATE TABLE IF NOT EXISTS friends(install_a TEXT NOT NULL, install_b TEXT NOT NULL, created INTEGER NOT NULL, PRIMARY KEY(install_a, install_b));
        CREATE INDEX IF NOT EXISTS friends_b ON friends(install_b);
        CREATE TABLE IF NOT EXISTS friend_profiles(owner_install TEXT NOT NULL, friend_install TEXT NOT NULL, envelope TEXT NOT NULL,
            PRIMARY KEY(owner_install, friend_install));
        """;

    // A claim whose install is gone reads as no claim.
    private const string InviteSelect = """
        SELECT f.id, f.install_id, i.public_key, f.scope, f.tag, f.created, c.id, c.public_key, f.claim_sealed FROM friend_invites f
        JOIN installs i ON i.id = f.install_id
        LEFT JOIN installs c ON c.id = f.claim_install
        """;

    private const string UnexpiredInvite = "f.created > $cutoff";

    private long InviteCutoff => Now - (long)Limits.FriendInviteTtl.TotalMilliseconds;

    private static (string, string) Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);

    public (string Id, long ExpiresAt) CreateFriendInvite(string installId, string scope, string tag)
    {
        var now = Now;
        while (true)
        {
            var id = PairingCodes.Generate(PairingCodes.FriendInviteLength);
            var inserted = Execute("INSERT OR IGNORE INTO friend_invites(id, install_id, scope, tag, created) VALUES($id, $install, $scope, $tag, $now)",
                ("$id", id), ("$install", installId), ("$scope", scope), ("$tag", tag), ("$now", now));
            if (inserted == 1)
                return (id, now + (long)Limits.FriendInviteTtl.TotalMilliseconds);
        }
    }

    // Null when unknown or expired.
    public FriendInviteRecord? FindFriendInvite(string id) =>
        QuerySingle($"{InviteSelect} WHERE f.id = $id AND {UnexpiredInvite}", ReadInvite, ("$id", id), ("$cutoff", InviteCutoff));

    public int CountFriendInvites(string installId) =>
        QuerySingle($"SELECT COUNT(*) FROM friend_invites f WHERE f.install_id = $install AND {UnexpiredInvite}",
            reader => reader.GetInt32(0), ("$install", installId), ("$cutoff", InviteCutoff));

    public List<FriendInviteRecord> ListFriendInvites(string installId) =>
        Query($"{InviteSelect} WHERE f.install_id = $install AND {UnexpiredInvite} ORDER BY f.created",
            ReadInvite, ("$install", installId), ("$cutoff", InviteCutoff));

    // Returns the inviter on success, so it can be told.
    public (FriendInviteResult Status, string? Inviter) ClaimFriendInvite(string id, string installId, string sealedClaim)
    {
        lock (friendLock)
        {
            if (FindFriendInvite(id) is not { } invite)
                return (FriendInviteResult.NotFound, null);
            if (invite.ClaimInstall != null)
                return (FriendInviteResult.AlreadyClaimed, null);
            if (invite.InstallId == installId)
                return (FriendInviteResult.SelfInvite, null);
            if (AreFriends(invite.InstallId, installId))
                return (FriendInviteResult.AlreadyFriends, null);
            if (CountFriends(invite.InstallId) >= Limits.MaxPairedFriends || CountFriends(installId) >= Limits.MaxPairedFriends)
                return (FriendInviteResult.TooManyFriends, null);

            Execute("UPDATE friend_invites SET claim_install = $claimant, claim_sealed = $sealed WHERE id = $id",
                ("$id", id), ("$claimant", installId), ("$sealed", sealedClaim));
            return (FriendInviteResult.Ok, invite.InstallId);
        }
    }

    // Returns the claimant on success, so both sides can be told.
    public (FriendInviteResult Status, string? Claimant) AcceptFriendInvite(string id, string ownerInstall)
    {
        lock (friendLock)
        {
            if (FindFriendInvite(id) is not { ClaimInstall: { } claimant } invite || invite.InstallId != ownerInstall)
                return (FriendInviteResult.NotFound, null);
            if (!AreFriends(ownerInstall, claimant)
                && (CountFriends(ownerInstall) >= Limits.MaxPairedFriends || CountFriends(claimant) >= Limits.MaxPairedFriends))
                return (FriendInviteResult.TooManyFriends, null);

            AddFriend(ownerInstall, claimant);
            Execute("DELETE FROM friend_invites WHERE id = $id", ("$id", id));
            return (FriendInviteResult.Ok, claimant);
        }
    }

    // Expired invites can still be cancelled. Null when the invite is not the owner's.
    public FriendInviteRecord? DeleteFriendInvite(string id, string ownerInstall)
    {
        lock (friendLock)
        {
            var invite = QuerySingle($"{InviteSelect} WHERE f.id = $id AND f.install_id = $install", ReadInvite, ("$id", id), ("$install", ownerInstall));
            if (invite != null)
                Execute("DELETE FROM friend_invites WHERE id = $id", ("$id", id));
            return invite;
        }
    }

    public int DeleteExpiredFriendInvites() =>
        Execute("DELETE FROM friend_invites WHERE created <= $cutoff", ("$cutoff", InviteCutoff));

    public void AddFriend(string a, string b)
    {
        var (first, second) = Pair(a, b);
        Execute("INSERT OR IGNORE INTO friends(install_a, install_b, created) VALUES($a, $b, $now)", ("$a", first), ("$b", second), ("$now", Now));
    }

    public bool AreFriends(string a, string b)
    {
        var (first, second) = Pair(a, b);
        return QuerySingle("SELECT 1 FROM friends WHERE install_a = $a AND install_b = $b", reader => true, ("$a", first), ("$b", second));
    }

    public int CountFriends(string installId) =>
        QuerySingle("SELECT COUNT(*) FROM friends WHERE install_a = $install OR install_b = $install", reader => reader.GetInt32(0), ("$install", installId));

    // Profile is the envelope the friend uploaded for installId.
    public List<(string InstallId, string PublicKey, string? Profile)> ListFriends(string installId) =>
        Query("""
            SELECT i.id, i.public_key, p.envelope FROM friends f
            JOIN installs i ON i.id = CASE WHEN f.install_a = $install THEN f.install_b ELSE f.install_a END
            LEFT JOIN friend_profiles p ON p.owner_install = i.id AND p.friend_install = $install
            WHERE f.install_a = $install OR f.install_b = $install
            ORDER BY f.created, i.id
            """,
            reader => (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)), ("$install", installId));

    public void SetFriendProfile(string owner, string friend, string envelope) =>
        Execute("""
            INSERT INTO friend_profiles(owner_install, friend_install, envelope) VALUES($owner, $friend, $envelope)
            ON CONFLICT(owner_install, friend_install) DO UPDATE SET envelope = excluded.envelope
            """,
            ("$owner", owner), ("$friend", friend), ("$envelope", envelope));

    // Returns whether they were friends.
    public bool RemoveFriend(string a, string b)
    {
        lock (friendLock)
        {
            var (first, second) = Pair(a, b);
            var removed = Execute("DELETE FROM friends WHERE install_a = $a AND install_b = $b", ("$a", first), ("$b", second)) == 1;
            Execute("""
                DELETE FROM friend_profiles WHERE (owner_install = $a AND friend_install = $b) OR (owner_install = $b AND friend_install = $a)
                """,
                ("$a", a), ("$b", b));
            DeleteQueuedTellsBetween(a, b);
            return removed;
        }
    }

    // Also drops invites the install claimed, since the claimant can never be accepted.
    public void DeleteFriendData(string installId)
    {
        lock (friendLock)
        {
            Execute("DELETE FROM friend_invites WHERE install_id = $install OR claim_install = $install", ("$install", installId));
            Execute("DELETE FROM friends WHERE install_a = $install OR install_b = $install", ("$install", installId));
            Execute("DELETE FROM friend_profiles WHERE owner_install = $install OR friend_install = $install", ("$install", installId));
            Execute("DELETE FROM tell_queue WHERE sender = $install", ("$install", installId));
        }
    }

    private static FriendInviteRecord ReadInvite(SqliteDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetInt64(5) + (long)Limits.FriendInviteTtl.TotalMilliseconds,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(6) ? null : reader.GetString(8));
}
