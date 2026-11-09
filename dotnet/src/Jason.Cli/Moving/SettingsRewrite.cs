using System.Text;
using System.Text.Json;

namespace Jason.Cli.Moving;

/// <summary>One path the settings file named, as it was and as it is written here.</summary>
/// <param name="Setting">The setting, its sections joined by <c>:</c> as the configuration names it.</param>
/// <param name="From">The value as the other machine wrote it.</param>
/// <param name="To">The value written here.</param>
public sealed record SettingRewritten(string Setting, string From, string To);

/// <summary>A path the settings file named outside the three places an import moves, which is not on this machine.</summary>
/// <param name="Setting">The setting.</param>
/// <param name="Path">The value, left as it was written.</param>
public sealed record SettingNotHere(string Setting, string Path);

/// <summary>What a rewrite of the settings file made of it.</summary>
/// <param name="Text">The file as it is written here.</param>
/// <param name="Rewritten">Every value changed, <c>Harness:Root</c> first.</param>
/// <param name="NotHere">Every absolute path left as written that names nothing on this machine.</param>
public sealed record SettingsRewritten(string Text, IReadOnlyList<SettingRewritten> Rewritten, IReadOnlyList<SettingNotHere> NotHere);

/// <summary>A place on the other machine and where it is on this one.</summary>
/// <param name="From">The folder as the other machine spelt it, without a trailing separator.</param>
/// <param name="To">The folder here.</param>
public sealed record PlaceMoved(string From, string To);

/// <summary>
/// The settings file of another machine, made this machine's: every absolute path under the other machine's home
/// directory, data directory or harness root is the same path under this machine's, and <c>Harness:Root</c> names the
/// harness root the import placed. Everything else is left exactly as it was written — comments, order, spacing, every
/// other value, a secret among them — because the change is made in the text, one string at a time, never by writing the
/// document again.
/// </summary>
internal static class SettingsRewrite
{
    /// <summary>
    /// Rewrites <paramref name="text"/>, or composes a file when there was none and the root needs naming.
    /// </summary>
    /// <param name="text">The settings file the archive carried; null when it carried none.</param>
    /// <param name="fromSystem">The system the archive was written on: <c>windows</c>, <c>macos</c> or <c>linux</c>.</param>
    /// <param name="places">The other machine's places and this machine's, in any order.</param>
    /// <param name="root">The harness root placed here.</param>
    /// <param name="nameRoot">Whether the file must name the root when it does not already, which it must wherever the root is not the default.</param>
    /// <param name="exists">Whether a path names something on this machine.</param>
    /// <exception cref="JsonException">The text is not a JSON object the configuration reads.</exception>
    public static SettingsRewritten Rewrite(
        string? text,
        string fromSystem,
        IReadOnlyList<PlaceMoved> places,
        string root,
        bool nameRoot,
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(exists);

        var windows = string.Equals(fromSystem, "windows", StringComparison.Ordinal);
        var comparison = windows || string.Equals(fromSystem, "macos", StringComparison.Ordinal) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var longestFirst = places.OrderByDescending(place => place.From.Length).ToArray();

        if (text is null)
        {
            return nameRoot
                ? new SettingsRewritten(
                    "{" + "\n  " + Encoded(InstallationPlaces.RootSetting) + ": " + Encoded(root) + "\n}\n",
                    [new SettingRewritten(InstallationPlaces.RootSetting, string.Empty, root)],
                    [])
                : new SettingsRewritten(string.Empty, [], []);
        }

        // Read once whole first: whether the file names the root anywhere decides whether one is put at its top.
        var insert = nameRoot && !NamesRoot(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var output = new MemoryStream(bytes.Length + 256);
        var copied = 0;
        var rootRewrite = insert ? new SettingRewritten(InstallationPlaces.RootSetting, string.Empty, root) : null;
        var rewritten = new List<SettingRewritten>();
        var notHere = new List<SettingNotHere>();

        var frames = new Stack<Frame>();
        string? name = null;
        var insertAt = -1;

        while (reader.Read())
        {
            if (frames.Count == 0 && reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("The settings file is not a JSON object.");
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (insertAt >= 0 && frames.Count == 1)
                    {
                        Insert(output, bytes, ref copied, insertAt, root, comma: true);
                        insertAt = -1;
                    }

                    name = reader.GetString();
                    break;

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    if (frames.Count == 0)
                    {
                        frames.Push(new Frame(string.Empty, IsArray: false));
                        insertAt = insert ? (int)reader.TokenStartIndex + 1 : -1;
                        break;
                    }

                    frames.Push(new Frame(Element(frames, name), reader.TokenType == JsonTokenType.StartArray));
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (insertAt >= 0 && frames.Count == 1)
                    {
                        Insert(output, bytes, ref copied, insertAt, root, comma: false);
                        insertAt = -1;
                    }

                    frames.Pop();
                    Next(frames);
                    break;

                case JsonTokenType.String:
                {
                    var setting = Element(frames, name);
                    var value = reader.GetString()!;
                    var start = (int)reader.TokenStartIndex;
                    var end = start + reader.ValueSpan.Length + 2;
                    if (string.Equals(setting, InstallationPlaces.RootSetting, StringComparison.OrdinalIgnoreCase))
                    {
                        rootRewrite = new SettingRewritten(setting, value, root);
                        Replace(output, bytes, ref copied, start, end, root);
                    }
                    else if (Absolute(value, windows))
                    {
                        if (Moved(value, windows, comparison, longestFirst) is { } moved)
                        {
                            if (!string.Equals(moved, value, StringComparison.Ordinal))
                            {
                                rewritten.Add(new SettingRewritten(setting, value, moved));
                                Replace(output, bytes, ref copied, start, end, moved);
                            }
                        }
                        else if (!exists(value))
                        {
                            notHere.Add(new SettingNotHere(setting, value));
                        }
                    }

                    Next(frames);
                    break;
                }

                default:
                    Next(frames);
                    break;
            }
        }

        output.Write(bytes, copied, bytes.Length - copied);
        if (rootRewrite is not null)
        {
            rewritten.Insert(0, rootRewrite);
        }

        return new SettingsRewritten(Encoding.UTF8.GetString(output.ToArray()), rewritten, notHere);
    }

    /// <summary>The value as an absolute path of the other machine's system: a drive or a share there, a slash elsewhere.</summary>
    internal static bool Absolute(string value, bool windows) =>
        windows
            ? (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/')
              || value.StartsWith(@"\\", StringComparison.Ordinal)
            : value.StartsWith('/');

    /// <summary>The value under this machine's place, when it is under one of the other machine's; null when it is under none.</summary>
    private static string? Moved(string value, bool windows, StringComparison comparison, PlaceMoved[] longestFirst)
    {
        var spelt = windows ? value.Replace('/', '\\') : value;
        var separator = windows ? '\\' : '/';
        foreach (var place in longestFirst)
        {
            var from = windows ? place.From.Replace('/', '\\') : place.From;
            from = from.TrimEnd(separator);
            if (string.Equals(spelt.TrimEnd(separator), from, comparison))
            {
                return place.To;
            }

            if (spelt.Length > from.Length && spelt.StartsWith(from, comparison) && spelt[from.Length] == separator)
            {
                // The rest is written with this machine's separator, as every path here is.
                return string.Join(Path.DirectorySeparatorChar, [place.To, .. spelt[(from.Length + 1)..].Split(separator)]);
            }
        }

        return null;
    }

    private sealed class Frame(string path, bool IsArray)
    {
        public string Path { get; } = path;

        public bool Array { get; } = IsArray;

        public int Index { get; set; }
    }

    /// <summary>The name of the element the reader is at, as the configuration names it.</summary>
    private static string Element(Stack<Frame> frames, string? name)
    {
        var frame = frames.Peek();
        var own = frame.Array ? frame.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) : name ?? string.Empty;
        return frame.Path.Length == 0 ? own : frame.Path + ":" + own;
    }

    /// <summary>An element of an array was read: the next one has the next index.</summary>
    private static void Next(Stack<Frame> frames)
    {
        if (frames.Count > 0 && frames.Peek().Array)
        {
            frames.Peek().Index++;
        }
    }

    private static void Replace(MemoryStream output, byte[] bytes, ref int copied, int start, int end, string value)
    {
        output.Write(bytes, copied, start - copied);
        output.Write(Encoding.UTF8.GetBytes(Encoded(value)));
        copied = end;
    }

    /// <summary>
    /// The root's setting, put first in the file when the file does not name it and must: as one key spelt with a colon,
    /// which the configuration reads as the section, so a <c>Harness</c> section written for something else is left alone.
    /// </summary>
    private static void Insert(MemoryStream output, byte[] bytes, ref int copied, int at, string root, bool comma)
    {
        output.Write(bytes, copied, at - copied);
        output.Write(Encoding.UTF8.GetBytes("\n  " + Encoded(InstallationPlaces.RootSetting) + ": " + Encoded(root) + (comma ? "," : "\n")));
        copied = at;
    }

    private static string Encoded(string value) => JsonSerializer.Serialize(value);

    /// <summary>Whether the file gives the root's setting any value at all, a string or not.</summary>
    private static bool NamesRoot(string text)
    {
        using var document = JsonDocument.Parse(text, InstallationPlaces.Reading);
        return Names(document.RootElement, string.Empty);

        static bool Names(JsonElement element, string path) =>
            string.Equals(path, InstallationPlaces.RootSetting, StringComparison.OrdinalIgnoreCase)
            || (element.ValueKind == JsonValueKind.Object
                && element.EnumerateObject().Any(property => Names(property.Value, path.Length == 0 ? property.Name : path + ":" + property.Name)));
    }
}
