using System.Text.RegularExpressions;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The documents and the code, kept from drifting apart. A refusal an operator meets in the field is only
/// actionable if the page they go to names it, and a page that names five of six codes is worse than one that
/// names none: it reads as complete.
/// </summary>
public partial class ExecutionProfileDocTests
{
    /// <summary>The codes an agent claim can refuse with, each of which the published order must name.</summary>
    private static readonly string[] AgentPreflightCodes =
    [
        AttemptErrors.LineageResolutionUnsupported,
        AttemptErrors.ProfileNotFound,
        AttemptErrors.ProfileDisabled,
        AttemptErrors.HostNotAvailable,
        AttemptErrors.RoleSkillInvalid,
        AttemptErrors.RoleNotLaunchable,
    ];

    [Fact]
    public void Every_agent_preflight_code_appears_in_the_published_order()
    {
        var published = File.ReadAllText(Path.Combine(DocumentsDirectory(), "work-execution.md"));

        var missing = AgentPreflightCodes.Where(code => !published.Contains(code, StringComparison.Ordinal)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void The_profile_contract_names_every_reason_ai_work_can_be_blocked()
    {
        var contract = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md"));

        var missing = AgentPreflightCodes.Where(code => !contract.Contains(code, StringComparison.Ordinal)).ToList();
        Assert.Empty(missing);
    }

    /// <summary>
    /// Why a launched role's memory is a table here at all, and what that does not make it. The claim is easy
    /// to lose in a later edit — a section about a feature drifts into describing what it does and stops saying
    /// what it is worth — and a reader who loses it will write code that believes a note.
    /// </summary>
    [Fact]
    public void The_profile_contract_says_why_a_launched_roles_memory_lives_here_and_what_it_is_worth()
    {
        // Read with its line breaks flattened. A published page wraps where the column runs out, so a guard
        // that searched the raw text would be asserting where a paragraph happens to break rather than what
        // it says — and would go red on a reflow that changed nothing.
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains("no harness that survives its attempt", contract, StringComparison.Ordinal);
        Assert.Contains("not authoritative", contract, StringComparison.Ordinal);
        Assert.Contains("INV-MEM-001", contract, StringComparison.Ordinal);

        // The cap and the size of a note are what a role is held to, so the page has to carry the number.
        Assert.Contains(
            (Jason.Runtime.Notes.RoleNoteService.MaxNoteBytes / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " KiB",
            contract,
            StringComparison.Ordinal);

        // And what that number counts. The canonical form escapes everything outside ASCII, so the figure alone
        // is misleading by a factor of three to anybody writing in another script — and the page that gives the
        // figure is where they will look after a refusal.
        Assert.Contains("outside ASCII are escaped", contract, StringComparison.Ordinal);
        Assert.Contains("note_bytes", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one sentence a reader has to be able to trust: a profile holds no credential. It is true because the
    /// revision has nowhere to put one, and the document says so — if the entity ever grows a field that could
    /// hold a secret, the sentence becomes a lie and this is where it is noticed.
    /// </summary>
    [Fact]
    public void A_revision_has_no_field_a_credential_could_live_in()
    {
        var fields = typeof(Runtime.Persistence.ExecutionProfileRevision)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(fields, name =>
            name.Contains("Environment", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One space wherever the source had any run of whitespace, so a fragment can span a line break.</summary>
    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string DocumentsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs");
    }
}
