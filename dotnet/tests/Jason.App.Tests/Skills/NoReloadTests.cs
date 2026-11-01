using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Skills;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// There is no reload.
/// </summary>
/// <remarks>
/// Unlike plugins, which are frozen into a snapshot and need an explicit reload, the role skills root is read
/// on <em>every</em> launch. That is why a deployment is live the moment it lands and why the install verb
/// needs no reload of its own — and it is exactly the kind of property somebody optimises away for good
/// reasons, breaking the installer in a way nothing reports.
/// </remarks>
public class NoReloadTests
{
    private const string Role = "researcher";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_second_launch_is_taught_the_bytes_the_deployment_changed_to()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        await InstallAsync(data, tree, harness, "FIRST", force: false);
        var first = WorkDirectory.Prepare(Work(data, "att_1"), [], Role, data.Paths.RoleSkillsDirectory, 1024 * 1024);

        await InstallAsync(data, tree, harness, "SECOND-AND-LONGER", force: true);
        var second = WorkDirectory.Prepare(Work(data, "att_2"), [], Role, data.Paths.RoleSkillsDirectory, 1024 * 1024);

        Assert.Contains("FIRST", Taught(data, "att_1"), StringComparison.Ordinal);
        Assert.True(
            Taught(data, "att_2").Contains("SECOND-AND-LONGER", StringComparison.Ordinal),
            "The second launch was taught what the first was taught, although the deployment changed in "
            + "between: the role skills root is read at every launch and there is nothing to reload.");

        Assert.NotEqual(first.Skill!.Bytes, second.Skill!.Bytes);

        // Nothing was restarted, nothing was reloaded, and no runtime was running at all.
        Assert.Null(first.RefusalCode);
        Assert.Null(second.RefusalCode);
    }

    private static async Task InstallAsync(TempDataDir data, TempTree tree, string harness, string marker, bool force)
    {
        var source = Path.Combine(tree.Root, $"source-{marker}");
        var skill = Path.Combine(source, "skills", "runtime", "roles", Role);
        Directory.CreateDirectory(skill);
        await File.WriteAllTextAsync(
            Path.Combine(skill, RoleSkillRules.SkillFile),
            $"---\nname: {Role}\ndescription: one line\n---\n\n{marker}\n",
            Ct);

        string[] arguments = force
            ? ["skills", "install", "--source", source, "--root", harness, "--force"]
            : ["skills", "install", "--source", source, "--root", harness];

        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            arguments,
            new CliEnvironment(
                new StringWriter(),
                error,
                data.Paths,
                Processes: new FakeProcessControl(),
                Harnesses: HarnessLocators.At(harness),
                Programs: new FakeProgramRunner()),
            Ct);

        Assert.True(exit == ExitCodes.Success, $"The deployment failed: {error}");
    }

    private static string Work(TempDataDir data, string attempt) => data.Paths.AttemptWorkDirectory("wi_A", attempt);

    private static string Taught(TempDataDir data, string attempt) =>
        File.ReadAllText(Path.Combine(Work(data, attempt), ".claude", "skills", Role, RoleSkillRules.SkillFile));
}
