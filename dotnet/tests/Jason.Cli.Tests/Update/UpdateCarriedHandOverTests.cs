using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// <c>jason update apply</c> run as the jason Jason Desktop carries: the update handed to the Desktop's updater with no
/// window, as <c>docs/release-and-update.md</c> §7, "Handing the update to Jason Desktop", says, on macOS: the carried jason
/// plans, copies itself aside and hands the rest to that copy; the copy drains the runtime, starts the application with the
/// request, reads its answer and waits for the application's <c>version.json</c>; the new carried jason finishes. A refusal,
/// no answer, or an install that does not land leaves the runtime as it was found. Windows, where the carried process moves
/// its own image out of the folder instead, is <see cref="UpdateCarriedWindowsTests"/>.
/// </summary>
public sealed class UpdateCarriedHandOverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.I2, "a carried jason updates with no window, through the Desktop's updater")]
    public async Task A_carried_jason_hands_the_update_to_Jason_Desktop_and_the_new_one_finishes_it()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        var env = installation.Machine!;

        var exit = await UpdateCommands.ApplyAsync(env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(installation.To.ToString(), answer.To);

        // One document, every part's lines in order: the carried jason's plan, the copy's drain, request, keep and swap, and
        // the new jason's finish, the machine setup and the settling among them.
        var order = new[] { "staged:", "asked:", "drained:", "stopped:", "kept:", "swapped:", "plugins:", "started:", "healthy:", "complete:", "skills:", "install:", "ui:", "settle:" };
        var at = order.Select(prefix => answer.Steps.ToList().FindIndex(step => step.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        Assert.DoesNotContain(-1, at);
        Assert.Equal(at.Order().ToList(), at);
        Assert.Contains(answer.Steps, step => step == "settle: Jason Desktop recorded the update as kept");

        // The parts, as programs: the copy under the data directory, the application with the request, the new carried jason
        // with --finish, and the application once more to settle its record.
        var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
        Assert.True(File.Exists(copy), "no copy of the carried jason was put aside to continue from");
        var runs = installation.Processes.Runs.Select(run => run.Command).ToList();
        Assert.Equal(4, runs.Count);
        Assert.Equal(copy, runs[0][0]);
        Assert.Equal(["update", "apply", "--feed", installation.Feed.AbsoluteUri, "--drain-seconds", "0"], runs[0].Skip(1));
        Assert.Equal(desktop.Executable, runs[1][0]);
        Assert.Equal("--update-apply", runs[1][1]);
        Assert.Equal(["--to", installation.To.ToString()], runs[1].Skip(2).Take(2));
        Assert.Equal("--request", runs[1][4]);
        Assert.Matches("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", runs[1][5]);
        Assert.Equal("--await-go", runs[1][6]);
        Assert.DoesNotContain("--feed", runs[1]);
        Assert.Equal([installation.InstallPath, "update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], runs[2]);
        Assert.Equal([desktop.Executable, "--update-settle"], runs[3]);

        // The runtime: drained by the copy, stopped by the Desktop, started by the new jason from the application's own file.
        Assert.Contains("system.drain", installation.Operations);
        Assert.True(installation.Running);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Equal([installation.InstallPath], installation.Processes.Executables.Single());

        // One ledger names the update whole, the application the Desktop kept among it.
        var ledger = installation.Ledger()!;
        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.InstallPath, ledger.InstallPath);
        Assert.Equal(desktop.Kept, ledger.KeptApplication);
        Assert.False(File.Exists(Path.Combine(installation.Update.Root, "ledger.before-desktop.json")), "the record kept for a refusal was left behind");

        // The machine setup ran as `jason install` runs it, with no --roles: the terminal command placed in the test's home.
        Assert.True(File.Exists(Path.Combine(desktop.Home, ".local", "bin", "jason")), "the machine setup did not place the terminal command");
    }

    /// <summary>A feed on the loopback address goes to the Desktop as the folder its own update file is read from.</summary>
    [Fact]
    public async Task A_loopback_feed_is_handed_to_the_desktop_as_its_folder()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        installation.ServedFromADirectory();

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var start = desktop.Starts.First();
        Assert.Equal("http://127.0.0.1:8099/", start[start.ToList().IndexOf("--feed") + 1]);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8099/manifest.json", "http://127.0.0.1:8099/")]
    [InlineData("http://localhost:7000/feed/manifest.json", "http://localhost:7000/feed/")]
    [InlineData("https://get.jasonharness.ai/releases/latest/download/manifest.json", null)]
    [InlineData("http://example.com/manifest.json", null)]
    [InlineData("http://127.0.0.1:8099/manifest.json?x=1", null)]
    public void The_desktop_takes_a_feed_only_on_the_loopback_address(string feed, string? folder)
    {
        Assert.Equal(folder, CarriedUpdate.DesktopFeed(new Uri(feed)));
    }

    [Fact]
    public void The_application_is_found_beside_the_jason_it_carries_on_both_systems()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jason-layout"));
        var windows = Path.Combine(root, "jason-desktop", "resources", "bundle", "jason.exe");
        var mac = Path.Combine(root, "Jason.app", "Contents", "Resources", "bundle", "jason");

        Assert.Equal(Path.Combine(root, "jason-desktop", "Jason.exe"), CarriedUpdate.ApplicationOf(windows, SetupSystem.Windows));
        Assert.Equal(Path.Combine(root, "jason-desktop"), CarriedUpdate.ApplicationFolderOf(windows, SetupSystem.Windows));
        Assert.Equal(Path.Combine(root, "Jason.app", "Contents", "MacOS", "Jason"), CarriedUpdate.ApplicationOf(mac, SetupSystem.MacOs));
        Assert.Equal(Path.Combine(root, "Jason.app"), CarriedUpdate.ApplicationFolderOf(mac, SetupSystem.MacOs));
    }

    /// <summary>
    /// A refusal before the download ends the verb with the Desktop's reason and sentence: nothing was drained, stopped or
    /// installed, the runtime was never touched, and the record that stood before is put back.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "a refused hand-over leaves the runtime and the record as they were")]
    public async Task The_desktops_refusal_ends_with_its_reason_and_the_runtime_as_it_was_found()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        installation.Killed(UpdateStep.Complete);
        installation.WithRuntime();
        desktop.Becomes(installation.From);
        var before = File.ReadAllText(installation.Update.Ledger);
        var found = installation.State;
        desktop.Before = (id, to) =>
        {
            desktop.Write(id, to, "refused", reason: "version_mismatch", message: "The releases offer 9.9.8, not 9.9.9.", offered: "9.9.8");
            return 0;
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_refused_by_desktop", refusal.Code);
        Assert.Contains("(version_mismatch): The releases offer 9.9.8, not 9.9.9.", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Its feed offered 9.9.8.", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was drained, stopped or installed, and the runtime was not touched.", refusal.Message, StringComparison.Ordinal);
        Assert.False(refusal.Retryable);

        Assert.True(installation.Running);
        Assert.Equal(found, installation.State);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.DoesNotContain("system.resume", installation.Operations);
        Assert.Empty(installation.Processes.Launches);
        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(before, File.ReadAllText(installation.Update.Ledger));
    }

    /// <summary>A refusal after the Desktop stopped the runtime it carries: the verb starts it again, from the carried file.</summary>
    [Fact]
    public async Task A_refusal_after_the_desktop_stopped_the_runtime_starts_it_again()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.Answer = (id, to) =>
        {
            var stopped = desktop.StopRuntime();
            desktop.Write(id, to, "refused", reason: "keep_failed", message: "The copy could not be kept.", runtimeStopped: stopped);
            return 0;
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_refused_by_desktop", refusal.Code);
        Assert.Contains($"was started again from {installation.InstallPath}", refusal.Message, StringComparison.Ordinal);
        Assert.True(installation.Running);
        Assert.Equal([installation.InstallPath], installation.Processes.Executables.Single());

        // Nothing was recorded before this update, so nothing stands after it.
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A running window takes the request, and the start exits 3: the answer comes in the window's own time, and a refusal
    /// there that a person can clear by closing it is worth trying again.
    /// </summary>
    [Fact]
    public async Task A_window_that_took_the_request_answers_in_its_own_time()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.TakenByAWindow = true;
        desktop.Before = (id, to) =>
        {
            desktop.Write(id, to, "refused", reason: "window_open", message: "Jason Desktop is open: install from its menu, or quit it and ask again.", window: true);
            return 0;
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_refused_by_desktop", refusal.Code);
        Assert.Contains("(window_open)", refusal.Message, StringComparison.Ordinal);
        Assert.True(refusal.Retryable, "a window closed is a request that can be taken");
        Assert.True(installation.Running);
        Assert.DoesNotContain("system.drain", installation.Operations);
    }

    /// <summary>A start that ends and leaves no file is no answer: said, with the runtime as it was found.</summary>
    [Fact]
    public async Task A_start_that_writes_no_answer_is_no_answer()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.Before = (_, _) => 1;

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_desktop_unanswered", refusal.Code);
        Assert.Contains("its start ended with exit code 1", refusal.Message, StringComparison.Ordinal);
        Assert.True(refusal.Retryable);
        Assert.True(installation.Running);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A file of the request's id that was there before the request is no answer to it, and the request is not even made.
    /// </summary>
    [Fact]
    public async Task A_request_file_already_there_is_no_answer()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.Write("cli-taken", installation.To.ToString(), "installing", kept: desktop.Kept);
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.MacOs)
        {
            NewRequest = () => "cli-taken",
            Poll = TimeSpan.FromMilliseconds(10),
        };

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.DesktopUnanswered, refused.Code);
        Assert.Contains("was there before the request was made", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Starts);
        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.True(installation.Running);
    }

    /// <summary>
    /// A window that takes the request and fails before it serves it never answers; the wait for it is bounded, and says so.
    /// </summary>
    [Fact]
    public async Task A_window_that_never_answers_is_waited_for_within_a_bound()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.TakenByAWindow = true;
        desktop.Before = (_, _) => 0;
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.MacOs)
        {
            WindowAnswers = TimeSpan.FromMilliseconds(100),
            WindowWait = TimeSpan.FromMilliseconds(300),
            Poll = TimeSpan.FromMilliseconds(10),
        };

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.DesktopUnanswered, refused.Code);
        Assert.Contains("a running Jason Desktop took the request", refused.Message, StringComparison.Ordinal);
        Assert.Contains("a window that failed before it served the request never names one", refused.Message, StringComparison.Ordinal);
        Assert.True(installation.Running);
        Assert.Equal(DispatcherState.Running, installation.State);
    }

    /// <summary>
    /// The installer was handed the update and the application never became the new version: the verb waits its bound, says
    /// so, puts back the record, and starts the runtime the Desktop stopped from the application, which is still whole.
    /// </summary>
    [Fact]
    public async Task An_install_that_never_lands_is_said_and_the_runtime_comes_back()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.Answer = (id, to) =>
        {
            var stopped = desktop.StopRuntime();
            desktop.Write(id, to, "installing", runtimeStopped: stopped, kept: desktop.Kept);
            return 0;
        };
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.MacOs)
        {
            InstallWait = TimeSpan.FromMilliseconds(200),
            Poll = TimeSpan.FromMilliseconds(10),
        };

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.DesktopNotInstalled, refused.Code);
        Assert.Contains($"names {installation.From}", refused.Message, StringComparison.Ordinal);
        Assert.Contains($"was started again from {installation.InstallPath}", refused.Message, StringComparison.Ordinal);
        Assert.True(installation.Running);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// The copy the carried jason handed the rest to ends with no answer, after the Desktop stopped the runtime: the carried
    /// jason, which asked for that stop, starts it again from the application's file, and says where the update stands.
    /// </summary>
    [Fact]
    public async Task A_copy_that_ends_with_no_answer_starts_the_runtime_again()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
        installation.OnHandOver = command =>
        {
            if (command[0] == copy)
            {
                desktop.StopRuntime();
                throw new IOException("the copy could not be run");
            }
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.NotHealthy, refusal.Code);
        Assert.Contains($"was handed to the copy of this jason at {copy}", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'staged'", refusal.Message, StringComparison.Ordinal);
        Assert.True(installation.Running, "the runtime the update had stopped was not started again");
        Assert.Equal([installation.InstallPath], installation.Processes.Executables.Single());
    }

    /// <summary>
    /// The new carried jason ends with no answer after the Desktop installed it: the runtime the Desktop stopped is started
    /// again from the application's file, under the update's mark, and the message names the step and the way on.
    /// </summary>
    [Fact]
    public async Task A_finish_that_ends_with_no_answer_starts_the_runtime_again()
    {
        var (installation, _) = FakeDesktop.Carried();
        using var __ = installation;
        installation.OnHandOver = command =>
        {
            if (command.Contains("--finish"))
            {
                throw new IOException("the new jason could not be run");
            }
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.NotHealthy, refusal.Code);
        Assert.Contains("'swapped'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Run `jason update apply` to finish it", refusal.Message, StringComparison.Ordinal);
        Assert.True(installation.Running);
        Assert.Equal([installation.InstallPath], installation.Processes.Executables.Single());
        Assert.Equal("swapped", installation.Processes.UpdateLaunches.Single());
        Assert.Equal(installation.To.ToString(), installation.Installed());
    }

    /// <summary>
    /// An update stopped before the swap whose application is the new version already, updated from its window say, is
    /// finished by the new carried jason itself: nothing is asked of the Desktop but to settle.
    /// </summary>
    [Fact]
    public async Task An_update_whose_application_is_already_new_is_finished_without_a_request()
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false);
        using var _ = installation;
        new UpdateLedger(SemanticVersion.Current, installation.To, UpdateStep.Stopped, FakeInstallation.Interrupted, installation.InstallPath, installation.Update.StagedExecutable(installation.To), installation.Update.PreviousExecutable)
            .Write(installation.Update.Ledger);
        desktop.Becomes(installation.To);

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
        Assert.Equal([[desktop.Executable, "--update-settle"]], desktop.Starts);
        Assert.True(installation.Running);
    }

    /// <summary>
    /// A rollback of an update a carried jason handed to the Desktop puts back what the command line kept — the plugin
    /// packages and, where nothing has been recorded since, the database — starts nothing, since the jason that would start
    /// it is still the newer one, and names the one step by hand that puts the application back.
    /// </summary>
    [Fact]
    [Critical(Family.P2, "a carried rollback restores the database and names the application by hand")]
    public async Task A_carried_rollback_puts_back_the_database_and_names_the_application_by_hand()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        installation.Migrates = true;
        installation.WriteDatabase("the database as the old version left it");
        Assert.Equal(ExitCodes.Success, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));
        installation.WriteDatabase("migrated by the new version, nothing done since");
        installation.Out.GetStringBuilder().Clear();

        var exit = await CliApp.RunAsync(["update", "rollback"], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal(FakeInstallation.BackupContent, File.ReadAllText(installation.Paths.DatabaseFile));
        Assert.False(installation.Running, "a runtime was started from the newer application on the database put back");
        Assert.Equal(installation.To.ToString(), installation.Installed());
        var byHand = Assert.Single(answer.Steps, step => step.Contains("by hand", StringComparison.Ordinal));
        Assert.Contains(desktop.Folder, byHand, StringComparison.Ordinal);
        Assert.Contains(desktop.Kept, byHand, StringComparison.Ordinal);
        Assert.Contains("`jason runtime start`", byHand, StringComparison.Ordinal);
    }

    /// <summary>
    /// A carried rollback of a record that stands before kept puts back nothing, since nothing was put aside yet: where the
    /// application is still the version the update left it refuses with nothing_to_roll_back, where the installer already
    /// landed the new one with update_in_progress naming the finish; the record is left as it stood either way.
    /// </summary>
    [Theory]
    [Critical(Family.P2, "a carried rollback before kept is refused")]
    [InlineData(false, UpdateCodes.NothingToRollBack)]
    [InlineData(true, UpdateCodes.InProgress)]
    public async Task A_carried_rollback_of_a_record_before_kept_is_refused(bool landed, string code)
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        installation.Killed(UpdateStep.Stopped);
        if (landed)
        {
            desktop.Names(installation.To);
        }

        var before = File.ReadAllText(installation.Update.Ledger);

        var exit = await CliApp.RunAsync(["update", "rollback"], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(code, refusal.Code);
        Assert.Contains("stands at 'stopped'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("`jason update apply`", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(installation.Update.Ledger));
        Assert.Empty(desktop.Starts);
    }

    /// <summary>A carried jason does not roll back an update whose record names another jason's file.</summary>
    [Fact]
    public async Task A_carried_rollback_of_another_jasons_record_is_refused()
    {
        var (installation, _) = FakeDesktop.Carried();
        using var __ = installation;
        var other = Path.Combine(installation.Root, "bin", ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "jason 0.48.2");
        installation.Killed(UpdateStep.Complete);
        var ledger = installation.Ledger()! with { InstallPath = other };
        ledger.Write(installation.Update.Ledger);
        var before = File.ReadAllText(installation.Update.Ledger);

        var exit = await CliApp.RunAsync(["update", "rollback"], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_carried_by_desktop", refusal.Code);
        Assert.Contains($"`{other} update rollback`", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(installation.Update.Ledger));
    }

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);

    /// <summary>
    /// A cancel while the copy carries the update, the copy ended with it past its hold, read by the carried process that
    /// waited on it by the rule the carried process keeps on Windows. Before the go nothing was installed: the record that
    /// stood is put back and the runtime as it was found, resumed where it answers drained, launched without a wait where it
    /// is gone. With the go given, or the installer handed the update (<c>kept</c>), and the application not yet the new
    /// version, the application is the installer's: nothing is put back and no runtime is started, the old build under the
    /// installer least of all, and the answer names the finish; once the install lands, `jason update apply` finishes it and
    /// starts the runtime from the new build. A Desktop that refused after the go installed nothing, and is put back from.
    /// </summary>
    [Theory]
    [Critical(Family.I2, "a cancel on macOS puts back only what the installer does not have")]
    [InlineData("before_the_go_drained")]
    [InlineData("before_the_go_gone")]
    [InlineData("after_the_go")]
    [InlineData("at_kept")]
    [InlineData("refused_after_the_go")]
    public async Task A_cancel_while_the_copy_carries_the_update_puts_back_only_what_the_installer_does_not_have(string where)
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
        const string Id = "cli-cancelled";
        var go = Path.Combine(installation.Update.Root, $"desktop-{Id}.go");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        installation.OnHandOver = command =>
        {
            if (command[0] != copy)
            {
                return;
            }

            // What the copy left when it was ended: its request recorded, the Desktop's answer, the go where it was given,
            // the ledger at the step it reached, and the runtime drained or stopped.
            File.WriteAllText(installation.Update.DesktopRequest, Id);
            var to = installation.To.ToString();
            var ledger = installation.Ledger()!;
            switch (where)
            {
                case "before_the_go_drained":
                    ledger.At(UpdateStep.Drained).Write(installation.Update.Ledger);
                    desktop.Write(Id, to, "downloaded", offered: to);
                    break;
                case "before_the_go_gone":
                    ledger.At(UpdateStep.Stopped).Write(installation.Update.Ledger);
                    desktop.Write(Id, to, "downloaded", offered: to);
                    desktop.StopRuntime();
                    break;
                case "after_the_go":
                    ledger.At(UpdateStep.Stopped).Write(installation.Update.Ledger);
                    desktop.Write(Id, to, "downloaded", offered: to);
                    File.WriteAllText(go, string.Empty);
                    desktop.StopRuntime();
                    break;
                case "at_kept":
                    ledger.At(UpdateStep.Kept).Write(installation.Update.Ledger);
                    desktop.Keep();
                    desktop.Write(Id, to, "installing", runtimeStopped: desktop.StopRuntime(), kept: desktop.Kept);
                    break;
                default:
                    ledger.At(UpdateStep.Stopped).Write(installation.Update.Ledger);
                    File.WriteAllText(go, string.Empty);
                    desktop.Write(Id, to, "refused", reason: "keep_failed", message: "The application could not be kept.", runtimeStopped: desktop.StopRuntime());
                    break;
            }

            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, cancel.Token);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.Cancelled, refusal.Code);
        if (where is "after_the_go" or "at_kept")
        {
            Assert.Contains("no runtime was started under it: the runtime stays stopped until the update is finished", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("`jason update apply` finishes the update and starts the runtime", refusal.Message, StringComparison.Ordinal);
            Assert.False(installation.Running);
            Assert.Empty(installation.Processes.Launches);
            Assert.Equal(where == "at_kept" ? UpdateStep.Kept : UpdateStep.Stopped, installation.Ledger()!.Step);

            // The installer lands the update, and the command the answer named finishes it and starts the runtime from the
            // new build.
            desktop.Becomes(installation.To);
            installation.Out.GetStringBuilder().Clear();
            Assert.Equal(ExitCodes.Success, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));
            Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
            Assert.True(installation.Running);
            Assert.Equal([installation.InstallPath], installation.Processes.Executables.Single());
            return;
        }

        Assert.Null(installation.Ledger());
        Assert.False(File.Exists(installation.Update.DesktopRequest), "the request was left recorded");
        Assert.False(File.Exists(go), "a go was left for a Desktop that refused");
        Assert.True(installation.Running);
        if (where == "before_the_go_drained")
        {
            Assert.Equal("system.resume", installation.Operations.Last());
            Assert.Empty(installation.Processes.Launches);
        }
        else
        {
            Assert.Contains("and not waited for", refusal.Message, StringComparison.Ordinal);
            Assert.Equal([installation.InstallPath], installation.Processes.Executables.Single());
        }
    }

    /// <summary>
    /// An apply typed right after a cancel while the installer has the update - the go given, or the record at <c>kept</c> -
    /// and before the application is the new version: Jason Desktop is not asked again and nothing is touched, so the record
    /// of the install as it lands stays, and the answer names the command that finishes it. Once the install lands, that
    /// command finishes it.
    /// </summary>
    [Theory]
    [InlineData("after_the_go")]
    [InlineData("at_kept")]
    public async Task An_apply_after_a_cancel_while_the_installer_has_the_update_asks_nothing_and_keeps_the_record(string where)
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
        const string Id = "cli-cancelled";
        var go = Path.Combine(installation.Update.Root, $"desktop-{Id}.go");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        installation.OnHandOver = command =>
        {
            if (command[0] != copy)
            {
                return;
            }

            File.WriteAllText(installation.Update.DesktopRequest, Id);
            var to = installation.To.ToString();
            var ledger = installation.Ledger()!;
            if (where == "at_kept")
            {
                ledger.At(UpdateStep.Kept).Write(installation.Update.Ledger);
                desktop.Keep();
                desktop.Write(Id, to, "installing", runtimeStopped: desktop.StopRuntime(), kept: desktop.Kept);
            }
            else
            {
                ledger.At(UpdateStep.Stopped).Write(installation.Update.Ledger);
                desktop.Write(Id, to, "downloaded", offered: to);
                File.WriteAllText(go, string.Empty);
                desktop.StopRuntime();
            }

            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };

        Assert.Equal(ExitCodes.ApiError, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, cancel.Token));
        Assert.Equal(UpdateCodes.Cancelled, RuntimeVerbs.Envelope(installation.Out.ToString()).Code);
        var record = File.ReadAllText(installation.Update.Ledger);
        var asked = desktop.Requests.Count;

        // The agent's retry, at once.
        installation.OnHandOver = null;
        installation.Out.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.ApiError, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));

        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.InProgress, refusal.Code);
        Assert.Contains("handed to it by an earlier `jason update apply`", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Jason Desktop was not asked again and nothing was changed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(record, File.ReadAllText(installation.Update.Ledger));
        Assert.Equal(asked, desktop.Requests.Count);
        Assert.Equal(Id, File.ReadAllText(installation.Update.DesktopRequest).Trim());
        Assert.False(installation.Running);
        Assert.Empty(installation.Processes.Launches);

        // The installer lands it, and the command the answer named finishes the update.
        desktop.Becomes(installation.To);
        installation.Out.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
        Assert.True(installation.Running);
    }

    /// <summary>
    /// A run that carries on a record an earlier run handed the installer, once that earlier hand-over is older than the
    /// install's wait, asks Jason Desktop again; and where it ends with nothing installed it never puts back the record it did
    /// not begin.
    /// </summary>
    [Fact]
    public async Task A_run_that_carries_on_an_earlier_hand_over_never_puts_its_record_back()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
        const string Id = "cli-earlier";
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        installation.OnHandOver = command =>
        {
            if (command[0] != copy)
            {
                return;
            }

            File.WriteAllText(installation.Update.DesktopRequest, Id);
            installation.Ledger()!.At(UpdateStep.Kept).Write(installation.Update.Ledger);
            desktop.Keep();
            desktop.Write(Id, installation.To.ToString(), "installing", runtimeStopped: desktop.StopRuntime(), kept: desktop.Kept);
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };
        Assert.Equal(ExitCodes.ApiError, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, cancel.Token));

        // The earlier hand-over is older than the install's wait: the installer did not land it.
        var old = DateTime.UtcNow - TimeSpan.FromMinutes(11);
        foreach (var file in Directory.EnumerateFiles(installation.Update.Root, "desktop-*"))
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        // Asked again, the Desktop refuses: nothing was installed by this run, and the record it did not begin stays.
        installation.OnHandOver = null;
        desktop.Before = (id, to) =>
        {
            desktop.Write(id, to, "refused", reason: "version_mismatch", message: "The releases offer another version.", offered: "0.0.1");
            return 1;
        };
        installation.Out.GetStringBuilder().Clear();
        var asked = desktop.Requests.Count;
        Assert.Equal(ExitCodes.ApiError, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));
        Assert.Equal(asked + 1, desktop.Requests.Count);
        Assert.NotEqual(UpdateCodes.InProgress, RuntimeVerbs.Envelope(installation.Out.ToString()).Code);
        Assert.Equal(UpdateStep.Kept, installation.Ledger()!.Step);
    }

    /// <summary>
    /// After a cancel the copy is given its hold to run its own way out, since a terminal's Ctrl+C reaches it too: one that
    /// answers within it is passed on as it answered, and the carried process puts back and starts nothing of its own.
    /// </summary>
    [Fact]
    public async Task A_copy_that_answers_within_its_hold_after_a_cancel_is_passed_on()
    {
        var (installation, _) = FakeDesktop.Carried();
        using var __ = installation;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        const string Answer = "{\"error\":{\"code\":\"update_cancelled\",\"message\":\"The copy put everything back.\",\"retryable\":true}}";
        installation.Processes.OnRun = async (_, command, _, token) =>
        {
            await cancel.CancelAsync();
            await Task.Delay(200, CancellationToken.None);
            Assert.False(token.IsCancellationRequested, "the copy was ended before its hold");
            return new FinishedCommand(ExitCodes.ApiError, Answer + "\n");
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, cancel.Token);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("The copy put everything back.", RuntimeVerbs.Envelope(installation.Out.ToString()).Message);
        Assert.Empty(installation.Processes.Launches);
        Assert.Equal(UpdateStep.Staged, installation.Ledger()!.Step);
    }

    /// <summary>A copy that has not ended when its hold has passed is ended then, and the carried process's own way out runs.</summary>
    [Fact]
    public async Task A_copy_that_does_not_end_within_its_hold_is_ended_and_the_way_out_runs()
    {
        var (installation, _) = FakeDesktop.Carried();
        using var __ = installation;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        TimeSpan? ended = null;
        installation.Processes.OnRun = async (_, _, _, token) =>
        {
            await cancel.CancelAsync();
            var since = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                ended = since.Elapsed;
            }

            throw new InvalidOperationException("unreachable");
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, cancel.Token);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(UpdateCodes.Cancelled, RuntimeVerbs.Envelope(installation.Out.ToString()).Code);
        Assert.True(ended >= UpdateCommands.ChildHold - TimeSpan.FromMilliseconds(100), $"the copy was ended after {ended}, before its hold");
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// The lock is never let go between the parts of an update: the carried process keeps it while its copy runs, the copy
    /// while the new jason finishes, and each child goes on under the one nonce it was handed. A second apply, a finishing
    /// step and a rollback beside any part find it held; the nonce goes with the lock.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "the update's lock is never let go between its parts")]
    public async Task The_lock_is_never_let_go_between_the_parts_of_an_update()
    {
        var (installation, _) = FakeDesktop.Carried();
        using var _ = installation;
        var beside = new List<(string Part, string Code)>();
        installation.OnHandOver = command =>
        {
            var part = command.Contains("--finish") ? "finish" : "copy";
            beside.Add((part, Assert.Throws<UpdateException>(() => UpdateLock.Take(installation.Update, "a second update").Dispose()).Code));
            var second = new StringWriter();
            var exit = UpdateCommands.ApplyAsync(installation.Machine! with { Out = second }, installation.Feed.AbsoluteUri, null, 0, human: false, Ct).GetAwaiter().GetResult();
            beside.Add((part + " apply", exit == ExitCodes.ApiError ? RuntimeVerbs.Envelope(second.ToString()).Code : "ran"));
            var rollback = new StringWriter();
            CliApp.RunAsync(["update", "rollback"], installation.Machine! with { Out = rollback }, Ct).GetAwaiter().GetResult();
            beside.Add((part + " rollback", RuntimeVerbs.Envelope(rollback.ToString()).Code));
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
        Assert.All(beside, seen => Assert.Equal(UpdateCodes.InProgress, seen.Code));
        Assert.Equal(["copy", "copy apply", "copy rollback", "finish", "finish apply", "finish rollback"], beside.Select(seen => seen.Part));
        var handed = installation.Processes.RunLocks.Where(nonce => nonce is not null).Distinct().ToList();
        Assert.Single(handed);
        Assert.Equal(2, installation.Processes.RunLocks.Count(nonce => nonce == handed[0]));
        Assert.False(File.Exists(installation.Update.LockHolder), "the nonce outlived the lock");
        using var free = UpdateLock.Take(installation.Update, "the next update");
    }

    /// <summary>
    /// A carried update run elevated - an administrator's elevated terminal, root - is refused before anything is read, staged
    /// or asked, in one sentence that names an ordinary terminal and the Desktop's menu.
    /// </summary>
    [Theory]
    [InlineData(SetupSystem.MacOs, false)]
    [InlineData(SetupSystem.MacOs, true)]
    [InlineData(SetupSystem.Windows, false)]
    public async Task An_elevated_update_is_refused_before_anything_is_begun(SetupSystem system, bool finish)
    {
        var (installation, desktop) = FakeDesktop.Carried(system: system);
        using var _ = installation;
        using var __ = desktop;
        installation.Machine = installation.Machine! with { Setup = installation.Machine.Setup! with { Elevated = true } };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine, installation.Feed.AbsoluteUri, null, 0, human: false, finish, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.Elevated, refusal.Code);
        Assert.Contains(system is SetupSystem.Windows ? "as an administrator" : "as root", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ordinary terminal", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Check for Updates in Jason Desktop's menu", refusal.Message, StringComparison.Ordinal);
        Assert.Null(installation.Ledger());
        Assert.Empty(desktop.Starts);
        Assert.Empty(installation.Operations);
        Assert.Empty(installation.Processes.Runs);
    }

    /// <summary>On macOS an install that did not land carries the end of ShipIt's own log, where ShipIt says why.</summary>
    [Fact]
    public async Task An_install_that_never_lands_on_macOS_carries_the_end_of_ShipIts_log()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        var log = Path.Combine(desktop.Home, "Library", "Caches", "ai.jasonharness.desktop.ShipIt", "ShipIt_stderr.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, "earlier lines\nInstallation error: the code signature of the update did not match\n");
        desktop.Answer = (id, to) =>
        {
            desktop.Write(id, to, "installing", runtimeStopped: desktop.StopRuntime(), kept: desktop.Kept);
            return 0;
        };
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.MacOs)
        {
            InstallWait = TimeSpan.FromMilliseconds(200),
            Poll = TimeSpan.FromMilliseconds(10),
        };

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.DesktopNotInstalled, refused.Code);
        Assert.Contains("ShipIt said: earlier lines Installation error: the code signature of the update did not match", refused.Message, StringComparison.Ordinal);
        Assert.Contains(log, refused.Message, StringComparison.Ordinal);
        Assert.True(installation.Running);
    }

    /// <summary>The application's settling start is waited for within a bound, ended past it, and the line says who records it then.</summary>
    [Fact]
    public async Task A_settle_that_never_ends_is_ended_within_its_bound()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.SettleHangs = true;
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.MacOs)
        {
            SettleWait = TimeSpan.FromMilliseconds(200),
        };

        await carried.SettleAsync(Ct);

        var line = Assert.Single(carried.Steps);
        Assert.StartsWith("settle: Jason Desktop did not record the update as kept within", line, StringComparison.Ordinal);
        Assert.EndsWith("its next Live window's start records it", line, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(2), new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.MacOs).SettleWait);
    }

    /// <summary>
    /// While one part of an update holds its lock, a second apply, a finishing step and a rollback beside it refuse with
    /// update_in_progress and touch nothing.
    /// </summary>
    [Theory]
    [InlineData("apply")]
    [InlineData("finish")]
    [InlineData("rollback")]
    public async Task A_second_update_beside_one_that_holds_the_lock_refuses(string verb)
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        if (verb == "rollback")
        {
            Assert.Equal(ExitCodes.Success, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));
            installation.Out.GetStringBuilder().Clear();
        }

        var before = File.Exists(installation.Update.Ledger) ? File.ReadAllText(installation.Update.Ledger) : null;
        var starts = desktop.Starts.Count;
        using var held = UpdateLock.Take(installation.Update, "a test's update");

        var exit = verb switch
        {
            "apply" => await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct),
            "finish" => await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, finish: true, Ct),
            _ => await CliApp.RunAsync(["update", "rollback"], installation.Machine!, Ct),
        };

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(UpdateCodes.InProgress, RuntimeVerbs.Envelope(installation.Out.ToString()).Code);
        Assert.Equal(before, File.Exists(installation.Update.Ledger) ? File.ReadAllText(installation.Update.Ledger) : null);
        Assert.Equal(starts, desktop.Starts.Count);
    }

    /// <summary>
    /// While <c>jason install</c> holds the lock, an apply, a finishing step and a rollback beside it refuse with
    /// install_in_progress, a sentence of their own that names the setup and no update, and touch nothing.
    /// </summary>
    [Theory]
    [InlineData("apply")]
    [InlineData("finish")]
    [InlineData("rollback")]
    public async Task An_update_beside_a_setup_that_holds_the_lock_refuses_as_beside_a_setup(string verb)
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        if (verb == "rollback")
        {
            Assert.Equal(ExitCodes.Success, await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct));
            installation.Out.GetStringBuilder().Clear();
        }

        var before = File.Exists(installation.Update.Ledger) ? File.ReadAllText(installation.Update.Ledger) : null;
        var starts = desktop.Starts.Count;
        using var held = UpdateLock.Take(installation.Update, "a test's setup", holder: UpdateLock.Holder.Install);

        var exit = verb switch
        {
            "apply" => await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct),
            "finish" => await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, finish: true, Ct),
            _ => await CliApp.RunAsync(["update", "rollback"], installation.Machine!, Ct),
        };

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(CliErrors.InstallInProgress, refusal.Code);
        Assert.StartsWith($"`jason install` is setting this machine up, process {System.Environment.ProcessId}", refusal.Message, StringComparison.Ordinal);
        Assert.True(refusal.Retryable);
        Assert.Equal(before, File.Exists(installation.Update.Ledger) ? File.ReadAllText(installation.Update.Ledger) : null);
        Assert.Equal(starts, desktop.Starts.Count);
    }

    /// <summary>
    /// The lock is the open file, not the file: a lock file and a holder named beside it that a process left when it ended,
    /// by a kill as much as by an end, refuse no update; the next one takes the lock and names itself.
    /// </summary>
    [Theory]
    [InlineData("update 999999 left-behind")]
    [InlineData("install 999999 left-behind")]
    public void A_lock_file_a_dead_process_left_refuses_no_update(string holder)
    {
        var (installation, _) = FakeDesktop.Carried();
        using var _installation = installation;
        Directory.CreateDirectory(installation.Update.Root);
        File.WriteAllText(installation.Update.Lock, string.Empty);
        File.WriteAllText(installation.Update.LockHolder, holder);

        using (var taken = UpdateLock.Take(installation.Update, "the next update"))
        {
            Assert.False(taken.Joined);
            Assert.StartsWith($"update {System.Environment.ProcessId} ", File.ReadAllText(installation.Update.LockHolder), StringComparison.Ordinal);
        }

        Assert.False(File.Exists(installation.Update.LockHolder), "a holder was left named beside a lock no one holds");
    }

    /// <summary>
    /// A console's close cancels the update as Ctrl+C does and is held until the way out has run, a few seconds at most.
    /// </summary>
    [Fact]
    public async Task A_consoles_close_cancels_the_update_and_is_held_until_it_settled()
    {
        using var closing = new UpdateCommands.Closing(Ct);
        var closed = Task.Run(() => closing.Closed(null), Ct);

        await Task.Delay(200, Ct);
        Assert.True(closing.Token.IsCancellationRequested);
        Assert.False(closed.IsCompleted, "the close was let through before the way out ran");
        closing.Settled();
        await closed.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.True(UpdateCommands.Closing.Hold < TimeSpan.FromSeconds(5), "Windows ends a process five seconds after a close");
    }
}
