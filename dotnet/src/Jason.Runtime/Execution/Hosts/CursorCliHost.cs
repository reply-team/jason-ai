using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless Cursor CLI session, started for one attempt. The runtime composes the whole of the session's shape and
/// leaves a profile only the program and whatever it wants to add at the end.
/// <para>
/// What is written here was read from Cursor's documentation and from the bundle of the installed version
/// 2026.10.22-27dcf5f, and was not run on a signed-in account: no key is held by Jason, and the sign-in is the
/// person's own, made as themselves with <c>agent login</c>. In print mode the host reads its prompt from the whole of
/// stdin when no word for one stands on the command line and stdin is not a terminal, and ignores stdin when a word
/// does, so the brief goes on stdin and no composed argument is a bare word. <c>--new-session-id</c> is not in the
/// host's help text; the bundle validates it as a version-4 UUID, and a profile may not name another one.
/// </para>
/// <para>
/// On Windows the word on the search path is a <c>.cmd</c> that runs a PowerShell script, which in turn runs the newest
/// version's <c>node.exe</c> with its <c>index.js</c>. A script is never started through a shell, so
/// <see cref="ProgramLayout"/> answers what the launcher script itself would start, by the rule that script uses.
/// </para>
/// </summary>
/// <param name="windows">
/// Whether this machine is Windows, which decides whether a launcher script has a layout. The platform this runs on,
/// unless a test says otherwise.
/// </param>
public sealed partial class CursorCliHost(bool windows) : IAgentHost
{
    /// <summary>The two names the installer gives the launcher script, which is the host's own and nobody else's.</summary>
    private static readonly string[] LauncherNames = ["agent.cmd", "cursor-agent.cmd"];

    /// <summary>The script both launchers run, which chooses the version directory.</summary>
    private const string LauncherScript = "cursor-agent.ps1";

    public CursorCliHost()
        : this(OperatingSystem.IsWindows())
    {
    }

    public AgentHostKind Kind => AgentHostKind.CursorCli;

    /// <summary>
    /// Everything this host reads that would change the shape composed below, plus the ones that would undo the
    /// confinement it rests on or hand the session an account, an endpoint or a directory of someone else's. Read from
    /// the help text and the bundle of 2026.10.22-27dcf5f.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "-p", "--print",
        "--output-format",
        "--stream-partial-output",

        // Every one of these makes a session act without asking, which the work directory's deny rules and the
        // allow list were written to make unnecessary, or runs it somewhere the host's own sandbox decides.
        "-f", "--force",
        "--yolo",
        "--auto-review",
        "--trust",
        "--sandbox",
        "--approve-mcps",

        // Where the session works and what else it reads.
        "--workspace",
        "--add-dir",
        "--mode",
        "--plan",
        "--resume",
        "--continue",
        "--new-session-id",
        "--exclude-tools",
        "-w", "--worktree",
        "--worktree-base",
        "--skip-worktree-setup",
        "--plugin-dir",
        "--disable-project-configs",
        "--statsig-overrides",

        // Whose account it is and where it calls: no key is held by Jason, and the sign-in is the person's own.
        "--api-key",
        "--auth-token",
        "-H", "--header",
        "-e", "--endpoint",
    };

    public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(memoryFiles);
        if (launch.Count == 0)
        {
            throw new ArgumentException("A launch names at least the program to start.", nameof(launch));
        }

        var session = Guid.NewGuid().ToString("D");

        // Written into the work directory, where a deny is honoured: writing is denied wholly, and a fetch is denied
        // unless the revision grants it.
        string[] deny = ["Write(**)", .. Enum.GetValues<AgentTool>().Except(revision.Tools).Where(tool => tool != AgentTool.WebSearch).Select(ToolRule)];

        return new HostLaunch(
            [
                .. launch,
                "-p",
                "--output-format",
                "stream-json",

                // The brief is on stdin. Trusting the work directory is what lets the host read the project's
                // permission file there at all; the marker it leaves is one directory of the person's own for
                // each attempt.
                "--trust",
                "--new-session-id",
                session,

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it. And no word
                // of it may be a prompt, which would make the host ignore stdin.
                .. revision.Args,
            ],
            session,
            [],
            deny);
    }

    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> deny)
    {
    }

    /// <summary>
    /// A fixed sentence when the host ended because the account is not signed in, which it says before it reads stdin,
    /// either as no sign-in or as a stored one the server refused; null for every other end. Nothing of the tail is
    /// carried: it can hold the person's address.
    /// </summary>
    public string? Unavailable(int exitCode, string? stderrTail)
    {
        if (exitCode == 0 || string.IsNullOrEmpty(stderrTail))
        {
            return null;
        }

        return stderrTail.Contains("agent login", StringComparison.OrdinalIgnoreCase)
            || stderrTail.Contains("stored authentication is invalid", StringComparison.OrdinalIgnoreCase)
            ? "Cursor CLI is not signed in on this account: run `agent login` as yourself."
            : null;
    }

    public string ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebFetch => "WebFetch(*)",
        AgentTool.WebSearch => "WebSearch",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    /// <summary>
    /// Whether a file name is one of the two this host's installer gives its launcher script. A name only: a profile
    /// is written before it is known whether the file is there.
    /// </summary>
    public bool NamesItsLauncher(string fileName) =>
        windows && LauncherNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the launcher script starts, as a command: the interpreter, then the entry script. Where a <c>node.exe</c>
    /// and an <c>index.js</c> stand beside the launcher the script starts those, and otherwise the newest directory
    /// under <c>versions</c> whose name reads as a version, newest by its date taken as a number — so a month written
    /// with one digit is older than one written with two — and by name when two share a day, which the script leaves
    /// unordered. A staged or previous directory is never one. Nothing is remembered between calls: a version installed
    /// since is the answer the next time. Null where this is not Windows, where the file is not one of the two
    /// launchers or its script is not beside it, or where the chosen directory lacks either file.
    /// </summary>
    public IReadOnlyList<string>? ProgramLayout(string shimFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shimFile);

        try
        {
            if (!windows || !NamesItsLauncher(Path.GetFileName(shimFile)) || Path.GetDirectoryName(shimFile) is not { Length: > 0 } root
                || !File.Exists(Path.Combine(root, LauncherScript)))
            {
                return null;
            }

            if (Pair(root) is { } beside)
            {
                return beside;
            }

            var versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions))
            {
                return null;
            }

            var newest = Directory.EnumerateDirectories(versions)
                .Select(directory => (Directory: directory, Date: DateOf(Path.GetFileName(directory))))
                .Where(version => version.Date is not null)
                .OrderByDescending(version => version.Date)
                .ThenByDescending(version => Path.GetFileName(version.Directory), StringComparer.Ordinal)
                .Select(version => version.Directory)
                .FirstOrDefault();

            return newest is null ? null : Pair(newest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A version directory's name as the launcher script reads one: a date, an optional time, and a hash.</summary>
    [GeneratedRegex(@"^(?<year>\d{4})\.(?<month>\d{1,2})\.(?<day>\d{1,2})(-\d{2}-\d{2}-\d{2})?-[a-f0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionName();

    private static string[]? Pair(string directory)
    {
        var node = Path.Combine(directory, "node.exe");
        var entry = Path.Combine(directory, "index.js");
        return File.Exists(node) && File.Exists(entry) ? [node, entry] : null;
    }

    private static long? DateOf(string name)
    {
        var match = VersionName().Match(name);
        return match.Success
            ? (long.Parse(match.Groups["year"].Value) * 10_000) + (long.Parse(match.Groups["month"].Value) * 100) + long.Parse(match.Groups["day"].Value)
            : null;
    }
}
