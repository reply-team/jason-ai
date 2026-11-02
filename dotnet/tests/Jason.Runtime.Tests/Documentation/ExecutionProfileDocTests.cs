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

        // The figure above follows the constant, so a constant and a page changed together would still agree; the
        // number the page has always stated is held here as written.
        Assert.Contains("64 KiB", contract, StringComparison.Ordinal);

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
    /// Two phrases say what the runtime does not do, and no text of the repository may say them: the skills, the contracts
    /// and the rest of the tree are read by outsiders, so the refusal runs over every Markdown file the tree holds, not
    /// over the two design pages alone.
    /// </summary>
    [Fact]
    public void No_Markdown_file_the_tree_holds_says_the_two_refused_phrases()
    {
        var root = RepositoryTree.Root();
        var files = RepositoryTree.Files(root).Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = Flattened(File.ReadAllText(Path.Combine(root, file)));
            foreach (var phrase in new[] { "first shape of learned practice", "by the runtime's own process" })
            {
                Assert.False(
                    text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    $"'{file}' says \"{phrase}\". No text of the repository says it; every Markdown file of the tree is refused it ({files.Count} files read).");
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

    /// <summary>
    /// A grant and a deny meet in one place, the host, and the host's deny wins. The page says so, and says what a
    /// profile that names both by the same word gets instead: a refusal when it is written, on the grant, with its
    /// code. A pattern is only written beside the grant, and the page claims for it no more than the one pattern run
    /// showed; the host's deny flag is reserved, so no deny arrives where that check cannot see it.
    /// </summary>
    [Fact]
    public void The_profile_contract_says_the_hosts_deny_wins_over_the_grant()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains("the host's deny wins over the grant", contract, StringComparison.Ordinal);
        Assert.Contains("`tools[i]` with the code `denied`", contract, StringComparison.Ordinal);
        Assert.Contains("`WebFetch(domain:example.com)`", contract, StringComparison.Ordinal);
        Assert.Contains(
            "a deny that is a pattern is not refused and is written beside the grant; how much of the grant it leaves is the host's to decide",
            contract,
            StringComparison.Ordinal);
        Assert.DoesNotContain("narrows the grant", contract, StringComparison.Ordinal);
        Assert.Contains("nor take one away: `--disallowed-tools` is one too", contract, StringComparison.Ordinal);
        Assert.Contains("the refusal says which index is the profile's", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The example that opens the web keeps the shell deny the line before it set, because a named deny list replaces
    /// the stored one whole; and the page says the way a grant is taken back, now that a deny of the tool is refused.
    /// </summary>
    [Fact]
    public void The_profile_contract_grants_beside_the_shell_deny_and_says_how_a_grant_is_withdrawn()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains(
            "jason profile update local-claude --deny Write --deny \"Bash(curl *)\" --tool web_search --tool web_fetch",
            contract,
            StringComparison.Ordinal);
        Assert.Contains("A grant is withdrawn by an update that names an empty `tools` (the CLI's `--file`)", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both pages that list the work directory's settings file name the deny of each ungranted web tool, so neither
    /// says the file holds the profile's rules and nothing else; and the record's <c>tools</c> is said to be absent
    /// where no revision was read, and to have host names beside it only where a launch was made.
    /// </summary>
    [Fact]
    public void The_pages_list_the_deny_of_each_ungranted_web_tool_and_when_a_record_has_tools()
    {
        var execution = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "work-execution.md")));
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains("a deny of each web tool its revision does not grant", execution, StringComparison.Ordinal);
        Assert.Contains("a deny of each web tool the revision does not grant (§7), each rule once", contract, StringComparison.Ordinal);
        Assert.Contains("where a launch was made, the host's names for them are in the argument list", contract, StringComparison.Ordinal);
        Assert.Contains("A record whose claim never read a revision", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The grant is closed by default, and the page says it as a promise, with the one reason an ungranted tool is
    /// denied rather than left unlisted, given as the run that showed it rather than as a rule of the host. A page that
    /// still said a profile without a grant was "not a promise" would describe a launch that no longer exists.
    /// </summary>
    [Fact]
    public void The_profile_contract_promises_that_an_ungranted_web_tool_is_not_there()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains("without the grant the tool is not there", contract, StringComparison.Ordinal);
        Assert.Contains(
            "run with neither an allow rule nor a deny, the same host fetched a page of its own documentation list without asking, while it refused an ordinary page",
            contract,
            StringComparison.Ordinal);
        Assert.Contains("the role was seen to reach them through the host's tool search, which no rule named", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("not a promise", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host that is not on the machine ends the attempt as <c>host_not_available</c>, whichever host it is; one that
    /// stops because it is not signed in does so only where that host reads a signal for it, which its row names. A host
    /// that reads none, a host whose not-signed-in exit looks like any other, ends as any exit does, so the page may not
    /// promise every host the code.
    /// </summary>
    [Fact]
    public void The_host_matrix_says_a_signed_out_host_ends_as_host_not_available_only_where_its_row_names_the_signal()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));

        Assert.Contains("A host whose program is not on this machine ends the attempt with `host_not_available`", contract, StringComparison.Ordinal);
        Assert.Contains(
            "so does a host that stops because it is not signed in, where that host's row names the signal Jason reads for it; a host whose row names none ends such an attempt as any exit of it ends",
            contract,
            StringComparison.Ordinal);
        Assert.DoesNotContain("or that stops because it is not signed in, ends the attempt", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host matrix has one home, §12, and it is held to the table the CLI and the runtime both read: every host this
    /// build knows has a row that says yes, with the skills root, the work directory's folder and the import file the
    /// table names; and a row for a host this build does not know yet says so, in the words a profile naming it meets.
    /// </summary>
    [Fact]
    public void The_host_matrix_has_a_row_for_every_host_this_build_knows_with_the_tables_folders()
    {
        const string NotYet = "not in this build yet: a profile naming it is refused";
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var at = page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal);
        Assert.True(at > 0, "execution-profiles.md has no section '12. The hosts'.");

        var rows = page[at..]
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1..^1])
            .ToList();
        var words = rows.Select(row => row[1].Trim('`')).ToList();

        Assert.Equal(words.Distinct(StringComparer.Ordinal).Count(), words.Count);
        Assert.All(words, word => Assert.Matches("^[a-z][a-z0-9_]*$", word));
        foreach (var facts in Jason.Contracts.Hosts.AgentHosts.All)
        {
            var word = Jason.Contracts.Hosts.AgentHosts.Word(facts.Kind);
            var row = Assert.Single(rows, row => row[1] == $"`{word}`");
            Assert.Equal($"`~/{facts.HomeSkills}`", row[2]);
            Assert.Equal($"`{facts.WorkSkills}/<role>/`", row[3]);
            Assert.Equal(facts.Instructions is { } file ? $"`{file.FileName}` (`{file.ImportLine}`)" : "—", row[4]);
            Assert.Equal("yes", row[5]);
        }

        Assert.All(
            rows.Where(row => Jason.Contracts.Hosts.AgentHosts.Parse(row[1].Trim('`')) is null),
            row => Assert.Equal(NotYet, row[5]));
    }

    /// <summary>
    /// The matrix carries each host's tool table, so a person can read why a grant or a deny was refused: for every host
    /// this build registers, each tool the host has a rule for is named with that rule, in the host's own word; a row for
    /// a host this build does not know yet has none.
    /// </summary>
    [Fact]
    public void The_host_matrix_names_each_registered_hosts_rule_for_every_tool_it_has()
    {
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var rows = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..]
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1..^1])
            .ToList();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Jason.Runtime.Hosting.Modules.DispatcherModule.AddDispatcherModule(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        foreach (var host in Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<Jason.Runtime.Execution.Hosts.IAgentHost>(provider))
        {
            var row = Assert.Single(rows, row => row[1] == $"`{Jason.Contracts.Hosts.AgentHosts.Word(host.Kind)}`");
            foreach (var tool in Enum.GetValues<Jason.Contracts.Api.AgentTool>())
            {
                if (host.ToolRule(tool) is { } rule)
                {
                    Assert.Contains($"`{Jason.Runtime.Persistence.SnakeCaseEnumConverter<Jason.Contracts.Api.AgentTool>.Format(tool)}` as `{rule}`", row[7], StringComparison.Ordinal);
                }
            }
        }

        Assert.All(
            rows.Where(row => Jason.Contracts.Hosts.AgentHosts.Parse(row[1].Trim('`')) is null),
            row => Assert.Equal("—", row[7]));
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
