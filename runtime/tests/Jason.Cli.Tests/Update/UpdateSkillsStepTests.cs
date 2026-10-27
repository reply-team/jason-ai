using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Skills;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The last thing <c>jason update apply</c> does: the skills this installation recorded, redeployed at the tag of the
/// build it has just installed — <c>jason skills update</c> as that build would run it.
/// </summary>
/// <remarks>
/// After an update the role skills stayed at the tag the build before had installed, so <c>jason status</c> said not
/// ready until somebody ran the skills verbs by hand, and every updater met it. The step is the verb's own code and
/// nothing else: the same source, the same pin, the same planner, deployer and record. What it may not do is fail the
/// update — the executable is replaced and the runtime is up by then — so a step that could not deploy says so, names
/// the line that finishes it, and leaves the record where it was for <c>jason status</c> to name again.
/// Every source here is a repository this test made, read over <c>file://</c>; nothing reaches a network, and the
/// harness is a home directory of the test's own.
/// </remarks>
public class UpdateSkillsStepTests
{
    private const string Before = "0.11.0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_update_redeploys_both_halves_at_the_new_release_s_tag()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var git = new RecordingRunner(ProgramRunners.ForThisMachine());

        var applier = Applier(installation, git);
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);

        // The tag the clone asks for is the release's own, with one `v` and nothing after its number.
        var clones = git.Requested.Where(arguments => arguments[0] == "clone").ToList();
        Assert.NotEmpty(clones);
        Assert.All(clones, arguments => Assert.True(
            arguments[arguments.IndexOf("--branch") + 1] == Tag(installation),
            $"The step cloned with: git {string.Join(' ', arguments)}"));
        Assert.Contains(TaggedSource.Body(Tag(installation)), await File.ReadAllTextAsync(Path.Combine(installation.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains(TaggedSource.Body(Tag(installation)), await File.ReadAllTextAsync(Path.Combine(Harness(installation), "operating", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains(TaggedSource.Body(Tag(installation)), await File.ReadAllTextAsync(Path.Combine(Harness(installation), "campaign-planning", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.All(Deployments(installation), deployment =>
        {
            Assert.True(deployment.Ref == Tag(installation), $"{deployment.Pack} is recorded at {deployment.Ref} after the update.");
            Assert.False(deployment.RefOverridden);
        });

        // Said as steps of the apply, after the one that says it is complete: one line for each pack the verb carried,
        // in the verb's own words.
        var skills = SkillsLines(applier);
        Assert.Equal("complete", applier.Steps[^(skills.Count + 2)].Split(':')[0]);
        Assert.Equal("ui", applier.Steps[^1].Split(':')[0]);
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        Assert.All(skills, line => Assert.Contains($"at {Tag(installation)}, the ref this build carries; its record named v{Before}.", line, StringComparison.Ordinal));
        Assert.All(skills, line => Assert.Contains("Written:", line, StringComparison.Ordinal));
    }

    /// <summary>
    /// A release that changed none of the skills still moves their records: <c>jason status</c> compares the ref, and a
    /// record left at the tag before would fail it with every file already right.
    /// </summary>
    [Fact]
    public async Task An_update_re_pins_a_record_whose_files_did_not_change()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}");
        await GitAsync(Path.Combine(dir.Paths.Root, "origin"), "tag", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);

        var applier = Applier(installation);
        await applier.ApplyAsync(Request(installation), Ct);

        Assert.All(Deployments(installation), deployment => Assert.True(deployment.Ref == Tag(installation), $"{deployment.Pack} is recorded at {deployment.Ref} after the update."));
        var skills = SkillsLines(applier);
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        Assert.All(skills, line => Assert.Contains("Already current: nothing to write", line, StringComparison.Ordinal));
    }

    /// <summary>
    /// A step that cannot deploy leaves the update complete — the new executable in place, exit 0, one document on stdout —
    /// and says so in one line naming what finishes it. The records stay where they were, so <c>jason status</c> names
    /// the same repair it named before the step existed.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "the skills step never undoes an update")]
    public async Task A_skills_step_that_fails_leaves_the_update_complete_and_status_naming_the_repair()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}");
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var env = Env(installation);

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());

        // One line for each pack group, each saying it was not updated, why, and what finishes it.
        var lines = answer.Steps.Where(step => step.StartsWith("skills:", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count == 2, $"The skills step said: {string.Join(" | ", lines)}");
        Assert.All(lines, line =>
        {
            Assert.Contains(" not updated — ", line, StringComparison.Ordinal);
            Assert.Contains($"could not be fetched at '{Tag(installation)}'", line, StringComparison.Ordinal);
            Assert.EndsWith("; run: jason skills update", line, StringComparison.Ordinal);
            Assert.DoesNotContain('\n', line);
        });
        Assert.Contains($"could not be fetched at '{Tag(installation)}'", installation.Error.ToString(), StringComparison.Ordinal);
        Assert.All(Deployments(installation), deployment => Assert.True(deployment.Ref == $"v{Before}", $"{deployment.Pack} moved to {deployment.Ref} in a step that failed."));

        var status = new StringWriter();
        await CliApp.RunAsync(["status"], env with { Out = status, Error = new StringWriter(), BuildVersion = installation.To.ToString() }, Ct);
        var harness = JsonDocument.Parse(status.ToString()).RootElement.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "harness_skills");
        Assert.True(
            harness.GetProperty("fix").GetString() == "jason skills update",
            $"After a failed step the harness check says: {harness}");
    }

    /// <summary>
    /// A machine with no <c>git</c> on its PATH: the runner answers as it does for any program that is not there, the
    /// fetch is refused in the verb's own words, and the update stands.
    /// </summary>
    [Fact]
    public async Task A_step_with_no_git_leaves_the_update_complete()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var env = Env(installation, new FakeProgramRunner(_ => new ProgramResult(-1, string.Empty, "An error occurred trying to start process 'git'. The system cannot find the file specified.", false)));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        var lines = answer.Steps.Where(step => step.StartsWith("skills:", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count == 2, $"The skills step said: {string.Join(" | ", lines)}");
        Assert.All(lines, line =>
        {
            Assert.Contains(" not updated — ", line, StringComparison.Ordinal);
            Assert.Contains("The system cannot find the file specified.", line, StringComparison.Ordinal);
            Assert.EndsWith("; run: jason skills update", line, StringComparison.Ordinal);
        });
        Assert.All(Deployments(installation), deployment => Assert.True(deployment.Ref == $"v{Before}", $"{deployment.Pack} moved to {deployment.Ref} with no git to fetch it."));
    }

    /// <summary>
    /// A record damaged so that a path in it holds a NUL: whatever reading it throws, the step says it could not deploy
    /// and the update stands. Before, that exception went past every name the step caught, and an update that had
    /// completed exited 1 with no document.
    /// </summary>
    [Fact]
    public async Task A_record_naming_a_path_with_a_nul_leaves_the_update_complete()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var record = Path.Combine(installation.Paths.RoleSkillsDirectory, SkillsRecord.FileName);
        var text = await File.ReadAllTextAsync(record, Ct);
        Assert.Contains("\"researcher/SKILL.md\"", text, StringComparison.Ordinal);
        await File.WriteAllTextAsync(record, text.Replace("\"researcher/SKILL.md\"", "\"researcher/SKILL\\u0000.md\"", StringComparison.Ordinal), Ct);

        var exit = await UpdateCommands.ApplyAsync(Env(installation), installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        var line = Assert.Single(answer.Steps, step => step.StartsWith("skills:", StringComparison.Ordinal));
        Assert.StartsWith("skills: not updated — ", line, StringComparison.Ordinal);
        Assert.EndsWith("; run: jason skills update", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// And any exception at all that the deployment did not name. Listing the ones that can happen has missed one twice,
    /// so the step takes everything, and says what it was.
    /// </summary>
    [Fact]
    public async Task A_step_that_meets_an_exception_nobody_named_leaves_the_update_complete()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var env = Env(installation, new FakeProgramRunner(_ => throw new InvalidOperationException("the runner gave up")));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        AssertTheFirstGroupStopped(installation, Assert.Single(answer.Steps, step => step.StartsWith("skills:", StringComparison.Ordinal)), "the runner gave up");
    }

    /// <summary>
    /// A filesystem that refuses the step — where the fetched sources are kept, a file stands in the way — is said in the
    /// step's line, and the update stands.
    /// </summary>
    [Fact]
    public async Task A_step_the_filesystem_refuses_leaves_the_update_complete()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var sources = installation.Paths.SkillsStagedSourcesDirectory;
        Directory.Move(sources, sources + "-aside");
        await File.WriteAllTextAsync(sources, "in the way", Ct);

        var exit = await UpdateCommands.ApplyAsync(Env(installation), installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        var line = Assert.Single(answer.Steps, step => step.StartsWith("skills:", StringComparison.Ordinal));
        Assert.StartsWith($"skills: {SkillPacks.Business} in {Harness(installation)} not updated — ", line, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(sources), line, StringComparison.Ordinal);
        Assert.EndsWith("; run: jason skills update", line, StringComparison.Ordinal);
        Assert.All(Deployments(installation), deployment => Assert.True(deployment.Ref == $"v{Before}", $"{deployment.Pack} moved to {deployment.Ref} in a step the filesystem refused."));
    }

    /// <summary>
    /// An update to a pre-release moves no skill: the tag its version names is not published as a release, which is what
    /// the verb says of any build that is not one, and the records stay where they are.
    /// </summary>
    [Fact]
    public async Task An_update_to_a_pre_release_moves_no_skill()
    {
        using var installation = new FakeInstallation(to: SemanticVersion.Parse("9.9.10-rc.1"));
        using var dir = new TempPaths();

        // The release the pre-release is on its way to is tagged here, so a step that dropped the pre-release and asked
        // for it would find it, and move every record.
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", "v9.9.10");
        await InstallAsync(installation, ["skills", "install", "--source", origin]);

        var applier = Applier(installation);
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.All(Deployments(installation), deployment => Assert.True(deployment.Ref == $"v{Before}", $"{deployment.Pack} moved to {deployment.Ref} on an update to a pre-release."));
        Assert.Contains(TaggedSource.Body($"v{Before}"), await File.ReadAllTextAsync(Path.Combine(installation.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        var skills = SkillsLines(applier);
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        Assert.All(skills, line => Assert.Contains("this build, 9.9.10-rc.1, is not a release", line, StringComparison.Ordinal));
    }

    /// <summary>
    /// A person who stops the apply while the step is fetching stops the step, not the update: by then the new executable
    /// is in place and its runtime is up, and the answer says so — exit 0, one document, the step's line naming what
    /// finishes it.
    /// </summary>
    [Fact]
    public async Task A_cancelled_step_leaves_the_update_complete()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // What the machine's runner does when its caller is cancelled while git runs: the wait is given up, and the
        // cancellation goes on up.
        var env = Env(installation, new FakeProgramRunner(_ =>
        {
            stop.Cancel();
            throw new OperationCanceledException(stop.Token);
        }));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, stop.Token);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        AssertTheFirstGroupStopped(installation, Assert.Single(answer.Steps, step => step.StartsWith("skills:", StringComparison.Ordinal)), "cancelled");
    }

    /// <summary>
    /// A cancellation nobody asked for — a timeout, which .NET raises as a cancellation of its own — is one more thing the
    /// step meets and says, not a stop: the person stopped nothing, the update is complete, and the line says what ended
    /// the step. It used to go past the step, and an update that had completed exited 1 with no document.
    /// </summary>
    [Fact]
    public async Task A_cancellation_nobody_asked_for_leaves_the_update_complete()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var env = Env(installation, new FakeProgramRunner(_ => throw new TaskCanceledException("the fetch timed out")));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        AssertTheFirstGroupStopped(installation, Assert.Single(answer.Steps, step => step.StartsWith("skills:", StringComparison.Ordinal)), "the fetch timed out");
    }

    /// <summary>
    /// A file the person edited in the harness's business pack is kept, and the pack still moves: its line says what was
    /// written and names the file kept, beside the runtime pack's line. An edit used to refuse its whole group, so the
    /// business pack stayed at the tag before for as long as the person kept their edit.
    /// </summary>
    [Fact]
    public async Task A_file_the_person_edited_is_kept_and_its_pack_group_still_moves()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var edited = Path.Combine(Harness(installation), "campaign-planning", "SKILL.md");
        await File.WriteAllTextAsync(edited, "---\nname: campaign-planning\ndescription: mine\n---\n\nmy own words\n", Ct);

        var exit = await UpdateCommands.ApplyAsync(Env(installation), installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        var skills = answer.Steps.Where(step => step.StartsWith("skills:", StringComparison.Ordinal)).ToList();
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        var carried = Assert.Single(skills, line => line.StartsWith("skills: jason-runtime-skills in ", StringComparison.Ordinal));
        Assert.Contains("Written:", carried, StringComparison.Ordinal);
        var business = Assert.Single(skills, line => line.StartsWith("skills: jason-business-skills in ", StringComparison.Ordinal));
        Assert.DoesNotContain(" not updated — ", business, StringComparison.Ordinal);
        Assert.Contains($"Kept 1 file(s) you have edited: {edited}.", business, StringComparison.Ordinal);
        Assert.Contains("my own words", await File.ReadAllTextAsync(edited, Ct), StringComparison.Ordinal);
        Assert.Contains(TaggedSource.Body(Tag(installation)), await File.ReadAllTextAsync(Path.Combine(installation.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        var recorded = Assert.Single(SkillsRecord.Read(Harness(installation))!.Packs, pack => pack.Pack == SkillPacks.Business);
        Assert.Equal(Tag(installation), recorded.Ref);
    }

    /// <summary>
    /// An exception in a later pack group, after an earlier one was redeployed, leaves the earlier group's line in the
    /// document. The records, the status and the exit were right already; the document said one "not updated" for
    /// everything, which is untrue of the group that landed.
    /// </summary>
    [Fact]
    public async Task An_exception_in_a_later_pack_group_keeps_the_line_of_the_group_that_was_redeployed()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);

        // The business group is fetched with two programs (the clone and the commit read) and deployed; the runtime group
        // reads the commit of what is already fetched, and that is the third program asked for.
        var asked = 0;
        var machine = ProgramRunners.ForThisMachine();
        var env = Env(installation, new FakeProgramRunnerOf((program, arguments, directory, timeout, cancellation) =>
            ++asked <= 2
                ? machine.RunAsync(program, arguments, directory, timeout, cancellation)
                : throw new InvalidOperationException("the runner gave up")));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        var skills = answer.Steps.Where(step => step.StartsWith("skills:", StringComparison.Ordinal)).ToList();
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        var landed = Assert.Single(skills, line => line.StartsWith("skills: jason-business-skills in ", StringComparison.Ordinal));
        Assert.Contains("Written:", landed, StringComparison.Ordinal);
        Assert.DoesNotContain("not updated", landed, StringComparison.Ordinal);
        AssertTheGroupThatStopped(installation, Assert.Single(skills, line => line != landed), "the runner gave up");
        var business = Assert.Single(SkillsRecord.Read(Harness(installation))!.Packs, pack => pack.Pack == SkillPacks.Business);
        Assert.True(business.Ref == Tag(installation), $"The business pack stands at {business.Ref}, not the tag it was redeployed at.");
        var runtime = Assert.Single(SkillsRecord.Read(installation.Paths.RoleSkillsDirectory)!.Packs, pack => pack.Pack == SkillPacks.Runtime);
        Assert.True(runtime.Ref == $"v{Before}", $"The runtime pack moved to {runtime.Ref} in a group that threw.");
    }

    /// <summary>
    /// And a cancel in a later pack group is the same: the group that was redeployed keeps its line, and the group the
    /// cancel stopped is named as not updated because it was cancelled.
    /// </summary>
    [Fact]
    public async Task A_cancel_in_a_later_pack_group_keeps_the_line_of_the_group_that_was_redeployed()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);

        // As above: the third program is the runtime group's, and the person cancels while it runs.
        var asked = 0;
        var machine = ProgramRunners.ForThisMachine();
        using var person = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var env = Env(installation, new FakeProgramRunnerOf((program, arguments, directory, timeout, cancellation) =>
        {
            if (++asked <= 2)
            {
                return machine.RunAsync(program, arguments, directory, timeout, cancellation);
            }

            person.Cancel();
            throw new OperationCanceledException(person.Token);
        }));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, person.Token);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        var skills = answer.Steps.Where(step => step.StartsWith("skills:", StringComparison.Ordinal)).ToList();
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        var landed = Assert.Single(skills, line => line.StartsWith($"skills: {SkillPacks.Business} in ", StringComparison.Ordinal));
        Assert.Contains("Written:", landed, StringComparison.Ordinal);
        AssertTheGroupThatStopped(installation, Assert.Single(skills, line => line != landed), "cancelled");
    }

    /// <summary>The line for the later group, the runtime's, when the verb stopped in it: its pack and roots, not updated, and why.</summary>
    private static void AssertTheGroupThatStopped(FakeInstallation installation, string line, string why) =>
        AssertStoppedIn(line, SkillPacks.Runtime, installation.Paths.RoleSkillsDirectory, why);

    /// <summary>And for the first group, the business pack's, which is where a step that throws on its first program stops.</summary>
    private static void AssertTheFirstGroupStopped(FakeInstallation installation, string line, string why) =>
        AssertStoppedIn(line, SkillPacks.Business, Harness(installation), why);

    /// <summary>The line for the group the verb was in when it stopped: that group's pack and roots, not updated, and why.</summary>
    private static void AssertStoppedIn(string line, string pack, string root, string why) =>
        Assert.True(
            line.StartsWith($"skills: {pack} in ", StringComparison.Ordinal)
            && line.Contains(root, StringComparison.Ordinal)
            && line.EndsWith($" not updated — {why}; run: jason skills update", StringComparison.Ordinal),
            $"The line for the group that stopped reads '{line}', which does not name {pack} in '{root}' as not updated because '{why}'.");

    /// <summary>
    /// A step that failed and has nothing to say for itself still gives a line that reads: no reason is said as none given,
    /// rather than as a dash with nothing after it.
    /// </summary>
    [Fact]
    public async Task A_step_that_fails_with_no_reason_does_not_print_a_dash_with_nothing_after_it()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        var env = Env(installation, new FakeProgramRunner(_ => throw new InvalidOperationException(string.Empty)));

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.True(exit == ExitCodes.Success, $"The update exited {exit}: {installation.Out} {installation.Error}");
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        AssertTheFirstGroupStopped(installation, Assert.Single(answer.Steps, step => step.StartsWith("skills:", StringComparison.Ordinal)), "no reason was given");
    }

    /// <summary>
    /// A skill the step could not replace — a launch of that role holding it, the case the new runtime makes likely —
    /// leaves its pack's record at the tag before, so the step's failure is one <c>jason status</c> can still see.
    /// </summary>
    [Fact]
    public async Task A_skill_refused_during_the_step_leaves_its_pack_s_record_where_it_was()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin]);
        await File.WriteAllTextAsync(installation.Paths.SkillsStagingDirectory, "in the way", Ct);

        var applier = Applier(installation);
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        var line = Assert.Single(SkillsLines(applier), candidate => candidate.StartsWith("skills: jason-runtime-skills in ", StringComparison.Ordinal));
        Assert.Contains(" not updated — 'researcher' could not be written", line, StringComparison.Ordinal);
        var roles = Assert.Single(SkillsRecord.Read(installation.Paths.RoleSkillsDirectory)!.Packs);
        Assert.True(roles.Ref == $"v{Before}", $"The role root is recorded at {roles.Ref} while its researcher is the tree before.");
    }

    /// <summary>
    /// The step carries a root only where its record says a deployment was made there, as the verb does: an
    /// installation taught with <c>--roles-only</c> is not written into an agent's configuration by an update.
    /// </summary>
    [Fact]
    public async Task An_update_writes_into_no_root_without_a_record()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--roles-only", "--source", origin]);

        var applier = Applier(installation);
        await applier.ApplyAsync(Request(installation), Ct);

        Assert.False(Directory.Exists(Harness(installation)), $"The update wrote into '{Harness(installation)}', where nothing was ever deployed.");
        var roles = Assert.Single(SkillsRecord.Read(installation.Paths.RoleSkillsDirectory)!.Packs);
        Assert.True(roles.Ref == Tag(installation), $"The role root is recorded at {roles.Ref} after the update.");
    }

    /// <summary>An installation nothing was deployed to is told so, and no source is fetched for it.</summary>
    [Fact]
    public async Task An_installation_with_no_skills_deployed_is_told_so_and_nothing_is_fetched()
    {
        using var installation = new FakeInstallation();
        var programs = new FakeProgramRunner();

        var applier = Applier(installation, programs);
        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(["skills: nothing on this machine was deployed by jason skills, so nothing was redeployed"], SkillsLines(applier));
        Assert.Empty(programs.Requested);
    }

    /// <summary>A ref somebody chose stays chosen through an update, as it does through the verb.</summary>
    [Fact]
    public async Task A_ref_chosen_with_ref_stays_chosen()
    {
        using var installation = new FakeInstallation();
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, $"v{Before}", Tag(installation));
        await InstallAsync(installation, ["skills", "install", "--source", origin, "--ref", $"v{Before}"]);

        var applier = Applier(installation);
        await applier.ApplyAsync(Request(installation), Ct);

        Assert.All(Deployments(installation), deployment =>
        {
            Assert.True(deployment.Ref == $"v{Before}", $"{deployment.Pack} moved from its chosen ref to {deployment.Ref}.");
            Assert.True(deployment.RefOverridden);
        });
        var skills = SkillsLines(applier);
        Assert.True(skills.Count == 2, $"The skills step said: {string.Join(" | ", skills)}");
        Assert.All(skills, line => Assert.Contains($"at v{Before}, the ref its record says was chosen over this build's own.", line, StringComparison.Ordinal));
    }

    /// <summary>The tag of the release the fake installation updates to.</summary>
    private static string Tag(FakeInstallation installation) => $"v{installation.To}";

    private static List<string> SkillsLines(UpdateApplier applier) =>
        [.. applier.Steps.Where(step => step.StartsWith("skills:", StringComparison.Ordinal))];

    /// <summary>The home directory the installation's agent harness lives in: one of the test's own.</summary>
    private static string Home(FakeInstallation installation) => Path.Combine(installation.Paths.Root, "home");

    private static string Harness(FakeInstallation installation) => Path.Combine(Home(installation), ".claude", "skills");

    private static IEnumerable<SkillsDeployment> Deployments(FakeInstallation installation) =>
        [.. SkillsRecord.Read(installation.Paths.RoleSkillsDirectory)?.Packs ?? [], .. SkillsRecord.Read(Harness(installation))?.Packs ?? []];

    /// <summary>
    /// The installation's own environment, with a harness to detect in a home directory of the test's own and a runner
    /// that really runs git — against <c>file://</c> repositories only. It is also the machine the executable an update
    /// hands itself to runs on, which is where the skills are redeployed from.
    /// </summary>
    private static CliEnvironment Env(FakeInstallation installation, IProgramRunner? programs = null)
    {
        Directory.CreateDirectory(Path.Combine(Home(installation), ".claude"));
        var env = installation.Env with
        {
            Harnesses = HarnessLocators.ForHome(Home(installation)),
            Programs = programs ?? ProgramRunners.ForThisMachine(),
            Autostart = new RecordingRegistrar(),
            SearchPath = string.Empty,
        };

        installation.Machine = env;
        return env;
    }

    private static UpdateApplier Applier(FakeInstallation installation, IProgramRunner? programs = null) =>
        new(Env(installation, programs), installation.Update, TimeProvider.System, installation.InstallPath);

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);

    /// <summary>A skills verb as the build before ran it, on this installation and its harness.</summary>
    private static async Task InstallAsync(FakeInstallation installation, string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(arguments, Env(installation) with { Out = output, Error = error, BuildVersion = Before }, Ct);
        Assert.True(exit == ExitCodes.Success, $"'{string.Join(' ', arguments)}' at {Before} exited {exit}: {error}");
    }

    /// <summary>A runner whose answer is a function of the call, for a test that lets some programs run and refuses others.</summary>
    private sealed class FakeProgramRunnerOf(Func<string, IReadOnlyList<string>, string?, TimeSpan, CancellationToken, Task<ProgramResult>> answer) : IProgramRunner
    {
        public Task<ProgramResult> RunAsync(string program, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken) =>
            answer(program, arguments, workingDirectory, timeout, cancellationToken);
    }

    /// <summary>The machine's runner, with every program it was asked to run kept for the test to read.</summary>
    private sealed class RecordingRunner(IProgramRunner inner) : IProgramRunner
    {
        public List<List<string>> Requested { get; } = [];

        public Task<ProgramResult> RunAsync(string program, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Requested.Add([.. arguments]);
            return inner.RunAsync(program, arguments, workingDirectory, timeout, cancellationToken);
        }
    }

    private static async Task GitAsync(string repository, params string[] arguments)
    {
        var result = await ProgramRunners.ForThisMachine().RunAsync("git", arguments, repository, TimeSpan.FromSeconds(60), Ct);
        Assert.True(result.ExitCode == 0, $"'git {string.Join(' ', arguments)}' failed while building this test's repository: {result.StandardError}");
    }
}
