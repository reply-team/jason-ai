using System.CommandLine;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Files;
using Jason.Cli.Logs;
using Jason.Cli.Skills;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;

namespace Jason.Cli.Moving;

/// <summary>What was asked of <c>jason export</c>, as parsed.</summary>
/// <param name="File">The archive to write, as typed; null for the default name in the folder the command runs in.</param>
/// <param name="Stop">Stop a running runtime for the export, and start it again after.</param>
/// <param name="Human">Render for people instead of printing JSON.</param>
public sealed record ExportOptions(string? File, bool Stop, bool Human)
{
    /// <summary>How long the stop waits for the runtime to go; null for <c>runtime stop</c>'s own.</summary>
    internal TimeSpan? StopTimeout { get; init; }

    /// <summary>How long the start waits for the runtime to answer; null for <c>runtime start</c>'s own.</summary>
    internal TimeSpan? StartTimeout { get; init; }

    /// <summary>How often the stop and the start look; null for their own.</summary>
    internal TimeSpan? Poll { get; init; }
}

/// <summary>What <c>jason export</c> prints after it wrote an archive.</summary>
/// <param name="File">The archive written.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="JasonVersion">The version that wrote it.</param>
/// <param name="ExportedAt">The moment written into it.</param>
/// <param name="HarnessRoot">The harness root it carries.</param>
/// <param name="Files">How many files it carries, <c>export.json</c> left out.</param>
/// <param name="DataFiles">How many of them are from the data directory.</param>
/// <param name="HarnessFiles">How many of them are from the harness root.</param>
/// <param name="Projects">How many project folders, by their markers.</param>
/// <param name="Campaigns">How many campaign folders, by their markers.</param>
/// <param name="AutostartRegistered">Whether it records that the runtime started at logon.</param>
/// <param name="Stopped">Whether the runtime was stopped for it.</param>
/// <param name="Started">Whether the runtime was started again after.</param>
/// <param name="Warnings">What a person must read: the settings file travels with its secrets.</param>
public sealed record ExportWritten(
    string File,
    long Bytes,
    string JasonVersion,
    DateTimeOffset ExportedAt,
    string HarnessRoot,
    int Files,
    int DataFiles,
    int HarnessFiles,
    int Projects,
    int Campaigns,
    bool AutostartRegistered,
    bool Stopped,
    bool Started,
    IReadOnlyList<string> Warnings);

/// <summary>
/// <c>jason export [&lt;file&gt;]</c>: this installation in one archive, for <c>jason import</c> on another machine — the data
/// directory's carried parts and the harness root's tree, with <c>export.json</c> saying where they came from and the digest
/// of every file.
/// </summary>
/// <remarks>
/// <para>
/// One of the names in this CLI with no API operation behind it, and it cannot have one: it copies the runtime's files cold,
/// the database among them, which the runtime cannot do of itself while it runs, and asks the runtime nothing but to stop
/// and to start, as <c>runtime stop</c> and <c>runtime start</c> ask it.
/// </para>
/// <para>
/// <b>Only while the runtime is stopped.</b> A database copied under a running runtime is a copy of a moment that never
/// was. So a running runtime is a refusal naming <c>jason runtime stop</c>, unless <c>--stop</c> is given; then the verb
/// stops it itself and starts it again when the archive is written, and only then.
/// </para>
/// <para>
/// <b>Never over a file</b>, and every name it writes one <c>jason import</c> takes: a link, or a name another system cannot
/// hold, is refused by its path before anything is written, by the rules both verbs share (<see cref="ArchiveNames"/>).
/// </para>
/// </remarks>
public static class ExportCommand
{
    public const string NamePrefix = "jason-export-";

    public const string Extension = ".zip";

    /// <summary>Beside the archive, the file it is written to first and moved from once it is whole.</summary>
    internal const string PartialSuffix = ".partial";

    /// <summary>The line every export says, on stderr and in its answer.</summary>
    public const string SecretsLine =
        "The archive carries the settings file with everything in it, a token under Plugins:Config included: keep the archive as you keep that file.";

    /// <summary>
    /// The trees of the data directory carried whole, by their names under it, in the layout <paramref name="paths"/> answer
    /// by: in layout 1 <c>config</c>, <c>plugins</c>, <c>skills/roles</c>, <c>ui/custom</c> and <c>logs</c>, as every Jason
    /// before the layout stamp carried them; in layout 2 <c>config</c>, <c>setup</c> and <c>logs</c>, without what
    /// <see cref="LeftOut"/> names.
    /// </summary>
    internal static string[] CarriedTrees(JasonPaths paths) => paths.Layout < 2
        ? [Under(paths, paths.ConfigDirectory), Under(paths, paths.PluginsDirectory), Under(paths, paths.RoleSkillsDirectory), Under(paths, paths.UiCustomDirectory), Under(paths, paths.LogsDirectory)]
        : [Under(paths, paths.ConfigDirectory), JasonPaths.SetupFolder, Under(paths, paths.LogsDirectory)];

    /// <summary>What a carried tree holds and no archive carries: the role skills' staging, which layout 2 keeps in <c>setup/</c>.</summary>
    internal static string[] LeftOut(JasonPaths paths) => paths.Layout < 2 ? [] : [Under(paths, paths.SkillsStagingDirectory)];

    /// <summary>A path of the data directory by its name under it, with <c>/</c> between its parts, as an archive names it.</summary>
    internal static string Under(JasonPaths paths, string path) => Path.GetRelativePath(paths.Root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The database's files, each carried when it is there.</summary>
    internal static readonly string[] DatabaseFiles = ["state/jason.db", "state/jason.db-wal", "state/jason.db-shm"];

    /// <summary>What no archive carries, as the help names it.</summary>
    internal static readonly string[] NeverCarried =
        [
            "data/state/backups/", "data/run/", "data/work/", "data/skills/.staging/", "data/skills/staged/", "data/ui/default/",
            "a replaced-<stamp>/ directory at the data directory's top, which an import moved aside",
            "data/desktop/, the desktop application's own files, the conversations of its chat among them",
        ];

    /// <summary>
    /// The moment as every name these verbs write carries it: UTC to the second, digits and two letters, which every file
    /// system takes in a name — <c>20261109T100005Z</c>.
    /// </summary>
    public static string Stamp(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        const string Summary = "Write this installation into one archive that 'jason import' places on another machine.";
        var command = new Command("export", Summary)
        {
            Description = Summary + Environment.NewLine + Environment.NewLine + Contract(env.Paths),
        };

        var file = new Argument<string?>("file")
        {
            Description = "The archive to write. A file that is already there is refused. Default: " + NamePrefix + "<UTC stamp>" + Extension + " in the current folder.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var stop = new Option<bool>("--stop")
        {
            Description = "Stop a running runtime for the export, and start it again when the archive is written.",
        };
        var human = VerbOptions.Human();

        command.Arguments.Add(file);
        command.Options.Add(stop);
        command.Options.Add(human);

        command.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new ExportOptions(parse.GetValue(file), parse.GetValue(stop), parse.GetValue(human)),
            TimeProvider.System,
            cancellationToken));
        return command;
    }

    /// <summary>
    /// The verb, at the moment <paramref name="clock"/> says: public, with that clock, for the reason
    /// <c>DoctorCommand.RunAsync</c> is.
    /// </summary>
    public static async Task<int> RunAsync(CliEnvironment env, ExportOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        // One moment, read once: the archive's name and the moment in it are the same second.
        var now = clock.GetUtcNow().ToUniversalTime();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));

        if (Target(env, options.File, now, out var refusal) is not { } target)
        {
            return Refuse(env, options.Human, CliErrors.ExportUnwritable, refusal!);
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return Refuse(env, options.Human, CliErrors.ExportOutputExists, $"There is already something at {target}, and an archive is never written over anything. Name a file that is not there yet. Nothing was written.");
        }

        var partial = target + PartialSuffix;
        if (File.Exists(partial) || Directory.Exists(partial))
        {
            return Refuse(env, options.Human, CliErrors.ExportOutputExists, $"There is already something at {partial}, where an archive is written before it is moved into place, perhaps the part of an export that did not finish; it is yours to remove, and an export never writes over it. Remove it, or name another file. Nothing was written.");
        }

        var running = RuntimeHold.Running(env);
        if (running && !options.Stop)
        {
            return Refuse(env, options.Human, CliErrors.RuntimeRunning, RuntimeHold.RunningRefusal(env, "export"));
        }

        HarnessRootPlace root;
        try
        {
            var settings = File.Exists(env.Paths.UserSettingsFile) ? File.ReadAllText(env.Paths.UserSettingsFile) : null;
            root = InstallationPlaces.HarnessRoot(env, settings);
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException or UnauthorizedAccessException or SecurityException)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.ExportRefused,
                $"The settings file at {env.Paths.UserSettingsFile} cannot be read ({unreadable.Message}), so the harness root it may name is not known. Mend it, or name the root in {InstallationPlaces.RootVariable}. Nothing was written.");
        }

        // The runtime's own rule for a root, held to the root as it was named and where it leads: a root that holds the data
        // directory would carry the parts no archive carries, and one that is the home would carry the home.
        if (root.Refusal(env) is { } why)
        {
            return Refuse(env, options.Human, CliErrors.ExportRefused, $"The harness root, named by {Named(root.From)}, cannot be carried: {why} Nothing was written.");
        }

        var warnings = new List<string>();
        var autostart = Registration(env, warnings);
        if (File.Exists(env.Paths.UserSettingsFile))
        {
            warnings.Add(SecretsLine);
        }

        var stopped = false;
        if (running)
        {
            if (await RuntimeHold.StopAsync(env, options.StopTimeout, options.Poll, cancellationToken).ConfigureAwait(false) is { } said)
            {
                return Refuse(env, options.Human, CliErrors.RuntimeStillRunning, $"The runtime did not stop, so nothing was written. Stop it with 'jason runtime stop' and run this again. It said: {said}");
            }

            stopped = true;
        }

        // The runtime's own instance lock, held for the whole read: a runtime this verb did not see by its descriptor still
        // holds it, and none can start while the archive is read. It is let go before the runtime is started again.
        var held = RuntimeHold.Lock(env);
        if (held is null && options.Stop && !stopped)
        {
            // Stopped only when a runtime was live by its descriptor as the stop was asked: a stop that found none stopped
            // nothing, and a verb starts again only a runtime it stopped.
            var live = RuntimeHold.Running(env);
            if (await RuntimeHold.StopAsync(env, options.StopTimeout, options.Poll, cancellationToken).ConfigureAwait(false) is { } said)
            {
                return Refuse(env, options.Human, CliErrors.RuntimeStillRunning, $"The runtime did not stop, so nothing was written. Stop it with 'jason runtime stop' and run this again. It said: {said}");
            }

            stopped = live;
            held = RuntimeHold.Lock(env);
        }

        if (held is null)
        {
            return !options.Stop
                ? Refuse(env, options.Human, CliErrors.RuntimeRunning, RuntimeHold.RunningRefusal(env, "export"))
                : Refuse(env, options.Human, CliErrors.RuntimeStillRunning, $"The runtime's lock at {env.Paths.LockFile} is held, so a runtime still runs on {env.Paths.Root}, and nothing was written. Stop it with 'jason runtime stop' and run this again.");
        }

        Written written;
        try
        {
            var items = Items(env, root.Path, warnings);
            var manifest = new ExportManifest(
                ExportManifest.CurrentFormat,
                SkillsSource.BuildVersion(env),
                env.Paths.Layout,
                now,
                ExportManifest.ThisSystem(),
                env.Paths.Root,
                root.Path,
                InstallationPlaces.Home(env),
                autostart,
                [],
                [.. items.Where(item => item.Directory).Select(item => item.Name)]);
            written = Write(target, partial, items, manifest);
        }
        catch (ExportRefusal refused)
        {
            held.Dispose();
            var restart = stopped ? await RuntimeHold.StartAsync(env, options.StartTimeout, options.Poll, cancellationToken).ConfigureAwait(false) : null;
            var tail = !stopped ? string.Empty
                : restart is null ? " The runtime, stopped for it, was started again."
                : $" The runtime, stopped for it, did not start again; start it with 'jason runtime start'. It said: {restart}";
            return Refuse(env, options.Human, refused.Code, refused.Message + tail);
        }
        finally
        {
            held.Dispose();
        }

        var started = false;
        if (stopped)
        {
            if (await RuntimeHold.StartAsync(env, options.StartTimeout, options.Poll, cancellationToken).ConfigureAwait(false) is { } said)
            {
                foreach (var warning in warnings)
                {
                    env.Error.WriteLine(warning);
                }

                return Refuse(
                    env,
                    options.Human,
                    CliErrors.RuntimeStartFailed,
                    $"The archive was written to {target}, and the runtime, stopped for it, did not start again. Start it with 'jason runtime start'. It said: {said}");
            }

            started = true;
        }

        var answer = new ExportWritten(
            target,
            written.Bytes,
            SkillsSource.BuildVersion(env),
            now,
            root.Path,
            written.DataFiles + written.HarnessFiles,
            written.DataFiles,
            written.HarnessFiles,
            written.Projects,
            written.Campaigns,
            autostart.Registered,
            stopped,
            started,
            warnings);

        env.Out.WriteLine(options.Human ? Sentence(answer) : AsciiJson.Serialize(answer));
        foreach (var warning in warnings)
        {
            env.Error.WriteLine(warning);
        }

        return ExitCodes.Success;
    }

    // ---- where -------------------------------------------------------------------------------------------------------

    /// <summary>What named the harness root, in the words a refusal says it in.</summary>
    internal static string Named(string from) => from switch
    {
        InstallationPlaces.FromVariable => "the " + InstallationPlaces.RootVariable + " variable",
        InstallationPlaces.FromSettings => "the settings file",
        InstallationPlaces.FromOption => "--root",
        _ => "nothing, so it is " + InstallationPlaces.DefaultFolder + " in the home directory",
    };

    private static string? Target(CliEnvironment env, string? file, DateTimeOffset now, out string? refusal)
    {
        refusal = null;
        if (file is not null && string.IsNullOrWhiteSpace(file))
        {
            throw new UsageException($"The file to export to must be named; '{file}' names none.");
        }

        var name = file ?? NamePrefix + Stamp(now) + Extension;
        if (!Path.IsPathFullyQualified(name) && env.WorkingDirectory is null)
        {
            refusal = $"The folder this command runs in cannot be named, so '{name}' has nowhere to be written. Name the archive by a full path. Nothing was written.";
            return null;
        }

        try
        {
            var full = Path.IsPathFullyQualified(name) ? Path.GetFullPath(name) : Path.GetFullPath(name, env.WorkingDirectory!);
            if (Path.EndsInDirectorySeparator(name) || Path.GetFileName(full).Length == 0)
            {
                refusal = $"'{name}' names a directory, not a file. Name the archive to write, for example {Path.Join(full, NamePrefix + Stamp(now) + Extension)}. Nothing was written.";
                return null;
            }

            return full;
        }
        catch (Exception unusable) when (unusable is ArgumentException or NotSupportedException or PathTooLongException)
        {
            refusal = $"'{name}' cannot be written: {unusable.Message} Nothing was written.";
            return null;
        }
    }

    // ---- the registration --------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the runtime of this data directory starts at logon, read as <c>runtime autostart status</c> reads it: a
    /// registration that starts another data directory is not this installation's.
    /// </summary>
    private static ExportAutostart Registration(CliEnvironment env, List<string> warnings)
    {
        var registrar = RuntimeAutostartCommands.Registrar(env);
        var platform = RuntimeAutostartCommands.Name(registrar.Platform);
        try
        {
            var state = registrar.Read();
            var named = RuntimeAutostartCommands.DataDirectoryIn(state.Run);
            var ours = state.Registered && named is not null && FolderPaths.Same(FolderPaths.Normal(named), FolderPaths.Normal(env.Paths.Root));
            return new ExportAutostart(ours, platform);
        }
        catch (AutostartException unread)
        {
            warnings.Add($"The logon registration could not be read ({unread.Message}), so the archive records none.");
            return new ExportAutostart(false, platform);
        }
    }

    // ---- what -------------------------------------------------------------------------------------------------------

    /// <summary>One file or directory to carry: its name in the archive and where it is.</summary>
    private sealed record Item(string Name, string Path, bool Directory);

    /// <summary>A refusal found while the archive was composed or written; nothing is left behind.</summary>
    private sealed class ExportRefusal(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    /// <summary>Everything carried, in the order it is written, each name checked.</summary>
    private static List<Item> Items(CliEnvironment env, string harnessRoot, List<string> warnings)
    {
        var items = new List<Item>();
        var data = env.Paths.Root;
        var dataReal = FolderPaths.Real(data);
        var harnessReal = FolderPaths.Real(harnessRoot);
        if (DatabaseFiles.Any(file => File.Exists(Path.Combine(data, file))))
        {
            items.Add(new Item(ExportManifest.DataPrefix + "state/", Path.Combine(data, "state"), Directory: true));
            foreach (var file in DatabaseFiles)
            {
                var path = Path.Combine(data, file);
                if (File.Exists(path))
                {
                    Refuse(path);
                    items.Add(new Item(ExportManifest.DataPrefix + file, path, Directory: false));
                }
            }
        }

        // The layout stamp, when there is one: the layout the archive carries travels with it.
        if (File.Exists(env.Paths.LayoutFile))
        {
            Refuse(new FileInfo(env.Paths.LayoutFile));
            items.Add(new Item(ExportManifest.DataPrefix + LayoutStamp.FileName, env.Paths.LayoutFile, Directory: false));
        }

        var leftOut = LeftOut(env.Paths).Select(part => ExportManifest.DataPrefix + part).ToHashSet(StringComparer.Ordinal);
        foreach (var tree in CarriedTrees(env.Paths))
        {
            var path = Path.Combine([data, .. tree.Split('/')]);
            if (Directory.Exists(path))
            {
                if (new DirectoryInfo(path).LinkTarget is not null)
                {
                    Judge(path, dataReal, harnessReal);
                }

                Walk(path, ExportManifest.DataPrefix + tree, items, leftOut);
            }
        }

        // A harness root that is a link is followed, and the runtime's rule for a root has judged where it leads already.
        if (Directory.Exists(harnessRoot))
        {
            Walk(harnessRoot, ExportManifest.HarnessPrefix.TrimEnd('/'), items);
        }
        else
        {
            items.Add(new Item(ExportManifest.HarnessPrefix, harnessRoot, Directory: true));
            warnings.Add($"The harness root at {harnessRoot} is not there, so the archive carries it empty, and no memory file or folder of a project or campaign travels.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!names.Add(item.Name.TrimEnd('/')))
            {
                throw new ExportRefusal(
                    CliErrors.ExportRefused,
                    $"{item.Path} has the name of another file or folder beside it but for the case of its letters, which a system that ignores case cannot hold apart. Rename one of them. Nothing was written.");
            }
        }

        return items;
    }

    /// <summary>
    /// A carried place's top that is a link, judged where it leads: into the data directory or the harness root, or to a
    /// folder holding either, it would carry a part twice or carry what is never carried.
    /// </summary>
    private static void Judge(string link, string dataReal, string harnessReal)
    {
        var leads = FolderPaths.Real(link);
        if ((Clash(leads, dataReal, "the data directory") ?? Clash(leads, harnessReal, "the harness root")) is { } where)
        {
            throw new ExportRefusal(
                CliErrors.ExportRefused,
                $"{link} is a link to {leads}, which {where}. A link at the top of a carried place is followed and carried as what it leads to, and this one would carry a part twice or carry what is never carried. Point it at a folder outside both, or put what it leads to in its place. Nothing was written.");
        }
    }

    private static string? Clash(string leads, string place, string named) =>
        FolderPaths.Same(leads, place) ? "is " + named
        : FolderPaths.Inside(leads, place) ? "is inside " + named
        : FolderPaths.Inside(place, leads) ? "holds " + named
        : null;

    /// <summary>
    /// A directory and everything in it, each name checked, every link inside refused: the top is the caller's to judge, a
    /// link there followed.
    /// </summary>
    private static void Walk(string directory, string name, List<Item> items, IReadOnlySet<string>? leftOut = null)
    {
        items.Add(new Item(name + "/", directory, Directory: true));

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = [.. new DirectoryInfo(directory).EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal)];
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new ExportRefusal(CliErrors.ExportRefused, $"{directory} cannot be read ({unreadable.Message}). Nothing was written.");
        }

        foreach (var entry in entries)
        {
            var below = name + "/" + entry.Name;
            if (leftOut?.Contains(below) == true)
            {
                continue;
            }

            if (!ArchiveNames.Plain(below))
            {
                throw new ExportRefusal(
                    CliErrors.ExportRefused,
                    $"{entry.FullName} has a name another system cannot hold, so it cannot travel. Rename or move it. Nothing was written.");
            }

            Refuse(entry);
            if (entry is DirectoryInfo)
            {
                Walk(entry.FullName, below, items, leftOut);
            }
            else
            {
                items.Add(new Item(below, entry.FullName, Directory: false));
            }
        }
    }

    /// <summary>
    /// A link is refused: what it leads to is not this installation's to carry, and an archive holds no link. A link is what
    /// names a target; a file the system keeps elsewhere for itself, a cloud placeholder say, is read as the file it is.
    /// </summary>
    private static void Refuse(string path) => Refuse(Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path));

    private static void Refuse(FileSystemInfo info)
    {
        if (info.LinkTarget is not null)
        {
            throw new ExportRefusal(
                CliErrors.ExportRefused,
                $"{info.FullName} is a link, and an archive carries no link. Put what it leads to in its place, or move it away. Nothing was written.");
        }
    }

    // ---- write ------------------------------------------------------------------------------------------------------

    private sealed record Written(long Bytes, int DataFiles, int HarnessFiles, int Projects, int Campaigns);

    /// <summary>
    /// The archive, written to <paramref name="partial"/> and moved to <paramref name="target"/> once it is whole, neither
    /// ever over a file; whatever goes wrong, the part written is taken away again.
    /// </summary>
    private static Written Write(string target, string partial, List<Item> items, ExportManifest manifest)
    {
        FileStream file;
        try
        {
            file = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception failure) when (Unwritable(failure))
        {
            throw AlreadyThere(failure)
                ? new ExportRefusal(CliErrors.ExportOutputExists, $"Something was put at {partial} in the meantime, and an export never writes over anything; it is yours to remove. Nothing was written.")
                : new ExportRefusal(CliErrors.ExportUnwritable, $"{partial} cannot be written: {failure.Message} Nothing was written.");
        }

        var entries = new List<ExportEntry>();
        int projects = 0, campaigns = 0;
        try
        {
            using (file)
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var buffer = new byte[81920];
                foreach (var item in items)
                {
                    if (item.Directory)
                    {
                        archive.CreateEntry(item.Name);
                        continue;
                    }

                    var entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long length = 0;
                    FileStream source;
                    try
                    {
                        source = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    }
                    catch (Exception failure) when (Unwritable(failure))
                    {
                        throw Unreadable(item.Path, failure);
                    }

                    using (source)
                    using (var destination = entry.Open())
                    {
                        while (true)
                        {
                            int read;
                            try
                            {
                                read = source.Read(buffer, 0, buffer.Length);
                            }
                            catch (Exception failure) when (Unwritable(failure))
                            {
                                throw Unreadable(item.Path, failure);
                            }

                            if (read == 0)
                            {
                                break;
                            }

                            hash.AppendData(buffer, 0, read);
                            destination.Write(buffer, 0, read);
                            length += read;
                        }
                    }

                    entries.Add(new ExportEntry(item.Name, length, Convert.ToHexStringLower(hash.GetHashAndReset())));
                    if (item.Name.StartsWith(ExportManifest.HarnessPrefix, StringComparison.Ordinal)
                        && item.Name.EndsWith("/" + HarnessMarker.Directory + "/" + HarnessMarker.FileName, StringComparison.Ordinal))
                    {
                        var marker = HarnessMarker.In(Path.GetDirectoryName(Path.GetDirectoryName(item.Path)!)!);
                        projects += marker?.Kind == HarnessMarker.Project ? 1 : 0;
                        campaigns += marker?.Kind == HarnessMarker.Campaign ? 1 : 0;
                    }
                }

                var listing = archive.CreateEntry(ExportManifest.FileName, CompressionLevel.Optimal);
                using var written = listing.Open();
                written.Write((manifest with { Entries = entries }).Serialize());
            }
        }
        catch (Exception failure) when (failure is ExportRefusal || Unwritable(failure))
        {
            throw TakenAway(partial, failure is ExportRefusal refused ? refused : new ExportRefusal(CliErrors.ExportUnwritable, $"{partial} could not be written: {failure.Message}"));
        }

        try
        {
            File.Move(partial, target, overwrite: false);
        }
        catch (Exception failure) when (Unwritable(failure))
        {
            throw TakenAway(
                partial,
                AlreadyThere(failure)
                    ? new ExportRefusal(CliErrors.ExportOutputExists, $"Something was put at {target} in the meantime, and an archive is never written over anything. Nothing was written.")
                    : new ExportRefusal(CliErrors.ExportUnwritable, $"{partial} could not be moved to {target}: {failure.Message}"));
        }

        var data = entries.Count(entry => entry.Path.StartsWith(ExportManifest.DataPrefix, StringComparison.Ordinal));
        return new Written(new FileInfo(target).Length, data, entries.Count - data, projects, campaigns);
    }

    /// <summary>A source that cannot be read: it cannot travel, which is the person's to mend, not the target's.</summary>
    private static ExportRefusal Unreadable(string path, Exception failure) =>
        new(CliErrors.ExportRefused, $"{path} cannot be read ({failure.Message}), so it cannot travel. Close what holds it, or mend it, and run this again. Nothing was written.");

    /// <summary>The part written taken away again, and the refusal saying whether it went.</summary>
    private static ExportRefusal TakenAway(string partial, ExportRefusal refused)
    {
        try
        {
            File.Delete(partial);
            return new ExportRefusal(refused.Code, refused.Message.EndsWith("Nothing was written.", StringComparison.Ordinal) ? refused.Message : refused.Message + " Nothing was left behind.");
        }
        catch (Exception leftover) when (Unwritable(leftover))
        {
            return new ExportRefusal(refused.Code, refused.Message + $" The part written could not be taken away again: {partial}.");
        }
    }

    private static bool Unwritable(Exception failure) =>
        failure is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException or InvalidDataException;

    /// <summary>Whether a create failed because a file is there, by the system's own code for it, as <c>doctor</c> reads it.</summary>
    private static bool AlreadyThere(Exception failure) =>
        failure is IOException { HResult: var code }
        && (OperatingSystem.IsWindows() ? code is unchecked((int)0x80070050) or unchecked((int)0x800700B7) : code == 17);

    // ---- what is printed ---------------------------------------------------------------------------------------------

    private static string Sentence(ExportWritten answer)
    {
        var runtime = answer.Stopped ? " The runtime was stopped for it and started again." : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Exported Jason {answer.JasonVersion} to {answer.File} ({answer.Bytes} bytes): {answer.DataFiles} files from the data directory and {answer.HarnessFiles} from the harness root at {answer.HarnessRoot}, with {answer.Projects} project and {answer.Campaigns} campaign folders.{runtime}");
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

    // ---- the contract ------------------------------------------------------------------------------------------------

    /// <summary>The contract, printed in help, every list read from the one the verb applies.</summary>
    private static string Contract(JasonPaths paths)
    {
        var nl = Environment.NewLine;
        return "Carried: " + string.Join(", ", DatabaseFiles.Select(file => "data/" + file)) + " (each when it is there); data/" + LayoutStamp.FileName + " (when it is there); "
            + string.Join(", ", CarriedTrees(paths).Select(tree => "data/" + tree + "/")) + ", each whole" + string.Concat(LeftOut(paths).Select(left => " but data/" + left + "/")) + ", the role skills' record "
            + "included; harness/, the harness root's tree as it stands, every folder in it whole, a .git included, and empty "
            + "with a warning when the root is not there; and export.json at the root, naming the Jason version, "
            + "the layout it carries, the moment, the operating system, the data directory, the harness root, the home directory, whether the runtime "
            + "starts at logon, and every file with its size and SHA-256." + nl
            + "Never carried: " + string.Join(", ", NeverCarried) + ", and anything outside the data directory and the harness root." + nl
            + "The harness root: " + InstallationPlaces.RootVariable + ", else the Harness:Root the settings file names, else "
            + InstallationPlaces.DefaultFolder + " in the home directory, held to the runtime's rule for a root." + nl
            + "Secrets travel whole. " + SecretsLine + nl
            + "The runtime: the files are read only while it is stopped, its lock on data/run/runtime.lock held for the read. "
            + "While it runs this refuses, naming 'jason runtime stop', unless --stop is given; then it stops the runtime and "
            + "starts it again once the archive is written, and starts none that was not running." + nl
            + "Links: a link at the top of a carried place, or a harness root that is one, is followed and judged where it leads, "
            + "and refused when that is inside the data directory or the harness root, or holds either; a link inside is refused "
            + "by its path." + nl
            + "Never over anything: a file that is already there is refused; the archive is written to <file>" + PartialSuffix
            + " and moved into place once whole, and a " + PartialSuffix + " already there is refused as yours to remove. A name "
            + "another system cannot hold, and two names apart only by the case of their letters, are refused by their path, "
            + "before anything is written. It reaches no network." + nl
            + "Exit codes: 0 written; 1 refused, with nothing written, or written and the runtime stopped for it did not start "
            + "again, which the answer says; 2 usage.";
    }
}
