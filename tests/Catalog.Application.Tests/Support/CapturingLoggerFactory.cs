using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Catalog.Application.Tests.Support;

/// <summary>A logger that keeps what it is given, so a test can prove what the cache wrote to the log and what it did not.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries];

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Capture(_entries);

    public void Dispose()
    {
    }

    private sealed class Capture(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue($"{logLevel}: {formatter(state, exception)}{(exception is null ? string.Empty : $" [{exception.GetType().Name}: {exception.Message}]")}");
    }
}
