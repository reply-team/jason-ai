using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The role skills this repository ships, held to the rules the launcher enforces on them. A skill is looked up
/// by the directory it lives in and must name itself the same way; a host answers a mismatch by loading nothing
/// and saying nothing, so a shipped skill that got this wrong would teach nobody and nobody would notice.
/// </summary>
public partial class ShippedRoleSkillTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_shipped_role_skill_is_named_after_its_directory()
    {
        var packs = Directory.GetDirectories(RoleSkillsPack());
        Assert.NotEmpty(packs);

        foreach (var pack in packs)
        {
            var file = Path.Combine(pack, WorkDirectory.SkillFile);
            Assert.True(File.Exists(file), $"'{pack}' is a role skill directory with no {WorkDirectory.SkillFile} in it.");
            Assert.Equal(Path.GetFileName(pack), FrontMatter(file, "name"));
        }
    }

    /// <summary>
    /// And every one of them really is deliverable: composed into a data directory the way an operator composes
    /// one, it is copied into the work directory rather than refused for its name or its size. The launcher's
    /// own code decides that here, over the whole pack, so no skill can pass the loop above and fail a launch.
    /// </summary>
    [Fact]
    public void Every_shipped_role_skill_reaches_a_work_directory_rather_than_being_refused()
    {
        var packs = Directory.GetDirectories(RoleSkillsPack());
        Assert.NotEmpty(packs);

        foreach (var pack in packs)
        {
            var role = Path.GetFileName(pack);
            using var data = new TempDataDir();
            Compose(pack, Path.Combine(data.Paths.RoleSkillsDirectory, role));

            var workDir = data.Paths.AttemptWorkDirectory("wi_A", "att_A");
            var report = WorkDirectory.Prepare(workDir, ["Write"], role, data.Paths.RoleSkillsDirectory, new RolesOptions().MaxSkillBytes);

            Assert.Null(report.RefusalCode);
            Assert.NotNull(report.Skill);
            Assert.True(report.Skill.Copied, $"The skill in '{pack}' was not given to the role.");
            Assert.Equal(role, report.Skill.Name);
            Assert.True(File.Exists(Path.Combine(workDir, ".claude", "skills", role, WorkDirectory.SkillFile)));
        }
    }

    /// <summary>The pack, files and directories alike, as an operator would have composed it.</summary>
    private static void Compose(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var child in Directory.GetDirectories(source))
        {
            Compose(child, Path.Combine(target, Path.GetFileName(child)));
        }
    }

    /// <summary>
    /// The two rules a launched role cannot work out for itself and cannot be told twice: the callback is the
    /// plain command word the envelope names, and a note is the role's own memory rather than what is true.
    /// </summary>
    [Fact]
    public async Task The_researchers_skill_teaches_the_plain_callback_and_what_a_note_is_worth()
    {
        // Flattened, so a fragment may span a line break: what is guarded is what the skill says, not where
        // its paragraphs happen to wrap.
        var skill = Whitespace().Replace(
            await File.ReadAllTextAsync(Path.Combine(RoleSkillsPack(), "researcher", WorkDirectory.SkillFile), Ct), " ");

        // The callback, in the form the allow rule grants.
        Assert.Contains("rolenote set", skill, StringComparison.Ordinal);
        Assert.Contains("workitem complete", skill, StringComparison.Ordinal);

        // What the rule was actually seen to decide: the plain callback ran. A chain, a pipe and a redirect were
        // neither shown to run nor shown to be refused, so the skill promises neither — and says so, because a
        // role told only "anything else is refused" would read a refusal as the runtime's rule and stop.
        Assert.Contains("neither promised to run nor promised to be refused", skill, StringComparison.Ordinal);

        // A launched role has no file-writing tool in this version, so a skill that tells it to write a file
        // first sends it to a denial in the middle of a paid attempt. The note and the result go inline.
        Assert.Contains("no file-writing tool", skill, StringComparison.Ordinal);
        Assert.Contains("--note '", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("--note-file", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("--result-file", skill, StringComparison.Ordinal);

        // INV-MEM-001, in the skill the role actually reads rather than only in a document nobody hands it.
        Assert.Contains("not authoritative", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime state wins", skill, StringComparison.OrdinalIgnoreCase);

        // And the answer's shape, which is the other thing a refused completion is usually about.
        Assert.Contains("result_format", skill, StringComparison.Ordinal);

        // The verb that lists who is on a campaign. A launched role cannot ask what a command should have
        // been, so a verb it is likely to want and not told about is a turn spent on a usage error: the one
        // gated run against a real host spent one guessing at `contact list --campaign`, which does not exist.
        Assert.Contains("campaign list-contacts", skill, StringComparison.Ordinal);

        // Why the note is in the runtime's store at all, in the role's own terms: it is the scratch file it
        // would keep beside the job, and it has nowhere else to keep one. Half of that sentence without the
        // other half reads as "the runtime keeps notes for you", which is the belief this whole thing is
        // arranged to prevent.
        Assert.Contains("scratch file", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("goes away with it", skill, StringComparison.OrdinalIgnoreCase);

        // And what the cap really counts, which a role writing in another script would otherwise meet as a
        // refusal naming a number three times what it thought it had written.
        Assert.Contains("outside ASCII are escaped", skill, StringComparison.Ordinal);
        Assert.Contains("note_bytes", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the manager cannot work out for itself in the middle of a review, and cannot be told twice: why it
    /// was woken, what it reads and in which order, what it may decide alone, how it asks, where a directive
    /// goes, and the one line it always leaves. Every assertion here is a sentence a real review would go wrong
    /// without.
    /// </summary>
    [Fact]
    public async Task The_managers_skill_teaches_the_review_the_boundary_and_the_line_it_always_leaves()
    {
        // Flattened, so a fragment may span a line break: what is guarded is what the skill says, not where
        // its paragraphs happen to wrap.
        var skill = Whitespace().Replace(
            await File.ReadAllTextAsync(Path.Combine(RoleSkillsPack(), "manager", WorkDirectory.SkillFile), Ct), " ");

        // 1. The brief says why the review exists and nothing else; a manager that reads the campaign before it
        // reads the brief treats a triggered review as a scheduled one and misses the thing it was woken for.
        Assert.Contains("why you were woken", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`review_intent`", skill, StringComparison.Ordinal);
        Assert.Contains("`cause`", skill, StringComparison.Ordinal);

        // 2. INV-MEM-001, in the order that makes it bite: the runtime first, the note afterwards, and the
        // runtime wins where they disagree. A manager is the role most tempted to believe its own last note.
        Assert.Contains("Read runtime state before memory", skill, StringComparison.Ordinal);
        Assert.Contains("not authoritative", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime state wins", skill, StringComparison.OrdinalIgnoreCase);

        // 3. The method, step by step, because a review that reads only what the cause points at is a review
        // of one line. Failed work is where the campaign is quietly stopping; blocked and stale work is where
        // it stalled; what waits on a person is not to be duplicated; the chronicle is what happened; the
        // context is what everybody is bound by.
        Assert.Contains("Failed items are your inbox", skill, StringComparison.Ordinal);
        Assert.Contains("Stale and blocked work", skill, StringComparison.Ordinal);
        Assert.Contains("Pending approvals and pending decisions", skill, StringComparison.Ordinal);
        Assert.Contains("The chronicle since the last review", skill, StringComparison.Ordinal);
        Assert.Contains("The campaign context is the shared knowledge", skill, StringComparison.Ordinal);

        // 4. The boundary, named verb by verb, and the sentence that closes it. A manager told only what it
        // may do would infer the rest; a manager told "everything else is escalated" does not have to.
        Assert.Contains("reprioritize", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create work", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cancel work", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask for a specialist", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("update campaign context", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("write the chronicle", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Everything else is escalated", skill, StringComparison.Ordinal);

        // 5. How to ask: one question, with the reading already done and a recommendation on it. A person who
        // opens ten questions from one review answers none of them, and one that carries no recommendation
        // sends the person to do the review over.
        Assert.Contains("one question", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("what is already known and what you recommend", skill, StringComparison.Ordinal);
        Assert.Contains("decision raise", skill, StringComparison.Ordinal);

        // 6. Where a directive goes. The context is authoritative, journalled and read by every role; the note
        // is one role's private memory. A directive written into the note binds nobody and is read by nobody
        // else, which is the failure the two concepts are kept apart to prevent.
        Assert.Contains("campaign context", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("authoritative, journalled, visible to every role", skill, StringComparison.Ordinal);
        Assert.Contains("private pacing heuristics", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may stay in the note", skill, StringComparison.Ordinal);

        // 7. The line every run leaves, under a kind of the role's own: the runtime refuses its reserved kinds
        // from anybody but itself, so a manager that reached for one would fail its one unconditional duty.
        // And an empty tact is that line, not silence — the next run and the person both need to know the
        // campaign was looked at and why nothing changed.
        Assert.Contains("its own kind", skill, StringComparison.Ordinal);
        Assert.Contains("never one of the runtime's reserved kinds", skill, StringComparison.Ordinal);
        Assert.Contains("empty tact", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly that line", skill, StringComparison.Ordinal);
        Assert.Contains("journal append", skill, StringComparison.Ordinal);

        // 8. No outbound effect, and the way a decision still becomes one: through a work item that parks on
        // approval like everybody else's. A manager that reached a provider itself would be an effect nobody
        // approved, performed by the role whose job is to notice such things.
        Assert.Contains("no outbound effect", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parks on approval like anybody else's", skill, StringComparison.Ordinal);

        // 9. The plain callback, with what the rule was actually seen to decide and nothing more; no file to
        // write first; the note whole and the result inline. The same three facts the researcher is taught,
        // because the same launch grants them.
        Assert.Contains("runtime.cli_command", skill, StringComparison.Ordinal);
        Assert.Contains("neither promised to run nor promised to be refused", skill, StringComparison.Ordinal);
        Assert.Contains("no file-writing tool", skill, StringComparison.Ordinal);
        Assert.Contains("replaced whole", skill, StringComparison.Ordinal);
        Assert.Contains("--note '", skill, StringComparison.Ordinal);
        Assert.Contains("--result '", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("--note-file", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("--result-file", skill, StringComparison.Ordinal);

        // 10. The outcome a review is allowed to have. A manager that believes it must act to have reviewed
        // will act, and the campaign will be managed by a role looking for something to report.
        Assert.Contains("`outcome: \"nothing\"` is a good outcome", skill, StringComparison.Ordinal);

        // 11. The one verb it never types. A review that answered its own question would be the loop talking
        // to itself, and the brief's own list says so.
        Assert.Contains("never answer a decision", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`decision.answer` is not in your `allowed_operations`", skill, StringComparison.Ordinal);

        // 12. The read a manager does for itself. One scan's read is one review, so a question answered behind
        // a failure in the same pass is consumed by the review the failure summoned, whose brief names the
        // failure. A manager that trusted the brief to name every answered question would leave that answer
        // unacted on until the cadence came round.
        Assert.Contains("jason decision list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status answered", skill, StringComparison.Ordinal);
        Assert.Contains("one read is one review", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rather than trusting the brief to name one", skill, StringComparison.Ordinal);

        // And no actor on any of it: the environment attributes a launched role's calls to its attempt, and a
        // manager that typed one would break the chain the work it creates inherits.
        Assert.DoesNotContain("--actor", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// One word, one meaning. A check-in is the work item the runtime creates to have a campaign reviewed;
    /// what a role does to say it is still alive is a heartbeat. A skill that spends the noun on the verb
    /// teaches a second meaning to the one reader who has no way to ask which was meant.
    /// </summary>
    [Fact]
    public async Task Only_the_manager_is_taught_the_word_check_in()
    {
        foreach (var pack in Directory.GetDirectories(RoleSkillsPack()))
        {
            var role = Path.GetFileName(pack);
            if (role == "manager")
            {
                continue;
            }

            var skill = await File.ReadAllTextAsync(Path.Combine(pack, WorkDirectory.SkillFile), Ct);
            Assert.DoesNotContain("check in", skill, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("check-in", skill, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// An honest status, the way the other shipped skill carries one. A first draft that called itself
    /// finished would be the one claim in it a reader could not check.
    /// </summary>
    [Fact]
    public void Every_shipped_role_skill_says_what_it_is()
    {
        foreach (var pack in Directory.GetDirectories(RoleSkillsPack()))
        {
            var file = Path.Combine(pack, WorkDirectory.SkillFile);
            Assert.Equal("draft", FrontMatter(file, "status"));
            Assert.False(string.IsNullOrWhiteSpace(FrontMatter(file, "description")), $"'{file}' describes nothing.");
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The name a skill gives itself, read the way the launcher reads it.</summary>
    private static string? FrontMatter(string file, string key)
    {
        var lines = File.ReadLines(file).Take(64).ToList();
        Assert.True(lines.Count > 0 && lines[0].Trim() == "---", $"'{file}' does not open with front matter.");

        foreach (var line in lines.Skip(1))
        {
            if (line.Trim() == "---")
            {
                break;
            }

            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return line[(key.Length + 1)..].Trim().Trim('"', '\'');
            }
        }

        return null;
    }

    private static string RoleSkillsPack()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "skills", "runtime", "roles");
    }
}
