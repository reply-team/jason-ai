using System.Net.Http.Headers;
using System.Text;

namespace Jason.Contracts.Update;

/// <summary>
/// Reading the release feed: one GET of a <c>manifest.json</c>, bounded, and turned into a
/// <see cref="UpdateManifest"/> or into a refusal with a code.
/// </summary>
/// <remarks>
/// <para>
/// This is the only type in the contracts assembly that performs I/O, and the exception is deliberate. Two
/// callers ask this question — the runtime, unattended, and <c>jason update check</c>, when a person asks —
/// and what a Jason installation sends to a web page has to be one rule rather than two. The thing that would
/// drift first if it were copied is the thing that must not: which bytes a digest is compared against, and what
/// the request carries. The type opens nothing by itself; it takes an <see cref="HttpClient"/> from its caller,
/// which is what lets a test put a stub where the network would be.
/// </para>
/// <para>
/// What is sent is a GET and a <c>User-Agent</c> of <c>jason/&lt;version&gt;</c>. No body, no query, no
/// identifier, nothing about the machine, and nothing about what is installed on it.
/// </para>
/// </remarks>
public sealed class UpdateFeed(HttpClient client)
{
    /// <summary>
    /// The feed of the public repository's latest release. The asset names carry no version, so this one URL
    /// resolves for every release there will ever be and nothing that names it needs editing.
    /// </summary>
    public static Uri Default { get; } = new("https://github.com/reply-team/jason-ai/releases/latest/download/manifest.json");

    /// <summary>The same feed for one named release, for a caller that asked for a version rather than for the newest.</summary>
    public static Uri PinnedFor(Uri feed, SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var latest = feed.ToString();
        var marker = "/releases/latest/download/";
        return latest.Contains(marker, StringComparison.Ordinal)
            ? new Uri(latest.Replace(marker, $"/releases/download/v{version}/", StringComparison.Ordinal))
            : new Uri(feed, $"../download/v{version}/{ReleaseAssets.Manifest}");
    }

    /// <summary>
    /// Where one artifact is fetched from: the feed's own directory and the name the manifest carried, which
    /// has already been held to being a file name.
    /// </summary>
    /// <remarks>
    /// Never a URL from inside the document. A feed that could say where to download from could say anywhere,
    /// and the digest it also carries would then be checking the bytes it chose against the hash it chose.
    /// </remarks>
    public static Uri DownloadUrlFor(Uri feed, string asset)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return ReleaseAssets.IsWellFormedAssetName(asset)
            ? new Uri(feed, asset)
            : throw new UpdateFeedException(UpdateFeedException.Invalid, $"'{asset}' is not an asset name this build will fetch.");
    }

    /// <exception cref="UpdateFeedException">The feed could not be read, or what it answered is not a manifest.</exception>
    public async Task<UpdateManifest> ReadAsync(Uri feed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        Allowed(feed);

        using var request = new HttpRequestMessage(HttpMethod.Get, feed);

        // Set per request rather than on the client: a caller that built its own client cannot forget it, and
        // what this product tells a web page about itself stays one line in one place.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("jason", JasonVersion.Current));

        // The whole exchange under one deadline, because reading the headers and reading the body are two waits
        // and the client's own timeout covers only the first: with ResponseHeadersRead it is satisfied the
        // moment the headers arrive, and a feed that then sends one byte a minute would hold the runtime's
        // checker for ever and a person's `update check` until they pressed Ctrl+C.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
        {
            deadline.CancelAfter(client.Timeout);
        }

        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateFeedException(
                    UpdateFeedException.Unreachable,
                    $"The update feed at {feed} answered {(int)response.StatusCode}.");
            }

            // Inside the guard, not after it. A body that stops arriving mid-transfer — a dropped connection,
            // which is an ordinary thing on a hotel network — threw a raw IOException out of here, so the CLI
            // printed a bare line with nothing on stdout and the checker logged a code that does not exist.
            return UpdateManifest.Read(await BoundedAsync(response, deadline.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException
                                      && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateFeedException(UpdateFeedException.Unreachable, $"The update feed at {feed} could not be reached.", error);
        }
    }

    /// <summary>
    /// The body, read to one byte past what a manifest may be and no further. A page that answers with a
    /// gigabyte is a refusal rather than a memory problem, and the reading stops rather than the checking.
    /// </summary>
    private static async Task<string> BoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[UpdateManifest.MaxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return read > UpdateManifest.MaxBytes
            ? throw new UpdateFeedException(
                UpdateFeedException.Invalid,
                $"The update feed could not be read: the feed is longer than the {UpdateManifest.MaxBytes} bytes a manifest is read to.")
            : Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// https, or http on loopback. The loopback exception is for the stub a test stands in front of this; a
    /// plain http feed on a network is a manifest anybody on the way can rewrite, and the digest it carries
    /// would then be the attacker's digest for the attacker's bytes.
    /// </summary>
    private static void Allowed(Uri feed)
    {
        if (!IsAllowed(feed))
        {
            throw new UpdateFeedException(
                UpdateFeedException.Insecure,
                $"The update feed must be an https address; '{feed}' is not one.");
        }
    }

    /// <summary>
    /// The rule above as a question, for the settings validator: a feed the reader would refuse at the first
    /// check is refused when the file is read instead, and the two cannot disagree about which feeds those are.
    /// </summary>
    public static bool IsAllowed(Uri feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return feed.IsAbsoluteUri
            && (feed.Scheme == Uri.UriSchemeHttps || (feed.Scheme == Uri.UriSchemeHttp && feed.IsLoopback));
    }
}
