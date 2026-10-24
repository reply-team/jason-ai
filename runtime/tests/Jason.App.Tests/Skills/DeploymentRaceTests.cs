using System.Collections.Concurrent;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Skills;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// A launch that happens while a deployment is in flight.
/// </summary>
/// <remarks>
/// <para>
/// A test that deploys and then launches proves nothing about this. The failure is a race, it only shows up
/// under load, and it looks like a model failure rather than an installer failure — a role that was taught
/// half of one tree and half of another, reported as having been taught.
/// </para>
/// <para>
/// Both sides are asserted, because the two platforms fail on different sides of the call. On Windows a
/// rename of a directory holding an open file is refused, so the pressure lands on the installer. Elsewhere
/// the rename succeeds and the pressure lands on the launch. A test that examined only the reports would pass
/// on Windows while the deployment had quietly failed.
/// </para>
/// </remarks>
public class DeploymentRaceTests
{
    private const string Role = "researcher";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_launch_during_a_deployment_is_taught_a_whole_tree_or_nothing_and_the_deployment_still_lands()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();

        var live = Path.Combine(data.Paths.RoleSkillsDirectory, Role);
        Compose(live, files: 80, marker: "OLD");
        var before = RoleSkillRules.Read(live, Role, int.MaxValue).Bytes;

        var source = Path.Combine(tree.Root, "source");
        Compose(Path.Combine(source, "skills", "runtime", "roles", Role), files: 120, marker: "NEW-AND-LONGER-BODY");
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        var reports = new ConcurrentBag<WorkDirectoryReport>();
        var thrown = new ConcurrentBag<Exception>();
        using var stop = new CancellationTokenSource();

        // The loop has launched once before the deployment begins, so what it collects is never nothing: the
        // assertions over its reports below would otherwise hold of an empty list on a machine slow enough that
        // the deployment finished before the loop's first launch.
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var launching = Task.Run(
            async () =>
            {
                var attempt = 0;
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        reports.Add(WorkDirectory.Prepare(
                            data.Paths.AttemptWorkDirectory("wi_A", $"att_{attempt++}"),
                            [],
                            Role,
                            data.Paths.RoleSkillsDirectory,
                            1024 * 1024));
                    }
                    catch (Exception exception)
                    {
                        thrown.Add(exception);
                    }

                    launched.TrySetResult();

                    // A gap, because real launches are seconds apart. A loop with none is not the scenario: it
                    // is a permanent lock, under which the installer is meant to fail and does.
                    await Task.Delay(2, CancellationToken.None);
                }
            },
            CancellationToken.None);

        await launched.Task.WaitAsync(Ct);

        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", source, "--root", harness],
            Machine(data, harness),
            Ct);

        // What the loop collected, counted before the settled launch below joins the list: `NotEmpty` over the
        // list after that join held whatever the loop did.
        var collected = reports.Count;

        // One launch after the deployment has landed, before the loop is stopped.
        //
        // The assertion at the end of this test -- that some launch was taught the new tree -- is what keeps
        // it from being vacuous, and leaving that to the scheduler is why it went red on a release build
        // while the product was whole: two launches were collected, both read the old tree, and the install
        // landed after the last of them. A concurrent launch may or may not observe the new tree. This one
        // must, because the deployment has already returned.
        reports.Add(WorkDirectory.Prepare(
            data.Paths.AttemptWorkDirectory("wi_A", "att_settled"),
            [],
            Role,
            data.Paths.RoleSkillsDirectory,
            1024 * 1024));

        await stop.CancelAsync();
        await launching;

        // Side one: the deployment landed.
        Assert.Equal(ExitCodes.Success, exit);
        var after = RoleSkillRules.Read(live, Role, int.MaxValue).Bytes;
        Assert.NotEqual(before, after);

        // Side two: every report the launching loop collected.
        Assert.Empty(thrown);
        Assert.True(collected > 0, "the launching loop collected nothing, so nothing here raced the deployment.");
        foreach (var report in reports)
        {
            Assert.True(
                report.RefusalCode is null,
                $"A launch during the deployment was refused with '{report.RefusalCode}': {report.Message}. A "
                + "deployment may not turn a valid skill into a refused one.");

            Assert.NotNull(report.Skill);
            Assert.True(
                report.Skill.Bytes == before || report.Skill.Bytes == after || report.Skill is { Copied: false, Bytes: 0 },
                $"A launch that ran while the deployment was in flight was taught {report.Skill.Bytes} bytes, "
                + $"which is neither the tree that was there ({before}) nor the tree being deployed ({after}) "
                + "nor nothing at all.");
        }

        // Certain, not hoped for: the settled launch above took place after the deployment returned. What a
        // concurrent launch proves is the invariant in the loop above -- whole old tree, whole new tree, or
        // nothing -- and what this proves is that the new tree is reachable at all.
        Assert.Contains(reports, report => report.Skill!.Bytes == after);
    }

    /// <summary>
    /// A reader that never lets go. On Windows the rename is refused while a file inside is open, so the verb
    /// fails — and must fail cleanly, with the live tree exactly as it was and a message naming the cause
    /// rather than a path and no reason. Elsewhere a reader does not block a rename at all, so the verb
    /// succeeds and the launch is the side that tolerates it, which the launcher's own tests hold.
    /// </summary>
    [Fact]
    public async Task A_role_held_open_throughout_fails_the_verb_cleanly_on_windows_and_does_not_elsewhere()
    {
        using var data = new TempDataDir();
        using var tree = new TempTree();

        var live = Path.Combine(data.Paths.RoleSkillsDirectory, Role);
        Compose(live, files: 2, marker: "OLD");
        var before = RoleSkillRules.Read(live, Role, int.MaxValue).Bytes;

        var source = Path.Combine(tree.Root, "source");
        Compose(Path.Combine(source, "skills", "runtime", "roles", Role), files: 2, marker: "NEW-AND-LONGER-BODY");
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        var error = new StringWriter();
        using var held = File.Open(Path.Combine(live, RoleSkillRules.SkillFile), FileMode.Open, FileAccess.Read, FileShare.Read);

        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", source, "--root", harness],
            Machine(data, harness) with { Error = error },
            Ct);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.Contains("something is holding this role's skill open", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("Nothing was changed for it.", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(before, RoleSkillRules.Read(live, Role, int.MaxValue).Bytes);
        }
        else
        {
            Assert.Equal(ExitCodes.Success, exit);
            Assert.NotEqual(before, RoleSkillRules.Read(live, Role, int.MaxValue).Bytes);
        }
    }

    /// <summary>
    /// A pack with no record yet, one of whose skills is refused, is not recorded at all. The record is written last
    /// to say what is on disk, and it was written at the new ref with the refused skill's files left out: the pack then
    /// read as current, <c>jason status</c> named no repair, and an update found nothing to move. With no earlier
    /// record there is no old deployment to keep, so the honest record is none, and the run that finds the skill free
    /// completes the pack and records it.
    /// </summary>
    [Fact]
    public async Task A_skill_refused_in_a_pack_with_no_record_leaves_the_pack_unrecorded_until_a_run_completes_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to move a directory that holds an open file.");

        using var data = new TempDataDir();
        using var tree = new TempTree();

        var live = Path.Combine(data.Paths.RoleSkillsDirectory, Role);
        Compose(live, files: 1, marker: "OLD");

        var source = Path.Combine(tree.Root, "source");
        Compose(Path.Combine(source, "skills", "runtime", "roles", Role), files: 1, marker: "NEW");
        Compose(Path.Combine(source, "skills", "runtime", "roles", "writer"), files: 1, marker: "NEW", name: "writer");
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;

        var error = new StringWriter();
        int refusedExit;
        using (File.Open(Path.Combine(live, RoleSkillRules.SkillFile), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refusedExit = await CliApp.RunAsync(
                ["skills", "install", "--source", source, "--root", harness],
                Machine(data, harness) with { Error = error },
                Ct);
        }

        Assert.True(refusedExit == ExitCodes.ApiError, $"The install with a role held open exited {refusedExit}: {error}");
        var recorded = SkillsRecord.Read(data.Paths.RoleSkillsDirectory)?.Packs ?? [];
        Assert.True(
            recorded.Count == 0,
            $"The role root records {string.Join(", ", recorded.Select(pack => $"{pack.Pack} with {pack.Files.Count} files"))} after '{Role}' was refused and no record had existed.");
        AssertRepairNamed(error, data, "jason skills install");

        var retry = new StringWriter();
        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", source, "--root", harness],
            Machine(data, harness) with { Error = retry },
            Ct);

        Assert.True(exit == ExitCodes.Success, $"The install once nothing held the role exited {exit}: {retry}");
        var completed = Assert.Single(SkillsRecord.Read(data.Paths.RoleSkillsDirectory)!.Packs, pack => pack.Pack == SkillPacks.Runtime);
        Assert.True(
            completed.Files.Count(file => file.Path.StartsWith($"{Role}/", StringComparison.Ordinal)) == 2,
            $"The completed record names {string.Join(", ", completed.Files.Select(file => file.Path))}.");
    }

    /// <summary>
    /// And the repair the refusal names is the install that was run, narrowed the way it was: a pack named with
    /// <c>--pack</c> is named again, and the role skills alone are deployed again alone. An update would not do, since it
    /// moves recorded packs only, and the line says so.
    /// </summary>
    [Theory]
    [InlineData("--pack", "jason skills install --pack jason-runtime-skills")]
    [InlineData("--roles-only", "jason skills install --roles-only")]
    public async Task A_pack_left_unrecorded_names_the_install_that_completes_it(string narrowed, string repair)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to move a directory that holds an open file.");

        using var data = new TempDataDir();
        using var tree = new TempTree();

        var live = Path.Combine(data.Paths.RoleSkillsDirectory, Role);
        Compose(live, files: 1, marker: "OLD");

        var source = Path.Combine(tree.Root, "source");
        Compose(Path.Combine(source, "skills", "runtime", "roles", Role), files: 1, marker: "NEW");
        Compose(Path.Combine(source, "skills", "runtime", "roles", "writer"), files: 1, marker: "NEW", name: "writer");
        var harness = Directory.CreateDirectory(Path.Combine(tree.Root, "harness")).FullName;
        string[] arguments = narrowed == "--pack"
            ? ["skills", "install", "--source", source, "--root", harness, "--pack", SkillPacks.Runtime]
            : ["skills", "install", "--source", source, "--roles-only"];

        var error = new StringWriter();
        int exit;
        using (File.Open(Path.Combine(live, RoleSkillRules.SkillFile), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            exit = await CliApp.RunAsync(arguments, Machine(data, harness) with { Error = error }, Ct);
        }

        Assert.True(exit == ExitCodes.ApiError, $"The install with a role held open exited {exit}: {error}");
        AssertRepairNamed(error, data, repair);
    }

    /// <summary>The line that says the runtime's pack was left unrecorded in the role root, and which install completes it.</summary>
    private static void AssertRepairNamed(StringWriter error, TempDataDir data, string repair)
    {
        var expected = $"{SkillPacks.Runtime} in {data.Paths.RoleSkillsDirectory} is left unrecorded, because a skill of it was refused. "
            + $"Run '{repair}' to complete it and record it; 'jason skills update' will not, since it moves recorded packs only.";
        Assert.True(
            error.ToString().Split(Environment.NewLine).Contains(expected, StringComparer.Ordinal),
            $"The install said:{Environment.NewLine}{error}{Environment.NewLine}and not the line: {expected}");
    }

    private static CliEnvironment Machine(TempDataDir data, string harness) =>
        new(
            new StringWriter(),
            new StringWriter(),
            data.Paths,
            Processes: new FakeProcessControl(),
            Harnesses: HarnessLocators.At(harness),
            Programs: new FakeProgramRunner());

    private static void Compose(string directory, int files, string marker, string name = Role)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, RoleSkillRules.SkillFile),
            $"---\nname: {name}\ndescription: one line\n---\n\n{marker}\n");

        for (var index = 0; index < files; index++)
        {
            File.WriteAllText(Path.Combine(directory, $"reference-{index}.md"), $"{marker} {index}");
        }
    }
}
