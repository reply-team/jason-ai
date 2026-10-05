using System.Text.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Logging;

/// <summary>
/// How a failure to read a payload is said in a log: its kind and where the reader stopped, and nothing the reader
/// was reading. The parser's own message can quote the input it could not read — a character, a property name, a
/// value a converter rejected — and a payload is exactly what the logs promise never to hold. The exception is
/// kept out of the log along with its message, because a log sink writes an exception whole. A stored value that
/// cannot be read is one of these failures, and is said by the column it came from as well.
/// </summary>
/// <remarks>
/// The position is counted as an editor counts, from 1, the way a settings file's failure is said: the one reading of
/// it that exists is used rather than written again. The parser's path is left out on purpose. Under a free-form object
/// — a work item's context is one — it names the keys a person typed, and a key is as much their text as a value is:
/// only names the code itself knows may travel into a log, and the path cannot promise that.
/// </remarks>
internal static class ParseFailure
{
    /// <summary>True when the exception is a read failure — of JSON, or of a stored value — or holds one anywhere inside it.</summary>
    internal static bool Is(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Find(exception) is not null;
    }

    /// <summary>
    /// The failure as a clause of a sentence: <c>JsonException at line 2, byte 3</c>, or the type alone when the
    /// exception knows no position; for a stored value, which column and that its value cannot be read.
    /// </summary>
    internal static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Find(exception) switch
        {
            UnreadableStoredValueException stored => stored.Describe(),
            { } failure => SettingsFileFailure.Of(failure).Describe(),
            null => SettingsFileFailure.Of(exception).Describe(),
        };
    }

    /// <summary>
    /// The first read failure inside the exception, looking through what wraps it: an inner exception, and every
    /// one of an aggregate's failures — only the first of those is its inner exception, and a read failure that
    /// came after another failure is as much one as the first.
    /// </summary>
    private static Exception? Find(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.TryPop(out var current))
        {
            if (current is JsonException or UnreadableStoredValueException)
            {
                return current;
            }

            if (current is AggregateException aggregate)
            {
                // Pushed last to first, so they come off in the order they failed.
                for (var i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                {
                    pending.Push(aggregate.InnerExceptions[i]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return null;
    }
}
