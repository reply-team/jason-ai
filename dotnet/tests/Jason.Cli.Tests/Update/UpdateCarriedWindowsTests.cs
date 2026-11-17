using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The carried update on Windows, as <c>docs/release-and-update.md</c> §7 says, in the order of the go: the Desktop is asked
/// to download and wait; at <c>downloaded</c> the carried process drains the runtime, stops it, moves its own image out of the
/// application's folder to the updater's store and records itself beside the carried file for the installer's look to pass
/// over; the go is its last act; then it waits for the installer and hands the new jason the rest as a child it waits for.
/// Every way it ends where the installer put no new jason at the carried path puts one back first, and the runtime is as it
/// was found — a row for each way.
/// </summary>
/// <remarks>
/// Nothing here starts a process: the carried file is a file of the test's own, moved and copied as the real image is.
/// </remarks>
public sealed class UpdateCarriedWindowsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <remarks>Only the Windows leg proves this row: it skips on the other systems.</remarks>
    [Fact]
    [Critical(Family.I2, "a carried jason on Windows moves its own image out and answers with one document")]
    public async Task On_Windows_the_carried_jason_moves_its_own_image_out_and_answers_with_one_document()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The machine setup the finish runs writes a Windows Path only on Windows.");
        var (installation, desktop) = FakeDesktop.Carried(system: SetupSystem.Windows);
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);

        // What the store held when the installer had the update: the image, and the version it came from beside it.
        var store = CarriedUpdate.Store(desktop.Places)!;
        (string Image, string Version)? stored = null;
        desktop.Answer = (id, to) =>
        {
            var folder = Path.Combine(store, id);
            stored = (File.ReadAllText(Path.Combine(folder, ReleaseAssets.ExecutableName)), File.ReadAllText(Path.Combine(folder, CarriedUpdate.MovedVersionName)));
            return desktop.Install(id, to);
        };

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        var order = new[] { "staged:", "asked:", "drained:", "stopped: the runtime is gone", "moved:", "stopped: Jason Desktop told to go", "kept:", "swapped:", "plugins:", "started:", "healthy:", "complete:", "install:", "settle:" };
        var at = order.Select(prefix => answer.Steps.ToList().FindIndex(step => step.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        Assert.DoesNotContain(-1, at);
        Assert.Equal(at.Order().ToList(), at);

        // No copy: this process carried the update, and the programs it ran were the application, waiting for the go, and the
        // new jason.
        Assert.False(File.Exists(Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName)), "a copy was put aside on Windows");
        var runs = installation.Processes.Runs.Select(run => run.Command).ToList();
        Assert.Equal(3, runs.Count);
        Assert.Equal(desktop.Executable, runs[0][0]);
        Assert.Equal("--await-go", runs[0][^1]);
        Assert.Equal([installation.InstallPath, "update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], runs[1]);
        Assert.Equal([desktop.Executable, "--update-settle"], runs[2]);

        // At the go the folder had no jason, and the record named this process by its id and its start.
        var (carriedThere, pid) = Assert.Single(desktop.Seen);
        Assert.False(carriedThere, "the carried file was still in the application's folder when the installer was given the go");
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
        {
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{self.Id} {self.StartTime.ToFileTimeUtc()}"), pid);
        }

        // Afterwards: the record and the go taken away, the new jason in place and running. The image was in the updater's
        // store with the version it came from beside it, and the new jason's install step swept it once the update was
        // complete: nothing runs from it here, where on a machine the process still running from it keeps it until a later
        // sweep.
        Assert.False(File.Exists(Path.Combine(installation.InstallDirectory, CarriedUpdate.PidFileName)));
        var id = runs[0][runs[0].ToList().IndexOf("--request") + 1];
        Assert.False(File.Exists(Path.Combine(installation.Update.Root, $"desktop-{id}.go")), "the go was left behind");
        Assert.Equal((original, installation.From.ToString()), stored);
        Assert.False(Directory.Exists(Path.Combine(store, id)), "the store kept an image once the update was complete and nothing ran from it");
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.True(installation.Running);

        // The application the Desktop kept, copied while the file was out, was given this jason and none of the record.
        var kept = Path.Combine(desktop.Kept, "resources", "bundle");
        Assert.Equal(original, File.ReadAllText(Path.Combine(kept, ReleaseAssets.ExecutableName)));
        Assert.False(File.Exists(Path.Combine(kept, CarriedUpdate.PidFileName)));
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
    }

    /// <summary>
    /// Every end before the download: nothing was drained, stopped or moved, the record that stood is put back, and the
    /// runtime is as it was, never even drained.
    /// </summary>
    [Theory]
    [Critical(Family.I2, "every end before the go on Windows leaves the runtime untouched")]
    [InlineData("refused_download_failed", UpdateCodes.RefusedByDesktop)]
    [InlineData("refused_go_there", UpdateCodes.RefusedByDesktop)]
    [InlineData("no_answer", UpdateCodes.DesktopUnanswered)]
    [InlineData("desktop_killed", UpdateCodes.DesktopUnanswered)]
    [InlineData("desktop_not_startable", UpdateCodes.DesktopUnanswered)]
    [InlineData("desktop_hangs", UpdateCodes.DesktopUnanswered)]
    [InlineData("window_never_answers", UpdateCodes.DesktopUnanswered)]
    public async Task Every_end_before_the_download_leaves_everything_as_it_was(string outcome, string code)
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        desktop.Before = outcome switch
        {
            "refused_download_failed" => (id, to) => Refuse(desktop, id, to, "download_failed"),
            "refused_go_there" => (id, to) => Refuse(desktop, id, to, "go_there"),
            "no_answer" => (_, _) => 1,
            "desktop_killed" => (_, _) => -1,
            "desktop_not_startable" => (_, _) => throw new Win32Exception(2, "The system cannot find the file specified."),
            "window_never_answers" => (_, _) => 0,
            _ => null,
        };
        desktop.TakenByAWindow = outcome == "window_never_answers";
        desktop.HangsBeforeDownload = outcome == "desktop_hangs";

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(code, refused.Code);
        Assert.Contains("Nothing was drained, stopped or installed, and the runtime was not touched.", refused.Message, StringComparison.Ordinal);
        Assert.Single(desktop.Requests);
        Assert.Empty(desktop.Seen);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.False(File.Exists(carried.PidFile));
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.True(installation.Running);
        Assert.Empty(installation.Processes.Launches);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// Every end after the go where the installer put no new jason at the carried path: the verb puts one back, a copy of its
    /// own image, takes the record away, and starts the runtime it stopped again from the carried path, before it answers.
    /// </summary>
    [Theory]
    [Critical(Family.I2, "every outcome on Windows where nothing was installed puts a jason back")]
    [InlineData("refused_runtime_not_stopped", UpdateCodes.RefusedByDesktop)]
    [InlineData("refused_keep_failed", UpdateCodes.RefusedByDesktop)]
    [InlineData("killed_after_the_go", UpdateCodes.DesktopUnanswered)]
    [InlineData("installer_never_ran", UpdateCodes.DesktopNotInstalled)]
    public async Task Every_outcome_without_a_new_jason_puts_one_back_and_starts_the_runtime_again(string outcome, string code)
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        desktop.Answer = outcome switch
        {
            "refused_runtime_not_stopped" => (id, to) => Refuse(desktop, id, to, "runtime_not_stopped"),
            "refused_keep_failed" => (id, to) => Refuse(desktop, id, to, "keep_failed"),
            "killed_after_the_go" => (_, _) => -1,
            "installer_never_ran" => (id, to) =>
            {
                desktop.Keep();
                desktop.Write(id, to, "installing", kept: desktop.Kept);
                return 0;
            },
            _ => null,
        };

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(code, refused.Code);
        Assert.False(Assert.Single(desktop.Seen).CarriedThere, "the image was not out of the folder at the go");
        AssertPutBack(installation, carried, original);
        Assert.Contains($"was started again from {installation.InstallPath}", refused.Message, StringComparison.Ordinal);
        Assert.Contains(carried.Steps, step => step.StartsWith("put back: a jason at ", StringComparison.Ordinal));
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A cancel before the download stopped nothing: the Desktop's start is ended, the record that stood put back, and the
    /// runtime never drained. A kill there is as harmless: the Desktop, given no go, installs nothing.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "a cancel before the go on Windows leaves the runtime untouched")]
    public async Task A_cancel_before_the_download_leaves_everything_as_it_was()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        desktop.HangsBeforeDownload = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var install = carried.InstallAsync(ledger, Request(installation), cancel.Token);
        await Until(() => desktop.Requests.Count > 0);
        await cancel.CancelAsync();
        var refused = await Assert.ThrowsAsync<UpdateException>(() => install);

        Assert.Equal(UpdateCodes.Cancelled, refused.Code);
        Assert.Contains("the runtime was not touched", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.True(installation.Running);
        Assert.Null(installation.Ledger());
        Assert.True(File.Exists(installation.InstallPath));
    }

    /// <summary>
    /// The ways out between the download and the go, each a row: the drain's wait cancelled, the verb's own stop cancelled,
    /// the record at <c>stopped</c> that cannot be written, the go's bound passed, a move that copied, and a go that was there
    /// already. Each leaves the carried file where it was or a copy of it, the record that stood, and the runtime
    /// claiming work or started again; the Desktop is given no go and its start is ended.
    /// </summary>
    [Theory]
    [Critical(Family.I2, "every way out between the download and the go on Windows puts everything back")]
    [InlineData("cancel_in_the_drain", UpdateCodes.Cancelled)]
    [InlineData("cancel_in_the_stop", UpdateCodes.Cancelled)]
    [InlineData("stopped_unwritten", UpdateCodes.FileRefused)]
    [InlineData("past_the_go_bound", UpdateCodes.RuntimeUnreachable)]
    [InlineData("move_that_copies", UpdateCodes.FileRefused)]
    [InlineData("go_already_there", UpdateCodes.DesktopUnanswered)]
    public async Task Every_way_out_before_the_go_puts_everything_back(string way, string code)
    {
        var (installation, desktop, _) = Windows();
        using var __ = installation;
        using var ___ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var stoppedWrites = 0;
        var carried = Handing(
            installation,
            id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName),
            goBound: way == "past_the_go_bound" ? TimeSpan.FromMilliseconds(400) : null,
            writes: way == "stopped_unwritten"
                ? ledger =>
                {
                    // The second write of `stopped`, once the runtime is gone.
                    if (ledger.Step == UpdateStep.Stopped && ++stoppedWrites == 2)
                    {
                        throw new IOException("There is not enough space on the disk.");
                    }

                    ledger.Write(installation.Update.Ledger);
                }
                : null,
            mover: way == "move_that_copies" ? (from, to) => File.Copy(from, to) : null);
        var request = Request(installation);
        if (way is "cancel_in_the_drain")
        {
            installation.RunningAttempts = 1;
            installation.DrainEndsTheWork = false;
            request = request with { Drain = TimeSpan.FromMinutes(5) };
        }

        installation.OnStop = way switch
        {
            "cancel_in_the_stop" => () => cancel.Cancel(),
            "past_the_go_bound" => () => Thread.Sleep(600),
            _ => null,
        };
        desktop.PlantsAGo = way == "go_already_there";

        var ledger = await carried.PlanAsync(request, Ct);
        var install = carried.InstallAsync(ledger, request, cancel.Token);
        if (way == "cancel_in_the_drain")
        {
            await Until(() => installation.State == DispatcherState.Draining);
            await cancel.CancelAsync();
        }

        var refused = await Assert.ThrowsAsync<UpdateException>(() => install);

        Assert.Equal(code, refused.Code);
        Assert.Empty(desktop.Seen);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.False(File.Exists(carried.PidFile), "a record was left for the installer's look");
        Assert.True(installation.Running, "the runtime was left stopped");
        Assert.True(installation.State == DispatcherState.Running || installation.Processes.Launches.Count > 0, "the runtime was left drained");
        Assert.Null(installation.Ledger());
        Assert.Contains("Jason Desktop was given no go", refused.Message, StringComparison.Ordinal);
        if (way == "move_that_copies")
        {
            Assert.Contains("on another volume behind the same drive letter", refused.Message, StringComparison.Ordinal);
            Assert.Null(carried.Moved);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(installation.Root, "store"), ReleaseAssets.ExecutableName, SearchOption.AllDirectories));
        }
    }

    /// <summary>
    /// A cancel once the go was given and before the installer has the update: the Desktop's start is ended, the jason put
    /// back, the record that stood put back, and the runtime this verb stopped launched again without a wait, all said in the
    /// answer.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "a cancel on Windows puts back the jason, the record and the runtime")]
    public async Task A_cancel_while_the_desktop_is_waited_for_puts_the_jason_the_record_and_the_runtime_back()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        desktop.Hangs = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var install = carried.InstallAsync(ledger, Request(installation), cancel.Token);
        await Until(() => desktop.Seen.Count > 0);
        await cancel.CancelAsync();
        var refused = await Assert.ThrowsAsync<UpdateException>(() => install);

        Assert.Equal(UpdateCodes.Cancelled, refused.Code);
        Assert.Contains("and not waited for", refused.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.False(File.Exists(carried.PidFile));
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A cancel while the installer has the update: the folder is the installer's, so nothing is copied into it, no record put
    /// back and no runtime started there; the answer names the command that finishes the update once it lands, which starts
    /// the runtime. A kill there leaves the same, and the same command finishes it.
    /// </summary>
    [Fact]
    public async Task A_cancel_while_the_installer_has_the_update_leaves_the_folder_to_it_and_names_the_finish()
    {
        var (installation, desktop, _) = Windows();
        using var __ = installation;
        using var ___ = desktop;
        var carried = Handing(installation, id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName), installWait: TimeSpan.FromMinutes(5));
        desktop.Answer = (id, to) =>
        {
            desktop.Keep();
            desktop.Write(id, to, "installing", kept: desktop.Kept);
            return 0;
        };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var install = carried.InstallAsync(ledger, Request(installation), cancel.Token);
        await Until(() => installation.Ledger()?.Step == UpdateStep.Kept);
        await cancel.CancelAsync();
        var refused = await Assert.ThrowsAsync<UpdateException>(() => install);

        Assert.Equal(UpdateCodes.Cancelled, refused.Code);
        Assert.Contains("`jason update apply` finishes the update and starts the runtime", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(installation.InstallPath), "a jason was copied into the folder the installer has");
        Assert.False(File.Exists(carried.PidFile));
        Assert.Equal(UpdateStep.Kept, installation.Ledger()!.Step);
        Assert.False(installation.Running);
        Assert.Empty(installation.Processes.Launches);
    }

    /// <summary>A record at <c>kept</c> that cannot be written stops nothing: the installer has the update, and it lands.</summary>
    [Fact]
    public async Task A_record_at_kept_that_cannot_be_written_lets_the_install_land()
    {
        var (installation, desktop, _) = Windows();
        using var __ = installation;
        using var ___ = desktop;
        var carried = Handing(installation, id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName), writes: ledger =>
        {
            if (ledger.Step == UpdateStep.Kept)
            {
                throw new IOException("There is not enough space on the disk.");
            }

            ledger.Write(installation.Update.Ledger);
        });

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var swapped = await carried.InstallAsync(ledger, Request(installation), Ct);

        Assert.Equal(UpdateStep.Swapped, swapped.Step);
        Assert.Contains(carried.Steps, step => step.StartsWith("kept: the record of the update could not be written at kept", StringComparison.Ordinal));
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Equal(UpdateStep.Swapped, installation.Ledger()!.Step);
    }

    /// <summary>
    /// A record at <c>swapped</c> that cannot be written once the install landed: the new jason at the carried path is the
    /// application's, the runtime is started from it, the record stays where it was, and the answer says the application is
    /// new and how the update is finished.
    /// </summary>
    [Fact]
    public async Task A_record_at_swapped_that_cannot_be_written_starts_the_runtime_from_the_new_jason()
    {
        var (installation, desktop, _) = Windows();
        using var __ = installation;
        using var ___ = desktop;
        var carried = Handing(installation, id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName), writes: ledger =>
        {
            if (ledger.Step == UpdateStep.Swapped)
            {
                throw new IOException("There is not enough space on the disk.");
            }

            ledger.Write(installation.Update.Ledger);
        });

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains($"The application is {installation.To}", refused.Message, StringComparison.Ordinal);
        Assert.Contains("`jason update apply` finishes the update", refused.Message, StringComparison.Ordinal);
        Assert.Equal($"jason {installation.To}", File.ReadAllText(installation.InstallPath));
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
        Assert.Equal(UpdateStep.Kept, installation.Ledger()!.Step);
    }

    /// <summary>
    /// The installer placed a new jason and the application's record never named the new version: that file is the
    /// installer's and is left as it is, nothing is copied over it, and the runtime is started again from it.
    /// </summary>
    [Fact]
    public async Task A_jason_the_installer_placed_is_never_written_over()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        desktop.Answer = (id, to) =>
        {
            desktop.Keep();
            desktop.Write(id, to, "installing", kept: desktop.Kept);
            File.WriteAllText(installation.InstallPath, "jason half installed");
            return 0;
        };

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.DesktopNotInstalled, refused.Code);
        Assert.Equal("jason half installed", File.ReadAllText(installation.InstallPath));
        Assert.DoesNotContain(carried.Steps, step => step.StartsWith("put back: a jason at ", StringComparison.Ordinal));
        Assert.False(File.Exists(carried.PidFile));
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
    }

    /// <summary>
    /// A move Windows refuses ends before the go, with the carried file where it was, the record that stood put back, and the
    /// runtime this verb stopped started again.
    /// </summary>
    [Fact]
    public async Task A_move_that_is_refused_gives_no_go_and_starts_the_runtime_again()
    {
        var (installation, desktop, _) = Windows();
        using var __ = installation;
        using var ___ = desktop;
        var blocker = Path.Combine(installation.Root, "store-is-a-file");
        File.WriteAllText(blocker, "not a folder");
        var carried = Handing(installation, id => Path.Combine(blocker, id, ReleaseAssets.ExecutableName));
        var original = File.ReadAllText(installation.InstallPath);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("Jason Desktop was given no go", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Seen);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.False(File.Exists(carried.PidFile), "a record was left for the installer's look");
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A record for the installer's look that cannot be written ends before the go, with the jason put back at the carried
    /// path, and the runtime this verb stopped started again from it.
    /// </summary>
    [Fact]
    public async Task A_record_that_cannot_be_written_puts_the_jason_back_and_gives_no_go()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        Directory.CreateDirectory(carried.PidFile);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("Jason Desktop was given no go", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Seen);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A runtime an earlier version's Demo left running on its own folder beside the data directory runs the carried jason
    /// too: the verb asks it to stop after this installation's runtime, and where it is still there gives no go, names the
    /// folder and the way to stop it, and starts this installation's runtime again.
    /// </summary>
    [Fact]
    public async Task A_demo_runtime_that_will_not_stop_gives_no_go_and_is_named()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var demo = Path.TrimEndingDirectorySeparator(installation.Paths.Root) + "-demo";
        var demoPaths = new JasonPaths(demo);
        Directory.CreateDirectory(demoPaths.RunDirectory);
        File.WriteAllText(Path.Combine(demo, Jason.Cli.Uninstall.DesktopRemoval.DemoMarkerName), Jason.Cli.Uninstall.DesktopRemoval.DemoMarkerText);
        const int DemoPid = 48_484;
        File.WriteAllText(
            demoPaths.DescriptorFile,
            JsonSerializer.Serialize(new RuntimeDescriptor("v1", installation.From.ToString(), "demo", DemoPid, "http://127.0.0.1:5001", "the-demo-token", DateTimeOffset.UnixEpoch), JasonJson.Options));
        installation.Processes.RunningPids.Add(DemoPid);
        var original = File.ReadAllText(installation.InstallPath);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.RuntimeUnreachable, refused.Code);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"process {DemoPid}, on {demo}, did not stop"), refused.Message, StringComparison.Ordinal);
        Assert.Contains($"JASON_DATA_DIR set to {demo}", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Jason Desktop was given no go", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Seen);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.True(installation.Running);
        Assert.Null(installation.Ledger());
    }

    /// <summary>A runtime that will not stop: nothing is moved and no go given, and the drained runtime claims work again.</summary>
    [Fact]
    public async Task A_runtime_that_will_not_stop_is_resumed_and_nothing_is_moved()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        installation.RefusesToStop = true;

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.RuntimeUnreachable, refused.Code);
        Assert.Contains("Jason Desktop was given no go", refused.Message, StringComparison.Ordinal);
        Assert.Contains("the runtime that was drained claims work again", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Seen);
        Assert.True(File.Exists(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.False(File.Exists(carried.PidFile), "a record was left for the installer's look");
        Assert.Equal("system.resume", installation.Operations.Last());
    }

    /// <summary>
    /// A store on another volume than the application is refused before the Desktop is asked anything, with nothing drained.
    /// </summary>
    /// <remarks>Only the Windows leg proves this row: it skips on the other systems.</remarks>
    [Fact]
    public async Task A_store_on_another_volume_is_refused_before_anything_is_asked()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Volumes are drive letters only on Windows.");
        var (installation, desktop, _) = Windows();
        using var __ = installation;
        using var ___ = desktop;
        var root = Path.GetPathRoot(Path.GetFullPath(installation.InstallPath))!;
        var other = root.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? @"Y:\" : @"Z:\";
        var carried = Handing(installation, id => Path.Combine(other, "store", id, ReleaseAssets.ExecutableName));

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("is not on the volume of", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was asked of Jason Desktop", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Starts);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.True(File.Exists(installation.InstallPath));
        Assert.True(installation.Running);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// Another process running the carried jason, which the installer would wait for: with no runtime of this installation
    /// running it is refused before the Desktop is asked anything; with one, after the stop, which is started again.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_process_at_the_carried_path_is_refused_by_its_id(bool runtime)
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: runtime, system: SetupSystem.Windows);
        using var _ = installation;
        using var __ = desktop;
        var remover = new Uninstall.RecordingRemover();
        remover.Running[installation.InstallPath] = [4321];
        installation.Machine = installation.Machine! with { Removes = remover };
        var carried = Handing(installation, id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName));

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("Other processes run the jason at", refused.Message, StringComparison.Ordinal);
        Assert.Contains("4321", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Seen);
        Assert.True(File.Exists(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.Null(installation.Ledger());
        if (runtime)
        {
            Assert.True(installation.Running, "the runtime stopped for the move was not started again");
        }
        else
        {
            Assert.Empty(desktop.Starts);
        }
    }

    /// <summary>A store an earlier request left an image in is swept before this request moves its own there.</summary>
    [Fact]
    public async Task An_earlier_requests_image_is_swept_from_the_store()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var earlier = Path.Combine(installation.Root, "store", "cli-earlier", ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(earlier)!);
        File.WriteAllText(earlier, "an image an earlier update left");
        desktop.Answer = (id, to) => Refuse(desktop, id, to, "keep_failed");

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.False(Directory.Exists(Path.GetDirectoryName(earlier)), "an earlier request's folder was left in the store");
        Assert.True(File.Exists(carried.Moved));
    }

    /// <summary>
    /// The finishing step with nothing in flight sweeps the store too, under its lock, before it brings anything up to the
    /// installed version.
    /// </summary>
    [Fact]
    public async Task The_finishing_step_with_nothing_in_flight_sweeps_the_store()
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, system: SetupSystem.Windows, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        var earlier = Path.Combine(CarriedUpdate.Store(desktop.Places)!, "cli-earlier", ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(earlier)!);
        File.WriteAllText(earlier, "an image an earlier update left");

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(Path.GetDirectoryName(earlier)), "the finishing step left an earlier request's folder in the store");
    }

    /// <summary>
    /// The sweep <c>jason install</c> runs takes the store's folders away where no update is in flight, and none while one
    /// stands short of complete: that update's image may be the only one of the application's jason.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(UpdateStep.Complete, true)]
    [InlineData(UpdateStep.Stopped, false)]
    [InlineData(UpdateStep.Swapped, false)]
    public void The_installs_sweep_keeps_the_store_while_an_update_is_in_flight(UpdateStep? step, bool swept)
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, system: SetupSystem.Windows);
        using var _ = installation;
        using var __ = desktop;
        var store = CarriedUpdate.Store(desktop.Places)!;
        var image = Path.Combine(store, "cli-moved", ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllText(image, "the image an update moved out");
        if (step is { } at)
        {
            installation.Killed(at);
        }

        CarriedUpdate.SweepWhereNothingIsInFlight(store, installation.Update);

        Assert.Equal(!swept, File.Exists(image));
        Assert.True(Directory.Exists(store), "the sweep took the store itself away");
    }

    /// <summary>
    /// The record as this jason writes it — its id and its start as .NET reads them, by <see cref="CarriedUpdate.PidRecord"/> —
    /// read by the installer's own look: the runtime filter of <c>desktop/installer/uninstall.nsh</c>, run by PowerShell as
    /// the installer runs it, passes over the process it names and finds it again where no record names it.
    /// </summary>
    /// <remarks>
    /// The process is a copy of the system's command interpreter of the test's own, named as the carried file is and started
    /// with <c> update apply</c> on its command line, waiting on its input until it is ended; it shows no window.
    /// </remarks>
    [Fact]
    public void The_record_this_jason_writes_is_the_one_the_installers_look_passes_over()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The installer's look is Windows' own.");
        using var dir = new TempPaths();
        var image = Path.Combine(dir.Paths.Root, ReleaseAssets.ExecutableName);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), image);
        var record = Path.Combine(dir.Paths.Root, CarriedUpdate.PidFileName);
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "installer", "uninstall.nsh"));
        var filter = System.Text.RegularExpressions.Regex.Match(script, "^!define JASON_OWN_RUNTIME `([^`]+)`$", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value.Replace("$$", "$", StringComparison.Ordinal);
        Assert.NotEmpty(filter);

        using var applying = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(image)
        {
            ArgumentList = { "/d", "/k", "rem", "update", "apply" },
            WorkingDirectory = dir.Paths.Root,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            // First the look finds the process where no record names it, waited for while a machine under load lists it, so
            // that its passing over below is the record's doing and not a process the look could not see.
            var none = Path.Combine(dir.Paths.Root, "no-record.pid");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!Found(none))
            {
                Assert.False(applying.HasExited, $"the process the record names ended with {(applying.HasExited ? $"{applying.ExitCode}: {applying.StandardError.ReadToEnd()}" : string.Empty)}");
                Assert.True(DateTime.UtcNow < deadline, "the installer's look did not find the process with no record");
                Thread.Sleep(250);
            }

            File.WriteAllText(record, CarriedUpdate.PidRecord(applying.Id, applying.StartTime.ToFileTimeUtc()));
            Assert.False(Found(record), "the installer's look found the process the record this jason writes names");
            Assert.True(Found(none), "the installer's look did not find the process with no record");
        }
        finally
        {
            applying.Kill();
            applying.WaitForExit();
        }

        // The installer's own command around the filter, with one difference: a query the system failed to answer, which a
        // machine under load meets, exits 2 and is asked again rather than read as nothing found.
        bool Found(string updating)
        {
            for (var attempt = 0; ; attempt++)
            {
                var exit = Look(updating);
                if (exit != 2 || attempt == 20)
                {
                    Assert.True(exit is 0 or 1, $"PowerShell ended with {exit}");
                    return exit == 0;
                }

                Thread.Sleep(250);
            }
        }

        int Look(string updating)
        {
            var look = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference = 'Stop'; try {{ if (@({filter}).Count -gt 0) {{ exit 0 }} else {{ exit 1 }} }} catch {{ exit 2 }}" },
                WorkingDirectory = dir.Paths.Root,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            look.Environment["JASON_DESKTOP_RUNTIME"] = image;
            look.Environment["JASON_DESKTOP_UPDATING"] = updating;
            using var run = System.Diagnostics.Process.Start(look)!;
            run.StandardOutput.ReadToEnd();
            run.StandardError.ReadToEnd();
            run.WaitForExit();
            return run.ExitCode;
        }
    }

    /// <summary>The move's default place: the updater's store under the local application data, a folder per request.</summary>
    [Fact]
    public void The_image_moves_to_the_updaters_store_a_folder_per_request()
    {
        var (installation, desktop) = FakeDesktop.Carried(system: SetupSystem.Windows);
        using var _ = installation;
        using var __ = desktop;
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.Windows);

        Assert.Equal(
            Path.Combine(desktop.LocalAppData, "jason-desktop-updater", "jason-carried", "cli-0123", ReleaseAssets.ExecutableName),
            carried.MovedImage("cli-0123"));
        Assert.Equal(Path.Combine(desktop.LocalAppData, "jason-desktop-updater", "jason-carried"), CarriedUpdate.Store(desktop.Places));
        Assert.Equal(Path.Combine(installation.InstallDirectory, "jason-update.pid"), carried.PidFile);
        Assert.Equal(Path.Combine(installation.Update.Root, "desktop-cli-0123.go"), carried.GoFile("cli-0123"));
        Assert.Equal("jason.version", CarriedUpdate.MovedVersionName);
    }

    /// <summary>
    /// The bounds the go is held to, as §7 states them: the verb gives it within ten minutes of the download, the Desktop
    /// waits fifteen, so five minutes stand between them, and the Desktop's own source says fifteen; the drain's wait fits
    /// inside the verb's bound, and the verb waits for the Desktop's start longer than its own bound with the go's.
    /// </summary>
    [Fact]
    public void The_go_is_given_within_a_bound_the_desktops_wait_outlasts()
    {
        var (installation, desktop) = FakeDesktop.Carried(system: SetupSystem.Windows);
        using var _ = installation;
        using var __ = desktop;
        var carried = new CarriedUpdate(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.Windows);

        Assert.Equal(TimeSpan.FromMinutes(10), carried.GoBound);
        Assert.Equal(TimeSpan.FromMinutes(15), CarriedUpdate.DesktopGoWait);
        Assert.True(CarriedUpdate.DesktopGoWait - carried.GoBound >= TimeSpan.FromMinutes(5));
        Assert.True(carried.DrainAtMost <= carried.GoBound - TimeSpan.FromMinutes(2));
        Assert.True(carried.DesktopWait >= TimeSpan.FromMinutes(30) + CarriedUpdate.DesktopGoWait + TimeSpan.FromMinutes(5));
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src", "update-request.ts"));
        Assert.Contains("export const goWaitMs = 15 * 60_000;", source, StringComparison.Ordinal);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "what the row waited for never came");
            await Task.Delay(10, Ct);
        }
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "global.json")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository's root was not found above the test's own folder.");
    }

    private static void AssertPutBack(FakeInstallation installation, CarriedUpdate carried, string original)
    {
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.False(File.Exists(carried.PidFile), "the record for the installer's look was left behind");
        Assert.NotNull(carried.Moved);
        Assert.True(installation.Running, "the runtime this verb stopped was not started again");
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
    }

    private static int Refuse(FakeDesktop desktop, string id, string to, string reason)
    {
        desktop.Write(id, to, "refused", reason: reason, message: $"The update was refused: {reason}.");
        return 0;
    }

    private static (FakeInstallation Installation, FakeDesktop Desktop, CarriedUpdate Carried) Windows()
    {
        var (installation, desktop) = FakeDesktop.Carried(system: SetupSystem.Windows);
        return (installation, desktop, Handing(installation, id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName)));
    }

    private static CarriedUpdate Handing(
        FakeInstallation installation,
        Func<string, string> movedTo,
        TimeSpan? goBound = null,
        Action<UpdateLedger>? writes = null,
        Action<string, string>? mover = null,
        TimeSpan? installWait = null) =>
        new(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.Windows)
        {
            MovesItself = true,
            MovedImageFor = movedTo,
            WarmUp = () => { },
            GoBound = goBound ?? TimeSpan.FromMinutes(10),
            Writes = writes,
            Mover = mover ?? ((from, to) => File.Move(from, to)),
            InstallWait = installWait ?? TimeSpan.FromMilliseconds(200),
            DesktopWait = TimeSpan.FromMilliseconds(600),
            WindowAnswers = TimeSpan.FromMilliseconds(100),
            WindowWait = TimeSpan.FromMilliseconds(300),
            Poll = TimeSpan.FromMilliseconds(10),
        };

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);
}
