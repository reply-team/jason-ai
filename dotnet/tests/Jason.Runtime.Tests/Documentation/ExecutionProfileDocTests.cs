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
    /// Every refusal of AI work is decided before a child exists but one: a host that ran and answered that it is not
    /// signed in ends its attempt as <c>host_not_available</c> after the child ran. Both pages that list the codes say so,
    /// and that such an end carries the fixed sentence and no byte of what the host wrote; neither says every code is
    /// decided before a child exists.
    /// </summary>
    [Fact]
    public void Both_pages_say_host_not_available_also_ends_an_attempt_after_the_child_ran()
    {
        foreach (var page in new[] { "execution-profiles.md", "work-execution.md" })
        {
            var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), page)));

            Assert.Contains(
                "except `host_not_available` where a host that ran answered that it is not signed in: that ends the attempt after the child ran, with the host's fixed sentence and no byte of what the host wrote",
                contract,
                StringComparison.Ordinal);
            Assert.DoesNotMatch(
                "Every one of these is decided before a child process exists(?!, except `host_not_available`)",
                contract.Replace("**", "", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The host matrix has one home, §12, and it is held to the table the CLI and the runtime both read: every host this
    /// build knows has a row that says yes, with the skills root, the work directory's folder and the import file the
    /// table names.
    /// </summary>
    [Fact]
    public void The_host_matrix_has_a_row_for_every_host_this_build_knows_with_the_tables_folders()
    {
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
    }

    /// <summary>
    /// The matrix carries each host's tool table, so a person can read why a grant or a deny was refused: for every host
    /// this build registers, each tool the host has a rule for is named with that rule, in the host's own word.
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
    }

    /// <summary>
    /// What holds for every host is said once, in the work-execution contract, and each host's own form of it in its
    /// row of the matrix: how a launched role is let read a memory file, and how the brief on standard input reaches the
    /// host. A row of a host this build registers names both; and
    /// the contract states each rule for every host, with Claude Code's form as its example.
    /// </summary>
    [Fact]
    public void Each_host_s_row_says_how_it_reads_a_memory_file_and_takes_the_brief_and_the_contract_states_the_rule_for_every_host()
    {
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var header = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..]
            .Split('\n')
            .First(line => line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Split('|', StringSplitOptions.TrimEntries)[1..^1];
        Assert.Equal(["Reads a memory file by", "Takes the brief"], header[8..]);

        var rows = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..]
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1..^1])
            .ToList();
        Assert.All(rows, row => Assert.All(row[8..], cell => Assert.NotEqual("—", cell)));

        // A host that frames the envelope says so in its own row, where it takes the brief.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Jason.Runtime.Hosting.Modules.DispatcherModule.AddDispatcherModule(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        foreach (var host in Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<Jason.Runtime.Execution.Hosts.IAgentHost>(provider))
        {
            var row = Assert.Single(rows, row => row[1] == $"`{Jason.Contracts.Hosts.AgentHosts.Word(host.Kind)}`");
            Assert.Equal(host.FrameStdin("{}") != "{}", row[9].Contains("stream-json", StringComparison.Ordinal));
        }

        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "work-execution.md")));
        Assert.Contains("each host holds this rule in its own form", contract, StringComparison.Ordinal);
        Assert.Contains("composes no prompt from its parts, on every host. On a host that reads its prompt from standard input", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// The launch envelope's page states what holds of every host where it says how a role reads the memory files and how
    /// it is given the brief, and keeps one host's form for that host's section: the rule is a read for each path that is
    /// not null, a rule for the file on every host of the matrix but Gemini CLI, which is given the folder of each level;
    /// and the brief is on standard input on every host. Both halves are held to the matrix's own cells, so a host added
    /// that takes either differently is red until the page says so.
    /// </summary>
    [Fact]
    public void The_launch_envelope_states_the_memory_read_and_the_stdin_brief_for_every_host()
    {
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var rows = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..]
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1..^1])
            .Where(row => Jason.Contracts.Hosts.AgentHosts.Parse(row[1].Trim('`')) is not null)
            .ToList();
        Assert.All(rows, row => Assert.Contains("standard input", row[9], StringComparison.Ordinal));
        Assert.Equal(["gemini_cli"], rows.Where(row => row[8].Contains("--include-directories", StringComparison.Ordinal)).Select(row => row[1].Trim('`')));

        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "work-execution.md")));
        Assert.Contains("A profile is composed with a read for each path that is not null: a rule for the file, never its folder, on every host but Gemini CLI, which is given the folder of each level and reads it whole", contract, StringComparison.Ordinal);
        Assert.Contains("On a host that reads its prompt from standard input — every host of that matrix does, and its `Takes the brief` column says how — that standard input is the prompt", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("On the `claude_code` host a profile is composed with one read rule per path", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("`claude_code`, which runs in print mode, and each host", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host's install is told one way in the README and the install page: a link to the host's own installation page,
    /// where its commands are kept current, and never a command copied here. The only line either page pipes to a shell is
    /// Jason's own one-liner. Cursor CLI, the host whose commands were once copied, is held to the link.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/INSTALL.md")]
    public void A_host_s_install_is_a_link_to_its_own_page_and_no_command_copied_from_it(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryTree.Root(), file.Replace('/', Path.DirectorySeparatorChar))).ReplaceLineEndings("\n");

        var piped = text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"\|\s*(?:sh|bash|iex)\b"));
        Assert.All(piped, line => Assert.Contains("get.jasonharness.ai", line, StringComparison.Ordinal));
        Assert.Contains("](https://cursor.com/docs/cli/installation.md)", text, StringComparison.Ordinal);
        Assert.Contains("](https://github.com/google-gemini/gemini-cli)", text, StringComparison.Ordinal);
        Assert.Contains("](https://github.com/openai/codex)", text, StringComparison.Ordinal);
        Assert.Contains("](https://docs.github.com/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli)", text, StringComparison.Ordinal);
        Assert.Contains("](https://antigravity.google/docs/getting-started?tab=cli)", text, StringComparison.Ordinal);
        Assert.Contains("codex login", text, StringComparison.Ordinal);
        Assert.Contains("copilot login", text, StringComparison.Ordinal);
        Assert.Contains("--host codex --program codex", text, StringComparison.Ordinal);
        Assert.Contains("--host copilot_cli --program copilot", text, StringComparison.Ordinal);
        Assert.Contains("--host antigravity_cli --program agy", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The matrix of §12 is the vocabulary of the `host` field as the build knows it: one row for each member of
    /// <see cref="Jason.Contracts.Api.AgentHostKind"/>, in the word the API writes it, and no row for a word the build does
    /// not know. A host added to the enum turns this red until the page has its row, and a row left for a host that was
    /// removed, or one written ahead of its code, does too.
    /// </summary>
    [Fact]
    public void The_matrix_has_one_row_for_each_host_the_build_knows_and_none_besides()
    {
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var words = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..]
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[2].Trim('`'))
            .ToList();

        var known = Enum.GetValues<Jason.Contracts.Api.AgentHostKind>().Select(Jason.Contracts.Hosts.AgentHosts.Word).ToList();

        Assert.Equal(known.Order(StringComparer.Ordinal), words.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Gemini CLI's section says where the host is installed from, as a link to the host's own page and no command copied
    /// from it, so that the README, the install page and the release's notes have a source to follow.
    /// </summary>
    [Fact]
    public void The_gemini_section_links_the_host_s_own_installation_page()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));
        var section = contract[contract.IndexOf("### Gemini CLI", StringComparison.Ordinal)..];

        Assert.Contains("](https://github.com/google-gemini/gemini-cli)", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The GitHub Copilot CLI page holds what the host's host-row and section promise, held by words so that a rewrite
    /// that drops one is red: the matrix row's standing sentences (the version it was read at, the parser not read, the
    /// cost of that), the flags a launch composes to close what the person's settings would leave open, each thing the
    /// person's own setup can still do, what the skill tool reaches, what is and is not denied, what a call no rule
    /// allows becomes, and what has not been measured.
    /// </summary>
    [Fact]
    public void The_copilot_page_says_what_a_launch_closes_what_it_cannot_and_what_is_not_measured()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));
        var start = contract.IndexOf("| GitHub Copilot CLI | `copilot_cli` |", StringComparison.Ordinal);
        Assert.True(start >= 0, "the matrix has no row for the host");
        var row = contract[start..contract.IndexOf(" |", contract.IndexOf("built from the host's documentation", start, StringComparison.Ordinal), StringComparison.Ordinal)];
        var section = contract[contract.IndexOf("### GitHub Copilot CLI", StringComparison.Ordinal)..];

        // (a) the row's standing sentences.
        foreach (var sentence in new[]
        {
            "built from the host's documentation for version 1.0.105, the version the allowlist was read at",
            "parser not read, so a profile's arguments are only the documented options the person owns",
            "No undocumented flag, however harmless, is taken this release.",
            "Not signed in is a non-zero exit whose stderr names `copilot login`.",
            "Not yet run on an account",
        })
        {
            Assert.Contains(sentence, row, StringComparison.Ordinal);
        }

        // (b) the flags a launch composes, each named.
        foreach (var flag in new[] { "`--disallow-temp-dir`", "`--sync-session=off`", "`--mode=interactive`", "`--no-bash-env`" })
        {
            Assert.Contains(flag, section, StringComparison.Ordinal);
        }

        // (c) what the person's own setup can still do, one entry each.
        foreach (var entry in new[]
        {
            "user-level hooks",
            "MCP servers of their own",
            "`COPILOT_ASSISTED_APPROVAL=true`",
            "`defaultPermissionMode` set to `\"allow-all\"`",
            "`skillDirectories`",
            "`COPILOT_SKILLS_DIRS`",
            "keyed by the git root of the folder the run is in",
            "`COPILOT_ALLOW_ALL` is equal to `--allow-all`",
            "only the exact value `true` also trusts the folder",
        })
        {
            Assert.Contains(entry, section, StringComparison.Ordinal);
        }

        // (d) what the skill tool reaches, and the person's remedies.
        Assert.Contains("every skill the host discovers, the person's and plugins' included, not the role's own alone", section, StringComparison.Ordinal);
        Assert.Contains("`ignoredSkillsLocations`", section, StringComparison.Ordinal);
        Assert.Contains("`disabledSkills`", section, StringComparison.Ordinal);

        // (e) what is denied, said as it is.
        Assert.Contains("The file-writing tools are denied and not offered; the shell is held to the command word by the allow rule.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("any writing is denied", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("own environment trusts the folder", section, StringComparison.Ordinal);

        // (f) what has not been measured, as the list stands.
        var measured = section[section.IndexOf("**Not measured.**", StringComparison.Ordinal)..];
        foreach (var item in new[]
        {
            "whether a rule naming one file lets the role read it outside the work directory",
            "whether a run in a folder the host does not trust stops to ask",
            "whether the work directory's skills load there",
            "whether shell redirection by the callback can write a file the deny list names",
            "whether `taskbarPresence`",
            "whether version 1.0.105 accepts every flag above",
        })
        {
            Assert.Contains(item, measured, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("refused, or waits", measured, StringComparison.Ordinal);

        // (g) what a call no rule allows becomes.
        Assert.Contains(
            "a call no rule allows is denied, and the reason goes back to the role; the reference says so, no run has shown it",
            section,
            StringComparison.Ordinal);
    }

    /// <summary>The profile's fields say what an argument may not be, for the host that ships as for the others: a short flag stands alone.</summary>
    [Fact]
    public void The_args_field_says_a_short_flag_stands_alone_and_a_lone_or_double_dash_is_refused()
    {
        var page = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));
        var row = page[page.IndexOf("| `args` |", StringComparison.Ordinal)..];
        row = row[..row.IndexOf(" | `deny` |", StringComparison.Ordinal)];

        Assert.Contains("a short flag stands alone", row, StringComparison.Ordinal);
        Assert.Contains("a short flag with more letters or a value after it, a lone dash and a double dash are refused", row, StringComparison.Ordinal);

        // Every host this build registers refuses them, so the row names no host as the ones that do.
        Assert.Contains("naming the host, on every host", row, StringComparison.Ordinal);
        Assert.DoesNotContain("under Claude Code and under Cursor CLI", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a person relying on the Cursor CLI host is told, as the page's notes say it: that the person's own
    /// configuration can allow more than Jason does and Jason cannot narrow it, that the union of the layers holds for
    /// every kind of rule and the person's approval mode is theirs, which shell command Jason allows, what the settings
    /// file carries and nothing else, how the Windows launcher chooses a version and what a person's own run does to a
    /// staged update, and that all three spellings of the sign-in word are read. It names no count of hosts, which is a
    /// sentence that goes stale when the matrix grows.
    /// </summary>
    [Fact]
    public void The_Cursor_CLI_notes_say_what_the_persons_own_configuration_can_do_and_count_no_hosts()
    {
        var page = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")));
        var start = page.IndexOf("### Cursor CLI", StringComparison.Ordinal);
        Assert.True(start > 0, "execution-profiles.md has no 'Cursor CLI' section.");
        var section = page[start..];
        var end = section.IndexOf(" ## ", StringComparison.Ordinal);
        section = end > 0 ? section[..end] : section;

        Assert.DoesNotMatch(@"\b(?:two|three|four|five|six|seven|eight|nine|ten) hosts\b", section);
        Assert.Contains("the widest of the hosts of the matrix", section, StringComparison.Ordinal);
        Assert.Contains("this is the widest read form of the matrix", section, StringComparison.Ordinal);
        Assert.Contains("the union holds for every kind of allow rule, not for reads only", section, StringComparison.Ordinal);
        Assert.Contains("`approvalMode`", section, StringComparison.Ordinal);
        Assert.Contains("Run Everything", section, StringComparison.Ordinal);
        Assert.Contains("the one shell command Jason allows", section, StringComparison.Ordinal);
        Assert.Contains("the person's own user-level configuration may allow more, and Jason cannot narrow it, so `Write(**)` does not stop a shell command that writes", section, StringComparison.Ordinal);
        Assert.Contains("permissions, the file's version key and the editor mode off, nothing else", section, StringComparison.Ordinal);
        Assert.Contains("a flat installation root is read first", section, StringComparison.Ordinal);
        Assert.Contains("within one day Jason breaks a tie by name", section, StringComparison.Ordinal);
        Assert.Contains("the person's own run of the launcher promotes a staged update", section, StringComparison.Ordinal);
        Assert.Contains("`agent login`, `cursor agent login` or `agent-local login`", section, StringComparison.Ordinal);
        Assert.Contains("a short flag with more letters or a value after it", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// Codex's row carries, one sentence each, what a person must know before naming it, and its section holds the
    /// detail: commands are not confined by name, the loopback-only network rests on an experimental feature, Windows may
    /// let a role read more than the list, the person's global <c>AGENTS.md</c> and skills reach every role, and calling
    /// home is not yet checked on any system. The section gives a refused fetch grant its own reason, names macOS beside
    /// Linux and Windows, and says what a folder holding a pattern character costs.
    /// </summary>
    [Fact]
    public void Codex_s_row_carries_one_sentence_for_each_thing_its_section_holds()
    {
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var hosts = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..];
        var row = hosts
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Host | `host` |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1..^1])
            .Single(cells => cells[1] == "`codex`");
        Assert.StartsWith("On Windows, first:", row[6], StringComparison.Ordinal);
        foreach (var sentence in new[]
                 {
                     "Commands are not confined by name: any program a role can read runs, inside the sandbox.",
                     "Holding the network to loopback rests on Codex's network proxy, an experimental feature in 0.166.0.",
                     "On Windows, the elevated sandbox may let a role read more than the list.",
                     "The person's global `AGENTS.md` and their skills reach every Codex role.",
                     "Whether `jason` reaches the runtime from inside the sandbox is not yet checked on Linux, macOS or Windows.",
                 })
        {
            Assert.Contains(sentence, row[6], StringComparison.Ordinal);
        }

        var start = hosts.IndexOf("\n### Codex CLI\n", StringComparison.Ordinal);
        Assert.True(start > 0, "execution-profiles.md has no section 'Codex CLI'.");
        var end = hosts.IndexOf("\n## ", start, StringComparison.Ordinal) is var next and > 0 ? next : hosts.Length;
        var section = Flattened(hosts[start..end]);
        foreach (var fragment in new[]
                 {
                     "Codex offers every role the skills in `~/.agents/skills`, in `$CODEX_HOME/skills` and in its own system cache",
                     "A grant of `web_fetch` is refused because Codex has no tool that fetches a page",
                     "On macOS, whether `jason` reaches the runtime from inside the sandbox is not yet checked.",
                     "the claim refuses the work with `host_not_available`, naming the path",
                 })
        {
            Assert.Contains(fragment, section, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("for the same reason", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the launcher writes into a work directory is said once for every host, by the host's row of section 12, and
    /// Claude Code's files are its example rather than the rule.
    /// </summary>
    [Fact]
    public void The_work_directory_holds_the_files_the_host_s_row_names()
    {
        var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), "work-execution.md")));

        Assert.Contains("the files the host's row of section 12 names", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("The launcher puts two things in that directory", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// A launched child's environment is the runtime's four and the constants a host's row declares, and the two pages that
    /// state it say so in the same words; every constant a registered host declares is named in that host's row, as
    /// <c>NAME=value</c>, so a person can read what the host's child is given beyond the four.
    /// </summary>
    [Fact]
    public void The_environment_is_the_runtimes_four_and_the_constants_a_hosts_row_declares_and_each_constant_is_in_its_row()
    {
        const string Sentence = "the runtime's four and the constants a host's row of";
        foreach (var page in new[] { "execution-profiles.md", "work-execution.md" })
        {
            var contract = Flattened(File.ReadAllText(Path.Combine(DocumentsDirectory(), page)));
            Assert.Contains(Sentence, contract, StringComparison.Ordinal);
            Assert.Contains("never a secret and never a value taken from a person", contract, StringComparison.Ordinal);
            Assert.DoesNotContain("four values and not one more", contract, StringComparison.Ordinal);

            // A count of the hosts that declare one goes stale with the next host; the page points at each host's row instead.
            Assert.DoesNotContain("Today one host declares", contract, StringComparison.Ordinal);
        }

        var text = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Jason.Runtime.Hosting.Modules.DispatcherModule.AddDispatcherModule(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var notes = text[text.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..];
        foreach (var host in Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<Jason.Runtime.Execution.Hosts.IAgentHost>(provider))
        {
            foreach (var (name, value) in host.Environment)
            {
                Assert.Contains($"`{name}={value}`", notes, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// What the Gemini CLI notes say a person cannot see in the host's row: which names a profile's arguments may not use,
    /// what the exit of a failed authentication also means, that the client level is the harness root and so every
    /// project's and campaign's memory file under it is readable, what the person's own setup adds to every role, and what
    /// detection does not read.
    /// </summary>
    [Fact]
    public void The_gemini_cli_notes_state_the_facts_a_person_cannot_see_in_the_row()
    {
        var text = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var notes = Flattened(text[text.IndexOf("\n### Gemini CLI\n", StringComparison.Ordinal)..]);

        string[] fragments =
        [
            "a long flag's name is letters and digits with single dashes between them",
            "`GEMINI_CLI_IDE_WORKSPACE_PATH=`",
            "A value may hold a space and no control character",
            "declares no telemetry flag",
            "`GEMINI_TELEMETRY_ENABLED=false`",
            "A role's run sends no telemetry whatever the person's own setting says",
            "runs the host themselves; making it a profile's choice is for a later release",
            "also an enforced sign-in type that does not match",
            "\"could not authenticate\"",
            "the client level is the harness root, so a role reads every project's and campaign's memory file under it",
            "the person's own skills in `~/.gemini/skills` and `~/.agents/skills`",
            "`GEMINI_CLI_HOME`",
            "turns a bare `@AGENTS.md` into a pointer to the file without inlining it",
            "a short option's letter written with two dashes, `--y` being `-y`",
            "holds any entry whose name ends in `.toml`",
            "the attempt is refused before a child exists, as `host_not_available`, with a sentence that names the folder",
            "`/Library/Application Support/GeminiCli/policies`",
        ];
        foreach (var fragment in fragments)
        {
            Assert.Contains(fragment, notes, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("which this runtime does not hold back", notes, StringComparison.Ordinal);
    }

    /// <summary>The row of the host table names the sign-in signal in one clause, so a person reading only the table knows what a signed-out run looks like.</summary>
    [Fact]
    public void The_gemini_cli_row_names_the_exit_of_a_failed_sign_in()
    {
        var text = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var row = text.Split('\n').Single(line => line.Contains("| `gemini_cli` |", StringComparison.Ordinal));

        Assert.Contains("a signed-out run ends with exit 41, which the notes read as \"could not authenticate\"", Flattened(row), StringComparison.Ordinal);
    }

    /// <summary>
    /// What moves a host's own traffic is the person's environment's, for every host, and the general part of section 12
    /// says it once, ahead of every host's section, so no host's section has to say it again or is read as the only one.
    /// </summary>
    [Fact]
    public void The_general_part_of_the_hosts_section_says_the_flags_that_move_a_hosts_traffic_are_the_persons()
    {
        var page = File.ReadAllText(Path.Combine(DocumentsDirectory(), "execution-profiles.md")).ReplaceLineEndings("\n");
        var hosts = page[page.IndexOf("\n## 12. The hosts\n", StringComparison.Ordinal)..];
        var general = Flattened(hosts[..hosts.IndexOf("\n### ", StringComparison.Ordinal)]);

        Assert.Contains(
            "Flags and variables that move a host's own traffic, such as Gemini CLI's `--proxy` or a proxy variable, are the person's environment's to set: Jason sets none of them and reserves none of them",
            general,
            StringComparison.Ordinal);
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
