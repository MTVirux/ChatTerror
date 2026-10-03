using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatTerror.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ChatPayload), "chat")]
[JsonDerivedType(typeof(BacklogPayload), "backlog")]
[JsonDerivedType(typeof(SendResultPayload), "sendResult")]
[JsonDerivedType(typeof(SettingsPayload), "settings")]
[JsonDerivedType(typeof(HelloPayload), "hello")]
[JsonDerivedType(typeof(SendChatPayload), "sendChat")]
[JsonDerivedType(typeof(PrefsPayload), "prefs")]
[JsonDerivedType(typeof(TellKeyPayload), "tellKey")]
public abstract record Payload
{
    public long Seq { get; init; }
}

public sealed record ChatPayload(ChatItem Item) : Payload;

public sealed record BacklogPayload(IReadOnlyList<ChatItem> Items, bool Done) : Payload;

public sealed record SendResultPayload(string RequestId, bool Ok, string? Error = null) : Payload;

public sealed record SettingsPayload(
    [Optional, DefaultParameterValue(null)] string? Character,
    IReadOnlyList<ChatChannel> RelayChannels,
    IReadOnlyList<ChatChannel> SendChannels,
    int MaxLength,
    IReadOnlyList<TellContact>? Contacts = null) : Payload;

public sealed record TellKeyPayload(string PublicKey) : Payload;

// A routable character of a paired friend. Key is that friend's paired install key.
public sealed record TellContact(string Character, string CharacterWorld, string CharacterHash, string Name, string World, string Hash, string InstallId, string Key);

public sealed record HelloPayload(long SinceTs) : Payload;

public sealed record SendChatPayload(string RequestId, ChatChannel Channel, [Optional, DefaultParameterValue(null)] string? Target, string Text) : Payload;

public sealed record PrefsPayload(IReadOnlyList<ChatChannel> MutedChannels, IReadOnlyList<ChannelPref>? Channels = null) : Payload;

public enum NotifyMode
{
    All,
    None,
}

// Partner is only set for tells and is "Name@World" when the world is known.
public sealed record ChannelPref(
    string Character,
    ChatChannel Channel,
    [Optional, DefaultParameterValue(null)] string? Partner,
    NotifyMode Notify);

public static class SendErrors
{
    public const string ChannelNotAllowed = "channelNotAllowed";
    public const string InvalidText = "invalidText";
    public const string TooLong = "tooLong";
    public const string InvalidTarget = "invalidTarget";
    public const string NotLoggedIn = "notLoggedIn";
    public const string Busy = "busy";
    public const string Disabled = "disabled";
}

public static class Payloads
{
    public static string SealPayload(byte[] key, Direction direction, Payload payload)
    {
        var json = ProtocolJson.Serialize(payload);
        return Base64Url.Encode(E2eCrypto.Seal(key, direction, Encoding.UTF8.GetBytes(json)));
    }

    // Throws CryptographicException when decryption fails and JsonException when the plaintext is not a payload.
    public static Payload OpenPayload(byte[] key, Direction direction, string envelope)
    {
        byte[] bytes;
        try
        {
            bytes = Base64Url.Decode(envelope);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Envelope is not base64url.", ex);
        }

        var json = Encoding.UTF8.GetString(E2eCrypto.Open(key, direction, bytes));
        return ProtocolJson.Deserialize<Payload>(json) ?? throw new JsonException("Empty payload.");
    }
}
