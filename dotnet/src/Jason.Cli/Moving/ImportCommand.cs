using System.CommandLine;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Files;
using Jason.Cli.Logs;
using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;

namespace Jason.Cli.Moving;

/// <summary>What was asked of <c>jason import</c>, as parsed.</summary>
/// <param name="File">The archive <c>jason export</c> wrote, as typed.</param>
/// <param name="Replace">Move an installation that is here aside and place this one instead.</param>
/// <param name="Root">The harness root to place the archive's into, as typed; null for the one this machine resolves.</param>
/// <param name="Stop">Stop a running runtime for the import.</param>
/// <param name="Human">Render for people instead of printing JSON.</param>
public sealed record ImportOptions(string File, bool Replace, string? Root, bool Stop, bool Human)
{
    /// <summary>How long the stop waits for the runtime to go; null for <c>runtime stop</c>'s own.</summary>
    internal TimeSpan? StopTimeout { get; init; }

    /// <summary>How long the start waits for the runtime to answer; null for <c>runtime start</c>'s own.</summary>
    internal TimeSpan? StartTimeout { get; init; }

    /// <summary>How often the stop and the start look; null for their own.</summary>
    internal TimeSpan? Poll { get; init; }
}

/// <summary>A project folder the import asked the runtime to record where it now stands.</summary>
/// <param name="ProjectId">The project its marker names.</param>
/// <param name="Path">The folder here.</param>
public sealed record ImportLinked(string ProjectId, string Path);

/// <summary>A project folder whose link the runtime refused; the folder is in place.</summary>
/// <param name="ProjectId">The project its marker names.</param>
/// <param name="Path">The folder here.</param>
/// <param name="Code">The runtime's code for the refusal.</param>
/// <param name="Message">What it said.</param>
public sealed record ImportLinkRefused(string ProjectId, string Path, string Code, string Message);

/// <summary>A role skills pack the record says came from a directory that is not on this machine.</summary>
/// <param name="Pack">The pack.</param>
/// <param name="Source">The directory the record names.</param>
public sealed record ImportSkillsSource(string Pack, string Source);

/// <summary>The logon registration as the import left it.</summary>
/// <param name="Exported">Whether the other machine started the runtime at logon.</param>
/// <param name="Registered">Whether the import registered it here.</param>
/// <param name="Said">What a person reads about it, when there is something to read.</param>
public sealed record ImportAutostart(bool Exported, bool Registered, string? Said);

/// <summary>What <c>jason import</c> prints after it placed an archive.</summary>
public sealed record ImportPlaced(
    string File,
    string JasonVersion,
    DateTimeOffset ExportedAt,
    string DataDirectory,
    HarnessRootPlace HarnessRoot,
    int DataFiles,
    int HarnessFiles,
    int Projects,
    int Campaigns,
    IReadOnlyList<string> MovedAside,
    IReadOnlyList<SettingRewritten> Rewritten,
    IReadOnlyList<SettingNotHere> NotHere,
    IReadOnlyList<ImportSkillsSource> SkillsSourcesNotHere,
    ImportAutostart Autostart,
    bool Stopped,
    bool Started,
    IReadOnlyList<ImportLinked> Linked,
    IReadOnlyList<ImportLinkRefused> LinkRefused,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Next);

/// <summary>
/// <c>jason import &lt;file&gt;</c>: an installation <c>jason export</c> wrote, placed on this machine — its data directory's
/// parts into this one, its harness root's tree into the root here — and the runtime started on it, with each project
/// folder recorded where it now stands.
/// </summary>
/// <remarks>
/// <para>
/// One of the names in this CLI with no API operation behind it, and it cannot have one: it places the runtime's files
/// cold, the database among them, which no runtime can take while it runs on them. Of the runtime it asks to stop, only
/// under <c>--stop</c>; to start; and <c>project.link</c> once per project folder, as <c>jason project link</c> asks it.
/// </para>
/// <para>
/// <b>Nothing is written before every refusal is decided</b>: an archive that cannot be read, a name in it that escapes
/// where it would be placed, a file whose digest is not the one <c>export.json</c> lists, an archive a newer Jason wrote,
/// and an installation already here without <c>--replace</c>. <b>Nothing is deleted</b>: what <c>--replace</c> moves aside
/// stays, named in the answer, the person's to remove.
/// </para>
/// </remarks>
public static class ImportCommand
{
    /// <summary>What every import leaves to the person, in this order.</summary>
    public static readonly IReadOnlyList<string> LeftToYou =
    [
        "jason skills install, to deploy the skills for this machine's agent hosts",
        "jason runtime autostart enable, if the runtime should start at logon here",
        "jason status, to see that this installation can start work",
    ];

    /// <summary>The sentence a machine that cannot register at logon says, as <c>runtime autostart status</c> says it.</summary>
    public const string CannotRegister = RuntimeAutostartCommands.CannotRegister;

    private const string AsideName = "replaced-";

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        const string Summary = "Place an installation 'jason export' wrote on another machine here, and start the runtime on it.";
        var command = new Command("import", Summary)
        {
            Description = Summary + Environment.NewLine + Environment.NewLine + Contract(),
        };

        var file = new Argument<string>("file") { Description = "The archive 'jason export' wrote." };
        var replace = new Option<bool>("--replace")
        {
            Description = "An installation is already here: move its database, settings, plugins, role skills, UI and logs aside into <data>/replaced-<stamp>/, and its harness root to <root>.replaced-<stamp>, and place this one. Nothing is deleted.",
        };
        var root = new Option<string?>("--root")
        {
            Description = "The harness root to place the archive's into. Default: JASON_Harness__Root, else the Harness:Root of this machine's settings file, else Jason in the home directory.",
        };
        var stop = new Option<bool>("--stop") { Description = "Stop a running runtime for the import." };
        var human = VerbOptions.Human();

        command.Arguments.Add(file);
        command.Options.Add(replace);
        command.Options.Add(root);
        command.Options.Add(stop);
        command.Options.Add(human);

        command.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new ImportOptions(parse.GetValue(file)!, parse.GetValue(replace), parse.GetValue(root), parse.GetValue(stop), parse.GetValue(human)),
            TimeProvider.System,
            cancellationToken));
        return command;
    }

    /// <summary>The verb, at the moment <paramref name="clock"/> says.</summary>
    public static async Task<int> RunAsync(CliEnvironment env, ImportOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        var stamp = ExportCommand.Stamp(clock.GetUtcNow());
        var file = Full(options.File, env.WorkingDirectory, "The archive");

        // ---- the refusals, every one before anything is written ----

        Archive archive;
        try
        {
            archive = Archive.Read(file, env);
        }
        catch (ImportRefusal refused)
        {
            return Refuse(env, options.Human, refused.Code, refused.Message);
        }

        using (archive)
        {
            var manifest = archive.Manifest;
            var here = SkillsSource.BuildVersion(env);
            if (SemanticVersion.TryParse(manifest.JasonVersion, out var written)
                && SemanticVersion.TryParse(here, out var running)
                && written > running)
            {
                return Refuse(
                    env,
                    options.Human,
                    CliErrors.ImportNewerVersion,
                    $"The archive was written by Jason {manifest.JasonVersion}, and this is Jason {here}, which cannot know what a newer one keeps. Install Jason {manifest.JasonVersion} or later first, then import it. Nothing was written.");
            }

            HarnessRootPlace root;
            try
            {
                root = Root(env, options.Root);
            }
            catch (ImportRefusal refused)
            {
                return Refuse(env, options.Human, refused.Code, refused.Message);
            }

            var runtimeRunning = RuntimeHold.Running(env);
            if (runtimeRunning && !options.Stop)
            {
                return Refuse(env, options.Human, CliErrors.RuntimeRunning, RuntimeHold.RunningRefusal(env, "import"));
            }

            var there = What(env, root.Path);
            if (there.Installation && !options.Replace)
            {
                return Refuse(env, options.Human, CliErrors.ImportNotEmpty, NotEmpty(env, root.Path, there));
            }

            SettingsRewritten settings;
            try
            {
                settings = Settings(env, archive, root);
            }
            catch (JsonException unreadable)
            {
                return Refuse(
                    env,
                    options.Human,
                    CliErrors.ImportRefused,
                    $"The settings file in the archive is not JSON the configuration reads ({unreadable.Message}), so its paths cannot be made this machine's. Nothing was written.");
            }

            // ---- the runtime, stopped ----

            var stopped = false;
            if (runtimeRunning)
            {
                if (await RuntimeHold.StopAsync(env, options.StopTimeout, options.Poll, cancellationToken).ConfigureAwait(false) is { } said)
                {
                    return Refuse(env, options.Human, CliErrors.RuntimeStillRunning, $"The runtime did not stop, so nothing was written. Stop it with 'jason runtime stop' and run this again. It said: {said}");
                }

                stopped = true;
            }

            // ---- aside, then in place ----

            var aside = new List<string>();
            int dataFiles, harnessFiles;
            try
            {
                MoveAside(env, root.Path, there, stamp, aside);
                (dataFiles, harnessFiles) = archive.Place(env.Paths.Root, root.Path, settings);
            }
            catch (ImportRefusal refused)
            {
                var asideText = aside.Count == 0 ? string.Empty : " Moved aside before it stopped, and left there: " + string.Join(", ", aside) + ".";
                return Refuse(env, options.Human, refused.Code, refused.Message + asideText);
            }

            var warnings = new List<string>();
            foreach (var path in settings.NotHere)
            {
                warnings.Add($"The setting {path.Setting} names {path.Path}, which is not on this machine; it was left as written.");
            }

            var sources = SkillsSourcesNotHere(env, warnings);
            foreach (var source in sources)
            {
                warnings.Add($"The role skills pack {source.Pack} came from the directory {source.Source}, which is not on this machine; run 'jason skills install' to deploy it from where it is here.");
            }

            var autostart = Register(env, manifest.Autostart.Registered);
            if (autostart.Said is { } sentence)
            {
                warnings.Add(sentence);
            }

            var (projects, campaigns) = Markers(root.Path);

            // ---- the runtime, started, and each project folder recorded where it stands ----

            if (await RuntimeHold.StartAsync(env, options.StartTimeout, options.Poll, cancellationToken).ConfigureAwait(false) is { } failed)
            {
                foreach (var warning in warnings)
                {
                    env.Error.WriteLine(warning);
                }

                var asideText = aside.Count == 0 ? string.Empty : " Moved aside, yours to remove: " + string.Join(", ", aside) + ".";
                return Refuse(
                    env,
                    options.Human,
                    CliErrors.RuntimeStartFailed,
                    $"The installation was placed in {env.Paths.Root} and {root.Path}, and the runtime did not start on it, so no project folder was linked. Start it with 'jason runtime start', then link each project folder with 'jason project link --path <folder>'.{asideText} It said: {failed}");
            }

            var linked = new List<ImportLinked>();
            var refusedLinks = new List<ImportLinkRefused>();
            foreach (var (projectId, folder) in projects)
            {
                await Link(env, projectId, folder, linked, refusedLinks, cancellationToken).ConfigureAwait(false);
            }

            foreach (var refusal in refusedLinks)
            {
                warnings.Add($"The runtime did not record the project folder {refusal.Path} ({refusal.Code}): {refusal.Message}");
            }

            var answer = new ImportPlaced(
                file,
                manifest.JasonVersion,
                manifest.ExportedAt,
                env.Paths.Root,
                root,
                dataFiles,
                harnessFiles,
                projects.Count,
                campaigns,
                aside,
                settings.Rewritten,
                settings.NotHere,
                sources,
                autostart,
                stopped,
                Started: true,
                linked,
                refusedLinks,
                warnings,
                LeftToYou);

            env.Out.WriteLine(options.Human ? Sentences(answer) : AsciiJson.Serialize(answer));
            foreach (var warning in warnings)
            {
                env.Error.WriteLine(warning);
            }

            return ExitCodes.Success;
        }
    }

    // ---- where -------------------------------------------------------------------------------------------------------

    private static string Full(string given, string? workingDirectory, string what)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            throw new UsageException($"{what} must be named; '{given}' names none.");
        }

        if (Path.IsPathFullyQualified(given))
        {
            return FolderPaths.Normal(given);
        }

        return workingDirectory is { } start
            ? FolderPaths.Normal(Path.GetFullPath(given, start))
            : throw new UsageException($"The folder this command runs in cannot be named, so '{given}' cannot be read from it; name it by a full path.");
    }

    /// <summary>
    /// The harness root the archive's tree goes into: <c>--root</c>, else the <c>JASON_Harness__Root</c> variable when this
    /// machine's environment sets it, else <c>Jason</c> in the home directory — held to the rule the runtime holds a root to
    /// (<see cref="HarnessRootRule"/>), each place compared where it leads, before anything is placed.
    /// </summary>
    private static HarnessRootPlace Root(CliEnvironment env, string? option)
    {
        // Not this machine's settings file: the archive's replaces it, and its root is set to the one chosen here.
        var resolved = InstallationPlaces.HarnessRoot(env, settings: null);
        var root = resolved;
        if (option is not null)
        {
            var full = Full(option, env.WorkingDirectory, "--root");
            root = new HarnessRootPlace(full, InstallationPlaces.FromOption, full);

            // The runtime reads the variable over every setting, so a root it names is where the runtime will look.
            if (resolved.From == InstallationPlaces.FromVariable && !FolderPaths.Same(resolved.Path, root.Path))
            {
                throw new ImportRefusal(
                    CliErrors.ImportRefused,
                    $"{InstallationPlaces.RootVariable} names {resolved.Path}, which the runtime reads over any setting, and --root names {root.Path}: the runtime would not look where the folders were placed. Leave --root out, or change the variable. Nothing was written.");
            }
        }

        return root.Refusal(env) is { } why
            ? throw new ImportRefusal(CliErrors.ImportRefused, $"The harness root {root.Path}, named by {ExportCommand.Named(root.From)}, cannot be the root here: {why} Name another with --root. Nothing was written.")
            : root;
    }

    // ---- what is here ------------------------------------------------------------------------------------------------

    /// <summary>What of an installation is here: the parts an import places, and the harness root.</summary>
    private sealed record Here(IReadOnlyList<string> DataParts, bool Database, bool SettingsFile, bool RootFolder, bool RootHasEntries, int Markers, int Projects)
    {
        /// <summary>An installation is here: a database, or a harness root with a marker in it.</summary>
        public bool Installation => Database || Markers > 0;
    }

    private static Here What(CliEnvironment env, string root)
    {
        var parts = ExportCommand.DatabaseFiles.Concat(ExportCommand.CarriedTrees)
            .Where(part => File.Exists(DataPath(env.Paths.Root, part)) || Directory.Exists(DataPath(env.Paths.Root, part)))
            .ToArray();
        var database = File.Exists(DataPath(env.Paths.Root, ExportCommand.DatabaseFiles[0]));
        var folder = Directory.Exists(root);
        var entries = folder && Directory.EnumerateFileSystemEntries(root).Any();
        var markers = folder ? MarkerFolders(root).ToArray() : [];
        return new Here(
            parts,
            database,
            File.Exists(env.Paths.UserSettingsFile),
            folder,
            entries,
            markers.Length,
            markers.Count(marker => marker.Marker.Kind == HarnessMarker.Project));
    }

    private static string NotEmpty(CliEnvironment env, string root, Here there)
    {
        var named = new List<string>();
        if (there.Database)
        {
            named.Add($"a database at {DataPath(env.Paths.Root, ExportCommand.DatabaseFiles[0])}");
        }

        if (there.SettingsFile)
        {
            named.Add($"a settings file at {env.Paths.UserSettingsFile}");
        }

        if (there.Markers > 0)
        {
            named.Add(string.Create(CultureInfo.InvariantCulture, $"{there.Projects} project folder{(there.Projects == 1 ? string.Empty : "s")} in the harness root {root}"));
        }

        return "An installation is already here: " + string.Join(", ", named) + ". Give --replace to move it aside — "
            + $"its data into {Path.Join(env.Paths.Root, AsideName + "<stamp>")} and its harness root to {root}.{AsideName}<stamp>, nothing deleted — "
            + "and place this one instead. Nothing was written.";
    }

    // ---- aside -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every part of the data directory the archive's would land on, into one folder of the data directory, and the harness
    /// root beside itself, so what is placed lands on nothing. Moved, never deleted.
    /// </summary>
    private static void MoveAside(CliEnvironment env, string root, Here there, string stamp, List<string> aside)
    {
        try
        {
            if (there.DataParts.Count > 0)
            {
                var folder = Free(Path.Join(env.Paths.Root, AsideName + stamp));
                foreach (var part in there.DataParts)
                {
                    var from = DataPath(env.Paths.Root, part);
                    var to = DataPath(folder, part);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    if (Directory.Exists(from))
                    {
                        Directory.Move(from, to);
                    }
                    else
                    {
                        File.Move(from, to);
                    }
                }

                aside.Add(folder);
            }

            if (there.RootHasEntries)
            {
                var moved = Free(root + "." + AsideName + stamp);
                Directory.Move(root, moved);
                aside.Add(moved);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new ImportRefusal(CliErrors.ImportUnwritable, $"What is here could not be moved aside ({failure.Message}), so nothing was placed.");
        }
    }

    /// <summary>The name, or the name with the first free number after it.</summary>
    private static string Free(string path)
    {
        var free = path;
        for (var number = 2; File.Exists(free) || Directory.Exists(free); number++)
        {
            free = string.Create(CultureInfo.InvariantCulture, $"{path}-{number}");
        }

        return free;
    }

    private static string DataPath(string root, string relative) => Path.Combine([root, .. relative.Split('/')]);

    // ---- the settings ------------------------------------------------------------------------------------------------

    private static SettingsRewritten Settings(CliEnvironment env, Archive archive, HarnessRootPlace root)
    {
        var manifest = archive.Manifest;
        var text = archive.SettingsText();
        var places = new List<PlaceMoved>
        {
            new(manifest.Home, FolderPaths.Normal(InstallationPlaces.Home(env))),
            new(manifest.DataDirectory, FolderPaths.Normal(env.Paths.Root)),
            new(manifest.HarnessRoot, root.Path),
        };

        // The settings file names the root chosen here in every case, the default and a root the variable names included, so
        // the file says where its installation's folders are whatever the environment the runtime is started in.
        return SettingsRewrite.Rewrite(text, manifest.OperatingSystem, places, root.Path, nameRoot: true, path => File.Exists(path) || Directory.Exists(path));
    }

    // ---- what is left to say -----------------------------------------------------------------------------------------

    private static List<ImportSkillsSource> SkillsSourcesNotHere(CliEnvironment env, List<string> warnings)
    {
        try
        {
            return SkillsRecord.Read(env.Paths.RoleSkillsDirectory) is { } record
                ? [.. record.Packs
                    .Where(pack => pack.Ref == SkillsSource.DirectoryRef && Path.IsPathFullyQualified(pack.Source) && !Directory.Exists(pack.Source))
                    .Select(pack => new ImportSkillsSource(pack.Pack, pack.Source))]
                : [];
        }
        catch (SkillsRecordUnreadable unreadable)
        {
            warnings.Add($"{unreadable.Message} Run 'jason skills install' to deploy the role skills again.");
            return [];
        }
    }

    private static ImportAutostart Register(CliEnvironment env, bool exported)
    {
        if (!exported)
        {
            return new ImportAutostart(false, false, null);
        }

        var registrar = RuntimeAutostartCommands.Registrar(env);
        if (registrar.Platform is AutostartPlatform.Unsupported)
        {
            return new ImportAutostart(true, false, CannotRegister);
        }

        try
        {
            registrar.Apply(RuntimeAutostartCommands.Registration(env, registrar.Platform));
            return new ImportAutostart(true, true, null);
        }
        catch (AutostartException refused)
        {
            return new ImportAutostart(true, false, $"The runtime started at logon on the other machine, and registering it here was refused ({refused.Code}): {refused.Message}");
        }
    }

    /// <summary>The project folders and campaign folders the placed root holds, by their markers.</summary>
    private static (List<(string ProjectId, string Folder)> Projects, int Campaigns) Markers(string root)
    {
        var projects = new List<(string, string)>();
        var campaigns = 0;
        if (!Directory.Exists(root))
        {
            return (projects, campaigns);
        }

        foreach (var (folder, marker) in MarkerFolders(root))
        {
            if (marker.Kind == HarnessMarker.Project && marker.Id is { Length: > 0 } id)
            {
                projects.Add((id, folder));
            }
            else if (marker.Kind == HarnessMarker.Campaign)
            {
                campaigns++;
            }
        }

        return (projects, campaigns);
    }

    private static IEnumerable<(string Folder, HarnessMarker Marker)> MarkerFolders(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(root, HarnessMarker.FileName, options).Order(StringComparer.Ordinal))
        {
            var markerDirectory = Path.GetDirectoryName(path)!;
            if (!string.Equals(Path.GetFileName(markerDirectory), HarnessMarker.Directory, StringComparison.Ordinal))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(markerDirectory)!;
            if (HarnessMarker.In(folder) is { } marker)
            {
                yield return (folder, marker);
            }
        }
    }

    private static async Task Link(CliEnvironment env, string projectId, string folder, List<ImportLinked> linked, List<ImportLinkRefused> refused, CancellationToken cancellationToken)
    {
        var body = RequestBody.Empty()
            .Set("project_id", projectId)
            .Set("path", folder)
            .Set("reason", "jason import placed the project's folder here")
            .SetActor(null, env);

        var quiet = env with { Out = new StringWriter() };
        var (exit, response) = await OperationRunner.SendAsync(quiet, Operations.ProjectLink, body, cancellationToken).ConfigureAwait(false);
        if (exit == ExitCodes.Success && response is { IsSuccess: true })
        {
            linked.Add(new ImportLinked(projectId, folder));
            return;
        }

        var text = response is not null ? OperationRunner.ErrorText(response) : quiet.Out.ToString() ?? string.Empty;
        string code = "runtime_error", message = text.Trim();
        try
        {
            if (JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)?.Error is { Code.Length: > 0 } error)
            {
                (code, message) = (error.Code, error.Message);
            }
        }
        catch (JsonException)
        {
        }

        refused.Add(new ImportLinkRefused(projectId, folder, code, message));
    }

    // ---- the archive -------------------------------------------------------------------------------------------------

    /// <summary>A refusal decided while the archive was read or placed.</summary>
    private sealed class ImportRefusal(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    /// <summary>The archive, read whole and checked before anything is written.</summary>
    private sealed class Archive : IDisposable
    {
        private readonly ZipArchive _zip;
        private readonly string _file;

        private Archive(string file, ZipArchive zip, ExportManifest manifest)
        {
            _file = file;
            _zip = zip;
            Manifest = manifest;
        }

        public ExportManifest Manifest { get; }

        public void Dispose() => _zip.Dispose();

        /// <summary>Opens it and holds every name and every digest to <c>export.json</c>, or refuses saying which.</summary>
        public static Archive Read(string file, CliEnvironment env)
        {
            _ = env;
            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(file);
            }
            catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException or NotSupportedException)
            {
                throw new ImportRefusal(CliErrors.ImportUnreadable, $"{file} cannot be read as an archive 'jason export' wrote ({unreadable.Message}). Nothing was written.");
            }

            try
            {
                var manifest = Listing(file, zip);
                Check(file, zip, manifest);
                return new Archive(file, zip, manifest);
            }
            catch
            {
                zip.Dispose();
                throw;
            }
        }

        private static ExportManifest Listing(string file, ZipArchive zip)
        {
            var entry = zip.GetEntry(ExportManifest.FileName)
                ?? throw new ImportRefusal(CliErrors.ImportUnreadable, $"{file} holds no {ExportManifest.FileName}, so it is not an archive 'jason export' wrote. Nothing was written.");
            ExportManifest? manifest;
            try
            {
                using var stream = entry.Open();
                manifest = JsonSerializer.Deserialize<ExportManifest>(stream, JasonJson.Options);
            }
            catch (Exception unreadable) when (unreadable is JsonException or IOException or InvalidDataException or NotSupportedException)
            {
                throw new ImportRefusal(CliErrors.ImportUnreadable, $"The {ExportManifest.FileName} in {file} cannot be read ({unreadable.Message}). Nothing was written.");
            }

            if (manifest is null || manifest.Format != ExportManifest.CurrentFormat || manifest.Entries is null || manifest.Directories is null
                || manifest.JasonVersion is null || manifest.Home is null || manifest.DataDirectory is null || manifest.HarnessRoot is null
                || manifest.OperatingSystem is null || manifest.Autostart is null)
            {
                throw new ImportRefusal(
                    CliErrors.ImportUnreadable,
                    string.Create(CultureInfo.InvariantCulture, $"The {ExportManifest.FileName} in {file} is not the format {ExportManifest.CurrentFormat} this Jason reads. Nothing was written."));
            }

            return manifest;
        }

        /// <summary>Every entry: a plain name in a place an import writes, no link, listed, and with the digest listed.</summary>
        private static void Check(string file, ZipArchive zip, ExportManifest manifest)
        {
            var listed = new Dictionary<string, ExportEntry>(StringComparer.Ordinal);
            foreach (var entry in manifest.Entries)
            {
                Escapes(file, entry.Path, directory: false);
                if (!listed.TryAdd(entry.Path, entry))
                {
                    throw new ImportRefusal(CliErrors.ImportRefused, $"{ExportManifest.FileName} in {file} lists {entry.Path} twice. Nothing was written.");
                }
            }

            foreach (var directory in manifest.Directories)
            {
                Escapes(file, directory, directory: true);
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (string.Equals(name, ExportManifest.FileName, StringComparison.Ordinal))
                {
                    continue;
                }

                var directory = ArchiveNames.IsDirectory(entry);
                Escapes(file, name, directory);
                if (ArchiveNames.IsLink(entry))
                {
                    throw new ImportRefusal(CliErrors.ImportRefused, $"{name} in {file} is a link, and an import places no link. Nothing was written.");
                }

                if (!names.Add(name.TrimEnd('/')))
                {
                    throw new ImportRefusal(CliErrors.ImportRefused, $"{name} in {file} has the name of another entry but for the case of its letters, which a system that ignores case cannot hold apart. Nothing was written.");
                }

                if (directory)
                {
                    continue;
                }

                if (!listed.TryGetValue(name, out var expected))
                {
                    throw new ImportRefusal(CliErrors.ImportRefused, $"{name} in {file} is not listed in its {ExportManifest.FileName}. Nothing was written.");
                }

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long length = 0;
                try
                {
                    using var stream = entry.Open();
                    for (int read; (read = stream.Read(buffer, 0, buffer.Length)) > 0;)
                    {
                        hash.AppendData(buffer, 0, read);
                        length += read;
                    }
                }
                catch (Exception unreadable) when (unreadable is IOException or InvalidDataException or NotSupportedException)
                {
                    throw new ImportRefusal(CliErrors.ImportUnreadable, $"{name} in {file} cannot be read ({unreadable.Message}). Nothing was written.");
                }

                var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (length != expected.Bytes || !string.Equals(digest, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ImportRefusal(
                        CliErrors.ImportDigestMismatch,
                        string.Create(CultureInfo.InvariantCulture, $"{name} in {file} is {length} bytes with the SHA-256 {digest}, and {ExportManifest.FileName} lists {expected.Bytes} bytes with {expected.Sha256}: the archive changed after it was written. Nothing was written."));
                }

                seen.Add(name);
            }

            if (listed.Keys.FirstOrDefault(name => !seen.Contains(name)) is { } missing)
            {
                throw new ImportRefusal(CliErrors.ImportDigestMismatch, $"{ExportManifest.FileName} in {file} lists {missing}, which the archive does not hold. Nothing was written.");
            }
        }

        /// <summary>
        /// A name an import would write somewhere it must not: one the shared rules refuse, one outside <c>data/</c> and
        /// <c>harness/</c>, or one under <c>data/</c> outside the parts an export carries.
        /// </summary>
        private static void Escapes(string file, string name, bool directory)
        {
            var plain = directory ? name.TrimEnd('/') : name;
            var refused = name.Length == 0
                || (directory && !name.EndsWith('/'))
                || !ArchiveNames.Plain(plain)
                || !(Carried(plain, directory) || plain == "harness" || plain.StartsWith(ExportManifest.HarnessPrefix, StringComparison.Ordinal));
            if (refused)
            {
                throw new ImportRefusal(CliErrors.ImportRefused, $"The archive {file} names '{name}', which an import never writes: it escapes the data directory and the harness root, or is no part an export carries. Nothing was written.");
            }
        }

        private static bool Carried(string name, bool directory)
        {
            if (!name.StartsWith(ExportManifest.DataPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var relative = name[ExportManifest.DataPrefix.Length..];
            return (!directory && ExportCommand.DatabaseFiles.Contains(relative, StringComparer.Ordinal))
                || (directory && relative == "state")
                || ExportCommand.CarriedTrees.Any(tree => relative == tree || relative.StartsWith(tree + "/", StringComparison.Ordinal))
                || (directory && ExportCommand.CarriedTrees.Any(tree => tree.StartsWith(relative + "/", StringComparison.Ordinal)));
        }

        /// <summary>The settings file the archive carries, as text; null when it carries none.</summary>
        public string? SettingsText()
        {
            var entry = _zip.GetEntry(ExportManifest.DataPrefix + "config/settings.json");
            if (entry is null)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>Writes every directory and file into its place, the settings file as rewritten; never over anything.</summary>
        public (int Data, int Harness) Place(string dataDirectory, string root, SettingsRewritten settings)
        {
            int data = 0, harness = 0;
            var settingsName = ExportManifest.DataPrefix + "config/settings.json";
            try
            {
                Directory.CreateDirectory(dataDirectory);
                Directory.CreateDirectory(root);
                foreach (var directory in Manifest.Directories)
                {
                    Directory.CreateDirectory(Target(dataDirectory, root, directory.TrimEnd('/')));
                }

                foreach (var entry in _zip.Entries)
                {
                    if (entry.FullName == ExportManifest.FileName || ArchiveNames.IsDirectory(entry))
                    {
                        continue;
                    }

                    var target = Target(dataDirectory, root, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (entry.FullName == settingsName)
                        {
                            destination.Write(Encoding.UTF8.GetBytes(settings.Text));
                        }
                        else
                        {
                            using var source = entry.Open();
                            source.CopyTo(destination);
                        }
                    }

                    if (entry.FullName.StartsWith(ExportManifest.DataPrefix, StringComparison.Ordinal))
                    {
                        data++;
                    }
                    else
                    {
                        harness++;
                    }
                }

                // A root named nowhere else is written into a settings file of its own.
                if (Manifest.Entries.All(entry => entry.Path != settingsName) && settings.Text.Length > 0)
                {
                    var target = Target(dataDirectory, root, settingsName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    destination.Write(Encoding.UTF8.GetBytes(settings.Text));
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException)
            {
                throw new ImportRefusal(
                    CliErrors.ImportUnwritable,
                    $"The archive {_file} could not be placed in full ({failure.Message}); what was written stays in {dataDirectory} and {root}. Import it again with --replace once the cause is gone.");
            }

            return (data, harness);
        }

        private static string Target(string dataDirectory, string root, string name) =>
            name.StartsWith(ExportManifest.DataPrefix, StringComparison.Ordinal)
                ? DataPath(dataDirectory, name[ExportManifest.DataPrefix.Length..])
                : name == "harness" ? root
                : DataPath(root, name[ExportManifest.HarnessPrefix.Length..]);
    }

    // ---- what is printed ---------------------------------------------------------------------------------------------

    private static string Sentences(ImportPlaced answer)
    {
        var lines = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"Imported the installation Jason {answer.JasonVersion} exported at {answer.ExportedAt:yyyy-MM-dd HH:mm:ss}Z: {answer.DataFiles} files into {answer.DataDirectory} and {answer.HarnessFiles} into the harness root {answer.HarnessRoot.Path}, with {answer.Projects} project and {answer.Campaigns} campaign folders. The runtime is running on it."),
        };
        lines.AddRange(answer.MovedAside.Select(path => $"Moved aside, yours to remove: {path}"));
        lines.AddRange(answer.Rewritten.Select(rewrite => rewrite.From.Length == 0 ? $"Set {rewrite.Setting} to {rewrite.To}." : $"Rewrote {rewrite.Setting}: {rewrite.From} -> {rewrite.To}"));
        lines.AddRange(answer.Linked.Select(link => $"Linked the project {link.ProjectId} at {link.Path}."));
        lines.AddRange(answer.LinkRefused.Select(link => $"The project folder {link.Path} was not linked ({link.Code}): {link.Message}"));
        lines.Add("Left to you:");
        lines.AddRange(answer.Next.Select(next => "  " + next));
        return string.Join(Environment.NewLine, lines);
    }

    private static int Refuse(CliEnvironment env, bool human, string code, string message)
    {
        if (human)
        {
            env.Error.WriteLine(message);
        }
        else
        {
            env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: false));
        }

        return ExitCodes.ApiError;
    }

    private static string Contract()
    {
        var nl = Environment.NewLine;
        return "Refused before anything is written: an archive that cannot be read; a name in it that escapes (a parent or empty "
            + "segment, a rooted name, a backslash, a link, a name outside data/, harness/ and export.json, or a part of the data "
            + "directory no export carries); a file whose size or SHA-256 is not the one export.json lists; an archive a newer "
            + "Jason wrote, naming the version to install first; and, without --replace, an installation already here — a "
            + "database, or a harness root with a marker in it — named with what it holds." + nl
            + "Nothing is deleted: --replace moves the data directory's parts into <data>/replaced-<stamp>/ and the harness root "
            + "to <root>.replaced-<stamp>, and names both as yours to remove." + nl
            + "The settings file: every absolute path under the other machine's home directory, data directory or harness root "
            + "is rewritten to the same path here, each rewrite said, Harness:Root first; a path outside those three that is not "
            + "on this machine is said and left as written; every other value, a secret among them, is placed as it was." + nl
            + "The runtime: while it runs this refuses, naming 'jason runtime stop', unless --stop is given. It registers at "
            + "logon when the other machine did, or says this machine cannot; starts the runtime and waits for it; and asks it "
            + "to link each project folder by its marker. A refused link is said, and the folder stays. It reaches no network "
            + "but the runtime's own loopback." + nl
            + "Left to you: " + string.Join("; ", LeftToYou) + "." + nl
            + "Exit codes: 0 placed and the runtime answered; 1 refused, with nothing written, or placed and the runtime did not "
            + "start, which the answer says; 2 usage.";
    }
}
