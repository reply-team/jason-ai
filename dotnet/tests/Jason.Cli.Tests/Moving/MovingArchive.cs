using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Jason.Cli.Moving;
using Jason.Contracts.Harness;

namespace Jason.Cli.Tests.Moving;

/// <summary>
/// An archive as <c>jason export</c> writes one, made by hand so that a row can make it wrong in exactly one way: a name, a
/// digest, a version, an entry the listing does not name.
/// </summary>
internal sealed class MovingArchive
{
    public static readonly DateTimeOffset ExportedAt = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    public MovingArchive(MovingMachine from)
    {
        From = from;
    }

    public MovingMachine From { get; }

    /// <summary>The files the archive holds and lists, by their names in it.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public List<string> Directories { get; } = [];

    public string Version { get; set; } = MovingMachine.Version;

    /// <summary>The layout the archive says it carries; null is an archive written before the layout stamp, which says none.</summary>
    public int? Layout { get; set; } = 1;

    public bool Registered { get; set; }

    /// <summary>Changes the listing after it is composed from the files.</summary>
    public Func<ExportManifest, ExportManifest>? Listing { get; set; }

    /// <summary>Adds entries to the archive that the listing knows nothing of.</summary>
    public Action<ZipArchive>? Beside { get; set; }

    public MovingArchive With(string name, string text = "made up")
    {
        Files[name] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    /// <summary>A folder's marker, as the runtime writes it.</summary>
    public MovingArchive Mark(string folder, string kind, string? id)
    {
        Files[folder + "/" + HarnessMarker.Directory + "/" + HarnessMarker.FileName] = new HarnessMarker(kind, id).Bytes();
        return this;
    }

    /// <summary>A small installation: a database, a settings file and one project folder with one campaign.</summary>
    public MovingArchive Installation()
    {
        With("data/state/jason.db", "a database, made up");
        With("data/config/settings.json", "{}");
        With("data/logs/jason-20261108.log", "{}");
        Mark("harness", HarnessMarker.Root, null);
        Mark("harness/projects/acme", HarnessMarker.Project, "prj_01ACME");
        With("harness/projects/acme/AGENTS.md", "# Acme");
        Mark("harness/projects/acme/campaigns/q4", HarnessMarker.Campaign, "cmp_01Q4");
        return this;
    }

    public ExportManifest Manifest()
    {
        var manifest = new ExportManifest(
            ExportManifest.CurrentFormat,
            Version,
            Layout,
            ExportedAt,
            ExportManifest.ThisSystem(),
            From.Data,
            From.Harness,
            From.Home,
            new ExportAutostart(Registered, "windows"),
            [.. Files.Select(file => new ExportEntry(file.Key, file.Value.Length, Convert.ToHexStringLower(SHA256.HashData(file.Value))))],
            Directories);
        return Listing is null ? manifest : Listing(manifest);
    }

    /// <summary>Writes it under the source machine's folder and says where.</summary>
    public string Write(string name = "jason-export-20261108T093000Z.zip")
    {
        var path = Path.Combine(From.Here, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var directory in Directories)
            {
                archive.CreateEntry(directory);
            }

            foreach (var (entryName, bytes) in Files)
            {
                using var stream = archive.CreateEntry(entryName).Open();
                stream.Write(bytes);
            }

            Beside?.Invoke(archive);
            using var listing = archive.CreateEntry(ExportManifest.FileName).Open();
            listing.Write(Manifest().Serialize());
        }

        return path;
    }
}
