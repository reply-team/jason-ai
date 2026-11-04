using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Persistence;

/// <summary>
/// What happens to a journal line of a reserved kind that is off its document under <c>docs/contracts/journal/</c>:
/// strict refuses the write, which is what every test's context does, so a writer that drifts from its document fails
/// the test that drives it; logged commits the line and writes one warning naming the kind and nothing else, which is
/// what a running installation does, because a line the runtime meant to keep is never lost to its own check.
/// </summary>
public sealed class JournalLines
{
    private static readonly Action<ILogger, string, Exception?> OffDocument = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(OffDocument)),
        "A journal line of kind {Kind} was written off its document");

    private readonly ILogger? logger;

    private JournalLines(ILogger? logger) => this.logger = logger;

    /// <summary>A line off its document is refused, naming the kind and the places it is off, never a value.</summary>
    public static JournalLines Strict { get; } = new(null);

    /// <summary>A line off its document is written, and <paramref name="logger"/> is told its kind.</summary>
    public static JournalLines Logged(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return new JournalLines(logger);
    }

    public bool IsStrict => logger is null;

    internal void Report(string kind, IReadOnlyList<string> pointers)
    {
        if (logger is null)
        {
            throw new InvalidOperationException(
                $"A journal line of kind `{kind}` is off its document under docs/contracts/journal/ at {string.Join(", ", pointers.Select(pointer => pointer.Length == 0 ? "the body" : pointer))}.");
        }

        OffDocument(logger, kind, null);
    }
}
