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
    private UnreadableStoredValueException(string column, string failure)
        : base($"The value stored in {column} cannot be read ({failure}). The database was changed outside this runtime; that value has to be repaired or removed before the runtime can read it.")
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
}
