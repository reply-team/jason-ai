using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// The first step of an update: fetch the archive a manifest names for this platform, prove it is the file the
/// manifest described, and take the one executable out of it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here treats the archive as hostile, because it arrives over the network from a page this process
/// did not write. The digest is computed while the bytes are written, so nothing unverified is ever unpacked;
/// the name of the file that comes out is composed from this platform, never from the archive; and exactly one
/// entry is taken — by name, with every other entry ignored rather than filtered. Ignoring is what kills the
/// whole family of "zip slip" tricks outright: there is no path to sanitise, because no path from the archive
/// is ever used.
/// </para>
/// <para>
/// A download is bounded in size and in time. A feed that answers with an endless stream must cost this process
/// a refusal rather than a disk.
/// </para>
/// </remarks>
public sealed class UpdateStager(HttpClient client)
{
    /// <summary>Larger than any release this project publishes, and small enough to be a bound.</summary>
    public const long MaxArtifactBytes = 512L * 1024 * 1024;

    /// <summary>Long enough for a slow connection and a large archive; short enough to end.</summary>
    public static TimeSpan DownloadTimeout { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Downloads the artifact this platform needs, verifies it and unpacks the executable, leaving the staged
    /// directory holding exactly that file.
    /// </summary>
    /// <returns>The path of the staged executable.</returns>
    /// <exception cref="UpdateException">The download failed, or what arrived was not what the manifest described.</exception>
    public async Task<string> StageAsync(
        UpdateManifest manifest,
        string rid,
        Uri feed,
        UpdatePaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(paths);

        if (!manifest.Artifacts.TryGetValue(rid, out var artifact))
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The release of {manifest.Version} publishes nothing for {rid}.");
        }

        if (artifact.Size > MaxArtifactBytes)
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The artifact for {rid} says it is {artifact.Size} bytes, and this build downloads at most {MaxArtifactBytes}.");
        }

        // The staged directory is emptied first: what is in it is the last update's download, and an update that
        // resumed into a directory holding two versions' files would be reading whichever it happened to find.
        var directory = paths.StagedFor(manifest.Version);
        Delete(paths.Staging);
        Directory.CreateDirectory(directory);

        var archive = Path.Combine(directory, "download");
        try
        {
            var digest = await DownloadAsync(UpdateFeed.DownloadUrlFor(feed, artifact.Asset), archive, artifact.Size, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(digest, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(
                    UpdateCodes.ArtifactCorrupt,
                    $"The download of {artifact.Asset} hashes to {digest}, and the manifest says {artifact.Sha256}.");
            }

            var executable = paths.StagedExecutable(manifest.Version);
            Unpack(archive, artifact.Asset, executable);
            MakeRunnable(executable);

            // The archive has served its purpose, and what is left is what an update installs and nothing else.
            File.Delete(archive);
            return executable;
        }
        catch
        {
            // Nothing unverified is left where a later step could pick it up: a staged directory either holds an
            // executable this build has proved, or it holds nothing. The archive goes with the directory.
            Delete(directory);
            throw;
        }
    }

    /// <summary>Writes the body to a file while hashing it, and refuses a body longer than it said it would be.</summary>
    private async Task<string> DownloadAsync(Uri url, string destination, long expected, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DownloadTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException(
                    UpdateCodes.ArtifactUnreachable,
                    $"{url} answered {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            await using var file = File.Create(destination);
            using var hash = SHA256.Create();

            var buffer = new byte[81920];
            long written = 0;
            while (true)
            {
                var read = await body.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                written += read;
                if (written > expected)
                {
                    throw new UpdateException(
                        UpdateCodes.ArtifactCorrupt,
                        $"The download of {url} is longer than the {expected} bytes the manifest said it would be.");
                }

                hash.TransformBlock(buffer, 0, read, null, 0);
                await file.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
            }

            if (written != expected)
            {
                throw new UpdateException(
                    UpdateCodes.ArtifactCorrupt,
                    $"The download of {url} is {written} bytes, and the manifest said {expected}.");
            }

            hash.TransformFinalBlock([], 0, 0);
            return Convert.ToHexStringLower(hash.Hash!);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException
                                      && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException(UpdateCodes.ArtifactUnreachable, $"{url} could not be downloaded: {error.Message}", error);
        }
    }

    /// <summary>
    /// Takes the one entry this platform's release is supposed to carry, and ignores everything else in the
    /// archive — including anything whose name is a path.
    /// </summary>
    private static void Unpack(string archive, string asset, string destination)
    {
        var wanted = ReleaseAssets.ExecutableName;
        if (asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);

            // FullName and not Name: an entry called `..\jason.exe` has the name `jason.exe`, and matching on
            // that would take it. The whole name must be the file this build is looking for.
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, wanted, StringComparison.Ordinal))
                ?? throw Missing(asset, wanted);

            using var source = entry.Open();
            using var file = File.Create(destination);
            source.CopyTo(file);
            return;
        }

        using var compressed = File.OpenRead(archive);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                || !string.Equals(entry.Name, wanted, StringComparison.Ordinal))
            {
                continue;
            }

            using var file = File.Create(destination);
            entry.DataStream!.CopyTo(file);
            return;
        }

        throw Missing(asset, wanted);
    }

    private static UpdateException Missing(string asset, string wanted) => new(
        UpdateCodes.ArtifactUnexpected,
        $"{asset} does not carry a file named '{wanted}', which is the one file a release for this platform holds.");

    /// <summary>
    /// The execute bit, which a zip does not carry at all and a tar carries as somebody else's idea of who may
    /// run this. The file is about to be the program, so it is made runnable by its owner here — the same thing
    /// <c>install.sh</c> does after it unpacks.
    /// </summary>
    private static void MakeRunnable(string executable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(executable);
        File.SetUnixFileMode(executable, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
