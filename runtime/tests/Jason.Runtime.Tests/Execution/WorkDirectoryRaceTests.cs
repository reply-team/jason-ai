using Jason.Contracts.Skills;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What a launch does when a deployment renames the role's tree out from under it.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic on purpose. The behaviour under test exists to make a probabilistic failure impossible, and a
/// probabilistic proof of it is a test that can pass without ever exercising what it guards. So the rename
/// happens at the one instant that matters, through the launcher's own hook.
/// </para>
/// <para>
/// This is the whole reason <c>Jason.Runtime</c> carries the repository's only friend declaration.
/// </para>
/// </remarks>
public class WorkDirectoryRaceTests : IDisposable
{
    private const string Role = "researcher";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public WorkDirectoryRaceTests() => Directory.CreateDirectory(_root);

    private string WorkDir => Path.Combine(_root, "work");

    private string SkillsRoot => Path.Combine(_root, "skills", "roles");

    private string Live => Path.Combine(SkillsRoot, Role);

    private string Taught => Path.Combine(WorkDir, ".claude", "skills", Role, "SKILL.md");

    /// <summary>
    /// A tree renamed under a launch is read again rather than throwing. Before this, the launcher was copying
    /// from paths it had captured before the rename: on a platform where that rename succeeds, the copy died
    /// with an IO exception, which is neither the tree that was there, nor the one that arrived, nor nothing.
    /// </summary>
    [Fact]
    public void A_tree_renamed_under_a_launch_is_read_again_rather_than_throwing()
    {
        Deploy(Live, "OLD");
        var staged = Path.Combine(_root, "staging", Role);
        Deploy(staged, "NEW-BODY-AND-LONGER");

        WorkDirectory.BetweenReadAndCopy.Value = () =>
        {
            // Once, so the second read finds the tree that arrived.
            WorkDirectory.BetweenReadAndCopy.Value = null;
            Swap(staged);
        };

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Null(report.RefusalCode);
        Assert.True(report.Rescued, "The launch was not rescued by a second read; it did not notice the rename.");
        Assert.Contains("NEW-BODY-AND-LONGER", File.ReadAllText(Taught), StringComparison.Ordinal);
        Assert.DoesNotContain("OLD", File.ReadAllText(Taught), StringComparison.Ordinal);
    }

    /// <summary>
    /// And nothing of the tree that was there survives into the one that arrived. A file the new tree does not
    /// have would otherwise sit in the work directory beside the new skill, and the agent would read both.
    /// </summary>
    [Fact]
    public void Nothing_of_the_tree_that_was_there_survives_into_the_one_that_arrived()
    {
        Deploy(Live, "OLD");
        File.WriteAllText(Path.Combine(Live, "only-in-the-old-tree.md"), "gone");
        var staged = Path.Combine(_root, "staging", Role);
        Deploy(staged, "NEW-BODY-AND-LONGER");

        WorkDirectory.BetweenReadAndCopy.Value = () =>
        {
            WorkDirectory.BetweenReadAndCopy.Value = null;
            Swap(staged);
        };

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.True(report.Rescued);
        Assert.False(
            File.Exists(Path.Combine(WorkDir, ".claude", "skills", Role, "only-in-the-old-tree.md")),
            "A file from the tree that was replaced survived into the work directory, so the agent sees a blend.");
    }

    /// <summary>
    /// Losing it twice running is refused, in its own words, rather than run with no skill. A role that was
    /// given a skill and did not receive it would do the job untaught at the price of a real launch, which is
    /// what this launcher already refuses over — and it is not the code for a skill that is invalid, because
    /// nothing about this one is.
    /// </summary>
    [Fact]
    public void A_launch_that_loses_the_race_twice_is_refused_rather_than_taught_nothing()
    {
        Deploy(Live, "OLD");

        // Every generation a different shape, so the second loss is one the launcher can see. Two deployments
        // that delivered identically shaped trees would leave a consistent tree behind and nothing to refuse.
        var generation = 0;
        WorkDirectory.BetweenReadAndCopy.Value = () =>
        {
            generation++;
            var staged = Path.Combine(_root, $"staging-{generation}", Role);
            Deploy(staged, new string('x', 32 * generation));
            Swap(staged);
        };

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Equal(AttemptErrors.RoleSkillUnreadable, report.RefusalCode);
        Assert.Contains("a deployment was in flight", report.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing outside a test ever arms the hook.
    /// </summary>
    /// <remarks>
    /// Read off the sources rather than off the value. Asserting the value is null proves nothing at all: an
    /// <see cref="AsyncLocal{T}"/> read on a fresh test flow is null whatever any other flow did, so the
    /// assertion could not see the thing its own summary claimed it held. This is what that fact was asked
    /// for, and a scan is the only shape that can fail.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_product_arms_the_read_hook()
    {
        var sources = Directory
            .EnumerateFiles(ProductSources(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.True(sources.Count > 50, $"Only {sources.Count} source files were scanned; the product tree was not found.");

        foreach (var file in sources)
        {
            Assert.True(
                !File.ReadAllText(file).Contains($"{nameof(WorkDirectory.BetweenReadAndCopy)}.Value =", StringComparison.Ordinal),
                $"'{Path.GetFileName(file)}' arms the launcher's test hook. It exists so a race can be made "
                + "deterministic in a test, and nothing that ships may set it.");
        }
    }

    private static string ProductSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "runtime", "src");
    }

    /// <summary>
    /// The replacement a deployment performs: the live tree aside first, then the staged tree in. The first is
    /// the one a reader can refuse; the second targets a path nothing can hold open.
    /// </summary>
    private void Swap(string staged)
    {
        var aside = Path.Combine(_root, $"aside-{Guid.NewGuid():N}");
        Directory.Move(Live, aside);
        Directory.Move(staged, Live);
    }

    private static void Deploy(string directory, string marker)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, RoleSkillRules.SkillFile),
            $"---\nname: {Role}\ndescription: one line\n---\n\n{marker}\n");
        File.WriteAllText(Path.Combine(directory, "reference.md"), marker);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        WorkDirectory.BetweenReadAndCopy.Value = null;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
