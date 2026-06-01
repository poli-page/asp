using Microsoft.Extensions.Logging;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// Captures every log entry written through any logger built from this provider. Tests assert
// against the entry list (event id, level, message) for warning paths like SmokeEndpointUnguarded.
internal sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(this, categoryName);
    public void Dispose() { }

    internal void Append(LogEntry entry)
    {
        lock (_entries)
            _entries.Add(entry);
    }

    public sealed record LogEntry(LogLevel LogLevel, EventId EventId, string Category, string Message, Exception? Exception);

    private sealed class InMemoryLogger(InMemoryLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            provider.Append(new LogEntry(logLevel, eventId, categoryName, formatter(state, exception), exception));
        }
    }
}
