namespace ChatTerror.Protocol;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) => System.Buffers.Text.Base64Url.EncodeToString(data);

    public static byte[] Decode(string text) => System.Buffers.Text.Base64Url.DecodeFromChars(text);
}
