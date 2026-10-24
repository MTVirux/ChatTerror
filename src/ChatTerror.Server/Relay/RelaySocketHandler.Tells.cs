using System.Text.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Push;

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
        // Recipient copies go first, so an unfriend since CheckTell refuses the tell before any self copy is queued.
        // Plugin copies go last, so a deferred device push is recorded before the plugin can ask for it.
        foreach (var copy in frame.Copies.OrderBy(copy => copy.Self).ThenBy(copy => copy.Target == TellTargets.Plugin))
        {
            var install = copy.Self ? conn.InstallId : recipientInstall!;
            if (copy.Self && copy.Target == TellTarget(conn))
                continue;

            if (!store.EnqueueTell(frame.Id, install, copy.Target, conn.InstallId, senderKey, copy.Envelope))
            {
                conn.Send(new TellResultFrame(frame.Id, false, TellErrors.NotPaired));
                return;
            }
            var tell = new TellFrame(frame.Id, conn.InstallId, copy.Envelope, senderKey);
            if (Online(install, copy.Target) is { } online)
            {
                online.Send(tell);
            }
            else if (!copy.Self && recipient!.Entries.Any(e => e.Target == copy.Target && e.Push)
                && store.FindDevice(copy.Target) is { Status: DeviceStatus.Active, Push: { } subscription } device
                && device.InstallId == install
                && !TryDeferTellPush(install, device.Id, frame.Id)
                && tellLimiter.TryPush(conn.InstallId, device.Id))
            {
                PushTell(device, subscription, tell);
            }
        }

        conn.Send(new TellResultFrame(frame.Id, true));
    }

    // A plugin listing tellPush runs its away check on its own copy of the tell, then asks for each push with a tellPush frame.
    private bool TryDeferTellPush(string installId, string deviceId, string tellId) =>
        registry.Plugin(installId)?.Features.Contains(RelayFeatures.TellPush) == true
        && deferredTellPushes.TryAdd(installId, deviceId, tellId);

    // Only pushes what a direct push would have, so a replayed or stray request can't add notifications.
    private void PushQueuedTell(Conn plugin, TellPushFrame frame)
    {
        if (!deferredTellPushes.TryTake(plugin.InstallId, frame.DeviceId, frame.Id)
            || OwnDevice(plugin, frame.DeviceId) is not { Status: DeviceStatus.Active, Push: { } subscription } device
            || Online(plugin.InstallId, device.Id) != null)
            return;

        var tell = store.PendingTells(plugin.InstallId, device.Id).FirstOrDefault(queued => queued.Id == frame.Id);
        if (tell == null || tell.From == plugin.InstallId
            || TellBundleOf(plugin.InstallId)?.Entries.Any(e => e.Target == device.Id && e.Push) != true)
            return;

        if (tellLimiter.TryPush(tell.From, device.Id))
            PushTell(device, subscription, tell);
    }

    private void PushTell(DeviceRecord device, PushSubscriptionRecord subscription, TellFrame tell)
    {
        var body = JsonSerializer.Serialize(new { t = "tell", i = tell.Id, f = tell.From, e = tell.Envelope, k = tell.FromKey, d = device.Id });
        _ = PushAsync(device, subscription, body);
    }

    private TellBundle? TellBundleOf(string installId) =>
        store.FindTellBundle(installId) is { } signed ? TellBundles.Read(signed) : null;

    private string? CheckTell(Conn conn, TellSendFrame frame, out string? recipientInstall, out TellBundle? recipient)
    {
        recipientInstall = null;
        recipient = null;
        if (frame.Id.Length is 0 or > MaxTellIdLength)
            return TellErrors.BadCopies;
        if (!store.AreFriends(conn.InstallId, frame.To))
            return TellErrors.NotPaired;

        recipientInstall = frame.To;
        recipient = TellBundleOf(recipientInstall);
        if (recipient == null)
            return TellErrors.NotChatTerror;

        var own = TellBundleOf(conn.InstallId);
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
