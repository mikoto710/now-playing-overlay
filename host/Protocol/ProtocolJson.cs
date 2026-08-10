using System.Text.Json;
using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal static class ProtocolJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static JsonSerializerOptions Options => new(SerializerOptions);

    public static string Serialize(NowPlayingStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, SerializerOptions);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
