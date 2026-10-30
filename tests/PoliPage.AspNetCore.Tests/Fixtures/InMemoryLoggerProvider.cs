using Microsoft.Extensions.Logging;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// Captures every log entry written through any logger built from this provider, at every level,
// and the state of every scope opened on them. Tests assert against the entry list (event id,
// level, message, structured state) for warning paths like SmokeEndpointUnguarded and, with the
// scopes, for what a log line must never carry (an API key).
internal sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly List<LogEntry> _entries = new();
    private readonly List<string> _scopes = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public IReadOnlyList<string> Scopes
    {
        get
        {
            lock (_scopes)
                return _scopes.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(this, categoryName);
    public void Dispose() { }

    internal void Append(LogEntry entry)
    {
        lock (_entries)
            _entries.Add(entry);
    }

    internal void AppendScope(string state)
    {
        lock (_scopes)
            _scopes.Add(state);
    }

    // The structured values a sink such as JSON or OpenTelemetry would export, beside the
    // formatted message a text sink prints.
    private static string ValuesOf(object? state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join('\n', pairs.Select(p => $"{p.Key}={p.Value}"))
            : state?.ToString() ?? string.Empty;

    public sealed record LogEntry(
        LogLevel LogLevel, EventId EventId, string Category, string Message, string State, Exception? Exception);

    private sealed class InMemoryLogger(InMemoryLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            provider.AppendScope(ValuesOf(state));
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            provider.Append(new LogEntry(logLevel, eventId, categoryName, formatter(state, exception), ValuesOf(state), exception));
        }
    }
}
