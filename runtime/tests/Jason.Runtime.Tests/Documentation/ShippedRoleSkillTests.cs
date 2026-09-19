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
