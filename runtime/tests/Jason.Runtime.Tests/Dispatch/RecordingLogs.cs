using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Keeps what was logged, so a test can read a diagnostic the way an operator does. Some of what this
/// runtime decides is only visible in a log line — a summon that failed and was survived writes nothing
/// else anywhere — and a line naming an identifier nobody can type is a line that says nothing.
/// </summary>
internal sealed class RecordingLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _lines = new();

    public IReadOnlyList<string> Warnings =>
        [.. _lines.Where(line => line.Level >= LogLevel.Warning).Select(line => line.Message)];

    public ILogger CreateLogger(string categoryName) => new Recorder(_lines);

    public void Dispose()
    {
        // Nothing is held open: the lines are in memory and go with the provider.
    }

    private sealed class Recorder(ConcurrentQueue<(LogLevel, string)> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            lines.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
