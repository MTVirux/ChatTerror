using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace ChatTerror.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(AuthFrame), "auth")]
[JsonDerivedType(typeof(SendFrame), "send")]
[JsonDerivedType(typeof(PairDecisionFrame), "pairDecision")]
[JsonDerivedType(typeof(AuthOkFrame), "authOk")]
[JsonDerivedType(typeof(AuthFailFrame), "authFail")]
[JsonDerivedType(typeof(MsgFrame), "msg")]
[JsonDerivedType(typeof(DeviceOnlineFrame), "deviceOnline")]
[JsonDerivedType(typeof(DeviceOfflineFrame), "deviceOffline")]
[JsonDerivedType(typeof(PairRequestFrame), "pairRequest")]
[JsonDerivedType(typeof(DeviceRevokedFrame), "deviceRevoked")]
[JsonDerivedType(typeof(PluginStatusFrame), "pluginStatus")]
[JsonDerivedType(typeof(PairedFrame), "paired")]
[JsonDerivedType(typeof(RevokedFrame), "revoked")]
[JsonDerivedType(typeof(ErrorFrame), "error")]
[JsonDerivedType(typeof(TellSendFrame), "tellSend")]
[JsonDerivedType(typeof(TellResultFrame), "tellResult")]
[JsonDerivedType(typeof(TellFrame), "tell")]
[JsonDerivedType(typeof(TellAckFrame), "tellAck")]
[JsonDerivedType(typeof(FriendsChangedFrame), "friendsChanged")]
public abstract record RelayFrame;

public sealed record AuthFrame(string Token) : RelayFrame;

// Plugin sends set To and Notify; device sends leave them out.
public sealed record SendFrame([Optional, DefaultParameterValue(null)] string? To, string Payload, bool Notify = false) : RelayFrame;

public sealed record PairDecisionFrame(string DeviceId, bool Approved) : RelayFrame;

public sealed record AuthOkFrame(string Role, string Id) : RelayFrame;

public sealed record AuthFailFrame : RelayFrame;

public sealed record MsgFrame(string From, string Payload) : RelayFrame;

public sealed record DeviceOnlineFrame(string DeviceId) : RelayFrame;

public sealed record DeviceOfflineFrame(string DeviceId) : RelayFrame;

public sealed record PairRequestFrame(string DeviceId, string DeviceName, string DevicePublicKey) : RelayFrame;

public sealed record DeviceRevokedFrame(string DeviceId) : RelayFrame;

public sealed record PluginStatusFrame(bool Online) : RelayFrame;

public sealed record PairedFrame : RelayFrame;

public sealed record RevokedFrame : RelayFrame;

public sealed record ErrorFrame(string Code) : RelayFrame;

// Self copies go to the sender's own install, the others to the install To.
public sealed record TellCopy(bool Self, string Target, string Envelope);

// To is the recipient install id.
public sealed record TellSendFrame(string Id, string To, IReadOnlyList<TellCopy> Copies) : RelayFrame;

public sealed record TellResultFrame(string Id, bool Ok, string? Error = null) : RelayFrame;

// From is the sending install id, FromKey its install key as the relay knows it.
public sealed record TellFrame(string Id, string From, string Envelope, string FromKey) : RelayFrame;

public sealed record TellAckFrame(IReadOnlyList<string> Ids) : RelayFrame;

public sealed record FriendsChangedFrame : RelayFrame;

public static class RelayRoles
{
    public const string Plugin = "plugin";
    public const string Device = "device";
}

public static class RelayErrors
{
    public const string RateLimited = "rateLimited";
    public const string TooLarge = "tooLarge";
    public const string UnknownDevice = "unknownDevice";
    public const string NotApproved = "notApproved";
    public const string BadFrame = "badFrame";
}
