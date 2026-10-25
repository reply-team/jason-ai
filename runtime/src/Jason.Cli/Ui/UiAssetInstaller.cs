using System.Globalization;
using System.IO.Compression;
using Jason.Cli.Files;
using Jason.Cli.Skills;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;

namespace Jason.Cli.Ui;

public sealed class UiAssetRefusedException(string message) : Exception(message);

public sealed record UiAssetInstalled(string Directory, UiManifest Manifest, string Digest, int FileCount);

/// <summary>What a build has to say it is: the release it was fetched for, and the API of the runtime it will be served by.</summary>
public sealed record UiExpected(SemanticVersion Version, string ApiVersion);

/// <summary>
/// Places <c>jason-ui.zip</c> as the default UI. Every entry is checked before anything is extracted — a file of the
/// build at the archive's root, nowhere else — the build is unpacked beside the installed one, read as a build, and
/// swapped in; the custom directory is never touched. A refusal leaves the installed UI as it was.
/// </summary>
public static class UiAssetInstaller
{
    public static UiAssetInstalled Install(string zipPath, JasonPaths paths, UiExpected expected, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(clock);

        using var archive = Open(zipPath);
        var files = Checked(archive);

        Directory.CreateDirectory(paths.UiDirectory);
        var staging = Path.Combine(paths.UiDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var entry in files)
            {
                var target = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Extract(entry, target);
            }

            var manifest = ReadManifest(staging);
            Hold(manifest, expected);
            var digest = Digest(staging);
            Swap(staging, paths.UiDefaultDirectory, clock);
            return new UiAssetInstalled(paths.UiDefaultDirectory, manifest, digest.Digest, digest.FileCount);
        }
        finally
        {
            Discard(() => DirectoryTrees.Delete(staging), staging);
        }
    }

    /// <summary>
    /// Tidying after the outcome is decided, which nothing may change: on Windows something can hold a file in what is
    /// left over, and a delete refused then would report a placed build as a failure, or bury the reason one was not.
    /// What it leaves is never served — only <c>custom</c> and <c>default</c> are — and the next install makes its own.
    /// </summary>
    internal static void Discard(Action delete, string path)
    {
        if (!Path.Exists(path))
        {
            return;
        }

        try
        {
            delete();
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ZipArchive Open(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new UiAssetRefusedException($"'{zipPath}' is not a readable zip archive: {exception.Message}");
        }
    }

    /// <summary>The file entries, each checked: a relative path of plain segments, none hidden, within the bounds by what they declare, and the two files a build must have.</summary>
    private static List<ZipArchiveEntry> Checked(ZipArchive archive)
    {
        var files = new List<ZipArchiveEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/'))
            {
                continue;
            }

            var segments = name.Split('/');
            if (name.StartsWith('/') || name.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(name)
                || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.StartsWith('.')))
            {
                throw new UiAssetRefusedException($"'{name}' is not a file of a UI build: entries are relative paths of plain segments, none hidden.");
            }

            if (segments.Length > 1 && segments[0] != UiAssetLayout.AssetsDirectory)
            {
                throw new UiAssetRefusedException($"'{name}' is not at the archive's root: a build holds its files at the root and under {UiAssetLayout.AssetsDirectory}/ only.");
            }

            if (!names.Add(name))
            {
                throw new UiAssetRefusedException($"'{name}' appears twice.");
            }

            files.Add(entry);
        }

        if (files.Count > PackageDigest.MaxFiles)
        {
            throw new UiAssetRefusedException(string.Create(CultureInfo.InvariantCulture, $"A UI build holds at most {PackageDigest.MaxFiles} files; this asset holds {files.Count}."));
        }

        if (files.Sum(entry => entry.Length) > PackageDigest.MaxBytes)
        {
            throw new UiAssetRefusedException(string.Create(CultureInfo.InvariantCulture, $"A UI build holds at most {PackageDigest.MaxBytes} bytes; this asset holds more."));
        }

        foreach (var required in (string[])[UiAssetLayout.IndexFile, UiAssetLayout.ManifestFile])
        {
            if (!names.Contains(required))
            {
                throw new UiAssetRefusedException($"The asset holds no {required} at its root, so it is not a UI build.");
            }
        }

        return files;
    }

    /// <summary>
    /// An entry yields no more than the length it declares, so the sum the check added up bounds what lands on disk
    /// (the installer's tests hold that of the runtime); a stream that is not what it claims is a refusal, not a crash.
    /// </summary>
    private static void Extract(ZipArchiveEntry entry, string target)
    {
        try
        {
            entry.ExtractToFile(target, overwrite: false);
        }
        catch (InvalidDataException corrupt)
        {
            throw new UiAssetRefusedException($"'{entry.FullName}' could not be read out of the archive: {corrupt.Message}");
        }
    }

    private static PackageDigestResult Digest(string staging)
    {
        try
        {
            return PackageDigest.Compute(staging);
        }
        catch (PackageTooLargeException refused)
        {
            throw new UiAssetRefusedException(refused.Message);
        }
    }

    private static UiManifest ReadManifest(string staging)
    {
        try
        {
            return UiManifest.Read(staging) ?? throw new UiAssetRefusedException($"The asset's {UiAssetLayout.ManifestFile} could not be read.");
        }
        catch (UiManifestException refused)
        {
            throw new UiAssetRefusedException(refused.Message);
        }
    }

    private static void Hold(UiManifest manifest, UiExpected expected)
    {
        if (!SemanticVersion.TryParse(manifest.Version, out var version) || version != expected.Version)
        {
            throw new UiAssetRefusedException($"The asset is the UI {manifest.Version}, and it was fetched for {expected.Version}.");
        }

        if (!string.Equals(manifest.ApiVersion, expected.ApiVersion, StringComparison.Ordinal))
        {
            throw new UiAssetRefusedException($"The asset is built for API {manifest.ApiVersion}, and the runtime it is for speaks {expected.ApiVersion}.");
        }
    }

    /// <summary>
    /// The old default moves aside, the staging moves in, the old one goes. A failure between the two moves puts the old
    /// one back. Each move waits while Windows reports a file in it held, as the runtime holds one it is serving.
    /// </summary>
    private static void Swap(string staging, string target, TimeProvider clock)
    {
        var aside = target + ".old-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(target))
        {
            Move(target, aside, clock);
        }

        try
        {
            Move(staging, target, clock);
        }
        catch
        {
            if (Directory.Exists(aside) && !Directory.Exists(target))
            {
                Directory.Move(aside, target);
            }

            throw;
        }

        Discard(() => DirectoryTrees.Delete(aside), aside);
    }

    private static void Move(string from, string to, TimeProvider clock) =>
        Move(() => Directory.Move(from, to), from, clock, Thread.Sleep);

    /// <summary>
    /// One rename, tried again while it is refused, as the skills' swap tries its own. Windows refuses a directory rename
    /// with access denied while anything holds a file inside the tree — the runtime serving it, a scanner reading what
    /// was just unpacked — and refuses it cleanly, so waiting is safe. Its code is a permission's, so no refusal is told
    /// apart from a hold: each is waited out, and one that outlasts the wait is said.
    /// </summary>
    internal static void Move(Action move, string from, TimeProvider clock, Action<TimeSpan> pause)
    {
        var started = clock.GetTimestamp();
        while (true)
        {
            try
            {
                move();
                return;
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                if (clock.GetElapsedTime(started) >= SkillsSwap.ReplaceTimeout)
                {
                    throw new UiAssetRefusedException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{from}' could not be moved: something is holding a file in it open, and has for {SkillsSwap.ReplaceTimeout.TotalSeconds} seconds. {refused.Message}"));
                }

                pause(TimeSpan.FromMilliseconds(25));
            }
        }
    }
}
