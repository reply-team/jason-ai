using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jason.Cli.Update;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;

namespace Jason.Cli.Skills;

/// <summary>
/// The packs from a release: its <see cref="ReleaseAssets.Skills"/>, fetched from the release's own directory beside its
/// <c>checksums.txt</c>, held to its line there, checked whole and only then unpacked into the staging directory.
/// </summary>
/// <remarks>
/// <para>
/// A source is a release feed when it is the address of a release's <c>manifest.json</c>, the one every other download of
/// this product reads: <see cref="SkillsSource.DefaultSource"/>, or a mirror or a directory served the same way. The
/// release is the one at the pinned ref, found by the same rule an update uses, so the ref has to be a release's tag.
/// </para>
/// <para>
/// It stages where a git fetch of the same ref stages, and each knows its own finished tree by its own mark: this one's
/// is a file at the tree's root, in a tree with no <c>.git</c>; a clone's is inside its <c>.git</c>. So neither is ever
/// taken for the other, and a tree the other made is removed and staged again.
/// </para>
/// </remarks>
internal static class SkillsRelease
{
    /// <summary>The packs are a megabyte or so; twice the most a package may unpack to is not one, whatever the address served.</summary>
    public static long MaxDownloadBytes => 2 * PackageDigest.MaxBytes;

    /// <summary>The package's own account of itself, at the archive's root, beside <c>skills/</c>.</summary>
    public const string SourceFile = "source.json";

    /// <summary>The directory of the archive that holds the packs, as it does in the repository.</summary>
    private const string SkillsDirectory = "skills";

    /// <summary>
    /// The file a staged package gets at its root once it has been unpacked whole, holding the commit it names, and not
    /// before. No package can carry one: only <see cref="SourceFile"/> may stand at a package's root.
    /// </summary>
    private const string CompletionMark = "jason-staged";

    /// <summary>A file that says three things is a few hundred bytes; one of 64 KB is not that file.</summary>
    private const int MaxSourceFileBytes = 64 * 1024;

    /// <summary>Whether a source is a release feed: an absolute http or https address of a release's <c>manifest.json</c>.</summary>
    public static bool IsFeed(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
        && address.AbsolutePath.EndsWith("/" + ReleaseAssets.Manifest, StringComparison.Ordinal);

    /// <summary>Whether a release's package finished the tree at that path: its mark is at the root, and the tree is no clone.</summary>
    public static bool Completed(string staged) =>
        File.Exists(Path.Combine(staged, CompletionMark)) && !Directory.Exists(Path.Combine(staged, ".git"));

    /// <summary>The packs of the release at <paramref name="pin"/>, staged, or nothing staged and a refusal that says why.</summary>
    public static async Task<StagedSource> StageAsync(CliEnvironment env, string feed, string pin, bool overridden, CancellationToken cancellationToken)
    {
        var address = new Uri(feed);
        if (!UpdateFeed.IsAllowed(address))
        {
            throw new SkillsSourceUnavailable($"'{feed}' is not a feed this program reads: an https address, or http on this machine.");
        }

        // A release is found by its tag, and a tag is "v" and a version, said exactly: a branch, a commit or a version
        // written another way names no release, and is refused before the feed is asked anything.
        if (!pin.StartsWith('v') || !SemanticVersion.TryParse(pin[1..], out var version) || "v" + version.ToString() != pin)
        {
            throw new SkillsSourceUnavailable(
                $"'{pin}' is not a release's tag, and a release feed publishes skills by tag: v and a version, such as v1.2.3. To take a branch or a commit, name a git repository with --source, such as {SkillsSource.EarlierDefaultSource}.");
        }

        var staged = env.Paths.SkillsStagedSourceDirectory(SkillsSource.DirectoryNameFor(pin));

        // A package already unpacked at this tag is used as it is. A tag is one commit, so fetching it again buys nothing.
        if (Completed(staged))
        {
            return Reused(staged, feed, pin, overridden);
        }

        var package = await FetchAsync(env, address, version, pin, overridden, cancellationToken).ConfigureAwait(false);

        // Read whole and checked whole before anything is written, so a package that is wrong leaves nothing behind it.
        using var archive = Open(package, pin);
        var entries = Checked(archive, pin);
        var commit = Commit(entries, pin);
        if (!entries.Any(entry => entry.FullName.Split('/').Length > 2))
        {
            throw new SkillsSourceUnavailable($"The skills package for '{pin}' holds no skills directory, so there is nothing here to install. Nothing was staged.");
        }

        Directory.CreateDirectory(env.Paths.SkillsStagedSourcesDirectory);
        SkillsSource.RemoveLeftovers(staged, Completed);

        var partial = $"{staged}.partial-{Guid.NewGuid():N}";
        var tree = Path.Combine(partial, "tree");
        bool placed;
        Directory.CreateDirectory(partial);
        using (new FileStream(Path.Combine(partial, SkillsSource.Claim), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            foreach (var entry in entries)
            {
                var target = Path.Combine(tree, Path.Combine(entry.FullName.Split('/')));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    entry.ExtractToFile(target, overwrite: false);
                }
                catch (InvalidDataException corrupt)
                {
                    throw new SkillsSourceUnavailable($"'{entry.FullName}' could not be read out of the skills package for '{pin}': {corrupt.Message}");
                }
            }

            // The last thing written before the move, so an unpack that ends any earlier is never taken for a finished one.
            await File.WriteAllTextAsync(Path.Combine(tree, CompletionMark), commit, cancellationToken).ConfigureAwait(false);

            try
            {
                Directory.Move(tree, staged);
                placed = true;
            }
            catch (IOException) when (Completed(staged))
            {
                // Another run unpacking the same tag finished first. A tag is one commit, so that tree is this one.
                placed = false;
            }
        }

        SkillsSource.TryRemove(partial);

        return placed ? new StagedSource(staged, feed, pin, overridden, commit) : Reused(staged, feed, pin, overridden);
    }

    /// <summary>A tree a package finished, with the commit its mark holds.</summary>
    private static StagedSource Reused(string staged, string feed, string pin, bool overridden)
    {
        SkillsSource.RequirePacks(staged, feed);
        var commit = File.ReadAllText(Path.Combine(staged, CompletionMark)).Trim();
        return new StagedSource(staged, feed, pin, overridden, commit.Length == 0 ? null : commit);
    }

    /// <summary>The release's checksums, then the package they list, held to its line, or a refusal naming the pin.</summary>
    private static async Task<byte[]> FetchAsync(CliEnvironment env, Uri feed, SemanticVersion version, string pin, bool overridden, CancellationToken cancellationToken)
    {
        var release = UpdateFeed.PinnedFor(feed, version);
        using var client = UpdateHttp.Client(env);
        // The deadline below is the one wait: the client's own hundred seconds would end a slow download first, and its
        // message would name a wait nobody set.
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SkillsSource.FetchTimeout);

        var sums = UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Checksums);
        var listed = Encoding.UTF8.GetString(await Fetch(client, sums, ReleaseFiles.MaxChecksumsBytes, $"a release's {ReleaseAssets.Checksums}", pin, overridden, cancellationToken, deadline.Token).ConfigureAwait(false));
        var expected = ReleaseChecksums.DigestOf(listed, ReleaseAssets.Skills, out var problem)
            ?? throw new SkillsSourceUnavailable(ReleaseChecksums.Lists(listed, ReleaseAssets.Skills)
                ? $"{problem} ({sums}) Nothing was staged."
                : NotPublished(pin, $"{sums} lists no {ReleaseAssets.Skills}.", overridden));

        var package = UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Skills);
        var bytes = await Fetch(client, package, MaxDownloadBytes, "the skills' package", pin, overridden, cancellationToken, deadline.Token).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new SkillsSourceUnavailable($"{package} does not match {ReleaseAssets.Checksums}: it hashes to {actual}, and the release lists {expected}. Nothing was staged.");
        }

        return bytes;
    }

    private static Task<byte[]> Fetch(HttpClient client, Uri address, long cap, string what, string pin, bool overridden, CancellationToken asked, CancellationToken deadline) =>
        ReleaseFiles.FetchAsync(
            client,
            address,
            cap,
            what,
            SkillsSource.FetchTimeout,
            refused => new SkillsSourceUnavailable(refused),
            answered => new SkillsSourceUnavailable(NotPublished(pin, answered, overridden)),
            asked,
            deadline);

    /// <summary>
    /// No release has published a package at the pin. The commonest case is the one this build creates: its pin is a
    /// release that is not published yet, and a directory works without one.
    /// </summary>
    private static string NotPublished(string pin, string answered, bool overridden) =>
        $"No skills package is published for '{pin}': {answered}"
        + (overridden
            ? " Name another release with --ref, or a git repository or a directory with --source."
            : $" This build's pinned ref is '{pin}'; if no release has published it yet, install from a directory with --source.");

    private static ZipArchive Open(byte[] package, string pin)
    {
        try
        {
            return new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException exception)
        {
            throw new SkillsSourceUnavailable($"The skills package for '{pin}' is not a readable zip archive: {exception.Message} Nothing was staged.");
        }
    }

    /// <summary>
    /// The file entries, each checked: a relative path of plain segments — names that start with a dot included, since each
    /// pack's <c>.claude-plugin</c> is one — under <c>skills/</c>, or <see cref="SourceFile"/> at the root; each once, no
    /// link, and within the bounds a package has by what they declare.
    /// </summary>
    private static List<ZipArchiveEntry> Checked(ZipArchive archive, string pin)
    {
        var files = new List<ZipArchiveEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/') && entry.Length == 0)
            {
                continue;
            }

            var segments = name.Split('/');
            if (name.StartsWith('/') || name.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(name)
                || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Any(Unsafe))
                || (name != SourceFile && (segments.Length < 2 || segments[0] != SkillsDirectory))
                || IsLink(entry))
            {
                throw new SkillsSourceUnavailable(
                    $"The skills package for '{pin}' holds '{name}', which is neither {SourceFile} nor a file under {SkillsDirectory}/. Nothing was staged.");
            }

            if (!names.Add(name))
            {
                throw new SkillsSourceUnavailable($"The skills package for '{pin}' holds '{name}' twice. Nothing was staged.");
            }

            files.Add(entry);
        }

        if (files.Count > PackageDigest.MaxFiles || files.Sum(entry => entry.Length) > PackageDigest.MaxBytes)
        {
            throw new SkillsSourceUnavailable(string.Create(
                CultureInfo.InvariantCulture,
                $"A skills package holds at most {PackageDigest.MaxFiles} files and {PackageDigest.MaxBytes} bytes; the one for '{pin}' holds more. Nothing was staged."));
        }

        return files;
    }

    /// <summary>A character no segment of a path here carries: a control character, or one a file name on some system cannot hold.</summary>
    private static bool Unsafe(char character) =>
        char.IsControl(character) || character is ':' or '*' or '?' or '"' or '<' or '>' or '|';

    /// <summary>What Unix zip tools write for a link: the entry's mode says it is one.</summary>
    private static bool IsLink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    /// <summary>
    /// The commit the package's <see cref="SourceFile"/> names, once it has said it is the tag it was fetched for.
    /// </summary>
    private static string Commit(List<ZipArchiveEntry> entries, string pin)
    {
        var source = entries.FirstOrDefault(entry => entry.FullName == SourceFile)
            ?? throw new SkillsSourceUnavailable($"The skills package for '{pin}' holds no {SourceFile}, so it says nothing a deployment could record. Nothing was staged.");
        if (source.Length > MaxSourceFileBytes)
        {
            throw new SkillsSourceUnavailable($"The {SourceFile} of the skills package for '{pin}' is larger than that file ever is. Nothing was staged.");
        }

        try
        {
            using var stream = source.Open();
            using var document = JsonDocument.Parse(stream);
            var said = document.RootElement;
            var reference = said.ValueKind == JsonValueKind.Object && said.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            var commit = said.ValueKind == JsonValueKind.Object && said.TryGetProperty("commit", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (reference != pin)
            {
                throw new SkillsSourceUnavailable(
                    $"The skills package fetched for '{pin}' says in its {SourceFile} that it is '{reference}'. Nothing was staged.");
            }

            if (commit is null || commit.Length != 40 || !commit.All(char.IsAsciiHexDigitLower))
            {
                throw new SkillsSourceUnavailable(
                    $"The {SourceFile} of the skills package for '{pin}' names no commit: forty lowercase hexadecimal digits. Nothing was staged.");
            }

            return commit;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new SkillsSourceUnavailable($"The {SourceFile} of the skills package for '{pin}' is not one this program reads: {exception.Message} Nothing was staged.");
        }
    }
}
