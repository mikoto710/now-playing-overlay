using System.Globalization;

namespace NowPlayingOverlay.Host.Configuration;

internal static class HostOptionsLoader
{
    private const string ArgumentPrefix = "--Host:";

    public static HostOptions Load(string[] args, int? persistedPort = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var port = persistedPort ?? HostOptions.DefaultPort;
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

            var name = argument[ArgumentPrefix.Length..separator];
            if (!string.Equals(name, nameof(HostOptions.Port), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Host option '{name}' is not supported. Only Host:Port is configurable.",
                    nameof(args));
            }

            var value = argument[(separator + 1)..];
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port))
            {
                throw new ArgumentException("Host option 'Port' must be an integer.", nameof(args));
            }
        }

        var options = new HostOptions { Port = port };
        options.Validate();
        return options;
    }
}
