using System.Text.Json;
using Jason.Cli.Files;
using Jason.Cli.Installation;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// Linux: Jason Desktop as an AppImage, one file, and the jason it carries, which runs as the copy each start of the
/// application makes under the data directory, <c>&lt;data&gt;/desktop/bundle/&lt;version&gt;/</c>, since what the file
/// carries is under a mount that lasts only while it runs. The application's start records where the file is, in
/// <c>&lt;data&gt;/desktop/appimage.json</c>, <c>{"file":"&lt;full path&gt;"}</c>; where it has not, the file is where the install
/// line puts it, <c>~/Applications/jason-desktop-linux-x64.AppImage</c>.
/// </summary>
internal static class AppImageCopy
{
    /// <summary>The file's name, as a release publishes it and the install line places it.</summary>
    public const string FileName = "jason-desktop-linux-x64.AppImage";

    /// <summary>How long one reading of the file by its own runtime is given.</summary>
    public static readonly TimeSpan ExtractWait = TimeSpan.FromMinutes(2);

    /// <summary>The folder of the copies, from a copy's jason: <c>&lt;data&gt;/desktop/bundle</c>.</summary>
    public static string BundlesOf(string copiedExecutable) =>
        Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(copiedExecutable))!)!;

    /// <summary>
    /// The AppImage file a copy's jason belongs to: the one the application's start recorded beside the copies, else the
    /// install line's place in this account's home.
    /// </summary>
    public static string FileOf(string copiedExecutable, string home)
    {
        var record = Path.Combine(Path.GetDirectoryName(BundlesOf(copiedExecutable))!, "appimage.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(record));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("file", out var file)
                && file.ValueKind == JsonValueKind.String
                && file.GetString() is { } named
                && Path.IsPathFullyQualified(named))
            {
                return named;
            }
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return Path.Combine(home, "Applications", FileName);
    }

    /// <summary>The folder of the copies under a data directory: <c>&lt;data&gt;/desktop/bundle</c>.</summary>
    public static string BundlesUnder(string dataRoot) => Path.Combine(dataRoot, "desktop", "bundle");

    /// <summary>The copy's jason for a version, under the copies' folder.</summary>
    public static string CopyOf(string bundles, SemanticVersion version) => Path.Combine(bundles, version.ToString(), ReleaseAssets.ExecutableName);

    /// <summary>Whether the copy's jason for a version is there and its version.json names that version.</summary>
    public static bool Holds(string bundles, SemanticVersion version) =>
        CarriedExecutable.Of(CopyOf(bundles, version)) is { } copy && SemanticVersion.TryParse(copy.Version, out var named) && named == version;

    /// <summary>
    /// The copy of the jason the file carries, made where the file is <paramref name="version"/> and no copy of it is there
    /// yet: the file's own runtime writes what it carries into a folder beside the copies, and the bundle folder is renamed
    /// into place, whole, only where its version.json names that version. Anything else is taken away again. True where the
    /// copy is there afterwards.
    /// </summary>
    public static Task<bool> MakeAsync(string appImage, string bundles, SemanticVersion version, CancellationToken cancellationToken) =>
        MakeAsync(appImage, bundles, version, ExtractAsync, cancellationToken);

    /// <summary>The same, reading the file through <paramref name="extract"/> (<see cref="ExtractAsync"/>'s shape).</summary>
    public static async Task<bool> MakeAsync(string appImage, string bundles, SemanticVersion version, Func<string, string, string, CancellationToken, Task<bool>> extract, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extract);
        if (Holds(bundles, version))
        {
            return true;
        }

        if (!File.Exists(appImage))
        {
            return false;
        }

        Directory.CreateDirectory(bundles);
        var reading = Path.Combine(bundles, $".{version}.tmp-{Environment.ProcessId}");
        try
        {
            DirectoryTrees.Delete(reading);
            Directory.CreateDirectory(reading);
            if (!await extract(appImage, "resources/bundle/*", reading, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var read = Path.Combine(reading, "squashfs-root", "resources", "bundle");
            var named = CarriedExecutable.Of(Path.Combine(read, ReleaseAssets.ExecutableName));
            if (named is null || !SemanticVersion.TryParse(named.Version, out var carries) || carries != version || Directory.Exists(Path.GetDirectoryName(CopyOf(bundles, version))))
            {
                return Holds(bundles, version);
            }

            Directory.Move(read, Path.GetDirectoryName(CopyOf(bundles, version))!);
            return Holds(bundles, version);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            try
            {
                DirectoryTrees.Delete(reading);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// What the file carries under <paramref name="pattern"/>, written by its own runtime (<c>--appimage-extract</c>) into
    /// <c>squashfs-root</c> in <paramref name="folder"/>, within <see cref="ExtractWait"/>. True where it exited 0.
    /// </summary>
    public static async Task<bool> ExtractAsync(string appImage, string pattern, string folder, CancellationToken cancellationToken)
    {
        var start = ChildProcess.StartInfo(appImage, ChildKind.SystemToolWithInput);
        start.ArgumentList.Add("--appimage-extract");
        start.ArgumentList.Add(pattern);
        start.WorkingDirectory = folder;
        start.Environment.Remove(UpdateLock.Variable);
        using var process = ChildProcess.Start(start) ?? throw new IOException($"{appImage} could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(ExtractWait);
        try
        {
            await process.WaitForExitAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        await Task.WhenAll(output, error).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
