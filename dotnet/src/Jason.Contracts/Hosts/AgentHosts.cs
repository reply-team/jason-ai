using System.Text.Json;
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
public sealed record AgentHostFacts(
    AgentHostKind Kind,
    string CommandWord,
    string HomeMarker,
    string HomeSkills,
    string WorkSkills,
    HostInstructionFile? Instructions,
    HomeMarkerKind MarkerKind = HomeMarkerKind.Directory);

/// <summary>
/// The one table of agent hosts: one row per <see cref="AgentHostKind"/>, held to the enum by tests and to the runtime's
/// registrations by another, so a host is added as a value, a row and a registration together or not at all.
/// </summary>
public static class AgentHosts
{
    /// <summary>The person's memory file in every harness folder; every host's instructions reach it.</summary>
    public const string MemoryFile = "AGENTS.md";

    /// <summary>Every host, in the order of the enum.</summary>
    public static IReadOnlyList<AgentHostFacts> All { get; } =
    [
        new(AgentHostKind.ClaudeCode, "claude", ".claude", ".claude/skills", ".claude/skills", new("CLAUDE.md", "@" + MemoryFile), HomeMarkerKind.Directory),
        new(AgentHostKind.AntigravityCli, "agy", ".gemini/antigravity-cli", ".gemini/antigravity-cli/skills", ".agents/skills", null, HomeMarkerKind.Directory),
    ];

    /// <summary>The instruction files a harness folder holds beside the memory file, each name once.</summary>
    public static IReadOnlyList<HostInstructionFile> ImportFiles { get; } =
        [.. All.Select(facts => facts.Instructions).OfType<HostInstructionFile>().DistinctBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The words a caller may send, in the order of the enum, for a refusal to name.</summary>
    public static string Vocabulary { get; } = string.Join(", ", Enum.GetValues<AgentHostKind>().Select(Word));

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

    /// <summary>A relative path of the table resolved under <paramref name="folder"/>, with the platform's separator.</summary>
    public static string Under(string folder, string relative) =>
        Path.Combine([folder, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}
