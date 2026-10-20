using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Jason.Cli.Update;
using Jason.Contracts;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;

namespace Jason.Cli.Ui;

public class UiInstallException(string message) : Exception(message);

/// <summary>The release publishes no UI: one from before the UI was published, or one the address does not have.</summary>
public sealed class UiNotPublishedException(string message) : UiInstallException(message);

/// <summary>What <c>jason ui install</c> answers with: the build now in place as the default UI.</summary>
public sealed record UiInstallResponse(string Version, string ApiVersion, string Directory, string Digest, int Files);

/// <summary>
/// A release's UI, placed as this machine's default build: <c>jason ui install</c>'s body, and the last step of
/// <c>jason update apply</c>. The package comes from the release's own directory beside its checksums.txt, never from
/// an address either file names, and it is held to the line there before it is opened.
/// </summary>
/// <remarks>
/// The role skills come from git at the release's tag because they are text with no build step. The UI is a build, so
/// it comes from the release, and only that differs: one command places it, the install guide runs it, an update runs
/// it, and <c>jason status</c> names it.
/// </remarks>
public static class UiInstaller
{
    /// <summary>A build is held to 16 MB unpacked; a package twice that is not one, whatever the address served.</summary>
    public static long MaxDownloadBytes => 32L * 1024 * 1024;

    /// <summary>checksums.txt is a line per file a release carries.</summary>
    private const long MaxChecksumsBytes = 64 * 1024;

    /// <summary>A package of a megabyte or two: what has not arrived in two minutes is not arriving.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    /// <summary>Fetches the release's UI for <paramref name="version"/>, checks it, and places it, or changes nothing and says why.</summary>
    /// <param name="apiVersion">The API the runtime it is for speaks.</param>
    public static async Task<UiAssetInstalled> PlaceAsync(CliEnvironment env, Uri feed, SemanticVersion version, string apiVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(feed);

        if (!UpdateFeed.IsAllowed(feed))
        {
            throw new UiInstallException($"'{feed}' is not a feed this program reads: an https address, or http on this machine.");
        }

        var release = UpdateFeed.PinnedFor(feed, version);
        using var client = UpdateHttp.Client(env);
        // The deadline below is the one wait: the client's own hundred seconds would end a slow download first, and its
        // message would name a wait nobody set.
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        var sums = UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Checksums);
        var listed = Encoding.UTF8.GetString(await FetchAsync(client, sums, version, MaxChecksumsBytes, $"a release's {ReleaseAssets.Checksums}", cancellationToken, deadline.Token).ConfigureAwait(false));
        var expected = ReleaseChecksums.DigestOf(listed, UiAssetLayout.AssetName, out var problem)
            ?? throw (ReleaseChecksums.Lists(listed, UiAssetLayout.AssetName)
                ? new UiInstallException($"{problem} ({sums}) Nothing was changed.")
                : new UiNotPublishedException($"No UI is published for {version}: {sums} lists no {UiAssetLayout.AssetName}."));

        var package = UpdateFeed.DownloadUrlFor(release, UiAssetLayout.AssetName);
        var bytes = await FetchAsync(client, package, version, MaxDownloadBytes, "a UI build", cancellationToken, deadline.Token).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new UiInstallException($"{package} does not match {ReleaseAssets.Checksums}: it hashes to {actual}, and the release lists {expected}. Nothing was changed.");
        }

        Directory.CreateDirectory(env.Paths.UiDirectory);
        var download = Path.Combine(env.Paths.UiDirectory, $".download-{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllBytesAsync(download, bytes, cancellationToken).ConfigureAwait(false);
            return UiAssetInstaller.Install(download, env.Paths, new UiExpected(version, apiVersion), TimeProvider.System);
        }
        catch (UiAssetRefusedException refused)
        {
            throw new UiInstallException($"{refused.Message} Nothing was changed.");
        }
        finally
        {
            UiAssetInstaller.Discard(() => File.Delete(download), download);
        }
    }

    /// <summary>One file of the release, whole and within <paramref name="cap"/>, or a refusal that names its address.</summary>
    private static async Task<byte[]> FetchAsync(HttpClient client, Uri address, SemanticVersion version, long cap, string what, CancellationToken asked, CancellationToken deadline)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("jason", JasonVersion.Current));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UiNotPublishedException($"No UI is published for {version}: {address} answered 404.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UiInstallException($"{address} answered {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > cap)
            {
                throw TooLarge(address, cap, what);
            }

            await using var body = await response.Content.ReadAsStreamAsync(deadline).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, deadline).ConfigureAwait(false)) > 0)
            {
                // Counted as it arrives, so an address that says nothing of its length, or says less than it sends, is
                // stopped at the cap rather than read whole into memory.
                if (buffer.Length + read > cap)
                {
                    throw TooLarge(address, cap, what);
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!asked.IsCancellationRequested)
        {
            throw new UiInstallException($"{address} did not arrive within {Deadline.TotalMinutes:0} minutes.");
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            throw new UiInstallException($"{address} could not be read: {failure.Message}");
        }
    }

    private static UiInstallException TooLarge(Uri address, long cap, string what) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"{address} is larger than {(cap >= 1024 * 1024 ? $"{cap / (1024 * 1024)} MB" : $"{cap / 1024} KB")}, more than {what} ever is. Nothing was changed."));
}
