using System.Text.Json;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A read failure of the kind a parser or a converter throws: its message quotes what it read, and its path names a
/// key somebody typed. A test that makes something fail with one holds that the log carries neither.
/// </summary>
internal static class UnreadableJson
{
    /// <summary>Text the failure's message quotes.</summary>
    public const string Quoted = "SENTINEL_QUOTED";

    /// <summary>A key the failure's path names.</summary>
    public const string Key = "SENTINEL_KEY";

    /// <summary>What the log says of <see cref="Failure"/>: the kind and the position, counted from 1.</summary>
    public const string Described = "JsonException at line 3, byte 5";

    public static JsonException Failure() => new($"'{Quoted}' is not valid here.", $"$.context.{Key}", lineNumber: 2, bytePositionInLine: 4);

    /// <summary>Whether a log carries either of the two texts anywhere a sink would write it.</summary>
    public static bool ReachedTheLog(RecordingLogs logs) => logs.Mentions(Quoted) || logs.Mentions(Key);
}
