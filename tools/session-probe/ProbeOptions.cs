using System.Globalization;

namespace NowPlayingOverlay.SessionProbe;

internal sealed record ProbeOptions(
    bool ShowHelp,
    TimeSpan? Duration,
    string? OutputPath)
{
    public static ProbeOptions Parse(IReadOnlyList<string> args)
    {
        var showHelp = false;
        TimeSpan? duration = null;
        string? outputPath = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;
                case "--duration":
                    var durationText = ReadValue(args, ref index, "--duration");
                    if (!double.TryParse(
                            durationText,
                            NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture,
                            out var seconds)
                        || seconds <= 0)
                    {
                        throw new ArgumentException("--duration must be a positive number of seconds.");
                    }

                    duration = TimeSpan.FromSeconds(seconds);
                    break;
                case "--output":
                    outputPath = Path.GetFullPath(ReadValue(args, ref index, "--output"));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new ProbeOptions(showHelp, duration, outputPath);
    }

    public static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Now Playing Overlay Windows media-session probe");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  dotnet run --project tools/session-probe -- [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --duration <seconds>  Stop automatically after the specified duration.");
        writer.WriteLine("  --output <path>       Also write newline-delimited JSON records to a local file.");
        writer.WriteLine("  -h, --help            Show this help.");
        writer.WriteLine();
        writer.WriteLine("The probe enumerates every session; it does not use GetCurrentSession().");
        writer.WriteLine("Press Ctrl+C to stop an open-ended run.");
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        return args[index];
    }
}
