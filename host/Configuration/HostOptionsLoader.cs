using System.Globalization;

namespace NowPlayingOverlay.Host.Configuration;

internal static class HostOptionsLoader
{
    private const string ArgumentPrefix = "--Host:";

    public static HostOptions Load(string[] args, int? persistedPort = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in args)
        {
            if (!argument.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = argument.IndexOf('=', ArgumentPrefix.Length);
            if (separator <= ArgumentPrefix.Length)
            {
                throw new ArgumentException(
                    $"Host argument '{argument}' must use --Host:Name=value syntax.",
                    nameof(args));
            }

            values[argument[ArgumentPrefix.Length..separator]] = argument[(separator + 1)..];
        }

        var options = new HostOptions
        {
            Port = GetInt32(values, nameof(HostOptions.Port), persistedPort ?? HostOptions.DefaultPort),
            MaximumConcurrentConnections = GetInt32(
                values,
                nameof(HostOptions.MaximumConcurrentConnections),
                32),
            MaximumSseConnections = GetInt32(
                values,
                nameof(HostOptions.MaximumSseConnections),
                4),
            MaximumRequestHeaderCount = GetInt32(
                values,
                nameof(HostOptions.MaximumRequestHeaderCount),
                32),
            MaximumRequestHeadersTotalSize = GetInt32(
                values,
                nameof(HostOptions.MaximumRequestHeadersTotalSize),
                16 * 1024),
            RequestHeadersTimeout = GetTimeSpan(
                values,
                nameof(HostOptions.RequestHeadersTimeout),
                TimeSpan.FromSeconds(10)),
            KeepAliveTimeout = GetTimeSpan(
                values,
                nameof(HostOptions.KeepAliveTimeout),
                TimeSpan.FromMinutes(2)),
            SseHeartbeatInterval = GetTimeSpan(
                values,
                nameof(HostOptions.SseHeartbeatInterval),
                TimeSpan.FromSeconds(15)),
            PortRebindGracePeriod = GetTimeSpan(
                values,
                nameof(HostOptions.PortRebindGracePeriod),
                TimeSpan.FromSeconds(5)),
            SessionSource = GetEnum(
                values,
                nameof(HostOptions.SessionSource),
                SessionSourceKind.Windows),
            RunFakeScenario = GetBoolean(
                values,
                nameof(HostOptions.RunFakeScenario),
                defaultValue: false),
            WebAssetMode = GetEnum(
                values,
                nameof(HostOptions.WebAssetMode),
                WebAssetMode.Embedded),
            DevelopmentWebRoot = GetString(values, nameof(HostOptions.DevelopmentWebRoot)),
        };
        options.Validate();
        return options;
    }

    private static int GetInt32(
        IReadOnlyDictionary<string, string> values,
        string name,
        int defaultValue)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"Host option '{name}' must be an integer.");
        }

        return result;
    }

    private static TimeSpan GetTimeSpan(
        IReadOnlyDictionary<string, string> values,
        string name,
        TimeSpan defaultValue)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"Host option '{name}' must be a TimeSpan.");
        }

        return result;
    }

    private static bool GetBoolean(
        IReadOnlyDictionary<string, string> values,
        string name,
        bool defaultValue)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out var result))
        {
            throw new ArgumentException($"Host option '{name}' must be true or false.");
        }

        return result;
    }

    private static TEnum GetEnum<TEnum>(
        IReadOnlyDictionary<string, string> values,
        string name,
        TEnum defaultValue)
        where TEnum : struct, Enum
    {
        if (!values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
        {
            throw new ArgumentException($"Host option '{name}' has an unsupported value.");
        }

        return result;
    }

    private static string? GetString(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        return values.TryGetValue(name, out var value) ? value : null;
    }
}
