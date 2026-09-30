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
public abstract record RelayFrame;

public sealed record AuthFrame(string Token) : RelayFrame;

// Plugin sends set To and Notify; device sends leave them out.
public sealed record SendFrame(string? To, string Payload, bool Notify) : RelayFrame;

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
