using System.Text.Json;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A value this runtime stored and can no longer read: a JSON column that does not parse, an object whose key is
/// written twice, text that names no value of an enum. It says which column, what kind of failure — the exception's
/// type and, where the reader knew one, the position — and nothing else.
/// </summary>
/// <remarks>
/// The failure it stands for is dropped, message and all, and is not kept as the inner exception. What a parser or a
/// converter says about stored text quotes that text — from a bad token to the end of the value, a key by name — and
/// stored text is somebody's: a campaign's brief, a contact's field. An exception kept inside another is written out
/// with it by every log sink there is, so the only way to keep it out of every log is not to keep it.
/// </remarks>
public sealed class UnreadableStoredValueException : Exception
{
    /// <remarks>
    /// The sentence says only what is known: a value this version of the runtime did not write. That is a hand edit as
    /// often as it is a newer version of Jason that wrote a value this one does not know, and for the second the remedy is
    /// that version, not a repair that throws data away.
    /// </remarks>
    private UnreadableStoredValueException(string column, string failure)
        : base($"The value stored in {column} cannot be read ({failure}). It was written by another version of Jason, or changed outside it; run the version that wrote it, or restore or repair that value, before this runtime can read it.")
    {
        Column = column;
        Failure = failure;
    }

    /// <summary>Where the value was read from, as <c>table.column</c>.</summary>
    public string Column { get; }

    /// <summary>The kind of failure: <c>JsonException at line 1, byte 12</c>, a type alone, or a value no enum has.</summary>
    public string Failure { get; }

    /// <summary>The failure as a clause of a sentence, for a log line: which column, and what kind of failure.</summary>
    public string Describe() => $"the value stored in {Column} cannot be read ({Failure})";

    /// <summary>The failure a reader threw, said by its type and position and never by its message.</summary>
    internal static UnreadableStoredValueException From(string column, Exception failure) =>
        new(column, failure is JsonException json ? SettingsFileFailure.Of(json).Describe() : failure.GetType().Name);

    /// <summary>Text that names no value of the enum, which is not repeated: whoever edited the column could have typed anything.</summary>
    internal static UnreadableStoredValueException NotA(string column, string enumName) => new(column, $"not a stored {enumName}");

    /// <summary>The JSON literal null, which is valid JSON and no value: a column that holds it holds nothing to read.</summary>
    internal static UnreadableStoredValueException Null(string column) => new(column, "the JSON literal null");

    /// <summary>
    /// The unreadable stored value inside a failure, however it was wrapped — as an inner exception, or as one of an
    /// aggregate's failures — or null where there is none.
    /// </summary>
    public static UnreadableStoredValueException? In(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.TryPop(out var current))
        {
            if (current is UnreadableStoredValueException stored)
            {
                return stored;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Reverse())
                {
                    pending.Push(inner);
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
