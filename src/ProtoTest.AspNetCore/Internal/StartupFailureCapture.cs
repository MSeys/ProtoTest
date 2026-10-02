namespace ProtoTest.AspNetCore.Internal;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Keeps the exception the application's host reported when it failed to start. WebApplicationFactory can lose
/// it: the application disposes its host on the entry point's thread, and a factory that reaches the host after
/// that reports the disposed service provider instead of the application's own exception.
/// </summary>
internal sealed class StartupFailureCapture : ILoggerProvider
{
    // The generic host logs every start failure under this category, with the exception it rethrows.
    private const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";
    private Exception? _failure;

    /// <summary>The first error the host logged with an exception, or <see langword="null"/>.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    public ILogger CreateLogger(string categoryName)
        => categoryName == HostCategory ? new HostLogger(this) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class HostLogger(StartupFailureCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                Interlocked.CompareExchange(ref capture._failure, exception, null);
            }
        }
    }
}
