using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Jason.Cli.Doctor;
using Jason.Cli.Files;
using Jason.Cli.Ui;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Files;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;

namespace Jason.Cli.Plugins;

/// <summary>A package refused, with the sentence that says why; nothing was changed.</summary>
/// <param name="message">The sentence.</param>
/// <param name="inPlaceDiffers">
/// The refusal is that what is in place is not what the caller expected, so a caller that may say to replace anything
/// can say how.
/// </param>
public sealed class PluginInstallException(string message, bool inPlaceDiffers = false) : Exception(message)
{
    /// <summary>What is in place is not what the caller expected there.</summary>
    public bool InPlaceDiffers { get; } = inPlaceDiffers;
}

/// <summary>
/// What a caller expects in <c>plugins/&lt;id&gt;</c> before the release's package goes in: nothing, exactly one package by its
/// content digest, or anything at all. Anything else in place refuses the install, and the check is made again at the swap,
/// so what is compared is what is moved.
/// </summary>
public sealed record PluginInPlace
{
    private PluginInPlace(string? expectedDigest, bool acceptsAnything)
    {
        ExpectedDigest = expectedDigest;
        AcceptsAnything = acceptsAnything;
    }

    /// <summary>Nothing may be there: <c>jason plugin install</c> without <c>--replace</c>.</summary>
    public static PluginInPlace Nothing { get; } = new(null, acceptsAnything: false);

    /// <summary>Whatever is there is replaced: <c>jason plugin install --replace</c>.</summary>
    public static PluginInPlace Anything { get; } = new(null, acceptsAnything: true);

    /// <summary>The digest the package in place must have, or null when nothing or anything is expected.</summary>
    public string? ExpectedDigest { get; }

    /// <summary>Whether anything in place is replaced.</summary>
    public bool AcceptsAnything { get; }

    /// <summary>Exactly the package with this content digest must be there: a caller that read it and decided to replace it.</summary>
    public static PluginInPlace Digest(string digest)
    {
        ArgumentException.ThrowIfNullOrEmpty(digest);
        return new PluginInPlace(digest, acceptsAnything: false);
    }
}

/// <summary>What became of the plugins directory: a package where there was none, one swapped for another, or the release's already there.</summary>
public enum PluginPlacement
{
    New,
    Replaced,
    AlreadyInPlace,
}

/// <summary>
/// The package now in place as <see cref="Directory"/>: the release's, by its version and content digest, and where the one it
/// replaced was kept, when the caller asked for it to be kept.
/// </summary>
public sealed record PluginPlaced(string Id, SemanticVersion Version, string Directory, string Digest, int FileCount, PluginPlacement Placement, string? Kept = null);

/// <summary>Whether the runtime loaded what was placed: it did, it kept the plugins it had, or none answered and it loads it when it starts.</summary>
public enum PluginLoad
{
    Activated,
    Rejected,
    AtNextStart,
}

/// <summary>
/// What <c>jason plugin install</c> prints: the package in place, how it came to be there, whether it is loaded, and, when
/// the runtime answered and refused the reload, its refusal as it gave it.
/// </summary>
public sealed record PluginInstallResponse(
    string Id,
    string Version,
    string Directory,
    string Digest,
    int Files,
    PluginPlacement Placed,
    PluginLoad Load,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ErrorBody? Error = null);

/// <summary>
/// A plugin package a release publishes, placed in the plugins directory: <c>jason plugin install</c>'s body, written to be
/// called as it is by anything else that places one. The release's manifest names the package — its file and the content
/// digest of the tree it unpacks to — and the package is fetched from the release's own directory beside its checksums.txt,
/// never from an address either file names, held to its line there, unpacked beside the plugins it joins, held to that
/// digest, and moved in whole. A refusal leaves the plugins directory as it was.
/// </summary>
/// <remarks>
/// It places and asks the runtime nothing: loading a package is <c>plugin.reload</c>'s, which the command asks for after,
/// and a caller that stops the runtime around its work has the runtime load it at its start. What may be in place is the
/// caller's to say (<see cref="PluginInPlace"/>), and the release's own package already in place is left as it is whatever
/// the caller expected, so a call made again after one that went through does nothing.
/// </remarks>
public static class PluginInstaller
{
    /// <summary>A package is held to 16 MB unpacked; an archive twice that is not one, whatever the address served.</summary>
    public static long MaxDownloadBytes => 2 * PackageDigest.MaxBytes;

    /// <summary>A package of a few hundred kilobytes: what has not arrived in two minutes is not arriving.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a rename of a package's directory is tried while a file in it is held, and how often: the uninstall's wait.
    /// Windows refuses to rename a directory with access denied while anything holds a file inside the tree, a scanner
    /// reading what was just unpacked or an editor with a file open, so here an access-denied counts as a hold.
    /// </summary>
    internal static HeldFileRule PackageMoves { get; } = InstallationRemovers.HeldFiles with { AccessDeniedCounts = true };

    /// <summary>
    /// Called just before each rename of a package's directory, with where from and where to; what it throws is that
    /// rename's refusal. A test's only way to refuse one move of the two and not the other: what the swap does then
    /// cannot otherwise be shown except by racing another program for the directory, and a race a test may lose is no proof.
    /// </summary>
    internal static AsyncLocal<Action<string, string>?> BeforeMove { get; } = new();

    /// <summary>
    /// <c>jason plugin install</c>'s rule: the package <paramref name="id"/> that the release of <paramref name="version"/>
    /// publishes goes where nothing is, or, when <paramref name="replace"/> says so, in place of whatever is there, which is
    /// not kept.
    /// </summary>
    /// <exception cref="PluginInstallException">The release, its manifest, its checksums.txt or the package refused it.</exception>
    public static async Task<PluginPlaced> InstallAsync(CliEnvironment env, Uri feed, SemanticVersion version, string id, bool replace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(feed);
        Checked(id, feed);

        var release = UpdateFeed.PinnedFor(feed, version);
        var package = await PackageAsync(env, release, version, id, cancellationToken).ConfigureAwait(false);
        return await PlaceAsync(env, release, id, package, replace ? PluginInPlace.Anything : PluginInPlace.Nothing, keepDirectory: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Places <paramref name="package"/>, which the release whose manifest is at <paramref name="release"/> names as
    /// <paramref name="id"/>, in place of what <paramref name="expected"/> says is there, or changes nothing and says why.
    /// </summary>
    /// <param name="env">Where Jason's data is, and how the release is reached.</param>
    /// <param name="release">The address of the release's own manifest; the package and checksums.txt are fetched beside it.</param>
    /// <param name="id">The plugin's id: its directory's name.</param>
    /// <param name="package">The manifest's entry for the package: its file and the content digest of the tree it unpacks to.</param>
    /// <param name="expected">What must be in place before the package goes in; checked before anything is fetched and again at the swap.</param>
    /// <param name="keepDirectory">
    /// Where the package replaced is moved to, on the same volume, and kept; it must not exist yet. Null: the package replaced
    /// is deleted once the release's is in.
    /// </param>
    /// <param name="cancellationToken">Ends the downloads, and a wait on a held file.</param>
    /// <exception cref="PluginInstallException">The release, its checksums.txt, the package or what is in place refused it.</exception>
    public static Task<PluginPlaced> PlaceAsync(
        CliEnvironment env,
        Uri release,
        string id,
        UpdatePluginPackage package,
        PluginInPlace expected,
        string? keepDirectory,
        CancellationToken cancellationToken) =>
        PlaceAsync(env, release, id, package, expected, keepDirectory, PackageMoves, TimeProvider.System, HeldFileRetry.Sleep, cancellationToken);

    /// <summary>The same, under a held-file rule, a clock and a pause of a test's own.</summary>
    internal static async Task<PluginPlaced> PlaceAsync(
        CliEnvironment env,
        Uri release,
        string id,
        UpdatePluginPackage package,
        PluginInPlace expected,
        string? keepDirectory,
        HeldFileRule rule,
        TimeProvider clock,
        Action<TimeSpan, CancellationToken> pause,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(expected);
        Checked(id, release);

        var target = env.Paths.PluginPackageDirectory(id);
        var existing = Existing(target);
        if (existing is not null && string.Equals(existing.Digest, package.Digest, StringComparison.Ordinal))
        {
            return new PluginPlaced(id, package.Version, target, existing.Digest, existing.FileCount, PluginPlacement.AlreadyInPlace);
        }

        Expect(target, existing, expected, id, package);
        if (keepDirectory is not null && Path.Exists(keepDirectory))
        {
            throw new PluginInstallException($"{keepDirectory}, where the package replaced is to be kept, already exists. Nothing was changed.");
        }

        using var client = UpdateHttp.Client(env);
        // The deadline below is the one wait: the client's own hundred seconds would end a slow download first, and its
        // message would name a wait nobody set.
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        var sums = UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Checksums);
        var listed = Encoding.UTF8.GetString(await FetchAsync(client, sums, ReleaseFiles.MaxChecksumsBytes, $"a release's {ReleaseAssets.Checksums}", cancellationToken, deadline.Token).ConfigureAwait(false));
        var checksum = ReleaseChecksums.DigestOf(listed, package.Asset, out var problem)
            ?? throw new PluginInstallException(ReleaseChecksums.Lists(listed, package.Asset)
                ? $"{problem} ({sums}) Nothing was changed."
                : $"{sums} lists no {package.Asset}, the file the release's manifest names for {id}. Nothing was changed.");

        var address = UpdateFeed.DownloadUrlFor(release, package.Asset);
        var bytes = await FetchAsync(client, address, MaxDownloadBytes, "a plugin package", cancellationToken, deadline.Token).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, checksum, StringComparison.Ordinal))
        {
            throw new PluginInstallException($"{address} does not match {ReleaseAssets.Checksums}: it hashes to {actual}, and the release lists {checksum}. Nothing was changed.");
        }

        var moves = new Moves(env, rule, clock, pause, cancellationToken);
        return Place(bytes, env.Paths.PluginsDirectory, target, id, package, expected, keepDirectory, moves);
    }

    private static void Checked(string id, Uri release)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!PluginIds.IsWellFormed(id))
        {
            throw new PluginInstallException($"'{id}' is not a plugin's id.");
        }

        if (!UpdateFeed.IsAllowed(release))
        {
            throw new PluginInstallException($"'{release}' is not a feed this program reads: an https address, or http on this machine.");
        }
    }

    /// <summary>
    /// Refuses when what is in place as <paramref name="target"/> is not what <paramref name="expected"/> says: the one
    /// compare, made before anything is fetched and again at the swap.
    /// </summary>
    private static void Expect(string target, PackageDigestResult? existing, PluginInPlace expected, string id, UpdatePluginPackage package)
    {
        if (expected.AcceptsAnything)
        {
            return;
        }

        if (expected.ExpectedDigest is null)
        {
            if (Path.Exists(target))
            {
                throw new PluginInstallException(
                    $"{target} already holds {Described(existing, package)}, not the release's {id} {package.Version}. Nothing was changed.",
                    inPlaceDiffers: true);
            }

            return;
        }

        if (existing is null || !string.Equals(existing.Digest, expected.ExpectedDigest, StringComparison.Ordinal))
        {
            throw new PluginInstallException(
                Path.Exists(target)
                    ? $"{target} holds {Described(existing, package)}, and {expected.ExpectedDigest} was expected there. Nothing was changed."
                    : $"{target} holds nothing, and {expected.ExpectedDigest} was expected there. Nothing was changed.",
                inPlaceDiffers: true);
        }
    }

    /// <summary>The package the release's manifest names as <paramref name="id"/>, from a manifest of this very release.</summary>
    private static async Task<UpdatePluginPackage> PackageAsync(CliEnvironment env, Uri release, SemanticVersion version, string id, CancellationToken cancellationToken)
    {
        UpdateManifest manifest;
        using (var client = UpdateHttp.Client(env))
        {
            client.Timeout = UpdateFeed.DefaultTimeout;
            try
            {
                manifest = await new UpdateFeed(client).ReadAsync(release, cancellationToken).ConfigureAwait(false);
            }
            catch (UpdateFeedException unread)
            {
                throw new PluginInstallException($"{unread.Message} Nothing was changed.");
            }
        }

        if (manifest.Version != version)
        {
            throw new PluginInstallException($"The release at {release} is {manifest.Version}, and this build is {version}: its packages are not this build's. Nothing was changed.");
        }

        if (manifest.Plugins.TryGetValue(id, out var package))
        {
            return package;
        }

        var named = manifest.Plugins.Keys.Order(StringComparer.Ordinal).ToList();
        throw new PluginInstallException(named.Count == 0
            ? $"The release {version} publishes no plugin package, so there is no '{id}' to install. Nothing was changed."
            : $"The release {version} publishes no plugin package '{id}'; it publishes {string.Join(", ", named)}. Nothing was changed.");
    }

    /// <summary>The package in place as <paramref name="target"/>, by its digest, or null when there is none or it cannot be read as one.</summary>
    private static PackageDigestResult? Existing(string target)
    {
        try
        {
            return File.Exists(Path.Combine(target, InstalledPlugins.ManifestFileName)) ? PackageDigest.Compute(target) : null;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException or PackageTooLargeException)
        {
            return null;
        }
    }

    /// <summary>What is in place, as a person reads it: a version the release's record knows by its digest, or a package it does not know.</summary>
    private static string Described(PackageDigestResult? existing, UpdatePluginPackage package) =>
        existing is null
            ? "something that is not a package this program can read"
            : package.VersionOf(existing.Digest) is { } recorded
                ? $"{recorded} of that package ({existing.Digest})"
                : $"a package the release's record does not know ({existing.Digest}): built or edited on this machine, or an older version";

    /// <summary>
    /// Unpacks <paramref name="bytes"/> beside the plugins in <paramref name="plugins"/>, holds the tree to the manifest's
    /// digest, and swaps it in as <paramref name="target"/>. The staging directory's name starts with a dot, so a reload that
    /// reads the plugins directory meanwhile never takes it for a package.
    /// </summary>
    private static PluginPlaced Place(byte[] bytes, string plugins, string target, string id, UpdatePluginPackage package, PluginInPlace expected, string? keep, Moves moves)
    {
        Directory.CreateDirectory(plugins);
        var staging = Path.Combine(plugins, $".staging-{id}-{Guid.NewGuid():N}");
        try
        {
            using (var archive = Open(bytes))
            {
                foreach (var entry in Checked(archive))
                {
                    var file = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    Extract(entry, file);
                }
            }

            Directory.CreateDirectory(staging);
            var unpacked = Digest(staging);
            if (!string.Equals(unpacked.Digest, package.Digest, StringComparison.Ordinal))
            {
                throw new PluginInstallException(
                    $"The package unpacks to {unpacked.Digest}, and the release's manifest says {id} {package.Version} is {package.Digest}. Nothing was changed.");
            }

            var replaced = Swap(staging, target, keep, moves, inPlace => Expect(target, inPlace, expected, id, package));
            return new PluginPlaced(
                id,
                package.Version,
                target,
                unpacked.Digest,
                unpacked.FileCount,
                replaced ? PluginPlacement.Replaced : PluginPlacement.New,
                replaced ? keep : null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new PluginInstallException($"The package could not be placed in {target}: {failure.Message} Nothing was changed.");
        }
        finally
        {
            UiAssetInstaller.Discard(() => DirectoryTrees.Delete(staging), staging);
        }
    }

    private static ZipArchive Open(byte[] bytes)
    {
        try
        {
            return new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException unreadable)
        {
            throw new PluginInstallException($"The package is not a readable zip archive: {unreadable.Message} Nothing was changed.");
        }
    }

    /// <summary>
    /// The file entries, each checked before anything is unpacked: a relative path of plain segments, none hidden — the digest
    /// would not count a hidden one, so it would land beside the package unvouched for — and none that Windows would read as
    /// something else: a drive or an alternate stream after a colon, which the digest never sees, a control character, a
    /// trailing dot or space it drops. None a link, and none a name <see cref="ArchiveNames"/> refuses. None twice, within
    /// what a package may hold by what they declare, and a <c>plugin.yaml</c> at the root.
    /// </summary>
    private static List<ZipArchiveEntry> Checked(ZipArchive archive)
    {
        var files = new List<ZipArchiveEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/'))
            {
                continue;
            }

            var segments = name.Split('/');
            if (name.StartsWith('/') || name.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(name)
                || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.StartsWith('.')
                    || segment.Contains(':', StringComparison.Ordinal) || segment.Any(char.IsControl) || segment.EndsWith('.') || segment.EndsWith(' ')))
            {
                throw new PluginInstallException(
                    $"'{Shown(name)}' is not a file of a plugin package: entries are relative paths of plain segments, none hidden, none naming a drive or a stream, none holding a control character, none ending in a dot or a space. Nothing was changed.");
            }

            // The rules every archive this CLI unpacks is held to, beside the package's own.
            if (!ArchiveNames.Plain(name) || ArchiveNames.IsLink(entry))
            {
                throw new PluginInstallException(
                    $"'{Shown(name)}' is not a file of a plugin package: it is a link, or a name some system cannot hold, a device name such as CON or NUL among them. Nothing was changed.");
            }

            if (!names.Add(name))
            {
                throw new PluginInstallException($"'{name}' appears twice in the package. Nothing was changed.");
            }

            files.Add(entry);
        }

        if (files.Count > PackageDigest.MaxFiles || files.Sum(entry => entry.Length) > PackageDigest.MaxBytes)
        {
            throw new PluginInstallException(string.Create(
                CultureInfo.InvariantCulture,
                $"A plugin package holds at most {PackageDigest.MaxFiles} files and {PackageDigest.MaxBytes} bytes; this one holds more. Nothing was changed."));
        }

        if (!names.Contains(InstalledPlugins.ManifestFileName))
        {
            throw new PluginInstallException($"The package holds no {InstalledPlugins.ManifestFileName} at its root, so it is not a plugin package. Nothing was changed.");
        }

        return files;
    }

    /// <summary>An entry's name as a refusal can print it: a control character as its <c>\uXXXX</c> escape.</summary>
    private static string Shown(string name) =>
        string.Concat(name.Select(character => char.IsControl(character)
            ? "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture)
            : character.ToString()));

    /// <summary>An entry yields no more than the length it declares, so the sum the check added up bounds what lands on disk.</summary>
    private static void Extract(ZipArchiveEntry entry, string file)
    {
        try
        {
            entry.ExtractToFile(file, overwrite: false);
        }
        catch (InvalidDataException corrupt)
        {
            throw new PluginInstallException($"'{entry.FullName}' could not be read out of the package: {corrupt.Message} Nothing was changed.");
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
            throw new PluginInstallException($"{refused.Message} Nothing was changed.");
        }
    }

    /// <summary>
    /// What is in place is compared once more, then moves aside — to <paramref name="keep"/> when the caller keeps it — the
    /// staging moves in, and an old one not kept goes; a failure between the two moves puts the old one back, and when that
    /// fails too the refusal says where the old one is. Every move is a rename within one volume, and each waits under the
    /// held-file rule while Windows reports a file in it held, as a scanner holds one it is reading. Returns whether a
    /// package was replaced.
    /// </summary>
    private static bool Swap(string staging, string target, string? keep, Moves moves, Action<PackageDigestResult?> compare)
    {
        compare(Existing(target));
        var replacing = Path.Exists(target);
        var aside = keep ?? Path.Combine(Path.GetDirectoryName(target)!, $".old-{Path.GetFileName(target)}-{Guid.NewGuid():N}");
        if (replacing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
            moves.Move(target, aside);
        }

        try
        {
            moves.Move(staging, target);
        }
        catch
        {
            if (replacing && Directory.Exists(aside) && !Path.Exists(target) && moves.MoveBack(aside, target) is { } stuck)
            {
                throw new PluginInstallException(
                    $"The release's package was not placed in {target}, and the package that was there could not be moved back: {stuck} It is in {aside}; move it back to {target} to use it again.");
            }

            throw;
        }

        if (replacing && keep is null)
        {
            UiAssetInstaller.Discard(() => DirectoryTrees.Delete(aside), aside);
        }

        return replacing;
    }

    /// <summary>One rename of a directory, tried again while a file in it is held, and given up at the end of the rule's wait.</summary>
    private sealed record Moves(CliEnvironment Env, HeldFileRule Rule, TimeProvider Clock, Action<TimeSpan, CancellationToken> Pause, CancellationToken CancellationToken)
    {
        public void Move(string from, string to)
        {
            Exception? held;
            try
            {
                held = Run(from, to, CancellationToken);
            }
            catch (Exception refused) when (HeldFileRetry.IsAccessDenied(refused))
            {
                throw new PluginInstallException($"{AccessRefused.Said(from, IdentityFacts.HomeFor(Env.Identity))} {NothingChanged}");
            }

            if (held is not null)
            {
                throw new PluginInstallException($"{Held(from, held)} {NothingChanged}");
            }
        }

        /// <summary>
        /// The rename under the rule, with the package's own files asked about an access-denied: a permission is thrown at
        /// once, and a held file is waited for.
        /// </summary>
        private Exception? Run(string from, string to, CancellationToken cancellationToken) =>
            HeldFileRetry.Run(() => Rename(from, to), Rule, Clock, Pause, cancellationToken, () => HeldFileReader.Read(from));

        /// <summary>
        /// The rename back of a package moved aside, under the same wait: null when it went through, or why it did not. It is
        /// tried even when the install was cancelled, since a package left aside is worse than the wait.
        /// </summary>
        public string? MoveBack(string from, string to)
        {
            try
            {
                return Run(from, to, CancellationToken.None) is { } held ? Held(from, held) : null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return HeldFileRetry.IsAccessDenied(failure) ? new AccessRefused(failure).Message : failure.Message;
            }
        }

        private string Held(string from, Exception held) =>
            new HeldOpen(from, isDirectory: true, Rule, held).Said(IdentityFacts.HomeFor(Env.Identity));

        /// <summary>
        /// The rename. The access-denied that Windows answers with for a held file inside the tree arrives as an IOException
        /// with that code, which the held-file rule counts as a hold wherever it counts an access-denied.
        /// </summary>
        private static void Rename(string from, string to)
        {
            BeforeMove.Value?.Invoke(from, to);
            Directory.Move(from, to);
        }
    }

    /// <summary>How every refusal before the swap ends, the release's own refusals of a file included.</summary>
    private const string NothingChanged = "Nothing was changed.";

    private static Task<byte[]> FetchAsync(HttpClient client, Uri address, long cap, string what, CancellationToken asked, CancellationToken deadline) =>
        ReleaseFiles.FetchAsync(
            client,
            address,
            cap,
            what,
            Deadline,
            refused => new PluginInstallException(
                refused.EndsWith(NothingChanged, StringComparison.Ordinal) ? refused : $"{refused} {NothingChanged}"),
            answered => new PluginInstallException($"{answered} {NothingChanged}"),
            asked,
            deadline);
}
