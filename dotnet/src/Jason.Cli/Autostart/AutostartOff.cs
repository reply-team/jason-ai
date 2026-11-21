using System.Globalization;
using System.Text.Json;
using Jason.Contracts;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Autostart;

/// <summary>
/// The person's word that the runtime is not to start at logon, at <see cref="JasonPaths.AutostartOffFile"/>
/// (<c>autostart/off.json</c> in the data directory). <c>jason runtime autostart disable</c> writes it, <c>enable</c> takes it
/// back, and <c>jason uninstall</c> removes it, with <see cref="JasonPaths.AutostartDefaultFile"/> beside it, where the data is
/// kept. No archive carries either: an import is a new machine.
/// </summary>
/// <remarks>
/// One JSON object, <c>{"since":"&lt;ISO-8601 UTC&gt;","version":"&lt;the jason that wrote it&gt;"}</c>. Its presence is what
/// decides; a record that cannot be read still stands. On macOS launchd's own record of the label as disabled, which a
/// <c>disable</c> leaves by <c>launchctl unload -w</c>, counts as the same word (<see cref="IAutostartRegistrar.RecordsOff"/>),
/// so a <c>disable</c> made before this file existed still holds there.
/// </remarks>
internal static class AutostartOff
{
    /// <summary>Writes the record, replacing one already there; the data directory's <c>autostart</c> folder is made where it is missing.</summary>
    public static void Write(JasonPaths paths, DateTimeOffset moment, string version)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Directory.CreateDirectory(paths.AutostartDirectory);
        var temporary = Path.Combine(paths.AutostartDirectory, $".off.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("since", moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                writer.WriteString("version", version);
                writer.WriteEndObject();
            }

            File.Move(temporary, paths.AutostartOffFile, overwrite: true);
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

    /// <summary>Whether the record stands in the data directory.</summary>
    public static bool Recorded(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return File.Exists(paths.AutostartOffFile);
    }

    /// <summary>
    /// Whether the person has turned the start at logon off: the record in the data directory, or, on macOS, launchd's own
    /// record of the label as disabled. A registrar that cannot say is taken as saying nothing.
    /// </summary>
    public static bool Stands(JasonPaths paths, IAutostartRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        if (Recorded(paths))
        {
            return true;
        }

        try
        {
            return registrar.RecordsOff();
        }
        catch (AutostartException)
        {
            return false;
        }
    }

    /// <summary>Takes the record back. True where none stands afterwards, false where it is there and could not be removed.</summary>
    public static bool Remove(JasonPaths paths) => Delete(paths?.AutostartOffFile ?? throw new ArgumentNullException(nameof(paths)));

    /// <summary>Removes the record that an update turned the start on once. True where none stands afterwards.</summary>
    public static bool RemoveDefault(JasonPaths paths) => Delete(paths?.AutostartDefaultFile ?? throw new ArgumentNullException(nameof(paths)));

    /// <summary>The version that writes the record: the build's own, as every record of this jason names it.</summary>
    public static string Version(CliEnvironment env) => env?.BuildVersion ?? JasonVersion.Current;

    private static bool Delete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return !File.Exists(file);
        }
    }
}
