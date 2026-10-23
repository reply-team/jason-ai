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
        AttemptErrors.RoleSkillUnreadable,
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
    /// What a launched session is given, and the one thing about it that is not confined. An operator deciding
    /// whether to let this runtime start an agent on their machine is deciding exactly this, and every line of
    /// it was established by running the shape rather than by reading the host's documentation.
    /// </summary>
    [Fact]
    public void The_profile_contract_says_what_a_launched_session_inherits_and_what_it_does_not()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        // Where its settings come from, and what therefore does not arrive with them.
        Assert.Contains("--setting-sources project", contract, StringComparison.Ordinal);
        Assert.Contains("--strict-mcp-config", contract, StringComparison.Ordinal);

        // The tools are all still there; what decides is the mode and the one allow rule. A page that said the
        // session "has only the callback" would be describing a tool list that does not exist.
        Assert.Contains("refused by the mode", contract, StringComparison.Ordinal);

        // No file-writing tool: the single fact a role's skill has to be written around.
        Assert.Contains("no file-writing tool", contract, StringComparison.Ordinal);

        // And the thing these flags do not take away, said plainly rather than left for somebody to discover in
        // their own home directory — and said at its true size. It is not a directory of notes: it is the whole
        // session, the launch envelope included, in the operator's own configuration. What is *not* in it
        // matters just as much, because that is the redaction the runtime does enforce.
        Assert.Contains("auto-memory", contract, StringComparison.Ordinal);
        Assert.Contains("the whole session", contract, StringComparison.Ordinal);
        Assert.Contains("the launch envelope", contract, StringComparison.Ordinal);
        Assert.Contains("no capability token", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three forms a launched session is composed with beyond the bare callback — the read rule per memory file,
    /// the callback through PowerShell, the exclusion of every <c>CLAUDE.md</c> — each with the host version it was
    /// measured on and where it was not, so that a later host has a line to be checked against rather than a
    /// behaviour somebody remembers.
    /// </summary>
    [Fact]
    public void The_profile_contract_records_what_a_launched_role_may_read_and_where_that_was_measured()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains("`Read(//c/Users/…/AGENTS.md)`", contract, StringComparison.Ordinal);
        Assert.Contains("`PowerShell(jason *)` beside `Bash(jason *)`", contract, StringComparison.Ordinal);
        Assert.Contains("`\"claudeMdExcludes\": [\"**/CLAUDE.md\", \"**/.claude/rules/**\", \"**/AGENTS.md\"]`", contract, StringComparison.Ordinal);
        Assert.Contains("Claude Code 2.1.309 on Windows", contract, StringComparison.Ordinal);
        Assert.Contains("a launched role loads no `CLAUDE.md` at all", contract, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(contract, "not measured\\*\\* on Linux or macOS"));
        Assert.Contains("gets no rule", contract, StringComparison.Ordinal);
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

        // And what that number counts: UTF-8, not the escaped form the note is named by, which counted three times
        // as much for anybody writing in another script — and the page that gives the figure is where they will
        // look after a refusal.
        Assert.Contains("UTF-8 bytes of the canonical form", contract, StringComparison.Ordinal);
        Assert.Contains("note_bytes", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// A role's notes at three levels are a decision of the architecture's, and the isolation it promises between
    /// campaigns says which of them it covers — in the isolation line, in the campaign invariant and in the manager's own
    /// decision: a campaign's own notes stay in it, and the client's and a project's are shared by design across the
    /// campaigns they cover. A page that still called role memory a campaign's alone would
    /// promise an isolation the runtime does not keep; one that still called learned practice "cross-campaign" would make
    /// a client's note one.
    /// </summary>
    [Fact]
    public void The_architecture_records_a_role_s_notes_at_three_levels_and_which_of_them_are_shared()
    {
        var architecture = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "architecture.md")));
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains(
            "| DEC-ROLE-003 | A role's memory is kept at three levels, the client's (its general craft), a project's and a campaign's,",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains(
            "the runtime composes nothing and reads none. `docs/execution-profiles.md` §8 is the contract. |",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains("## 8. Role notes", contract, StringComparison.Ordinal);

        Assert.Contains(
            "campaign-scoped state, a role's notes at the campaign's level, configuration overrides, work, execution lineage, plugin routes, and provider bindings must not leak between campaigns.",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains(
            "and a role's notes at the client's and a project's level, which cover every campaign of that scope, are shared by design.",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Role memory | Non-authoritative contextual continuity for an AI role, kept at one of three levels: the client's, a project's or a campaign's. |",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains(
            "| DEC-MGR-001 | Campaign manager is an AI role with isolated logical context and non-authoritative memory, kept per campaign and, above it, per project and for the client (DEC-ROLE-003). |",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains(
            "| INV-CAMP-001 | Campaign-scoped context, work, a role's campaign-level memory, route/binding overrides, and lineage remain logically isolated; explicitly configured user-level defaults, and a role's notes at the client's and a project's level, are shared by design. |",
            architecture,
            StringComparison.Ordinal);

        Assert.Contains("none of the other five knowledge concepts", contract, StringComparison.Ordinal);
        Assert.Contains("written by no role for itself", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("would be cross-campaign", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manager's memory is at the three levels like every role's, and the campaign's is its own: the architecture's
    /// section on the manager's context, the hydration it describes and the scheduled review all say so. One that still
    /// hydrated a manager with "campaign-scoped manager memory" would leave its craft at the client's and the project's
    /// level unread; and the rule of what a note holds at which level is the contract's, for every role.
    /// </summary>
    [Fact]
    public void The_manager_s_memory_is_at_three_levels_the_campaign_s_its_own_and_a_note_holds_its_own_level()
    {
        var architecture = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "architecture.md")));
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains(
            "Each business campaign has a dedicated logical manager context and a manager memory of its own at the campaign's "
            + "level; the manager's notes at the client's and the project's level are its own craft, shared across the "
            + "campaigns they cover (DEC-ROLE-003).",
            architecture,
            StringComparison.Ordinal);
        Assert.Contains("the manager's memory at the three levels, the campaign's its own", architecture, StringComparison.Ordinal);
        foreach (var gone in new[] { "isolated manager memory", "campaign-scoped manager memory", "campaign projections and isolated memory" })
        {
            Assert.DoesNotContain(gone, architecture, StringComparison.Ordinal);
        }

        Assert.Contains("a note never carries what belongs to a level below it", contract, StringComparison.Ordinal);
        Assert.Contains("a note never carries what belongs to a level below it", architecture, StringComparison.Ordinal);

        foreach (var page in new[] { architecture, contract })
        {
            Assert.DoesNotContain("first shape of learned practice", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("by the runtime's own process", page, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Two phrases say what the runtime does not do, and no public text may say them: the skills, the contracts and the rest of
    /// the open set are read by outsiders, so the refusal runs over every Markdown file the open set lists and over
    /// <c>docs/</c>, not over the two design pages alone.
    /// </summary>
    [Fact]
    public void No_Markdown_file_the_open_set_lists_or_docs_holds_the_two_refused_phrases()
    {
        var root = Publication.RepositoryRoot();
        var entries = OpenSet.Lines(root).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'));
        var files = entries.Concat(["docs/"])
            .SelectMany(entry => entry.EndsWith('/') ? OpenSet.Files(root, entry) : [entry])
            .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Where(file => File.Exists(Path.Combine(root, file)))
            .ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = Flattened(File.ReadAllText(Path.Combine(root, file)));
            foreach (var phrase in new[] { "first shape of learned practice", "by the runtime's own process" })
            {
                Assert.False(
                    text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    $"'{file}' says \"{phrase}\". No public text says it; the open set's Markdown files and docs/ are all refused it ({files.Count} files read).");
            }
        }
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
