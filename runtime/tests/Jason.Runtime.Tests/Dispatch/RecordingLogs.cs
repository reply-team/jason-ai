using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Keeps what was logged, so a test can read a diagnostic the way an operator does. Some of what this
/// runtime decides is only visible in a log line — a summon that failed and was survived writes nothing
/// else anywhere — and a line naming an identifier nobody can type is a line that says nothing.
/// </summary>
/// <remarks>
/// A line keeps the scopes that were open when it was written, flattened into one map, because an operator
/// filtering a log by campaign reads exactly that: the properties a line carries beyond its own message.
/// </remarks>
internal sealed class RecordingLogs : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<Line> _lines = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    /// <summary>
    /// One line: how loud it was, what it said, the scope properties it carried, and the exception it was handed.
    /// The exception is kept apart from the message because a sink renders it whole — type, message and stack — so
    /// a line whose own text is clean can still carry what a parser quoted.
    /// </summary>
    internal sealed record Line(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Scope, Exception? Exception = null);

    public IReadOnlyList<string> Warnings =>
        [.. _lines.Where(line => line.Level >= LogLevel.Warning).Select(line => line.Message)];

    /// <summary>Every line, at every level.</summary>
    public IReadOnlyList<string> All => [.. _lines.Select(line => line.Message)];

    /// <summary>Every line with the scope it was written in, at every level.</summary>
    public IReadOnlyList<Line> Lines => [.. _lines];

    /// <summary>How many lines said exactly this, at any level.</summary>
    public int Count(string message) => _lines.Count(line => line.Message == message);

    /// <summary>
    /// Whether any line carries this text anywhere a log sink would write it: in the message, in a scope
    /// property, or in the exception it was handed, rendered whole.
    /// </summary>
    public bool Mentions(string text) => _lines.Any(line =>
        line.Message.Contains(text, StringComparison.Ordinal)
        || line.Scope.Values.Any(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Contains(text, StringComparison.Ordinal) == true)
        || line.Exception?.ToString().Contains(text, StringComparison.Ordinal) == true);

    public ILogger CreateLogger(string categoryName) => new Recorder(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
        // Nothing is held open: the lines are in memory and go with the provider.
    }

    private Dictionary<string, object?> OpenScopes()
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        _scopes.ForEachScope(
            (scope, into) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        into[key] = value;
                    }
                }
            },
            properties);
        return properties;
    }

    private sealed class Recorder(RecordingLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            owner._lines.Enqueue(new Line(logLevel, formatter(state, exception), owner.OpenScopes(), exception));
        }
    }
}
