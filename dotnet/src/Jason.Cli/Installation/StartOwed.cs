using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Installation;

/// <summary>
/// The note that a start is owed, at <see cref="JasonPaths.StartOwedFile"/> (<c>run/start-owed.json</c> in the data
/// directory). <c>jason install</c> writes it once it has stopped this data directory's runtime; a runtime's own start
/// removes it, and so do a person's own <c>jason runtime stop</c> and an uninstall that keeps the data. So it stands from the
/// stop until a runtime runs on this data directory again, a routine killed after the stop and a caller that quit before it
/// started one included.
/// </summary>
/// <remarks>
/// One JSON object, <c>{"stopped_at":"&lt;ISO-8601 UTC&gt;"}</c>, the moment the runtime was found stopped. Its presence is what decides:
/// <c>jason status</c> names it while no runtime answers, with <c>jason runtime start</c>, and Jason Desktop starts a
/// runtime when it finds it standing with none answering. A note that cannot be read still stands.
/// </remarks>
internal static class StartOwed
{
    /// <summary>Writes the note, replacing one already there. The data directory's run folder is made where it is missing.</summary>
    public static void Write(JasonPaths paths, DateTimeOffset moment)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Directory.CreateDirectory(paths.RunDirectory);
        var temporary = Path.Combine(paths.RunDirectory, $".start-owed.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("stopped_at", moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            File.Move(temporary, paths.StartOwedFile, overwrite: true);
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
    /// Whether the note stands, and the moment it names where it can be read: null for a note whose moment cannot be read,
    /// which stands all the same.
    /// </summary>
    public static (bool Stands, DateTimeOffset? StoppedAt) Read(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!File.Exists(paths.StartOwedFile))
        {
            return (false, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(paths.StartOwedFile));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("stopped_at", out var stopped)
                && stopped.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(stopped.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment)
                ? (true, moment)
                : (true, null);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException)
        {
            return (true, null);
        }
    }

    /// <summary>Takes the note back: a person's own stop, or an uninstall that keeps the data. True where none is left.</summary>
    public static bool Remove(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            File.Delete(paths.StartOwedFile);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
