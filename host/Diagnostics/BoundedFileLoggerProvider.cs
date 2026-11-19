
namespace NowPlayingOverlay.Host.Diagnostics;

internal sealed class BoundedFileLoggerProvider(BoundedLogFile logFile) : ILoggerProvider
{
    private readonly BoundedLogFile _logFile = logFile ?? throw new ArgumentNullException(nameof(logFile));

    public ILogger CreateLogger(string categoryName)
    {
        return new BoundedFileLogger(_logFile, categoryName);
    }

    public ILogger<T> CreateLogger<T>()
    {
        return new BoundedFileLogger<T>(_logFile);
    }

    public void Dispose()
    {
        // Program owns the file so bootstrap and shutdown failures share the same bounded log.
    }

    private class BoundedFileLogger(BoundedLogFile logFile, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                logFile.Write(logLevel, category, eventId, formatter(state, exception), exception);
            }
        }
    }

    private sealed class BoundedFileLogger<T>(BoundedLogFile logFile)
        : BoundedFileLogger(logFile, typeof(T).FullName ?? typeof(T).Name), ILogger<T>
    {
    }
}
