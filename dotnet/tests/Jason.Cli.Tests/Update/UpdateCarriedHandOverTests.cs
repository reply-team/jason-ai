using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
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
        var order = new[] { "staged:", "drained:", "stopped:", "kept:", "swapped:", "plugins:", "started:", "healthy:", "complete:", "skills:", "install:", "ui:", "settle:" };
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
    /// A refusal ends the verb with the Desktop's reason and sentence, nothing installed, the runtime it drained claiming work
    /// again, and the record that stood before put back.
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
        desktop.Answer = (id, to) =>
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
        Assert.Contains("the runtime that was drained claims work again", refusal.Message, StringComparison.Ordinal);
        Assert.False(refusal.Retryable);

        Assert.True(installation.Running);
        Assert.Equal(DispatcherState.Running, installation.State);
        Assert.Equal("system.resume", installation.Operations.Last());
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
        desktop.Answer = (id, to) =>
        {
            desktop.Write(id, to, "refused", reason: "window_open", message: "Jason Desktop is open: install from its menu, or quit it and ask again.", window: true);
            return 3;
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_refused_by_desktop", refusal.Code);
        Assert.Contains("(window_open)", refusal.Message, StringComparison.Ordinal);
        Assert.True(refusal.Retryable, "a window closed is a request that can be taken");
        Assert.True(installation.Running);
    }

    /// <summary>A start that ends and leaves no file is no answer: said, with the runtime as it was found.</summary>
    [Fact]
    public async Task A_start_that_writes_no_answer_is_no_answer()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        desktop.Answer = (_, _) => 1;

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
        desktop.Answer = (_, _) => 3;
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

    /// <summary><c>--finish</c> finishes an update past the swap, and with none in flight is a usage error.</summary>
    [Fact]
    public async Task Finish_with_nothing_in_flight_is_a_usage_error()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(desktop.Starts);
        Assert.Null(installation.Ledger());
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
}
