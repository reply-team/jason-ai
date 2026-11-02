using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jason.Cli.Tests.Ui;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// A release built by the test: the archive, the manifest that describes it, and a transport that answers for
/// exactly the two addresses a real feed would. Nothing here touches the network, and nothing is checked in —
/// an archive that lives in the repository is an archive nobody can make hostile.
/// </summary>
public sealed class FakeRelease : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>The plugin packages the manifest names, by id, as the lines of its <c>plugins</c> object.</summary>
    private readonly SortedDictionary<string, string> _plugins = new(StringComparer.Ordinal);

    public FakeRelease(SemanticVersion version, byte[]? archive = null, string? asset = null, string? digest = null, long? size = null)
    {
        Version = version;
        Asset = asset ?? ReleaseAssets.For(Rid);
        var bytes = archive ?? Archive(Asset, Contents(version));
        _files[Asset] = bytes;

        Digest = digest ?? Convert.ToHexStringLower(SHA256.HashData(bytes));
        Size = size ?? bytes.LongLength;
        _files[ReleaseAssets.Manifest] = Encoding.UTF8.GetBytes(Manifest());
        WithUi(UiZip.Build(version.ToString()));
    }

    /// <summary>The release's UI package, and checksums.txt listing it after the archive, as a release publishes both.</summary>
    public FakeRelease WithUi(byte[] zip, string? digest = null)
    {
        _files[UiAssetLayout.AssetName] = zip;
        return ChecksumsSay($"{Digest}  {Asset}\n{digest ?? Convert.ToHexStringLower(SHA256.HashData(zip))}  {UiAssetLayout.AssetName}\n");
    }

    /// <summary>A release from before the UI was published: no package, and checksums.txt lists the archive alone.</summary>
    public FakeRelease WithoutUi()
    {
        _files.Remove(UiAssetLayout.AssetName);
        return ChecksumsSay($"{Digest}  {Asset}\n");
    }

    /// <summary>
    /// A plugin package the release publishes beside its archives: the file, named in the manifest's <c>plugins</c> with its
    /// version and content digest, and listed in checksums.txt after what is listed there already. <paramref name="checksum"/>
    /// is what that line says, the package's own hash when not given; <paramref name="contentDigest"/> is what the manifest
    /// says the unpacked tree hashes to. <paramref name="earlier"/> are the earlier versions the record holds beside it.
    /// </summary>
    public FakeRelease WithPlugin(
        string id,
        SemanticVersion version,
        byte[] zip,
        string contentDigest,
        string? checksum = null,
        IEnumerable<(SemanticVersion Version, string Digest)>? earlier = null)
    {
        var asset = $"jason-plugin-{id}.zip";
        _files[asset] = zip;
        var recorded = string.Join(", ", (earlier ?? []).Append((version, contentDigest)).Select(entry => $"\"{entry.Item1}\": \"{entry.Item2}\""));
        _plugins[id] = $$"""
            "{{id}}": { "version": "{{version}}", "asset": "{{asset}}", "digest": "{{contentDigest}}", "recorded": { {{recorded}} } }
            """;
        _files[ReleaseAssets.Manifest] = Encoding.UTF8.GetBytes(Manifest());
        var listed = Encoding.UTF8.GetString(_files[ReleaseAssets.Checksums]);
        return ChecksumsSay($"{listed}{checksum ?? Convert.ToHexStringLower(SHA256.HashData(zip))}  {asset}\n");
    }

    /// <summary>Replaces what checksums.txt says, for a test about a file that says something wrong.</summary>
    public FakeRelease ChecksumsSay(string text)
    {
        _files[ReleaseAssets.Checksums] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    /// <summary>The platform this test is running on: what a real applier would ask for.</summary>
    public static string Rid => ReleaseAssets.CurrentRid ?? "linux-x64";

    public SemanticVersion Version { get; }

    public string Asset { get; }

    public string Digest { get; }

    public long Size { get; }

    /// <summary>What the release's own feed address would be, if this were a release.</summary>
    public Uri Feed { get; private set; } = new("https://127.0.0.1/releases/latest/download/manifest.json");

    /// <summary>
    /// Serves this release from a plain directory instead — <c>http://127.0.0.1:8099/manifest.json</c> with the
    /// archive beside it, which is how CI serves one and how a mirror or a file server would.
    /// </summary>
    /// <remarks>
    /// A feed of this shape says nothing about where its older releases live, and a caller that invents an
    /// address for them asks for a path that was never going to exist. That really happened, and it took a CI
    /// runner to find it: every stub here was a GitHub-shaped address until now.
    /// </remarks>
    public FakeRelease ServedFromADirectory()
    {
        Feed = new Uri("http://127.0.0.1:8099/manifest.json");
        return this;
    }

    /// <summary>Every address this handler was asked for, so a test can assert what was fetched and from where.</summary>
    public List<string> Requested { get; } = [];

    /// <summary>The <c>User-Agent</c> each request carried, by the address it asked for, the last one kept.</summary>
    public Dictionary<string, string> UserAgents { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The length the UI's package is said to be, where a test wants a server that says less than it sends: a proxy, or
    /// the wrong file at the address. Unset, the length is the body's.
    /// </summary>
    public long? UiLengthSays { get; set; }

    /// <summary>Set to fail the next download in the way a network does.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>
    /// What a release's archive holds: an executable that says which version it is — the same thing a real one
    /// says when it is asked — and the licence the archive is required to carry.
    /// </summary>
    public static IEnumerable<(string Name, byte[] Content)> Contents(SemanticVersion version) =>
    [
        (ReleaseAssets.ExecutableName, Encoding.UTF8.GetBytes($"jason {version}")),
        ("LICENSE", Encoding.UTF8.GetBytes("MIT")),
    ];

    /// <summary>The bytes of a well-formed archive holding one executable and a licence, as a release publishes.</summary>
    public static byte[] Archive(string asset, IEnumerable<(string Name, byte[] Content)>? entries = null)
    {
        var contents = entries?.ToList() ??
        [
            (ReleaseAssets.ExecutableName, Encoding.UTF8.GetBytes("#!/bin/sh\necho jason\n")),
            ("LICENSE", Encoding.UTF8.GetBytes("MIT")),
        ];

        return asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? Zip(contents) : TarGz(contents);
    }

    public string Manifest(string? version = null, string? digest = null, long? size = null) => $$"""
        {
          "schema": 1,
          "version": "{{version ?? Version.ToString()}}",
          "published_at": "2026-09-19T08:00:00Z",
          "release_notes_url": null,
          "min_upgrade_from": null,
          "artifacts": {
            "{{Rid}}": { "asset": "{{Asset}}", "sha256": "{{digest ?? Digest}}", "size": {{size ?? Size}} }
          },
          "plugins": { {{string.Join(", ", _plugins.Values)}} }
        }
        """;

    /// <summary>Replaces the manifest this feed answers with, for a test about what a feed says.</summary>
    public void Says(string manifest) => _files[ReleaseAssets.Manifest] = Encoding.UTF8.GetBytes(manifest);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Answer(request));

    /// <summary>
    /// The same answer, for a handler that stands in front of this one: an installation routes the runtime's
    /// own address to itself and everything else to the release.
    /// </summary>
    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri!.AbsolutePath;
        Requested.Add(path);
        UserAgents[path] = request.Headers.UserAgent.ToString();

        if (FailWith is { } failure)
        {
            return new HttpResponseMessage(failure) { Content = new ByteArrayContent([]) };
        }

        // Only the two addresses a release really serves. Answering by file name whatever directory was asked
        // for is what let a composed-from-nothing path pass every test here and then 404 on a runner: a stub
        // that is more forgiving than the thing it stands in for proves the caller works against the stub.
        string[] served = Feed.AbsolutePath.StartsWith("/releases/", StringComparison.Ordinal)
            ?
            [
                $"/releases/latest/download/{ReleaseAssets.Manifest}",
                $"/releases/latest/download/{Asset}",
                $"/releases/download/v{Version}/{ReleaseAssets.Manifest}",
                $"/releases/download/v{Version}/{Asset}",
                $"/releases/download/v{Version}/{ReleaseAssets.Checksums}",
                $"/releases/download/v{Version}/{UiAssetLayout.AssetName}",
                .. _plugins.Keys.Select(id => $"/releases/download/v{Version}/jason-plugin-{id}.zip"),
            ]
            : [$"/{ReleaseAssets.Manifest}", $"/{Asset}", $"/{ReleaseAssets.Checksums}", $"/{UiAssetLayout.AssetName}", .. _plugins.Keys.Select(id => $"/jason-plugin-{id}.zip")];

        var name = path[(path.LastIndexOf('/') + 1)..];
        if (!served.Contains(path, StringComparer.Ordinal) || !_files.TryGetValue(name, out var bytes))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
        }

        var content = new ByteArrayContent(bytes);
        if (name == UiAssetLayout.AssetName && UiLengthSays is { } says)
        {
            content.Headers.ContentLength = says;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>What an entry of a test's archive is.</summary>
    public enum EntryKind
    {
        File,
        Directory,

        /// <summary>A link whose target is the entry's content, as text.</summary>
        SymbolicLink,
    }

    /// <summary>One entry of a test's archive, of any kind a hostile archive could hold.</summary>
    public sealed record Entry(string Name, byte[] Content, EntryKind Kind = EntryKind.File)
    {
        public Entry(string name, string text, EntryKind kind = EntryKind.File)
            : this(name, Encoding.UTF8.GetBytes(text), kind)
        {
        }
    }

    /// <summary>
    /// An archive holding exactly <paramref name="entries"/>, in order, each of the kind it says. A link in a zip is what
    /// Unix zip tools write: the entry's mode says it is one, and its content is the target.
    /// </summary>
    public static byte[] ArchiveOf(string asset, IEnumerable<Entry> entries)
    {
        using var memory = new MemoryStream();
        if (asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var entry in entries)
            {
                var written = zip.CreateEntry(entry.Kind is EntryKind.Directory ? entry.Name + "/" : entry.Name);
                if (entry.Kind is EntryKind.SymbolicLink)
                {
                    written.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
                }

                if (entry.Kind is not EntryKind.Directory)
                {
                    using var stream = written.Open();
                    stream.Write(entry.Content);
                }
            }
        }
        else
        {
            using var gzip = new GZipStream(memory, CompressionMode.Compress, leaveOpen: true);
            using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
            foreach (var entry in entries)
            {
                tar.WriteEntry(entry.Kind switch
                {
                    EntryKind.Directory => new PaxTarEntry(TarEntryType.Directory, entry.Name + "/"),
                    EntryKind.SymbolicLink => new PaxTarEntry(TarEntryType.SymbolicLink, entry.Name) { LinkName = Encoding.UTF8.GetString(entry.Content) },
                    _ => new PaxTarEntry(TarEntryType.RegularFile, entry.Name) { DataStream = new MemoryStream(entry.Content) },
                });
            }
        }

        return memory.ToArray();
    }

    private static byte[] Zip(IReadOnlyList<(string Name, byte[] Content)> entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return memory.ToArray();
    }

    private static byte[] TarGz(IReadOnlyList<(string Name, byte[] Content)> entries)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(content),
                };
                tar.WriteEntry(entry);
            }
        }

        return memory.ToArray();
    }
}
