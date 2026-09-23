using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The verb, run against the packs this repository actually ships.
/// </summary>
/// <remarks>
/// <para>
/// Nothing did this before, and that is how the defect got in. The pack roots are read only by <em>content</em>
/// guards — front matter, digests, the catalog roster — which never enumerate a root the way a deployment
/// does; and every deployer test composes its own tree by hand, which is a tree written by somebody who
/// already knew what the deployer expects. One increment put a <c>.claude-plugin</c> directory in each pack
/// root for the skills marketplace, a later one wrote an enumerator that refuses any directory with no
/// <c>SKILL.md</c>, and both were right on their own. A composition nobody runs is a composition nobody has
/// checked.
/// </para>
/// <para>
/// Deliberately the whole verb rather than the planner. What the two increments produced together was a
/// refusal reported by the command, and a test asserting over a plan object would not have seen it.
/// </para>
/// </remarks>
public class PackDeploymentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_packs_this_repository_ships_deploy_without_a_refusal()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", SkillPack.RepositoryRoot(), "--root", harness],
            Machine(data, harness, error),
            Ct);

        Assert.True(exit == ExitCodes.Success, $"Deploying this repository's own packs was refused: {error}");
    }

    /// <summary>
    /// And exactly the skills the packs hold arrive — not merely "no refusal". A rule that skipped a directory
    /// it should have deployed would satisfy the test above by writing less, which is the same silence a
    /// misspelled <c>--pack</c> used to answer with.
    /// </summary>
    /// <remarks>
    /// The names come from <see cref="SkillPack"/>, which is what the content guards already read, so the two
    /// halves of the repository are compared against each other rather than against a list written here that
    /// would have to be maintained beside both.
    /// </remarks>
    [Fact]
    public async Task Every_skill_the_packs_hold_arrives_and_nothing_else_does()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", SkillPack.RepositoryRoot(), "--root", harness],
            Machine(data, harness, new StringWriter()),
            Ct);

        Assert.Equal(ExitCodes.Success, exit);

        var interactive = SkillPack.All().Where(skill => !skill.IsRole).Select(skill => skill.Name);
        var business = SkillPack.Business().Select(skill => skill.Name);
        var roles = SkillPack.All().Where(skill => skill.IsRole).Select(skill => skill.Name);

        Assert.Equal(interactive.Concat(business).Order(StringComparer.Ordinal), Deployed(harness));
        Assert.Equal(roles.Order(StringComparer.Ordinal), Deployed(data.Paths.RoleSkillsDirectory));
    }

    /// <summary>Every directory that arrived is a skill a host can read, and none of them is metadata.</summary>
    [Fact]
    public async Task Every_directory_that_arrives_has_a_skill_file_and_none_of_them_is_a_dot_directory()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        await CliApp.RunAsync(
            ["skills", "install", "--source", SkillPack.RepositoryRoot(), "--root", harness],
            Machine(data, harness, new StringWriter()),
            Ct);

        foreach (var root in new[] { harness, data.Paths.RoleSkillsDirectory })
        {
            foreach (var directory in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(directory);
                Assert.False(name.StartsWith('.'), $"'{name}' is metadata beside the skills and was deployed as one.");
                Assert.True(
                    File.Exists(Path.Combine(directory, SkillPack.SkillFile)),
                    $"'{name}' was deployed and a host would load nothing from it.");
            }
        }
    }

    /// <summary>And both roots carry a record naming what was written, which is what an uninstall removes by.</summary>
    [Fact]
    public async Task Both_roots_carry_a_record_naming_what_was_written()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        await CliApp.RunAsync(
            ["skills", "install", "--source", SkillPack.RepositoryRoot(), "--root", harness],
            Machine(data, harness, new StringWriter()),
            Ct);

        foreach (var root in new[] { harness, data.Paths.RoleSkillsDirectory })
        {
            var record = SkillsRecord.Read(root);
            Assert.NotNull(record);
            Assert.NotEmpty(record.Packs);
            Assert.All(record.Packs, pack => Assert.NotEmpty(pack.Files));
        }
    }

    private static IEnumerable<string> Deployed(string root) =>
        Directory.Exists(root)
            ? Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)
            : [];

    private static CliEnvironment Machine(TempDataDir data, string harness, TextWriter error) =>
        new(
            new StringWriter(),
            error,
            data.Paths,
            Processes: new FakeProcessControl(),
            Harnesses: HarnessLocators.At(harness),
            Programs: new FakeProgramRunner());
}
