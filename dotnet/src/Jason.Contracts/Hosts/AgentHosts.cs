using System.Text.Json;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;

namespace Jason.Contracts.Hosts;

/// <summary>
/// The file a host reads as its instructions in a folder, when that is not the person's memory file itself, and the one
/// line that makes it read the memory file: a harness folder holds one such file per name, written where absent.
/// </summary>
/// <param name="FileName">The file's name, such as <c>CLAUDE.md</c>.</param>
/// <param name="ImportLine">Its whole content, a line that imports <see cref="AgentHosts.MemoryFile"/>.</param>
public sealed record HostInstructionFile(string FileName, string ImportLine);

/// <summary>Whether a host's home marker is a directory or a file; only that kind at its path says the host is there.</summary>
public enum HomeMarkerKind
{
    /// <summary>A directory, such as a host's own folder under home.</summary>
    Directory,

    /// <summary>A file, such as one host's settings inside a folder two hosts keep.</summary>
    File,
}

/// <summary>
/// What Jason knows about one agent host, read by both halves: the CLI that deploys skills into a home and the runtime
/// that launches the host in a work directory. Every path is relative, written with forward slashes, and resolved with
/// <see cref="AgentHosts.Under"/>. No display name is kept here: what a person reads names a host by its kind.
/// </summary>
/// <param name="Kind">The host's value; its word is <see cref="AgentHosts.Word"/>.</param>
/// <param name="CommandWord">The program a profile's <c>program</c> usually names, and the name a test stub takes.</param>
/// <param name="HomeMarker">A directory or file under the home folder whose presence says the host is installed there.</param>
/// <param name="HomeSkills">Where under the home folder the host reads skills.</param>
/// <param name="WorkSkills">Where under a work directory the host reads skills, so a role's skill is placed there.</param>
/// <param name="Instructions">The file the host reads instead of the memory file, or null when it reads the memory file itself.</param>
/// <param name="MarkerKind">Whether <paramref name="HomeMarker"/> is a directory or a file: a path of the other kind detects nothing.</param>
/// <param name="InteractivePromptFlag">
/// The option an interactive session's first prompt follows when a person's host is started on an item
/// (<c>jason session open</c>), or null where the host takes that prompt as its one positional word. Read from each
/// host's own command-line reference; the table in <c>docs/execution-profiles.md</c> names the page and version.
/// </param>
/// <param name="ResumeWord">
/// The word a conversation's id follows when a person's host is started to continue one of its own conversations
/// (<c>jason session open --resume</c>), an option or a subcommand, or null where Jason knows no such word for the host,
/// which then cannot be asked to. Read from each host's own <c>--help</c>.
/// </param>
/// <param name="InteractiveFolderFlag">
/// The option a second folder follows when a person's host is started on Jason itself (<c>jason session open --setup</c>,
/// which adds the plugins folder beside the role skills folder it opens at), or null where Jason knows no such option for
/// the host, which is then given none. Read from each host's own reference; the table in <c>docs/execution-profiles.md</c>
/// says which.
/// </param>
public sealed record AgentHostFacts(
    AgentHostKind Kind,
    string CommandWord,
    string HomeMarker,
    string HomeSkills,
    string WorkSkills,
    HostInstructionFile? Instructions,
    HomeMarkerKind MarkerKind = HomeMarkerKind.Directory,
    string? InteractivePromptFlag = null,
    string? ResumeWord = null,
    string? InteractiveFolderFlag = null);

/// <summary>
/// The one table of agent hosts: one row per <see cref="AgentHostKind"/>, held to the enum by tests and to the runtime's
/// registrations by another, so a host is added as a value, a row and a registration together or not at all.
/// </summary>
public static partial class AgentHosts
{
    /// <summary>The person's memory file in every harness folder; every host's instructions reach it.</summary>
    public const string MemoryFile = "AGENTS.md";

    /// <summary>Every host, in the order of the enum.</summary>
    public static IReadOnlyList<AgentHostFacts> All { get; } =
    [
        // Claude Code continues a conversation by its session id after --resume, and Codex by its thread id after its resume
        // subcommand; each as its own --help says. Each takes a second folder after --add-dir: Claude Code's --help
        // ("--add-dir <directories...>", every bare word after it one more folder), and Codex's source at rust-v0.122.0,
        // the version the desktop's chat needs (codex-rs/tui/src/cli.rs, one folder for each --add-dir).
        new(AgentHostKind.ClaudeCode, "claude", ".claude", ".claude/skills", ".claude/skills", new("CLAUDE.md", "@" + MemoryFile), HomeMarkerKind.Directory, ResumeWord: "--resume", InteractiveFolderFlag: "--add-dir"),

        // Codex keeps its sign-in and settings in ~/.codex and reads skills from ~/.agents/skills and from the
        // .agents/skills folder of the directory it runs in; it reads AGENTS.md itself, so it needs no import file.
        new(AgentHostKind.Codex, "codex", ".codex", ".agents/skills", ".agents/skills", null, HomeMarkerKind.Directory, ResumeWord: "resume", InteractiveFolderFlag: "--add-dir"),
        new(AgentHostKind.CopilotCli, "copilot", ".copilot/config.json", ".agents/skills", ".agents/skills", null, HomeMarkerKind.File, "-i"),
        new(AgentHostKind.CursorCli, "agent", ".cursor/cli-config.json", ".agents/skills", ".agents/skills", null, HomeMarkerKind.File),

        // Gemini CLI, from its documentation and source at 0.66.0. The marker is the settings file the host keeps in its own
        // folder: that folder is also where Antigravity keeps files of its own, so the folder alone would detect Gemini CLI
        // on every Antigravity install, where this file is written by Gemini CLI only.
        new(AgentHostKind.GeminiCli, "gemini", ".gemini/settings.json", ".agents/skills", ".agents/skills", new("GEMINI.md", "@" + MemoryFile), HomeMarkerKind.File, "-i"),

        // Antigravity CLI keeps its sign-in and settings in its own folder under ~/.gemini and reads AGENTS.md itself.
        new(AgentHostKind.AntigravityCli, "agy", ".gemini/antigravity-cli", ".gemini/antigravity-cli/skills", ".agents/skills", null, HomeMarkerKind.Directory, "-i"),
    ];

    /// <summary>The instruction files a harness folder holds beside the memory file, each name once.</summary>
    public static IReadOnlyList<HostInstructionFile> ImportFiles { get; } =
        [.. All.Select(facts => facts.Instructions).OfType<HostInstructionFile>().DistinctBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The words a caller may send, in the order of the enum, for a refusal to name.</summary>
    public static string Vocabulary { get; } = string.Join(", ", Enum.GetValues<AgentHostKind>().Select(Word));

    /// <summary>The words of the hosts that can continue a conversation by its id, in the order of the enum, for a refusal to name.</summary>
    public static string ResumeVocabulary { get; } =
        string.Join(", ", Enum.GetValues<AgentHostKind>().Where(kind => Of(kind).ResumeWord is not null).Select(Word));

    /// <summary>The row of <paramref name="kind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The kind has no row: the table and the enum have drifted apart.</exception>
    public static AgentHostFacts Of(AgentHostKind kind) =>
        All.FirstOrDefault(facts => facts.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "This host kind has no row in the table of agent hosts.");

    /// <summary>The kind as the API writes it: snake_case, the same word the JSON dialect gives the enum.</summary>
    public static string Word(AgentHostKind kind) => JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString());

    /// <summary>
    /// The kind a word names, exactly as the API writes it after surrounding white space is trimmed, or null. There is
    /// one spelling: another case, the member's own name or a hyphen names nothing.
    /// </summary>
    public static AgentHostKind? Parse(string? word)
    {
        var text = word?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (var kind in Enum.GetValues<AgentHostKind>())
        {
            if (string.Equals(Word(kind), text, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// What follows the host's program to start an interactive session with <paramref name="prompt"/> as its first
    /// input: the prompt as one argument, whole, after the host's flag where it takes one. Nothing else is passed — no
    /// permission, no model, no folder — since the session is the person's own, under their own settings.
    /// </summary>
    public static IReadOnlyList<string> InteractiveArguments(AgentHostKind kind, string prompt)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);
        return Of(kind).InteractivePromptFlag is { } flag ? [flag, prompt] : [prompt];
    }

    /// <summary>
    /// What follows the host's program to start an interactive session with <paramref name="prompt"/> as its first input
    /// and <paramref name="folder"/> as a second folder it may work in: the prompt's words, then the host's
    /// <see cref="AgentHostFacts.InteractiveFolderFlag"/> and the folder, each one argument. The folder comes last because
    /// a flag of many values (Claude Code's) takes every bare word after it as one more folder. Where the folder is null
    /// or the host has no such flag, only the prompt's words.
    /// </summary>
    public static IReadOnlyList<string> InteractiveArguments(AgentHostKind kind, string prompt, string? folder)
    {
        var prompted = InteractiveArguments(kind, prompt);
        return folder is { Length: > 0 } && Of(kind).InteractiveFolderFlag is { } flag ? [.. prompted, flag, folder] : prompted;
    }

    /// <summary>
    /// Whether <paramref name="id"/> has the one shape a host's conversation id is taken in: a letter or a digit, then at
    /// most 199 letters, digits, dots, underscores, colons and hyphens, all of them ASCII. Nothing else is handed to a host:
    /// no white space, no quote, no separator of a path, and no first character a host would read as an option.
    /// </summary>
    public static bool IsConversationId(string? id) => id is not null && ConversationIdShape().IsMatch(id);

    /// <summary>
    /// What follows the host's program to continue its own conversation <paramref name="id"/> instead of starting a new
    /// one: the host's <see cref="AgentHostFacts.ResumeWord"/> and the id, each one argument; or null where the host has
    /// no such word. Nothing else is passed, as for a first prompt.
    /// </summary>
    /// <exception cref="ArgumentException">The id is not of the shape <see cref="IsConversationId"/> takes.</exception>
    public static IReadOnlyList<string>? ResumeArguments(AgentHostKind kind, string id)
    {
        if (!IsConversationId(id))
        {
            throw new ArgumentException("A conversation's id is a letter or digit, then at most 199 letters, digits, dots, underscores, colons or hyphens.", nameof(id));
        }

        return Of(kind).ResumeWord is { } word ? [word, id] : null;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._:-]{0,199}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ConversationIdShape();

    /// <summary>A relative path of the table resolved under <paramref name="folder"/>, with the platform's separator.</summary>
    public static string Under(string folder, string relative) =>
        Path.Combine([folder, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}
