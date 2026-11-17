using System.Text.Json;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Installation;

/// <summary>
/// macOS: the profile files <c>jason install</c> created when it wrote its PATH lines, kept so that the uninstall takes
/// away a file Jason brought into being and nothing else. The record is
/// <c>~/Library/Application Support/ai.jasonharness.desktop/created-profiles.json</c>, the application's own support folder
/// by Apple's convention (its bundle identifier), which goes with the application; one JSON object,
/// <c>{"created":["&lt;full path&gt;", …]}</c>.
/// </summary>
/// <remarks>
/// The uninstall, once it has taken the lines out of a file, removes that file only where this record names it and what
/// is left is nothing but whitespace. A file the person had before, one they have written into since, and any file where
/// there is no record (an install before the record existed) stay: the safe direction.
/// </remarks>
internal static class CreatedProfiles
{
    /// <summary>The record's name in the application's support folder.</summary>
    public const string FileName = "created-profiles.json";

    /// <summary>The application's own support folder under the account's Library, named by its bundle identifier.</summary>
    public static string SupportFolder(string home) => Path.Combine(home, "Library", "Application Support", DesktopRemoval.Identifier);

    /// <summary>Where the record is.</summary>
    public static string RecordFile(string home) => Path.Combine(SupportFolder(home), FileName);

    /// <summary>The files the record names, as written; none where there is no record or it cannot be read.</summary>
    public static IReadOnlySet<string> Read(string home)
    {
        var file = RecordFile(home);
        try
        {
            if (!File.Exists(file))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("created", out var created)
                && created.ValueKind == JsonValueKind.Array
                ? created.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()!).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Adds a file this routine has just created to the record, written beside it and renamed into place.</summary>
    public static void Add(string home, string profile)
    {
        var files = new SortedSet<string>(Read(home), StringComparer.Ordinal) { Path.GetFullPath(profile) };
        var folder = SupportFolder(home);
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteStartArray("created");
                foreach (var file in files)
                {
                    writer.WriteStringValue(file);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.Move(temporary, RecordFile(home), overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
                // A temporary file of a dot name, which nothing reads, is all that is left.
            }

            throw;
        }
    }

    /// <summary>
    /// Writes a profile back without the lines just taken out of it, or removes it where the record says this routine created
    /// it and nothing but whitespace is left. A link is never removed. True where the file was removed.
    /// </summary>
    /// <param name="home">The account's home, whose Library holds the record.</param>
    /// <param name="profile">The profile file.</param>
    /// <param name="without">Its bytes with the lines taken out.</param>
    public static bool WriteBackOrRemove(string home, string profile, byte[] without)
    {
        ArgumentNullException.ThrowIfNull(without);

        if (Array.TrueForAll(without, IsWhitespace)
            && Read(home).Contains(Path.GetFullPath(profile))
            && !new FileInfo(profile).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            File.Delete(profile);
            return true;
        }

        File.WriteAllBytes(profile, without);
        return false;
    }

    private static bool IsWhitespace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or (byte)'\v';
}
