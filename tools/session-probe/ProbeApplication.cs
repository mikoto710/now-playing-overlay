namespace NowPlayingOverlay.SessionProbe;

internal static class ProbeApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            ProbeOptions.WriteHelp(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            ProbeOptions.WriteHelp(Console.Out);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        if (options.Duration is { } duration)
        {
            cancellation.CancelAfter(duration);
        }

        try
        {
            await using var sink = await ProbeLogSink.CreateAsync(options.OutputPath);
            await using var probe = new MediaSessionProbe(sink, options.ExerciseSource);
            await probe.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Session probe failed: {exception}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
