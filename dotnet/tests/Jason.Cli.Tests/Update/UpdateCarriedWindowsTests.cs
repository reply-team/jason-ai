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
/// The carried update on Windows, as <c>docs/release-and-update.md</c> §7 says: the installer refuses while a process runs
/// from the carried path or holds a file in the application's folder, so the carried process stops the runtime, moves its
/// own image out of the folder to the updater's store and goes on running from there, records itself beside the carried
/// file for the installer's look to pass over, and hands the new jason the rest as a child it waits for. Every way it ends
/// where the installer put no new jason at the carried path puts one back first — a row for each — and starts the runtime
/// it stopped again.
/// </summary>
/// <remarks>
/// Nothing here starts a process: the carried file is a file of the test's own, moved and copied as the real image is.
/// </remarks>
public sealed class UpdateCarriedWindowsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.I2, "a carried jason on Windows moves its own image out and answers with one document")]
    public async Task On_Windows_the_carried_jason_moves_its_own_image_out_and_answers_with_one_document()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The machine setup the finish runs writes a Windows Path only on Windows.");
        var (installation, desktop) = FakeDesktop.Carried(system: SetupSystem.Windows);
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);

        var exit = await UpdateCommands.ApplyAsync(installation.Machine!, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        var order = new[] { "staged:", "drained:", "stopped: the runtime is gone", "moved:", "stopped: Jason Desktop asked", "kept:", "swapped:", "plugins:", "started:", "healthy:", "complete:", "install:", "settle:" };
        var at = order.Select(prefix => answer.Steps.ToList().FindIndex(step => step.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        Assert.DoesNotContain(-1, at);
        Assert.Equal(at.Order().ToList(), at);

        // No copy: this process carried the update, and the programs it ran were the application and the new jason.
        Assert.False(File.Exists(Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName)), "a copy was put aside on Windows");
        var runs = installation.Processes.Runs.Select(run => run.Command).ToList();
        Assert.Equal(3, runs.Count);
        Assert.Equal(desktop.Executable, runs[0][0]);
        Assert.Equal([installation.InstallPath, "update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], runs[1]);
        Assert.Equal([desktop.Executable, "--update-settle"], runs[2]);

        // At the application's start the folder had no jason, and the record named this process by its id and its start.
        var (carriedThere, pid) = Assert.Single(desktop.Seen);
        Assert.False(carriedThere, "the carried file was still in the application's folder when the installer was asked for");
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
        {
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{self.Id} {self.StartTime.ToFileTimeUtc()}"), pid);
        }

        // Afterwards: the record taken away, the image in the updater's store, the new jason in place and running.
        Assert.False(File.Exists(Path.Combine(installation.InstallDirectory, CarriedUpdate.PidFileName)));
        var id = runs[0][runs[0].ToList().IndexOf("--request") + 1];
        var moved = Path.Combine(desktop.LocalAppData, "jason-desktop-updater", "jason-carried", id, ReleaseAssets.ExecutableName);
        Assert.Equal(original, File.ReadAllText(moved));
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.True(installation.Running);

        // The application the Desktop kept, copied while the file was out, was given this jason and none of the record.
        var kept = Path.Combine(desktop.Kept, "resources", "bundle");
        Assert.Equal(original, File.ReadAllText(Path.Combine(kept, ReleaseAssets.ExecutableName)));
        Assert.False(File.Exists(Path.Combine(kept, CarriedUpdate.PidFileName)));
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
    }

    /// <summary>
    /// Every outcome where the installer put no new jason at the carried path: the verb puts one back, a copy of its own
    /// image, takes the record away, and starts the runtime it stopped again from the carried path, before it answers.
    /// </summary>
    [Theory]
    [Critical(Family.I2, "every outcome on Windows where nothing was installed puts a jason back")]
    [InlineData("refused_download_failed", UpdateCodes.RefusedByDesktop)]
    [InlineData("refused_runtime_not_stopped", UpdateCodes.RefusedByDesktop)]
    [InlineData("no_answer", UpdateCodes.DesktopUnanswered)]
    [InlineData("desktop_killed", UpdateCodes.DesktopUnanswered)]
    [InlineData("desktop_not_startable", UpdateCodes.DesktopUnanswered)]
    [InlineData("desktop_hangs", UpdateCodes.DesktopUnanswered)]
    [InlineData("window_never_answers", UpdateCodes.DesktopUnanswered)]
    [InlineData("installer_never_ran", UpdateCodes.DesktopNotInstalled)]
    public async Task Every_outcome_without_a_new_jason_puts_one_back_and_starts_the_runtime_again(string outcome, string code)
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        desktop.Answer = outcome switch
        {
            "refused_download_failed" => (id, to) => Refuse(desktop, id, to, "download_failed"),
            "refused_runtime_not_stopped" => (id, to) => Refuse(desktop, id, to, "runtime_not_stopped"),
            "no_answer" => (_, _) => 1,
            "desktop_killed" => (_, _) => -1,
            "desktop_not_startable" => (_, _) => throw new Win32Exception(2, "The system cannot find the file specified."),
            "window_never_answers" => (_, _) => 3,
            "installer_never_ran" => (id, to) =>
            {
                desktop.Keep();
                desktop.Write(id, to, "installing", kept: desktop.Kept);
                return 0;
            },
            _ => null,
        };
        desktop.Hangs = outcome == "desktop_hangs";

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(code, refused.Code);
        Assert.False(Assert.Single(desktop.Seen).CarriedThere, "the image was not out of the folder when the Desktop started");
        AssertPutBack(installation, carried, original);
        Assert.Contains($"was started again from {installation.InstallPath}", refused.Message, StringComparison.Ordinal);
        Assert.Contains(carried.Steps, step => step.StartsWith("put back: a jason at ", StringComparison.Ordinal));
        Assert.Null(installation.Ledger());
    }

    /// <summary>A cancel while the Desktop is waited for puts the jason back before the cancel reaches the caller.</summary>
    [Fact]
    public async Task A_cancel_while_the_desktop_is_waited_for_puts_the_jason_back()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        desktop.Hangs = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var install = carried.InstallAsync(ledger, Request(installation), cancel.Token);
        while (desktop.Seen.Count == 0)
        {
            await Task.Delay(10, Ct);
        }

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);

        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.False(File.Exists(carried.PidFile));
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
    /// A move Windows refuses ends before the Desktop is asked anything, with the carried file where it was, the record
    /// that stood put back, and the runtime this verb stopped started again.
    /// </summary>
    [Fact]
    public async Task A_move_that_is_refused_asks_the_desktop_nothing_and_starts_the_runtime_again()
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
        Assert.Contains("Nothing was asked of Jason Desktop", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Starts);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.False(File.Exists(carried.PidFile), "a record was left for the installer's look");
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// A record for the installer's look that cannot be written ends before the Desktop is asked anything, with the jason put
    /// back at the carried path, and the runtime this verb stopped started again from it.
    /// </summary>
    [Fact]
    public async Task A_record_that_cannot_be_written_puts_the_jason_back_and_asks_the_desktop_nothing()
    {
        var (installation, desktop, carried) = Windows();
        using var _ = installation;
        using var __ = desktop;
        var original = File.ReadAllText(installation.InstallPath);
        Directory.CreateDirectory(carried.PidFile);

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        var refused = await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("Nothing was asked of Jason Desktop", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Starts);
        Assert.Equal(original, File.ReadAllText(installation.InstallPath));
        Assert.True(installation.Running);
        Assert.Equal(installation.InstallPath, installation.Processes.Executables.Last()[0]);
        Assert.Null(installation.Ledger());
    }

    /// <summary>A runtime that will not stop: nothing is moved and nothing asked, and the drained runtime claims work again.</summary>
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
        Assert.Contains("Nothing was asked of Jason Desktop", refused.Message, StringComparison.Ordinal);
        Assert.Contains("the runtime that was drained claims work again", refused.Message, StringComparison.Ordinal);
        Assert.Empty(desktop.Starts);
        Assert.True(File.Exists(installation.InstallPath));
        Assert.Null(carried.Moved);
        Assert.False(File.Exists(carried.PidFile), "a record was left for the installer's look");
        Assert.Equal("system.resume", installation.Operations.Last());
    }

    /// <summary>A store on another volume than the application is refused before anything is moved.</summary>
    [Fact]
    public async Task A_store_on_another_volume_is_refused_before_anything_is_moved()
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
        Assert.Empty(desktop.Starts);
        Assert.True(File.Exists(installation.InstallPath));
        Assert.True(installation.Running);
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
        desktop.Answer = (id, to) => Refuse(desktop, id, to, "check_failed");

        var ledger = await carried.PlanAsync(Request(installation), Ct);
        await Assert.ThrowsAsync<UpdateException>(() => carried.InstallAsync(ledger, Request(installation), Ct));

        Assert.False(Directory.Exists(Path.GetDirectoryName(earlier)), "an earlier request's folder was left in the store");
        Assert.True(File.Exists(carried.Moved));
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
        Assert.Equal(Path.Combine(installation.InstallDirectory, "jason-update.pid"), carried.PidFile);
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
        return 1;
    }

    private static (FakeInstallation Installation, FakeDesktop Desktop, CarriedUpdate Carried) Windows()
    {
        var (installation, desktop) = FakeDesktop.Carried(system: SetupSystem.Windows);
        return (installation, desktop, Handing(installation, id => Path.Combine(installation.Root, "store", id, ReleaseAssets.ExecutableName)));
    }

    private static CarriedUpdate Handing(FakeInstallation installation, Func<string, string> movedTo) =>
        new(installation.Machine!, installation.Update, TimeProvider.System, CarriedExecutable.Of(installation.InstallPath)!, SetupSystem.Windows)
        {
            MovesItself = true,
            MovedImageFor = movedTo,
            WarmUp = () => { },
            InstallWait = TimeSpan.FromMilliseconds(200),
            DesktopWait = TimeSpan.FromMilliseconds(300),
            WindowAnswers = TimeSpan.FromMilliseconds(100),
            WindowWait = TimeSpan.FromMilliseconds(300),
            Poll = TimeSpan.FromMilliseconds(10),
        };

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);
}
