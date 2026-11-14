using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Update;

namespace Jason.Contracts.Discovery;

/// <summary>What a data directory's <c>layout.json</c> was found to say: nothing, a layout, or that it cannot be read.</summary>
public enum LayoutStampState
{
    /// <summary>No file at the name: the layout every Jason before the stamp kept, layout 1.</summary>
    Missing,

    /// <summary>A stamp that reads, with its layout and the version that wrote it.</summary>
    Read,

    /// <summary>Something at the name that is not a stamp this build can read, with the reason.</summary>
    Unreadable,
}

/// <summary>
/// The layout stamp, <c>&lt;data&gt;/layout.json</c>: <c>{"layout": N, "written_by": "&lt;version&gt;", "at": "&lt;ISO UTC&gt;"}</c>.
/// It says how the data directory is laid out, so that a build which does not run that layout refuses before it reads or
/// writes a part that may have moved. A missing file is layout 1, the layout every Jason before the stamp kept.
/// </summary>
/// <remarks>
/// <para>
/// Read once, by <see cref="JasonPaths(string)"/>, and never thrown from: what was found is recorded here, and the points
/// that touch a part that moves refuse by <see cref="Refusal"/>. <c>jason status</c>, <c>jason doctor</c> and
/// <c>jason logs</c> are how such an installation is diagnosed, so they run on whatever was found.
/// </para>
/// <para>
/// Only a build that moves the parts writes a stamp, through a temporary file and a rename, so a stamp that cannot be read
/// is damage or a format newer than this build: in either case the parts may already be where a later layout keeps them,
/// and it is refused rather than read as layout 1. Members this build does not know are ignored; the number alone decides.
/// </para>
/// </remarks>
/// <param name="State">What was found.</param>
/// <param name="Path">The stamp's path.</param>
/// <param name="Layout">The layout a readable stamp names; null otherwise.</param>
/// <param name="WrittenBy">The version of the Jason that wrote a readable stamp; null otherwise.</param>
/// <param name="At">The moment a readable stamp says it was written, as written and never parsed; null when it says none.</param>
/// <param name="Problem">Why an unreadable stamp cannot be read, one of a fixed set of phrases; null otherwise.</param>
public sealed record LayoutStamp(LayoutStampState State, string Path, int? Layout, string? WrittenBy, string? At, string? Problem)
{
    /// <summary>The stamp's name in the data directory.</summary>
    public const string FileName = "layout.json";

    /// <summary>
    /// The layout this build runs on. A stamp naming a later one is refused with <see cref="NewerCode"/>; the build that
    /// moves the parts raises it.
    /// </summary>
    public const int ThisBuildRuns = 1;

    /// <summary>The refusal of a stamp naming a layout later than <see cref="ThisBuildRuns"/>.</summary>
    public const string NewerCode = "layout_newer";

    /// <summary>The refusal of a stamp that is there and cannot be read.</summary>
    public const string UnreadableCode = "layout_unreadable";

    /// <summary>A stamp is a few dozen bytes; a file larger than this is not one.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The layout the stamp says, as the paths answer by it: 1 when there is none or it cannot be read.</summary>
    public int Answered => State == LayoutStampState.Read ? Math.Min(Layout!.Value, JasonPaths.NewestTable) : 1;

    /// <summary>Whether a build that runs <see cref="ThisBuildRuns"/> refuses this stamp.</summary>
    public bool Refuses => State == LayoutStampState.Unreadable || (State == LayoutStampState.Read && Layout > ThisBuildRuns);

    /// <summary>
    /// The refusal a point that touches a part that moves gives on <paramref name="paths"/>, said for a jason of version
    /// <paramref name="current"/>; null where it goes on. Paths given a layout rather than read from a stamp are never
    /// refused: they are a test's, or the code that moves the parts.
    /// </summary>
    public static (string Code, string Message)? Refusal(JasonPaths paths, string current)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Stamp is { Refuses: true } stamp ? stamp.RefusalFor(current) : null;
    }

    /// <summary>This stamp's refusal, said for a jason of version <paramref name="current"/>; null where it refuses nothing.</summary>
    public (string Code, string Message)? RefusalFor(string current) => State switch
    {
        LayoutStampState.Read when Layout > ThisBuildRuns => (NewerCode, NewerSentence(WrittenBy!, current)),
        LayoutStampState.Unreadable =>
            (UnreadableCode, $"This data directory's layout stamp at {Path} cannot be read ({Problem}), so this jason cannot tell how the directory is laid out: use the Jason that set it up, or update this jason."),
        _ => null,
    };

    /// <summary>The sentence of a data directory a newer Jason set up.</summary>
    public static string NewerSentence(string writtenBy, string current) =>
        $"This data directory was set up by Jason {writtenBy}, newer than this jason ({current}): update this jason, or use the one that set it up.";

    /// <summary>The stamp of the data directory at <paramref name="root"/>, as found. Never throws on what is there.</summary>
    public static LayoutStamp Read(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var path = System.IO.Path.Combine(root, FileName);
        if (Directory.Exists(path))
        {
            return Unreadable(path, "it is a folder");
        }

        if (!File.Exists(path))
        {
            return new LayoutStamp(LayoutStampState.Missing, path, null, null, null, null);
        }

        byte[] bytes;
        try
        {
            // Shared for writing and deleting, so that the rename that puts a new stamp in place is never held up by a read,
            // and the read sees one stamp or the other.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxBytes)
            {
                return Unreadable(path, "it is larger than a stamp");
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Unreadable(path, "it could not be read");
        }

        return Parse(path, bytes);
    }

    /// <summary>The stamp <paramref name="bytes"/> hold, read by the rules the type states.</summary>
    public static LayoutStamp Parse(string path, ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (bytes.StartsWith(bom))
        {
            bytes = bytes[bom.Length..];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.ToArray());
        }
        catch (JsonException malformed)
        {
            return Unreadable(path, string.Create(CultureInfo.InvariantCulture, $"it is not JSON at line {malformed.LineNumber}, byte {malformed.BytePositionInLine}"));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Unreadable(path, "it is not a JSON object");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in root.EnumerateObject())
            {
                if (!names.Add(member.Name))
                {
                    return Unreadable(path, "it names a member twice");
                }
            }

            if (!root.TryGetProperty("layout", out var layoutElement) || layoutElement.ValueKind != JsonValueKind.Number || !layoutElement.TryGetInt32(out var layout))
            {
                return Unreadable(path, "it has no whole-number layout");
            }

            if (layout < 1)
            {
                return Unreadable(path, string.Create(CultureInfo.InvariantCulture, $"its layout is {layout}, and layouts count from 1"));
            }

            if (!root.TryGetProperty("written_by", out var writtenElement) || writtenElement.ValueKind != JsonValueKind.String
                || writtenElement.GetString() is not { } writtenBy || !SemanticVersion.TryParse(writtenBy, out _))
            {
                return Unreadable(path, "it names no version that wrote it");
            }

            string? at = null;
            if (root.TryGetProperty("at", out var atElement))
            {
                if (atElement.ValueKind != JsonValueKind.String)
                {
                    return Unreadable(path, "its at is not text");
                }

                at = atElement.GetString();
            }

            return new LayoutStamp(LayoutStampState.Read, path, layout, writtenBy, at, null);
        }
    }

    private static LayoutStamp Unreadable(string path, string problem) => new(LayoutStampState.Unreadable, path, null, null, null, problem);
}
