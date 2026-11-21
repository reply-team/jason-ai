using System.CommandLine;
using System.Text.RegularExpressions;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// What the chat of Jason Desktop is taught about the acts it runs on the person's word, by its instructions
/// (<c>desktop/chat-instructions.md</c>) and by every skill it may load (each <c>SKILL.md</c> under <c>skills/</c>). Outside
/// Plan only the chat runs those acts when the person tells it to, so no sentence says an act is the person's alone, sends
/// them to a button of the window or to its Settings for one, or says the chat cannot run one or refuses it, but a sentence
/// about Plan only; and no sentence says that a command the CLI has does not exist, or names a noun's whole vocabulary and
/// leaves one of its verbs out. A chat taught otherwise answers a person who asks it to archive their campaigns that only
/// they can, sends them to each agent's Settings, or tells them that a command they could have run does not exist.
/// </summary>
public partial class ChatWordsTests
{
    /// <summary>
    /// What no sentence may say, each with the words that let it stand: an act said to be the person's alone, which nothing
    /// lets stand (the mode's own name before "you", as in "In Plan only you run none of them", is not that); a button of the
    /// window for an act the chat runs, a page of the window to archive, the chat said to be unable to run an act or to
    /// refuse it, each standing only in a sentence about Plan only; and Settings named for the name, the tone or the send
    /// modes, standing where the same sentence says the person may tell the chat instead, or speaks of Plan only.
    /// </summary>
    private static readonly (string Says, Regex Sentence, Regex? StandsWith)[] Acts =
    [
        ("says an act is the person's alone", OnlyYours(), null),
        ("sends the person to a button of the window for an act the chat runs on their word", PressFor(), PlanOnly()),
        ("sends the person to the window to archive a campaign", ArchiveThere(), PlanOnly()),
        ("says the chat cannot run an act", ChatCannot(), PlanOnly()),
        ("says the chat refuses an act", ChatRefuses(), PlanOnly()),
        ("sends the person to Settings for what they may tell the chat", SettingsFor(), PlanOnlyOrTellTheChat()),
    ];

    [Fact]
    public void No_page_the_chat_reads_says_an_act_is_the_persons_alone_or_sends_them_to_the_window_for_one()
    {
        var pages = Pages(chatLoads: true);
        Assert.True(pages.Count > 15, $"Only {pages.Count} pages were read, so this guard read less than the chat does.");

        var found = new List<string>();
        foreach (var page in pages)
        {
            foreach (var sentence in Sentences(page))
            {
                foreach (var (says, pattern, standsWith) in Acts)
                {
                    if (pattern.IsMatch(sentence) && standsWith?.IsMatch(sentence) != true)
                    {
                        found.Add($"{Shown(page)} {says}: \"{sentence}\"");
                    }
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "Outside Plan only the chat runs these acts on the person's word, and these sentences say otherwise:\n" + string.Join('\n', found));
    }

    [Fact]
    public void No_page_the_chat_reads_says_a_command_the_cli_has_does_not_exist()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var commands = Commands(root, []).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("plugin grant", commands);

        var found = new List<string>();
        foreach (var page in Pages())
        {
            foreach (var sentence in Sentences(page))
            {
                foreach (Match claim in NoSuchCommand().Matches(sentence))
                {
                    var path = claim.Groups["path"].Value;
                    if (commands.Contains(path))
                    {
                        found.Add($"{Shown(page)} says there is no `jason {path}`, which the CLI has: \"{sentence}\"");
                    }
                }

                foreach (Match claim in DoNotExist().Matches(sentence))
                {
                    foreach (Match named in NamedCommand().Matches(claim.Groups["named"].Value))
                    {
                        var path = named.Groups["path"].Value;
                        if (commands.Contains(path))
                        {
                            found.Add($"{Shown(page)} says `{path}` does not exist, and the CLI has it: \"{sentence}\"");
                        }
                    }
                }

                foreach (Match claim in WholeVocabulary().Matches(sentence))
                {
                    var noun = claim.Groups["noun"].Value;
                    var group = root.Subcommands.FirstOrDefault(command => command.Name == noun);
                    if (group is null)
                    {
                        found.Add($"{Shown(page)} names the whole {noun} vocabulary, and the CLI has no {noun}: \"{sentence}\"");
                        continue;
                    }

                    var left = group.Subcommands
                        .Where(verb => !verb.Hidden)
                        .Select(verb => verb.Name)
                        .Where(verb => !sentence.Contains($"`{verb}`", StringComparison.Ordinal)
                            && !sentence.Contains($"jason {noun} {verb}", StringComparison.Ordinal))
                        .ToList();
                    if (left.Count > 0)
                    {
                        found.Add($"{Shown(page)} names the whole {noun} vocabulary and leaves out {string.Join(", ", left)}: \"{sentence}\"");
                    }
                }
            }
        }

        Assert.True(found.Count == 0, "These sentences say a command the CLI has does not exist:\n" + string.Join('\n', found));
    }

    /// <summary>
    /// The person's word has one form in the chat's instructions, and the CLI takes it: rules 11, 12, 13 and 19 name the
    /// person as Jason's first paragraph names them, <c>--actor human:&lt;account&gt;</c>, with a reason in the chat's own
    /// words in single quotes, at the end of the command, where a host's rule for the verb (<c>jason preference set
    /// sender.identity:*</c>) still matches; every <c>jason</c> command a sentence giving that form names is a verb that takes
    /// <c>--reason</c>, the start, the archive and the opt-out verdict's among them; and the reason the form shows holds no
    /// character but a letter, a digit, a space and <c>. , : -</c>, since text from outside never goes into a command. A form a
    /// verb did not take would fail the act the person asked for, and one taught as "<c>--actor</c> naming them" left the
    /// chat to guess the name.
    /// </summary>
    [Fact]
    public void The_instructions_give_the_persons_word_one_form_the_cli_takes()
    {
        const string form = "with `--actor human:<account>` as Jason's first paragraph names them and `--reason '...'` in your own words";
        var file = Path.Combine(RepositoryRoot(), "desktop", "chat-instructions.md");
        var text = File.ReadAllText(file);
        foreach (var number in new[] { 11, 12, 13, 19 })
        {
            var rule = Assert.Single(text.ReplaceLineEndings("\n").Split('\n'), line => line.StartsWith($"{number}. ", StringComparison.Ordinal));
            var whole = form + (number == 13 ? "" : " (the reason of rule 13)") + ", at the end of the command";
            Assert.True(rule.Contains(whole, StringComparison.Ordinal), $"Rule {number} does not give the person's word its form: {rule}");
        }

        Assert.DoesNotContain("`--actor` naming them", text, StringComparison.Ordinal);

        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var checkedVerbs = new List<string>();
        var found = new List<string>();
        foreach (var sentence in Sentences(file).Where(sentence => sentence.Contains(form, StringComparison.Ordinal)))
        {
            foreach (Match named in JasonCommand().Matches(sentence))
            {
                Command verb = root;
                var path = new List<string>();
                foreach (var word in named.Groups["words"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (verb.Subcommands.FirstOrDefault(sub => sub.Name == word) is not { } below)
                    {
                        break;
                    }

                    verb = below;
                    path.Add(word);
                }

                checkedVerbs.Add(string.Join(' ', path));
                if (path.Count < 2 || verb.Options.All(option => option.Name != "--reason"))
                {
                    found.Add($"`{named.Value}` is no verb that takes --reason: \"{sentence}\"");
                }
            }
        }

        Assert.True(found.Count == 0, "The chat's instructions give --reason to what takes none:\n" + string.Join('\n', found));
        Assert.Equal(
            ["approval approve", "approval reject", "approval unset-standing", "campaign archive", "campaign start", "decision answer", "preference set", "preference set", "reply record"],
            checkedVerbs.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The chat's instructions and every skill under <c>skills/</c>, the business pack's among them; or, for what the chat
    /// is taught about the acts it runs and the reasons it writes, the skills the chat loads, both packs without the roles'
    /// skills, which the roles read and the chat does not.
    /// </summary>
    private static List<string> Pages(bool chatLoads = false)
    {
        var root = RepositoryRoot();
        var roles = Path.Combine(root, "skills", "runtime", "roles") + Path.DirectorySeparatorChar;
        return
        [
            Path.Combine(root, "desktop", "chat-instructions.md"),
            .. Directory.EnumerateFiles(Path.Combine(root, "skills"), "SKILL.md", SearchOption.AllDirectories)
                .Where(file => !chatLoads || !file.StartsWith(roles, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// A reason the chat writes is its own short line, in single quotes and of letters, digits, spaces and <c>. , : -</c>
    /// alone, since text from outside never goes into a command and a reason is journaled; so every reason the chat is
    /// shown, in its instructions and in the skills it loads, an example's in a fenced block among them, is one: rule 13's
    /// example, every <c>--reason</c> a sentence or a printed command gives, quoted or not, and never
    /// <c>"&lt;their words&gt;"</c> or the person's words said to be the reason.
    /// </summary>
    [Fact]
    public void Every_reason_the_chat_is_shown_is_its_own_short_line_in_single_quotes()
    {
        var pages = Pages(chatLoads: true);
        var rule13 = Assert.Single(File.ReadAllLines(pages[0]), line => line.StartsWith("13. ", StringComparison.Ordinal));
        var example = Regex.Match(rule13, "such as '(?<reason>[^']*)'").Groups["reason"].Value;
        Assert.Matches(@"^[\p{L}\p{N} .,:-]+$", example);
        Assert.NotEqual("...", example);

        var found = new List<string>();
        var single = 0;
        foreach (var page in pages)
        {
            var text = File.ReadAllText(page).ReplaceLineEndings(" ");
            foreach (Match reason in Regex.Matches(text, @"--reason (?:(?<quote>[""'])(?<reason>.*?)\k<quote>|(?<reason>[^\s`'""]+))"))
            {
                if (reason.Groups["quote"].Value != "'" || !Regex.IsMatch(reason.Groups["reason"].Value, @"^[\p{L}\p{N} .,:-]+$"))
                {
                    found.Add($"{Shown(page)}: {reason.Value}");
                }
                else
                {
                    single++;
                }
            }

            if (text.Contains("<their words>", StringComparison.Ordinal))
            {
                found.Add($"{Shown(page)}: <their words>");
            }

            foreach (Match theirs in Regex.Matches(text, @"\btheir (?:own )?words as (?:the|a|its) reason\b"))
            {
                found.Add($"{Shown(page)}: {theirs.Value}");
            }
        }

        Assert.True(found.Count == 0, "These reasons are not the chat's own short line in single quotes:\n" + string.Join('\n', found));
        Assert.True(single > 20, $"Only {single} reasons were read, so this guard read less than the chat does.");
    }

    /// <summary>
    /// What the chat is taught to do with the person's word and with what it may read, one rule each: a plain command is
    /// done at once; an act that cannot be undone, or reaches many, is said and asked once only where the person did not
    /// spell it; a campaign's conversation reaches the campaigns the person names, or all; what waits is read fresh; the
    /// one-word verbs that say whether Jason runs; a file the person names is read wherever it is, with Jason's own files
    /// still refused; and a refusal is said exactly, with its path, and never worked around. Without them a chat asks
    /// "shall I?" after a plain command, archives in silence what the person did not spell, pauses one campaign of "all",
    /// says from memory what waits, says it does not know <c>jason status</c>, or answers that it works only in its folder.
    /// </summary>
    [Theory]
    [InlineData("a plain command", "20. When the person tells you plainly to do something, in their own words in this message, do it at once. Do not ask whether you should, do not send them to Settings for it, and do not run `--help` first for a verb these rules give. Rule 21 is the one exception.")]
    [InlineData("an act not spelled", "21. Before an act that cannot be undone, or one that reaches many items, which the person did not spell out, say in one or two sentences what will happen: how many, which, and that it cannot be undone. Then ask once; their answer in this chat is their word.")]
    [InlineData("an act spelled", "Where they spelled the act and its reach, as in \"archive all campaigns except X\", that is their word already: do all of it, one after another, and report once at the end.")]
    [InlineData("campaigns named", "Work on that campaign only, unless the person names others or says all: then pausing, starting and archiving alike reach what they named.")]
    [InlineData("what waits", "Never say from memory what waits: run `jason approval list` and `jason decision list` first, each time.")]
    [InlineData("whether Jason runs", "Two verbs stand alone, with no noun: `jason status` says whether Jason runs and can work, and `jason logs` reads its log files.")]
    [InlineData("a file named", "22. A file the person names is theirs to have read, wherever it is on their computer. In Full access, read it and write it as they ask. In Ask first and Auto-edit, read it; a change to it outside this chat's folders is shown to them once first. In Plan only, read it and change nothing. Never answer that this chat works only in its folder.")]
    [InlineData("Jason's own files", "Jason's own settings, run files and data are not the person's files: they stay refused in every mode.")]
    [InlineData("a refusal", "23. When something is refused, say in one sentence exactly what was refused and why, and give the path in a code block. Where opening a folder would help, name Open folder, in the menu of the agent's row and of its header. Never try another way around a refusal.")]
    public void The_instructions_hold_the_rule_for(string rule, string sentence)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "chat-instructions.md"));
        Assert.True(
            text.Contains(sentence, StringComparison.Ordinal),
            $"The chat's instructions do not hold the rule for {rule}: \"{sentence}\"");
    }

    /// <summary>
    /// The verbs the instructions give as standing alone are commands the CLI has, and the archive the managed skill
    /// teaches asks once only where the person did not spell which campaigns, as the instructions' rule 12 says.
    /// </summary>
    [Fact]
    public void The_verbs_given_alone_are_the_clis_and_the_skill_archives_what_was_spelled_and_asks_once_for_a_bare_all()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var commands = Commands(root, []).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("status", commands);
        Assert.Contains("logs", commands);

        var skill = File.ReadAllText(Path.Combine(RepositoryRoot(), "skills", "runtime", "managed-campaign-work", "SKILL.md"));
        var flat = System.Text.RegularExpressions.Regex.Replace(skill, @"\s+", " ");
        Assert.Contains("When they tell you to archive campaigns and spell which, by naming them, by saying all of these about a list you showed them, or by saying all but those they name, archive each with `jason campaign archive`, one after another, without asking first. When they say all or everything and nothing more, say which campaigns that takes, how many, and that an archive cannot be undone, and ask once: their answer is their word.", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing plain to cover", flat, StringComparison.Ordinal);
    }
    /// <summary>
    /// A page's sentences, outside its fenced blocks: each paragraph's lines joined, then cut where a sentence ends and
    /// the next begins with a capital, after any mark that opens it.
    /// </summary>
    private static List<string> Sentences(string file)
    {
        var sentences = new List<string>();
        var paragraph = new List<string>();
        var fenced = false;
        foreach (var line in File.ReadAllLines(file))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            if (line.Trim().Length == 0)
            {
                Cut(paragraph, sentences);
                continue;
            }

            paragraph.Add(line.Trim());
        }

        Cut(paragraph, sentences);
        return sentences;
    }

    private static void Cut(List<string> paragraph, List<string> sentences)
    {
        if (paragraph.Count > 0)
        {
            sentences.AddRange(SentenceEnd().Split(string.Join(' ', paragraph)).Where(sentence => sentence.Length > 0));
            paragraph.Clear();
        }
    }

    /// <summary>Every command of the tree as its words after <c>jason</c>, a group and a hidden command included.</summary>
    private static IEnumerable<string> Commands(Command command, string[] words)
    {
        foreach (var sub in command.Subcommands)
        {
            string[] path = [.. words, sub.Name];
            yield return string.Join(' ', path);
            foreach (var below in Commands(sub, path))
            {
                yield return below;
            }
        }
    }

    private static string Shown(string file) => Path.GetRelativePath(RepositoryRoot(), file).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[*""`(\[]?[A-Z])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?<!\bPlan )\bonly (?:yours|you)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OnlyYours();

    [GeneratedRegex(@"\bpress(?:es)? (?:Start|Resume|Approve|Reject|Answer|Archive)\b")]
    private static partial Regex PressFor();

    [GeneratedRegex(@"\barchive (?:it|them|one|each)\b[^.]*\b(?:Settings|tab|window)\b")]
    private static partial Regex ArchiveThere();

    [GeneratedRegex(@"\bIn (?:the|that) chat(?: of Jason Desktop)?,? you (?:cannot|can't|can do neither|neither)\b")]
    private static partial Regex ChatCannot();

    [GeneratedRegex(@"\bthe chat refuses\b")]
    private static partial Regex ChatRefuses();

    [GeneratedRegex(@"\bSettings\b.*\b(?:Your name and tone|How messages go)\b")]
    private static partial Regex SettingsFor();

    [GeneratedRegex(@"\bPlan only\b|\bonly reads and proposes\b")]
    private static partial Regex PlanOnly();

    [GeneratedRegex(@"\bPlan only\b|\bonly reads and proposes\b|\btell (?:you|me)\b")]
    private static partial Regex PlanOnlyOrTellTheChat();

    [GeneratedRegex(@"\b[Tt]here is no `jason (?<path>[a-z][a-z-]*(?: [a-z][a-z-]*)*)`")]
    private static partial Regex NoSuchCommand();

    [GeneratedRegex(@"(?<named>`[^`]+`(?:(?:,| and| or) `[^`]+`)*) (?:does|do) not exist\b")]
    private static partial Regex DoNotExist();

    [GeneratedRegex(@"`(?:jason )?(?<path>[a-z][a-z-]*(?: [a-z][a-z-]*)+)`")]
    private static partial Regex NamedCommand();

    [GeneratedRegex(@"\bthe whole (?<noun>[a-z]+) vocabulary\b")]
    private static partial Regex WholeVocabulary();

    [GeneratedRegex(@"`jason (?<words>[^`]+)`")]
    private static partial Regex JasonCommand();
}
