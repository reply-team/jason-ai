using System.Globalization;
using System.Net;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// One file of a release beside its checksums.txt, whole and within a cap: how <c>jason ui install</c> and
/// <c>jason plugin install</c> fetch what they place. Each caller says what a refusal is to it, so the words a person
/// reads stay its own, and the reading — the deadline, the cap counted as the bytes arrive — is one rule.
/// </summary>
internal static class ReleaseFiles
{
    /// <summary>checksums.txt is a line per file a release carries.</summary>
    public const long MaxChecksumsBytes = 64 * 1024;

    /// <summary>
    /// The file at <paramref name="address"/>, or a refusal: <paramref name="notPublished"/> for an answer of 404, given the
    /// address and what it answered, and <paramref name="refused"/> for everything else, given the whole sentence.
    /// </summary>
    /// <param name="what">What the file is, as the refusal of one too large names it: "a UI build".</param>
    /// <param name="within">The deadline <paramref name="deadline"/> was set to, as the refusal of a late one names it.</param>
    public static async Task<byte[]> FetchAsync(
        HttpClient client,
        Uri address,
        long cap,
        string what,
        TimeSpan within,
        Func<string, Exception> refused,
        Func<string, Exception> notPublished,
        CancellationToken asked,
        CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(refused);
        ArgumentNullException.ThrowIfNull(notPublished);

        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        UpdateFeed.SayWhoAsks(request);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw notPublished($"{address} answered 404.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw refused($"{address} answered {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > cap)
            {
                throw refused(TooLarge(address, cap, what));
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
                    throw refused(TooLarge(address, cap, what));
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!asked.IsCancellationRequested)
        {
            throw refused($"{address} did not arrive within {within.TotalMinutes:0} minutes.");
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            throw refused($"{address} could not be read: {failure.Message}");
        }
    }

    private static string TooLarge(Uri address, long cap, string what) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{address} is larger than {(cap >= 1024 * 1024 ? $"{cap / (1024 * 1024)} MB" : $"{cap / 1024} KB")}, more than {what} ever is. Nothing was changed.");
}
