using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatTerror.Protocol;

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowOutOfOrderMetadataProperties = true,
            RespectNullableAnnotations = true,
            // Constructor parameters without a default are required, so optional middle parameters
            // use [Optional, DefaultParameterValue(null)] to keep the positional order.
            RespectRequiredConstructorParameters = true,
            // Payloads never land in HTML, and escaping non-ASCII chat text would triple its size.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    // Throws JsonException for any malformed input, including a missing type discriminator.
    public static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (NotSupportedException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }
}
