using System.Globalization;
using System.Text;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless Codex CLI run, started for one attempt with <c>codex exec</c>. Codex confines a run by a sandbox rather
/// than by a list of the commands it may run, so what the runtime composes is the sandbox: a permissions profile of its
/// own, named <c>jason</c> and given whole on the command line, that reads only what a role needs, writes nowhere, and
/// reaches this machine's loopback, where the runtime listens, and no other address. A command the role runs is
/// confined by that sandbox, not by its name.
/// <para>
/// Built from the host's documentation and source for version 0.166.0 (the <c>rust-v0.166.0</c> tag): the brief is read
/// from stdin when no prompt is given; <c>--ignore-user-config</c> leaves the person's <c>config.toml</c> unread while
/// the sign-in is still found where the person keeps it; a <c>-c</c> value is read as TOML and, where it does not parse,
/// taken as a string without a word, which is why every value here is written as exact TOML and held so by tests;
/// <c>--strict-config</c> refuses a key the host does not know, so a setting of another version fails the start
/// rather than being dropped. Restricting the sandbox's network to loopback rests on the host's network proxy, an
/// experimental feature in this version.
/// </para>
/// <para>
/// What no flag reaches: Codex reads the person's global <c>AGENTS.md</c> in its own home for every run, so a role reads
/// it whatever the runtime composes; and it offers every role the skills in <c>~/.agents/skills</c>, in
/// <c>$CODEX_HOME/skills</c> and in its system cache, even with <c>--ignore-user-config</c>, since that flag still builds
/// an empty user layer and each user layer names those roots. Both are the host's facts, said where a person reads the
/// host's row, and not composed away: the switch for skills is a feature still under development in 0.166.0.
/// </para>
/// <para>
/// Measured with the CLI bundled in the Codex app, 0.166.0-alpha.5.1, on Windows: the sandbox that needs no administrator
/// refused every command of this profile, since it reads the whole disk or nothing and cannot hold the network to
/// loopback. So a Windows run names the elevated sandbox, which an administrator sets up once.
/// </para>
/// </summary>
/// <param name="runDirectory">
/// The data directory's <c>run</c> folder, which holds the descriptor the CLI reads to find the runtime; null where the
/// runtime has no data directory of its own, and then no such folder is named.
/// </param>
/// <param name="executable">
/// What of the executable the role calls home with must be readable for it to start: the one file a single-file build
/// is, or the folder of a build of many files; null where it is unknown.
/// </param>
/// <param name="extractionDirectory">
/// The folder that executable unpacks its native libraries into when it starts, which it reads again on every start;
/// null where it is unknown.
/// </param>
/// <param name="windows">Whether the paths this host is given are Windows paths.</param>
/// <param name="home">The person's home, for a deny written from it; the current user's unless a test says otherwise.</param>
public sealed class CodexHost(
    string? runDirectory, string? executable, string? extractionDirectory, bool windows, string? home = null) : IAgentHost
{
    /// <summary>The name of the permissions profile composed for every run.</summary>
    private const string Profile = "jason";

    /// <summary>
    /// The characters the sandbox reads in a filesystem path as a pattern. A pattern may only deny, so a file whose path
    /// holds one cannot be made readable as that one file.
    /// </summary>
    private static readonly char[] PatternCharacters = ['*', '?', '[', ']', '{', '}'];

    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public AgentHostKind Kind => AgentHostKind.Codex;

    /// <summary>
    /// A path this installation runs from that holds a pattern character: the sandbox would read it as a pattern, and a
    /// pattern may only deny, so a run could not read it and could not call the runtime back. Held to the same check as
    /// the memory files, but a memory file left out costs the run one file, and these cost it the runtime.
    /// </summary>
    public string? LaunchProblem()
    {
        foreach (var (path, what) in new[]
                 {
                     (runDirectory, "the folder of the data directory that tells a run how to reach the runtime"),
                     (executable, "the Jason executable a run calls the runtime back with"),
                     (extractionDirectory, "the folder that executable unpacks its libraries into"),
                 })
        {
            if (path is not null && path.IndexOfAny(PatternCharacters) is var at and >= 0)
            {
                return $"codex cannot be given '{path}', {what}, because it holds '{path[at]}', which codex reads in a "
                    + "path as a pattern rather than a name. Run Jason from a path that holds none of * ? [ ] { }.";
            }
        }

        return null;
    }

    /// <summary>
    /// The host as this process registers it: the descriptor's folder of the data directory this runtime keeps, and what
    /// calling home reads of this very build (<see cref="CallHomeReads"/>).
    /// </summary>
    public static CodexHost ForThisProcess(JasonPaths? paths)
    {
        var windows = OperatingSystem.IsWindows();
        var (executable, extraction) = CallHomeReads(
            SelfExecutable.InstalledImage,
            ProgramResolver.ExecutableDirectory,
            ExtractedLibraries.OfThisProcess(),
            ExtractedLibraries.DefaultBase(
                Environment.GetEnvironmentVariable(ExtractedLibraries.BaseVariable),
                windows,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetTempPath()));
        return new CodexHost(paths?.RunDirectory, executable, extraction, windows);
    }

    /// <summary>
    /// What a role that calls home must read of the build it calls, beside the descriptor. A single-file build is one
    /// file, which unpacks its native libraries into a folder of its own and reads them there again on every start: that
    /// file and that folder, and nothing else in either place. Any other build is a folder of many files, read whole, and
    /// where it would unpack is the base a build unpacks under by rule.
    /// </summary>
    /// <param name="image">The one file this build is installed as, or null where it is no single-file build.</param>
    /// <param name="executableDirectory">The folder of the file that carries this build.</param>
    /// <param name="extracted">The folder this running build unpacked into, as its host names it; null where none is named.</param>
    /// <param name="defaultExtraction">The base a build unpacks under where nothing names its own folder.</param>
    public static (string? Executable, string Extraction) CallHomeReads(
        string? image, string? executableDirectory, string? extracted, string defaultExtraction) =>
        image is not null && extracted is not null ? (image, extracted) : (executableDirectory, defaultExtraction);

    /// <summary>
    /// Everything that would change the shape composed below or undo the sandbox it rests on. <c>-c</c> is reserved whole:
    /// one more setting after the runtime's could replace any of them. A model is the person's to choose.
    /// <para>
    /// Read from the parser's own declarations at the tag this host is built from, <c>rust-v0.166.0</c>: every
    /// <c>long</c>, <c>short</c>, <c>alias</c> and <c>visible_alias</c> of the arguments <c>codex exec</c> parses, in
    /// <c>exec/src/cli.rs</c> and <c>utils/cli/src/shared_options.rs</c>, and the root's global ones in
    /// <c>cli/src/main.rs</c> and <c>utils/cli/src/config_override.rs</c>. Never from <c>--help</c>, which prints no
    /// alias: <c>--yolo</c>, a second name for <c>--dangerously-bypass-approvals-and-sandbox</c>, is in no help text.
    /// The parser infers no long flag from a prefix of one, so a whole name is the only name.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "exec",
        "--json", "--experimental-json",
        "--skip-git-repo-check",
        "--ephemeral",
        "--ignore-user-config",
        "--ignore-rules",
        "--strict-config",
        "-c", "--config",
        "--enable", "--disable",
        "-s", "--sandbox",
        "-a", "--ask-for-approval",
        "--approve-for-me", "--not-so-yolo",
        "--dangerously-bypass-approvals-and-sandbox", "--yolo",
        "--dangerously-bypass-hook-trust",
        "-P", "--permission-profile",
        "-p", "--profile",
        "-C", "--cd",
        "--add-dir",
        "--worktree",
        "-o", "--output-last-message",
        "--output-schema",
        "-i", "--image",
        "--search",
        "--remote", "--remote-auth-token-env",
        "--thread-source",
        "--cyber-access-program",
    };

    /// <summary>
    /// The Windows sandbox every run names on Windows, and nothing elsewhere. Without <c>windows.sandbox</c> this host's
    /// Windows sandbox is disabled and a command runs unconfined; the non-elevated one reads the whole disk and cannot
    /// hold the network to loopback, so only the elevated one keeps the profile above. It is set up once by an
    /// administrator, as Codex's Windows sandbox page says; before that, every command a role runs is refused.
    /// </summary>
    private string[] WindowsSandbox => windows ? ["-c", "windows.sandbox=\"elevated\""] : [];

    public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(memoryFiles);
        if (launch.Count == 0)
        {
            throw new ArgumentException("A launch names at least the program to start.", nameof(launch));
        }

        var (entries, unreadable) = FileSystem(revision, memoryFiles);
        // A search is live only where the revision grants it and denies none of it: the host's deny wins over the grant,
        // and a deny of some searches is one it can hold only whole.
        var search = revision.Tools.Contains(AgentTool.WebSearch) && !revision.Deny.Any(DeniesSearch) ? "live" : "disabled";

        return new HostLaunch(
            [
                .. launch,
                "exec",

                // A run that reports as it goes, outside a git repository, that leaves no session file of its own behind,
                // and that reads none of the person's own configuration or command rules; a setting the host does not
                // know fails the start.
                "--json",
                "--skip-git-repo-check",
                "--ephemeral",
                "--ignore-user-config",
                "--ignore-rules",
                "--strict-config",

                // Nobody is asked: a step the sandbox refuses is refused.
                "-c", "approval_policy=\"never\"",

                // The sandbox, as one named profile: what may be read, nothing written, and loopback only.
                "-c", $"default_permissions={Toml(Profile)}",
                "-c", $"permissions.{Profile}.filesystem={{{string.Join(", ", entries.Select(entry => $"{Toml(entry.Path)}={Toml(entry.Access)}"))}}}",
                "-c", $"permissions.{Profile}.network={{enabled=true, allow_local_binding=true}}",
                "-c", "features.network_proxy=true",

                // On Windows, the elevated sandbox, always. The person's own choice of it is in the configuration this
                // run does not read, and with none named the host runs a command with no sandbox at all. Until an
                // administrator has set that sandbox up, every command is refused, so the run fails closed.
                .. WindowsSandbox,
                "-c", $"web_search={Toml(search)}",

                // No instruction file of a folder above the work directory, and no hook, plugin, app, memory or MCP
                // server the person installed into the host. Their skills still reach the role, and their global
                // AGENTS.md: no flag of this version takes either away (the class comment says why).
                "-c", "project_root_markers=[]",
                "-c", "features.hooks=false",
                "-c", "features.plugins=false",
                "-c", "features.apps=false",
                "-c", "features.memories=false",
                "-c", "mcp_servers={}",

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it.
                .. revision.Args,
            ],
            null,
            unreadable,
            [],
            []);
    }

    /// <summary>Nothing: every rule of a run is on its command line, so the work directory is left as the runtime made it.</summary>
    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);
    }

    /// <summary>
    /// Nothing: this host checks no sign-in before it starts, and an end without one is an exit like any other.
    /// </summary>
    public string? Unavailable(int exitCode, string? stderrTail) => null;

    /// <summary>
    /// The names a deny of a command is written under. The host runs commands and confines each by its sandbox, whatever
    /// its name, so no such deny can be held.
    /// </summary>
    private static readonly string[] CommandTools = ["Bash", "PowerShell", "Shell"];

    /// <summary>The host mints the id of every run it starts and has no flag that names one, so a launch names none.</summary>
    public bool TakesSessionId => false;

    /// <summary>
    /// Flags only, each value joined to its flag by <c>=</c>. The host reads a bare word as the prompt, which would
    /// replace the brief on stdin, or as a subcommand; and it reads one dash and a letter run together with more as that
    /// letter's flag and its value, which the reserved flags would not catch.
    /// </summary>
    public string? ArgsProblem(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var arg in args)
        {
            var flag = arg.Split('=', 2)[0];
            var isLong = flag.Length > 2 && flag.StartsWith("--", StringComparison.Ordinal) && flag[2] != '-';
            var isShort = flag.Length == 2 && flag[0] == '-' && flag[1] != '-';
            if (!isLong && !isShort)
            {
                return $"'{arg}' is not a flag; codex reads a bare word as the prompt or a subcommand, so each argument is a flag with its value joined by '=' (--model=<name>).";
            }
        }

        return null;
    }

    /// <summary>
    /// The two denies this sandbox cannot hold: a command by its name, since every command runs inside the sandbox
    /// whatever its name, and a read of a path the sandbox cannot name, which is one from the root, a drive on Windows,
    /// or the home. Every other deny holds: no write root is granted, a search is switched off, and a tool the host does
    /// not have, a fetch or an MCP tool among them, is the person's word, kept as written.
    /// </summary>
    public string? DenyProblem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (Array.Exists(CommandTools, tool => word == tool || word.StartsWith($"{tool}(", StringComparison.Ordinal)))
        {
            return "codex confines every command a role runs by its sandbox, not by its name, so a command cannot be denied by name; it runs inside the sandbox whatever it is called.";
        }

        if ((word == "Read" || word.StartsWith("Read(", StringComparison.Ordinal)) && DeniedPath(word) is null)
        {
            return windows
                ? "a read is denied on codex by a path from a drive (Read(//d/...)) or the home (Read(~/...))."
                : "a read is denied on codex by a path from the root (Read(//...)) or the home (Read(~/...)).";
        }

        return null;
    }

    /// <summary>Whether a deny word takes a search away, whole or in part.</summary>
    private static bool DeniesSearch(string word) =>
        word == "WebSearch" || word.StartsWith("WebSearch(", StringComparison.Ordinal);

    /// <summary>
    /// A search is the one tool of the vocabulary this host has, and a profile takes it away by the deny word written for
    /// it. The host has no tool that fetches a page, so a grant of one is a tool it does not have.
    /// </summary>
    public string? ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebSearch => "WebSearch",
        AgentTool.WebFetch => null,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    /// <summary>
    /// The sandbox's filesystem entries, in order and each path once: what the host itself needs to start, the work
    /// directory, what calling home reads, each memory file the launch names, and last each path the profile denies
    /// reading. A deny wins over a read: a denied path the run would otherwise read is denied in the read's place, never
    /// dropped beside it.
    /// </summary>
    private (List<(string Path, string Access)> Entries, List<string> Unreadable) FileSystem(
        ExecutionProfileRevision revision, MemoryFileLocations memoryFiles)
    {
        var entries = new List<(string Path, string Access)>();
        void Add(string? path, string access)
        {
            if (path is null)
            {
                return;
            }

            var at = entries.FindIndex(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
            if (at < 0)
            {
                entries.Add((path, access));
            }
            else if (access == "deny")
            {
                entries[at] = (path, access);
            }
        }

        Add(":minimal", "read");
        Add(":workspace_roots", "read");
        Add(executable, "read");
        Add(extractionDirectory, "read");
        Add(runDirectory, "read");

        var unreadable = new List<string>();
        foreach (var (level, path) in new[] { ("client", memoryFiles.Client), ("project", memoryFiles.Project), ("campaign", memoryFiles.Campaign) })
        {
            if (path is null)
            {
                continue;
            }

            if (path.IndexOfAny(PatternCharacters) >= 0)
            {
                unreadable.Add(level);
            }
            else
            {
                Add(path, "read");
            }
        }

        foreach (var word in revision.Deny)
        {
            Add(DeniedPath(word), "deny");
        }

        return (entries, unreadable);
    }

    /// <summary>
    /// The path a deny word names, or null where it names none: <c>Read(//…)</c> from the root, <c>//d/…</c> on Windows
    /// from that drive, and <c>Read(~/…)</c> from the person's home. Every other word holds without an entry or is not
    /// a deny of reading.
    /// </summary>
    private string? DeniedPath(string word)
    {
        if (!word.StartsWith("Read(", StringComparison.Ordinal) || !word.EndsWith(')'))
        {
            return null;
        }

        var path = word["Read(".Length..^1];
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return $"{home.TrimEnd('/', '\\')}/{path[2..]}";
        }

        if (!path.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (!windows)
        {
            return path[1..];
        }

        return path.Length > 3 && char.IsAsciiLetter(path[2]) && path[3] == '/'
            ? $"{char.ToUpperInvariant(path[2])}:{path[3..]}"
            : null;
    }

    /// <summary>A TOML basic string: a backslash, a quote and a control character escaped, and nothing else changed.</summary>
    private static string Toml(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var character in value)
        {
            text.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                _ when char.IsControl(character) => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }

        return text.Append('"').ToString();
    }
}
