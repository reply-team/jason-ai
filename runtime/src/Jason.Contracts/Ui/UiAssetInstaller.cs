using System.Globalization;
using System.IO.Compression;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Ui;

public sealed class UiAssetRefusedException(string message) : Exception(message);

public sealed record UiAssetInstalled(string Directory, UiManifest Manifest, string Digest, int FileCount);

/// <summary>
/// Places <c>jason-ui.zip</c> as the default UI. Every entry is checked before anything is extracted — a file of the
/// build at the archive's root, nowhere else — the build is unpacked beside the installed one, read as a build, and
/// swapped in; the custom directory is never touched. A refusal leaves the installed UI as it was.
/// </summary>
public static class UiAssetInstaller
{
    public static UiAssetInstalled Install(string zipPath, JasonPaths paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(paths);

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
            var digest = Digest(staging);
            Swap(staging, paths.UiDefaultDirectory);
            return new UiAssetInstalled(paths.UiDefaultDirectory, manifest, digest.Digest, digest.FileCount);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
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

    /// <summary>The old default moves aside, the staging moves in, the old one goes. A failure between the two moves puts the old one back.</summary>
    private static void Swap(string staging, string target)
    {
        var aside = target + ".old-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(target))
        {
            Directory.Move(target, aside);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (Directory.Exists(aside) && !Directory.Exists(target))
            {
                Directory.Move(aside, target);
            }

            throw;
        }

        if (Directory.Exists(aside))
        {
            Directory.Delete(aside, recursive: true);
        }
    }
}
