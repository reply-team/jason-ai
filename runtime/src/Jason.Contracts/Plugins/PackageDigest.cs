using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jason.Contracts.Plugins;

/// <summary>A package refused because it is too big to be a plugin: a digest is meant to stay cheap.</summary>
public sealed class PackageTooLargeException(string message) : Exception(message);

public sealed record PackageDigestResult(string Digest, int FileCount, long TotalBytes);

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
            throw new PackageTooLargeException(string.Create(
                CultureInfo.InvariantCulture,
                $"A plugin package holds at most {MaxFiles} files; this one holds {files.Count}."));
        }

        var total = 0L;
        var lengths = new long[files.Count];
        for (var index = 0; index < files.Count; index++)
        {
            lengths[index] = new FileInfo(Path.Combine(root, files[index].Replace('/', Path.DirectorySeparatorChar))).Length;
            total += lengths[index];
            if (total > MaxBytes)
            {
                throw new PackageTooLargeException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A plugin package holds at most {MaxBytes} bytes; this one holds more."));
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        for (var index = 0; index < files.Count; index++)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(files[index]));
            hash.AppendData([0]);
            hash.AppendData(Encoding.ASCII.GetBytes(lengths[index].ToString(CultureInfo.InvariantCulture)));
            hash.AppendData([0]);

            using var stream = File.OpenRead(Path.Combine(root, files[index].Replace('/', Path.DirectorySeparatorChar)));
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return new PackageDigestResult($"{Algorithm}:{Convert.ToHexStringLower(hash.GetHashAndReset())}", files.Count, total);
    }

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
