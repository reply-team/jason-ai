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
        var pages = Pages();
        Assert.True(pages.Count > 20, $"Only {pages.Count} pages were read, so this guard read less than the chat does.");

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

    /// <summary>The chat's instructions and every skill under <c>skills/</c>, the business pack's among them.</summary>
    private static List<string> Pages()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "desktop", "chat-instructions.md"),
            .. Directory.EnumerateFiles(Path.Combine(root, "skills"), "SKILL.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal),
        ];
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

    [GeneratedRegex(@"\bPlan only\b")]
    private static partial Regex PlanOnly();

    [GeneratedRegex(@"\bPlan only\b|\btell (?:you|me)\b")]
    private static partial Regex PlanOnlyOrTellTheChat();

    [GeneratedRegex(@"\b[Tt]here is no `jason (?<path>[a-z][a-z-]*(?: [a-z][a-z-]*)*)`")]
    private static partial Regex NoSuchCommand();

    [GeneratedRegex(@"(?<named>`[^`]+`(?:(?:,| and| or) `[^`]+`)*) (?:does|do) not exist\b")]
    private static partial Regex DoNotExist();

    [GeneratedRegex(@"`(?:jason )?(?<path>[a-z][a-z-]*(?: [a-z][a-z-]*)+)`")]
    private static partial Regex NamedCommand();

    [GeneratedRegex(@"\bthe whole (?<noun>[a-z]+) vocabulary\b")]
    private static partial Regex WholeVocabulary();
}
