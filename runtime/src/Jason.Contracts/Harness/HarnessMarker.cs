using System.Text;
using System.Text.Json;

namespace Jason.Contracts.Harness;

/// <summary>
/// The file that makes a folder one of Jason's: <c>.jason/marker.json</c>, saying which level the folder is and, below
/// the root, the id of the project or campaign it belongs to. It carries the format's version, the kind and the id, and
/// nothing else — no name, since names change and the database is their authority, and no parent, since a copy of where
/// a campaign belongs would drift from the runtime that decides it. A folder is known by this file, never by its name.
/// </summary>
/// <param name="Kind">The level: <see cref="Root"/>, <see cref="Project"/> or <see cref="Campaign"/>; null for a marker this runtime cannot read.</param>
/// <param name="Id">The project's or campaign's public id; null at the root.</param>
public sealed record HarnessMarker(string? Kind, string? Id)
{
    public const int Version = 1;

    public const string Root = "root";

    public const string Project = "project";

    public const string Campaign = "campaign";

    /// <summary>The directory beside a level's two files that holds what is Jason's alone.</summary>
    public const string Directory = ".jason";

    public const string FileName = "marker.json";

    /// <summary>Where the marker of <paramref name="folder"/> is.</summary>
    public static string PathIn(string folder) => System.IO.Path.Combine(folder, Directory, FileName);

    /// <summary>
    /// The marker as it is written, exactly: <c>{"marker_version":1,"kind":"root"}</c> at the root and, below it,
    /// <c>{"marker_version":1,"kind":"project","id":"prj_…"}</c>, in UTF-8 with nothing after the closing brace.
    /// </summary>
    public byte[] Bytes()
    {
        var kind = Kind ?? throw new InvalidOperationException("A marker nobody could read is never written.");
        return Encoding.UTF8.GetBytes(Id is null
            ? $$"""{"marker_version":{{Version}},"kind":"{{kind}}"}"""
            : $$"""{"marker_version":{{Version}},"kind":"{{kind}}","id":"{{Id}}"}""");
    }

    /// <summary>Whether this is the marker <paramref name="kind"/> and <paramref name="id"/> would write.</summary>
    public bool Names(string kind, string? id) =>
        string.Equals(Kind, kind, StringComparison.Ordinal) && string.Equals(Id, id, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="folder"/> holds a marker, by its being there: never opened, so one another program holds open,
    /// one this account may not read and an offline placeholder are markers as any other is, and this never throws. What
    /// needs only to know that a folder is somebody's level, not which, asks this.
    /// </summary>
    public static bool IsIn(string folder) => File.Exists(PathIn(folder));

    /// <summary>
    /// The marker in <paramref name="folder"/>; null when it has none. One that is there and cannot be read as a marker —
    /// not JSON, another shape, a version this runtime does not know — is read as naming nothing this runtime knows, so it
    /// is never taken for one of its own.
    /// </summary>
    public static HarnessMarker? In(string folder)
    {
        if (!IsIn(folder))
        {
            return null;
        }

        var path = PathIn(folder);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var marker = document.RootElement;
            if (marker.ValueKind == JsonValueKind.Object
                && marker.TryGetProperty("marker_version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == Version
                && marker.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
            {
                var id = marker.TryGetProperty("id", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;
                return new HarnessMarker(kind.GetString(), id);
            }
        }
        catch (JsonException)
        {
        }

        return new HarnessMarker(null, null);
    }
}
