using System.Text.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;

namespace ChatTerror.Server.Relay;

public sealed partial class RelaySocketHandler
{
    private const int MaxTellIdLength = 64;

    private static string TellTarget(Conn conn) => conn.Role == RelayRoles.Plugin ? TellTargets.Plugin : conn.Id;

    private void DeliverQueuedTells(Conn conn)
    {
        foreach (var tell in store.PendingTells(conn.InstallId, TellTarget(conn)))
            conn.Send(tell);
    }

    private void AckTells(Conn conn, TellAckFrame ack) =>
        store.AckTells(conn.InstallId, TellTarget(conn), ack.Ids.Take(Limits.MaxQueuedTells));

    private void HandleTellSend(Conn conn, TellSendFrame frame)
    {
        var error = CheckTell(conn, frame, out var recipientInstall, out var recipient);
        if (error != null)
        {
            conn.Send(new TellResultFrame(frame.Id, false, error));
            return;
        }

        var senderKey = store.InstallPublicKey(conn.InstallId)!;
        foreach (var copy in frame.Copies)
        {
            var install = copy.Self ? conn.InstallId : recipientInstall!;
            if (copy.Self && copy.Target == TellTarget(conn))
                continue;

            store.EnqueueTell(frame.Id, install, copy.Target, conn.InstallId, senderKey, copy.Envelope);
            if (Online(install, copy.Target) is { } online)
            {
                online.Send(new TellFrame(frame.Id, conn.InstallId, copy.Envelope, senderKey));
            }
            else if (!copy.Self && recipient!.Entries.Any(e => e.Target == copy.Target && e.Push)
                && store.FindDevice(copy.Target) is { Status: DeviceStatus.Active, Push: { } subscription } device
                && device.InstallId == install
                && tellLimiter.TryPush(conn.InstallId, device.Id))
            {
                var body = JsonSerializer.Serialize(new { t = "tell", i = frame.Id, f = conn.InstallId, e = copy.Envelope, k = senderKey, d = device.Id });
                _ = PushAsync(device, subscription, body);
            }
        }

        conn.Send(new TellResultFrame(frame.Id, true));
    }

    private string? CheckTell(Conn conn, TellSendFrame frame, out string? recipientInstall, out TellBundle? recipient)
    {
        recipientInstall = null;
        recipient = null;
        if (frame.Id.Length is 0 or > MaxTellIdLength)
            return TellErrors.BadCopies;
        if (!store.AreFriends(conn.InstallId, frame.To))
            return TellErrors.NotPaired;

        recipientInstall = frame.To;
        recipient = store.FindTellBundle(recipientInstall) is { } signed ? TellBundles.Read(signed) : null;
        if (recipient == null)
            return TellErrors.NotChatTerror;

        var own = store.FindTellBundle(conn.InstallId) is { } ownSigned ? TellBundles.Read(ownSigned) : null;
        if (frame.Copies.Count is 0 || frame.Copies.Count > 2 * (Limits.MaxDevices + 1)
            || frame.Copies.DistinctBy(copy => (copy.Self, copy.Target)).Count() != frame.Copies.Count)
            return TellErrors.BadCopies;

        foreach (var copy in frame.Copies)
        {
            var bundle = copy.Self ? own : recipient;
            if (bundle == null || copy.Envelope.Length is 0 or > Limits.MaxTellEnvelopeChars || bundle.Entries.All(e => e.Target != copy.Target))
                return TellErrors.BadCopies;
        }
        return tellLimiter.TryTell(conn.InstallId, recipientInstall) ? null : TellErrors.RateLimited;
    }

    private Conn? Online(string installId, string target) =>
        target == TellTargets.Plugin
            ? registry.Plugin(installId)
            : registry.Device(target) is { } device && device.InstallId == installId ? device : null;
}
