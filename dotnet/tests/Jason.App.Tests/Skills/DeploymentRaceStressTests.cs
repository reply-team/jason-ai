using System.Collections.Concurrent;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Skills;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The window a real deployment opens, under enough launches to find it.
/// </summary>
/// <remarks>
/// <para>
/// The race test beside this one launches with a gap, the way real launches arrive, and asserts nothing is
/// refused. This one launches as hard as it can and asserts the thing that gap was hiding: <b>no launch is
/// ever recorded as having been taught nothing while a deployment says this role has a skill</b>.
/// </para>
/// <para>
/// That outcome was the defect. Between a replacement's two renames the role's directory does not exist, and
/// "not there" read as "this role was never given a skill" — no refusal, no retry, an attempt recorded as a
/// success that ran untaught at the price of a real launch. It beat the rescue path by two orders of
/// magnitude, because the rescue only covered a tree that moved <em>while</em> being read and this is a tree
/// that is not there at all.
/// </para>
/// <para>
/// The record in the role root is what tells the two apart: a role it names has a skill, so absent means
/// mid-replacement. A role it does not name never had one, and that is still reported as untaught.
/// </para>
/// </remarks>
public class DeploymentRaceStressTests
{
    private const string Role = "researcher";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_launch_is_recorded_as_taught_nothing_while_a_deployment_says_this_role_has_a_skill()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        await InstallAsync(data, tree, harness, "FIRST");
        Assert.Contains(Role, SkillsRecord.RolesDeployedIn(data.Paths.RoleSkillsDirectory));

        var live = Path.Combine(data.Paths.RoleSkillsDirectory, Role);
        var staging = Directory.CreateDirectory(Path.Combine(tree.Root, "swap")).FullName;

        var untaught = 0;
        var refused = new ConcurrentBag<string>();
        var taught = 0;
        var thrown = new ConcurrentBag<Exception>();

        using var stop = new CancellationTokenSource();
        var swapping = Task.Run(
            () =>
            {
                var generation = 0;
                while (!stop.IsCancellationRequested)
                {
                    var next = Path.Combine(staging, $"gen-{generation}", Role);
                    Compose(next, $"GENERATION-{generation}{new string('x', generation % 7)}");
                    var aside = Path.Combine(staging, $"aside-{generation}");
                    try
                    {
                        Directory.Move(live, aside);
                        Directory.Move(next, live);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    generation++;
                }
            },
            CancellationToken.None);

        for (var attempt = 0; attempt < 400; attempt++)
        {
            try
            {
                var report = WorkDirectory.Prepare(
                    data.Paths.AttemptWorkDirectory("wi_A", $"att_{attempt}"),
                    [],
                    Role,
                    data.Paths.RoleSkillsDirectory,
                    1024 * 1024);

                if (report.RefusalCode is { } code)
                {
                    refused.Add(code);
                }
                else if (report.Skill is { Copied: false })
                {
                    Interlocked.Increment(ref untaught);
                }
                else
                {
                    taught++;
                }
            }
            catch (Exception exception)
            {
                thrown.Add(exception);
            }
        }

        await stop.CancelAsync();
        await swapping;

        Assert.Empty(thrown);
        Assert.True(taught > 0, "No launch was taught anything at all, so this proved nothing.");

        Assert.True(
            untaught == 0,
            $"{untaught} of 400 launches were recorded as having been taught nothing, while the record in the "
            + "role root says this role has a skill. A role that was given a skill and did not receive it runs "
            + "untaught at the price of a real launch, which is what this launcher refuses over.");

        // A refusal is the honest answer when a deployment really will not let go, and it is not the one a
        // misread should produce: nothing here may be blamed on a skill that is perfectly valid.
        Assert.DoesNotContain(AttemptErrors.RoleSkillInvalid, refused);
    }

    /// <summary>
    /// And a role nothing ever deployed is still reported as untaught rather than refused. The record is the
    /// whole of the distinction, so a root with no record must behave exactly as it always did.
    /// </summary>
    [Fact]
    public void A_role_no_record_names_is_still_reported_as_having_no_skill()
    {
        using var data = new TempDataDir();

        var report = WorkDirectory.Prepare(
            data.Paths.AttemptWorkDirectory("wi_A", "att_A"),
            [],
            Role,
            data.Paths.RoleSkillsDirectory,
            1024 * 1024);

        Assert.Null(report.RefusalCode);
        Assert.NotNull(report.Skill);
        Assert.False(report.Skill.Copied);
    }

    private static async Task InstallAsync(TempDataDir data, TempTree tree, string harness, string marker)
    {
        var source = Path.Combine(tree.Root, $"source-{marker}");
        Compose(Path.Combine(source, "skills", "runtime", "roles", Role), marker);

        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", source, "--root", harness],
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

    private static void Compose(string directory, string marker)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, RoleSkillRules.SkillFile),
            $"---\nname: {Role}\ndescription: one line\n---\n\n{marker}\n");

        for (var index = 0; index < 6; index++)
        {
            File.WriteAllText(Path.Combine(directory, $"reference-{index}.md"), $"{marker} {index}");
        }
    }
}
