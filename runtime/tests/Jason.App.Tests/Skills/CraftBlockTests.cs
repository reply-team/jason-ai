using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// One role's judgement, in the two files that teach that role. The launched role's file is read by a role the
/// runtime starts on a work item, which cannot ask anybody anything; the session file is read by a person's own
/// session, which talks with them. What a planner owns, how it divides the work and what it never decides alone
/// is the same in both seats, so it is written once, between two markers, and held to the same bytes here. Hand
/// copies of a rule are chances for a correction to land in one of them and not the other.
/// </summary>
public partial class CraftBlockTests
{
    /// <summary>
    /// A session file is named for the role it pairs with. One named for a role this pack does not teach shares its
    /// judgement with nobody, and the roles this pack teaches are held to the roster the runtime seeds by
    /// <see cref="LaunchContractTests"/> — so a pairing with a taught role is a pairing with a real one.
    /// </summary>
    [Fact]
    public void Every_session_file_pairs_with_a_role_this_pack_teaches()
    {
        var sessions = CraftBlock.SessionFiles();
        Assert.True(sessions.Count > 0, "The pack has no session file, so every guard in this class holds nothing.");

        var roles = SkillPack.All().Where(skill => skill.IsRole).Select(skill => skill.Name).ToList();
        foreach (var session in sessions)
        {
            Assert.True(
                roles.Contains(session.Role, StringComparer.Ordinal),
                $"'{session.Skill.Name}' is named for the role '{session.Role}', and there is no "
                + $"skills/runtime/roles/{session.Role}/SKILL.md. A session file shares a role's judgement with that "
                + $"role's file; this one shares it with nobody. The roles this pack teaches: {string.Join(", ", roles)}.");
        }
    }

    [Fact]
    public void A_role_and_its_session_file_share_one_craft_block_byte_for_byte()
    {
        foreach (var session in CraftBlock.SessionFiles())
        {
            var role = SkillPack.Find(session.Role);

            var launched = CraftBlock.Extract(role.File);
            Assert.True(
                launched is not null,
                $"'{role.File}' carries no block between {CraftBlock.Begin} and {CraftBlock.End}, and "
                + $"'{session.Skill.Name}' pairs with it. The launched role would be taught a judgement the session "
                + "file does not share.");

            var inSession = CraftBlock.Extract(session.Skill.File);
            Assert.True(
                inSession is not null,
                $"'{session.Skill.File}' carries no block between {CraftBlock.Begin} and {CraftBlock.End}, so the "
                + $"session would sit as the {session.Role} without the {session.Role}'s judgement.");

            Assert.True(
                string.Equals(launched, inSession, StringComparison.Ordinal),
                $"'{session.Skill.Name}' and 'roles/{session.Role}' teach different judgement: "
                + MarkedBlock.FirstDifference(launched, inSession)
                + ". Make the same edit in both files.");
        }
    }

    /// <summary>
    /// One block per file, and only in a file that pairs. A second block in one file is read by no guard here, and
    /// a block in a file with no partner is held equal to nothing — both are the quiet way a correction lands
    /// somewhere and changes nothing.
    /// </summary>
    [Fact]
    public void A_craft_block_is_carried_once_and_only_by_a_role_and_its_session_file()
    {
        var sessions = CraftBlock.SessionFiles();
        var paired = sessions.Select(session => session.Role).ToHashSet(StringComparer.Ordinal);
        var sessionNames = sessions.Select(session => session.Skill.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var skill in SkillPack.All().Concat(SkillPack.Business()))
        {
            var expected = sessionNames.Contains(skill.Name) || (skill.IsRole && paired.Contains(skill.Name)) ? 1 : 0;
            var text = File.ReadAllText(skill.File);
            foreach (var marker in new[] { CraftBlock.Begin, CraftBlock.End })
            {
                var found = MarkedBlock.Occurrences(text, marker);
                Assert.True(
                    found == expected,
                    $"'{skill.File}' carries {marker} {found} times, and it should carry it {expected} times: once in "
                    + "a role that has a session file and once in that session file, and nowhere else.");
            }
        }
    }

    /// <summary>
    /// The block is judgement and prints no command. How the work reaches a role and how its answer leaves differ
    /// between the two seats — a launched role builds its keys from its own work item and raises a decision, a
    /// person's session uses words of its own choosing and asks the person — so a command in the shared block would
    /// teach one seat the other's mechanics.
    /// </summary>
    [Fact]
    public void A_craft_block_prints_no_command()
    {
        foreach (var session in CraftBlock.SessionFiles())
        {
            foreach (var file in new[] { session.Skill.File, SkillPack.Find(session.Role).File })
            {
                var block = CraftBlock.Extract(file) ?? string.Empty;
                foreach (var line in block.Split('\n'))
                {
                    Assert.False(
                        SkillPack.FenceMarker(line) is not null,
                        $"'{file}' opens a fenced block inside its craft block: '{line}'. The shared judgement prints "
                        + "nothing to type; the commands belong to the part of each file that is its own.");

                    var command = Command().Match(line);
                    Assert.False(
                        command.Success,
                        $"'{file}' names the command '{command.Value}' inside its craft block: '{line}'. The shared "
                        + "judgement holds in both seats, and a command teaches one seat the other's mechanics.");
                }
            }
        }
    }

    /// <summary>
    /// And names no verb of the program without its word either. The pack names a verb in prose as `campaign
    /// update-context`, which the guard above does not see because the program's own word is missing; it is still
    /// mechanics, and still one seat's.
    /// </summary>
    [Fact]
    public async Task A_craft_block_names_no_verb_of_the_program()
    {
        var (names, pairs) = await ProgramNamesAsync();

        foreach (var session in CraftBlock.SessionFiles())
        {
            foreach (var file in new[] { session.Skill.File, SkillPack.Find(session.Role).File })
            {
                var named = VerbsNamed(CraftBlock.Extract(file) ?? string.Empty, names, pairs);
                Assert.True(
                    named.Count == 0,
                    $"'{file}' names {string.Join(", ", named.Select(verb => $"'{verb}'"))} inside its craft block. The shared "
                    + "judgement names no verb; each file's own part does.");
            }
        }
    }

    /// <summary>
    /// What the verb guard reads as a verb, and what it leaves alone. Many of the program's pairs are plain English —
    /// "contact list", "campaign update", "runtime status" — so a pair is read only where it cannot be prose: in a code
    /// span, after the program's own word, or with a hyphenated verb nobody writes in a sentence.
    /// </summary>
    [Theory]
    [InlineData("Write it with `campaign update-context --set`.", true)]
    [InlineData("A change is written with campaign update-context and nothing else.", true)]
    [InlineData("Read it back with `campaign get` before you plan.", true)]
    [InlineData("Where no sourcing tool is at hand, the pilot can come from the person's own contact list.", false)]
    [InlineData("A campaign update that nobody asked for is noise, and so is a runtime status read twice.", false)]
    public async Task The_verb_guard_reads_a_verb_only_where_it_cannot_be_prose(string sentence, bool refused)
    {
        var (names, pairs) = await ProgramNamesAsync();
        var named = VerbsNamed(sentence, names, pairs);
        Assert.True(
            named.Count > 0 == refused,
            $"'{sentence}' {(refused ? "names no verb by this reading, and it names one" : $"reads as naming {string.Join(", ", named)}, and it is prose")}.");
    }

    private static async Task<(IReadOnlyList<string> Names, IReadOnlyList<string> Pairs)> ProgramNamesAsync()
    {
        var names = await ProgramVerbs.NamesAsync();
        var pairs = await ProgramVerbs.PairsAsync();
        Assert.True(names.Count > 0 && pairs.Count > 0, $"The help pages gave {names.Count} names and {pairs.Count} pairs, so the verb guard holds nothing.");
        return (names, pairs);
    }

    /// <summary>
    /// The verbs a text names: a code span whose first word is one of the program's top-level names, and a noun and verb
    /// pair after the program's own word or with a hyphenated verb, written out anywhere.
    /// </summary>
    private static IReadOnlyList<string> VerbsNamed(string text, IReadOnlyList<string> names, IReadOnlyList<string> pairs)
    {
        var flat = Whitespace().Replace(text, " ");
        var named = new List<string>();
        foreach (Match span in CodeSpan().Matches(flat))
        {
            var first = span.Groups[1].Value.Trim().Split(' ')[0];
            if (names.Contains(first, StringComparer.Ordinal))
            {
                named.Add(span.Value);
            }
        }

        foreach (var pair in pairs)
        {
            var hyphenated = pair.Split(' ')[^1].Contains('-', StringComparison.Ordinal);
            var pattern = hyphenated ? $@"(?<![\w-]){Regex.Escape(pair)}(?![\w-])" : $@"\bjason {Regex.Escape(pair)}(?![\w-])";
            if (Regex.IsMatch(flat, pattern))
            {
                named.Add(pair);
            }
        }

        return named;
    }

    /// <summary>
    /// The block holds in both seats and records no results. "The client" and "the person in front of you" are true in
    /// one seat only: a launched role has nobody in front of it, and the person who owns a decision is not always the
    /// one who asked. And what worked last time is a record of results — learned practice, which this version keeps
    /// nowhere, least of all in the text every campaign's planner is taught from.
    /// </summary>
    [Theory]
    [InlineData("Start from what worked last time.", true)]
    [InlineData("It belongs to the client.", true)]
    [InlineData("Ask the person in front of you one question.", true)]
    [InlineData("Open the way the last campaign opened, which booked twice the calls.", true)]
    [InlineData("In earlier campaigns a question in the subject line did best.", true)]
    [InlineData("Going on differently means keeping what worked in the pilot and dropping what did not.", false)]
    [InlineData("It belongs to the person who owns it, and you ask one question with the answers named.", false)]
    public void The_seat_and_record_refusal_reads_what_crosses_campaigns_and_not_this_campaigns_pilot(string sentence, bool refused) =>
        Assert.True(
            OneSeatOrRecord().IsMatch(sentence) == refused,
            $"'{sentence}' is {(refused ? "not refused, and it is true in one seat only or carries results across campaigns" : "refused, and it reads this campaign or holds in both seats")}.");

    [Fact]
    public void A_craft_block_holds_in_both_seats_and_records_no_results()
    {
        foreach (var session in CraftBlock.SessionFiles())
        {
            foreach (var file in new[] { session.Skill.File, SkillPack.Find(session.Role).File })
            {
                var word = OneSeatOrRecord().Match(Whitespace().Replace(CraftBlock.Extract(file) ?? string.Empty, " "));
                Assert.False(
                    word.Success,
                    $"'{file}' says '{word.Value}' inside its craft block. The shared judgement says \"the person who owns "
                    + "it\" and holds in both seats, and it records no results.");
            }
        }
    }

    /// <summary>
    /// Each file says its own status, outside the block. A host reading recorded for one file changes that file's
    /// status line; inside the block, verifying one seat would edit the other's text, and the other's reading would
    /// be of a body that is no longer there.
    /// </summary>
    [Fact]
    public void The_status_line_stays_outside_the_craft_block()
    {
        foreach (var session in CraftBlock.SessionFiles())
        {
            foreach (var file in new[] { session.Skill.File, SkillPack.Find(session.Role).File })
            {
                var block = CraftBlock.Extract(file) ?? string.Empty;
                Assert.False(
                    block.Contains("**Status:", StringComparison.Ordinal),
                    $"'{file}' says its status inside its craft block. Each file says its own status outside the "
                    + "block, because the two files are read, and verified, separately.");
            }
        }
    }

    /// <summary>
    /// A session file is read by a person's own session, which was launched by nobody. Told that its brief came on
    /// standard input, that it has no file-writing tool and must type no actor, it would follow rules written for a
    /// role the runtime started — in a conversation with the person those rules were never about.
    /// </summary>
    [Fact]
    public void A_session_file_carries_no_launch_contract()
    {
        foreach (var session in CraftBlock.SessionFiles())
        {
            var text = File.ReadAllText(session.Skill.File);
            foreach (var mark in new[] { LaunchContract.Begin, LaunchContract.End, "How this runtime launched you", "Call home with the plain command" })
            {
                Assert.False(
                    text.Contains(mark, StringComparison.Ordinal),
                    $"'{session.Skill.File}' carries '{mark}', which belongs to the launch contract. A session file is "
                    + "read by a person's own session, and the launch contract is about a role the runtime started.");
            }

            // Nor its vocabulary, read from the contract block of the role this file pairs with rather than typed
            // here: a sentence lifted from the contract into a session file would tell a person's session to answer in
            // a shape nobody asked for, or to send a heartbeat to an attempt that does not exist.
            var flat = Whitespace().Replace(text, " ");
            var terms = ContractVocabulary(SkillPack.Find(session.Role).File);
            Assert.True(terms.Count >= 20, $"The launch contract gave only {terms.Count} terms ({string.Join(", ", terms)}), so this guard holds little.");
            foreach (var term in terms)
            {
                Assert.False(
                    flat.Contains(term, StringComparison.OrdinalIgnoreCase),
                    $"'{session.Skill.File}' says '{term}', which is the launch contract's. A session file is read by a "
                    + "person's own session, and the launch contract is about a role the runtime started.");
            }
        }
    }

    /// <summary>
    /// The identifiers the contract names after its first fenced command are read too. A reader that paired the fence's
    /// backticks with the inline spans' lost them, and a session file could say `note_bytes` unrefused.
    /// </summary>
    [Fact]
    public void The_launch_contracts_vocabulary_is_read_past_its_fenced_commands()
    {
        var terms = ContractVocabulary(SkillPack.Find("planner").File);
        foreach (var expected in new[] { "note_bytes", "METHOD.md", "role_memory", "runtime.cli_command", "workitem complete", "Answer in the shape you were asked for" })
        {
            Assert.True(terms.Contains(expected, StringComparer.Ordinal), $"The launch contract's vocabulary is read as [{string.Join(", ", terms)}], without '{expected}'.");
        }

        Assert.True(terms.All(term => term.Length <= 60), $"The launch contract's vocabulary holds a chunk of prose rather than a term: [{string.Join(", ", terms.Where(term => term.Length > 60))}].");
    }

    /// <summary>
    /// The launch contract's own words: its headings, the identifiers it names (a code span holding an underscore or a
    /// dot), the phrases it sets in bold that are more than one word, and the noun and verb pairs of
    /// the commands it prints. Not its ordinary prose: the contract also says "brief", "note" and "lead", which a session
    /// file needs.
    /// </summary>
    private static IReadOnlyList<string> ContractVocabulary(string roleFile)
    {
        var block = LaunchContract.Extract(roleFile) ?? string.Empty;
        var terms = new List<string>();

        foreach (var line in block.Split('\n'))
        {
            if (line.StartsWith('#'))
            {
                terms.Add(line.TrimStart('#').Trim());
            }
        }

        // The prose without its fenced commands: their backticks would otherwise pair with the inline spans' and swallow
        // the identifiers that come after the first fence.
        var flat = Whitespace().Replace(
            string.Join('\n', SkillPack.Fenced(block.Split('\n')).Where(line => !line.Fenced).Select(line => line.Text)),
            " ");
        foreach (Match span in CodeSpan().Matches(flat))
        {
            var term = span.Groups[1].Value.Trim();
            if (term.Contains('_', StringComparison.Ordinal) || term.Contains('.', StringComparison.Ordinal))
            {
                terms.Add(term);
            }
        }

        foreach (Match bold in Bold().Matches(flat))
        {
            var term = bold.Groups[1].Value.Trim().TrimEnd('.');
            if (term.Contains(' ', StringComparison.Ordinal))
            {
                terms.Add(term);
            }
        }

        foreach (var command in SkillPack.PrintedCommands(block.Split('\n')))
        {
            var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 2)
            {
                terms.Add($"{words[1]} {words[2]}");
            }
        }

        return [.. terms.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// A launched role is handed its own work item's context and nothing of the campaign's: it reads the campaign only
    /// where its skill has it do so, and some roles never do. A file of a pair that said every role reads what is left in
    /// campaign context would teach the planner to leave a direction there and send a role that never reads it off
    /// without what it needs — in both seats at once, when the sentence is in the shared block.
    /// </summary>
    [Fact]
    public void No_file_of_a_pair_says_every_role_reads_the_campaign()
    {
        foreach (var session in CraftBlock.SessionFiles())
        {
            var role = SkillPack.Find(session.Role).File;
            foreach (var (where, text) in new[]
            {
                (session.Skill.File, File.ReadAllText(session.Skill.File)),
                (role + " (its craft block)", CraftBlock.Extract(role) ?? string.Empty),
            })
            {
                var claim = EveryRoleReads().Match(Whitespace().Replace(text, " "));
                Assert.False(
                    claim.Success,
                    $"'{where}' says '{claim.Value}'. A role reads the campaign only where its skill has it do so; one that "
                    + "does not works from its own work item's context alone.");
            }
        }
    }

    /// <summary>
    /// The two files are "the launched role's file" and "the session file" wherever a reader meets them. The words
    /// the design was talked through in — a chair, a projection — mean nothing to somebody reading the catalog, and
    /// "projection" already means something else in this project's own documents. Every published page this pair
    /// touches is read: the catalog, the pack table and the walkthrough, beside the files themselves.
    /// </summary>
    [Fact]
    public void The_session_files_and_the_craft_blocks_never_say_chair_or_projection()
    {
        var texts = new List<(string Where, string Text)>
        {
            (SkillPack.Catalog(), File.ReadAllText(SkillPack.Catalog())),
        };

        foreach (var page in new[] { Path.Combine("skills", "README.md"), Path.Combine("docs", "golden-path.md") })
        {
            var path = Path.Combine(SkillPack.RepositoryRoot(), page);
            texts.Add((path, File.ReadAllText(path)));
        }

        foreach (var session in CraftBlock.SessionFiles())
        {
            texts.Add((session.Skill.File, File.ReadAllText(session.Skill.File)));
            var role = SkillPack.Find(session.Role).File;
            texts.Add((role + " (its craft block)", CraftBlock.Extract(role) ?? string.Empty));
        }

        foreach (var (where, text) in texts)
        {
            var word = DesignWord().Match(text);
            Assert.False(
                word.Success,
                $"'{where}' says '{word.Value}'. A reader meets \"the launched role's file\" and \"the session file\"; "
                + "the words the design was talked through in mean nothing to them.");
        }
    }

    /// <summary>
    /// The catalog's section on a role that sits with a person says, in its own paragraph, that the shared block is the
    /// role's judgement and never a record of results. It is the one place a reader — and anybody editing the block —
    /// is told that "what worked last time" is learned practice and belongs in no skill, so the paragraph is held whole,
    /// in that section.
    /// </summary>
    [Fact]
    public void The_catalog_says_the_block_is_judgement_never_a_record_of_results()
    {
        var text = File.ReadAllText(SkillPack.Catalog()).Replace("\r\n", "\n", StringComparison.Ordinal);
        var from = text.IndexOf("\n## A role that sits with a person\n", StringComparison.Ordinal);
        var to = from < 0 ? -1 : text.IndexOf("\n## ", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"The catalog has no section 'A role that sits with a person' (found at {from}, next heading at {to}).");

        var section = text[from..to];
        var start = section.IndexOf("**The block is judgement, never a record of results.**", StringComparison.Ordinal);
        Assert.True(start >= 0, "The catalog's section on a role that sits with a person no longer says that the block is judgement, never a record of results.");
        var end = section.IndexOf("\n\n", start, StringComparison.Ordinal);

        Assert.Equal(
            "**The block is judgement, never a record of results.** What happened on a campaign is in Jason, where every role "
            + "can read it. What worked on one campaign, carried to the next, would be learned practice, which this version does "
            + "not keep, in a skill or anywhere else: a block that began to collect it would teach every campaign what one "
            + "campaign happened to find.",
            Whitespace().Replace(end < 0 ? section[start..] : section[start..end], " ").Trim());
    }

    /// <summary>
    /// And the packs table says what the runtime pack carries, the session file among it. A reader choosing a pack reads
    /// that row and nothing further, so a row that dropped the session file would hide the planner's seat in a person's
    /// own conversation from the one person deciding whether to install it.
    /// </summary>
    [Fact]
    public void The_packs_table_says_the_runtime_pack_carries_a_session_file()
    {
        var path = Path.Combine(SkillPack.RepositoryRoot(), "skills", "README.md");
        var row = File.ReadAllLines(path).SingleOrDefault(line => line.StartsWith("| [`skills/runtime`](runtime/README.md) |", StringComparison.Ordinal));
        Assert.Equal(
            "| [`skills/runtime`](runtime/README.md) | How to operate **this runtime**: durable campaign work, what waits on a "
            + "person, running the installation, why work stopped, reporting outside effects — one skill per launched role, "
            + "and a session file for a role that sits with a person | the Runtime API does |",
            row);
    }

    /// <summary>The program's own word followed by a verb group, as somebody would type it.</summary>
    [GeneratedRegex(@"(?<![A-Za-z])jason [a-z]+")]
    private static partial Regex Command();

    [GeneratedRegex(@"\b(chairs?|projections?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DesignWord();

    /// <summary>"every role reads it", "every role Jason launches … reads", and the like.</summary>
    [GeneratedRegex(@"\bevery role (reads|[^.;:]{0,60}?\breads)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EveryRoleReads();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex Bold();

    /// <summary>
    /// Words true in one seat only, and results carried across campaigns or time. Not "what worked" on its own: reading
    /// what worked in this campaign's pilot is the planner's judgement, not learned practice.
    /// </summary>
    [GeneratedRegex(@"\b(clients?|in front of you|last time|worked before|(the|a|our|your) (last|previous|earlier) campaign|(earlier|previous|past|other) campaigns)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OneSeatOrRecord();
}
