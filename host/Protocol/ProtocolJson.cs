using System.Text.Json;
using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal static class ProtocolJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
    private static readonly JsonSerializerOptions IndentedSerializerOptions = new(SerializerOptions)
    {
        WriteIndented = true,
    };

    public static JsonSerializerOptions Options => new(SerializerOptions);

    public static string Serialize(NowPlayingStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, SerializerOptions);
    }

    public static string Serialize(NowPlayingStateDto state, bool indented)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(
            state,
            indented ? IndentedSerializerOptions : SerializerOptions);
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
