using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jason.Contracts.Plugins;

/// <summary>A package refused because it is too big to be a plugin: a digest is meant to stay cheap.</summary>
public sealed class PackageTooLargeException(string message) : Exception(message);

public sealed record PackageDigestResult(string Digest, int FileCount, long TotalBytes);

/// <summary>One file of a package as the digest reads it: its forward-slashed relative path, its length, and its bytes.</summary>
public sealed record PackageFile(string Path, long Length, Func<Stream> Open);

/// <summary>
/// The identity of an installed package: SHA-256 over a canonical stream of its files. Both sides compute it —
/// the runtime at reload, the child before it runs — so a package edited between the two is caught rather than
/// executed. It pins THIS package on THIS machine: line endings are not normalised, because the bytes that will
/// run are the bytes that are hashed.
/// </summary>
public static class PackageDigest
{
    public const string Algorithm = "sha256";
    public const int MaxFiles = 1024;
    public const long MaxBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Every regular file under <paramref name="root"/> whose relative path has no segment starting with a dot,
    /// as a forward-slashed relative path, sorted ordinally.
    /// </summary>
    public static IReadOnlyList<string> Files(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!IsHidden(relative))
            {
                files.Add(relative);
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Hashes the canonical stream: for each file in order, its path, a zero byte, its decimal byte length,
    /// a zero byte, then its bytes.
    /// </summary>
    /// <exception cref="PackageTooLargeException">The package holds more files or more bytes than a plugin may.</exception>
    public static PackageDigestResult Compute(string root)
    {
        var files = Files(root);
        if (files.Count > MaxFiles)
        {
            throw TooMany(files.Count);
        }

        return Compute(files.Select(relative =>
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            return new PackageFile(relative, new FileInfo(full).Length, () => File.OpenRead(full));
        }));
    }

    /// <summary>
    /// The same stream over files that are not a directory on disk — the package a build carries as resources. Hidden
    /// paths are left out and the rest sorted exactly as <see cref="Files"/> does, so the same paths and bytes give the
    /// digest the directory holding them gives.
    /// </summary>
    /// <exception cref="PackageTooLargeException">The package holds more files or more bytes than a plugin may.</exception>
    public static PackageDigestResult Compute(IEnumerable<PackageFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var ordered = files.Where(file => !IsHidden(file.Path)).ToList();
        ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        if (ordered.Count > MaxFiles)
        {
            throw TooMany(ordered.Count);
        }

        var total = 0L;
        foreach (var file in ordered)
        {
            total += file.Length;
            if (total > MaxBytes)
            {
                throw new PackageTooLargeException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A plugin package holds at most {MaxBytes} bytes; this one holds more."));
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        foreach (var file in ordered)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Path));
            hash.AppendData([0]);
            hash.AppendData(Encoding.ASCII.GetBytes(file.Length.ToString(CultureInfo.InvariantCulture)));
            hash.AppendData([0]);

            using var stream = file.Open();
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return new PackageDigestResult($"{Algorithm}:{Convert.ToHexStringLower(hash.GetHashAndReset())}", ordered.Count, total);
    }

    private static PackageTooLargeException TooMany(int count) =>
        new(string.Create(CultureInfo.InvariantCulture, $"A plugin package holds at most {MaxFiles} files; this one holds {count}."));

    private static bool IsHidden(string relativePath)
    {
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment.StartsWith('.'))
            {
                return true;
            }
        }

        return false;
    }
}
