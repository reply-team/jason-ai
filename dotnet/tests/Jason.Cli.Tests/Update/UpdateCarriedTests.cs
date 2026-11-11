using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// <c>jason update apply</c> and <c>jason update rollback</c> run as a jason the desktop application carries. The file is
/// the application's: replacing it changes a signed application on macOS and a file the installer owns on Windows, and
/// the desktop's own update replaces the application and the jason inside it together. So both verbs refuse, before
/// anything is staged, drained or stopped, and point to the desktop's Check for Updates.
/// </summary>
public sealed class UpdateCarriedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.I2, "the desktop's jason is the desktop's to update")]
    public async Task Apply_refuses_a_carried_executable_before_anything_is_staged_drained_or_stopped()
    {
        using var installation = new FakeInstallation().WithRuntime();
        CarriedRecord.Beside(installation.InstallPath);
        var before = File.ReadAllText(installation.InstallPath);

        var exit = await CliApp.RunAsync(["update", "apply", "--feed", installation.Feed.ToString(), "--drain-seconds", "0"], installation.Env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_carried_by_desktop", refusal.Code);
        Assert.False(refusal.Retryable, "the application carries the same file next time");

        // Nothing staged, nothing fetched, no update recorded, the runtime neither drained nor stopped, and the application's file
        // as it was: the refusal came before anything at all.
        Assert.False(Directory.Exists(installation.Update.Root), "something was written under the data directory's update directory");
        Assert.Null(installation.Ledger());
        Assert.Empty(installation.Fetched);
        Assert.Empty(installation.Operations);
        Assert.Empty(installation.Processes.Runs);
        Assert.True(installation.Running, "the runtime was stopped for an update that was refused");
        Assert.Equal(before, File.ReadAllText(installation.InstallPath));

        Assert.Contains("`jason update apply` would change a file inside the application", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Check for Updates", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(installation.InstallPath, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rollback would put back a file the application never shipped, so it refuses the same way: a record an earlier
    /// update left behind stays as it is, and so does the runtime.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "the desktop's jason is the desktop's to roll back")]
    public async Task Rollback_refuses_a_carried_executable_and_touches_nothing()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Killed(UpdateStep.Complete);
        CarriedRecord.Beside(installation.InstallPath);
        var ledger = File.ReadAllText(installation.Update.Ledger);
        var operations = installation.Operations.Count;
        var before = File.ReadAllText(installation.InstallPath);

        var exit = await CliApp.RunAsync(["update", "rollback"], installation.Env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_carried_by_desktop", refusal.Code);
        Assert.Contains("`jason update rollback` would change a file inside the application", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Check for Updates", refusal.Message, StringComparison.Ordinal);

        Assert.Equal(ledger, File.ReadAllText(installation.Update.Ledger));
        Assert.True(File.Exists(installation.Update.PreviousExecutable), "the executable an earlier update kept was moved");
        Assert.Equal(operations, installation.Operations.Count);
        Assert.True(installation.Running, "the runtime was stopped for a rollback that was refused");
        Assert.Equal(before, File.ReadAllText(installation.InstallPath));
    }

    /// <summary>
    /// A person resuming by hand is refused too: an update in flight that has not reached <c>swapped</c> has replaced
    /// nothing yet, and a carried executable does not begin replacing.
    /// </summary>
    [Fact]
    public async Task An_update_in_flight_before_the_swap_is_not_carried_on_by_a_carried_executable()
    {
        using var installation = new FakeInstallation();
        installation.Killed(UpdateStep.Stopped);
        CarriedRecord.Beside(installation.InstallPath);
        var ledger = File.ReadAllText(installation.Update.Ledger);
        var before = File.ReadAllText(installation.InstallPath);

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("update_carried_by_desktop", RuntimeVerbs.Envelope(installation.Out.ToString()).Code);
        Assert.Equal(ledger, File.ReadAllText(installation.Update.Ledger));
        Assert.Equal(before, File.ReadAllText(installation.InstallPath));
    }

    /// <summary>
    /// A carried jason built before this refusal, as the candidate 0.34.0-rc.1's is, has none, and its applier takes every
    /// step to <c>swapped</c> in its own process: it downloads, stops the runtime, moves the carried file aside and moves
    /// the new one onto its path, and only then runs <c>jason update apply</c> from that path. No code of the new build
    /// runs before the carried file is replaced, so it cannot refuse in time; what it is handed, it finishes, rather than
    /// leave the runtime stopped and the update half done.
    /// </summary>
    /// <remarks>
    /// That build's applier is this one with no refusal in front of it: the step machine is the same, and the refusal
    /// lives in the verb, so driving the applier itself and then the hand-over plays that build.
    /// </remarks>
    [Fact]
    public async Task A_carried_jason_built_without_the_refusal_replaces_the_file_and_the_new_build_finishes_what_it_is_handed()
    {
        using var installation = new FakeInstallation().WithRuntime();
        CarriedRecord.Beside(installation.InstallPath);
        var feed = installation.Feed.AbsoluteUri;

        // The older build's apply, up to the hand-over.
        var old = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, installation.InstallPath) { HandsOver = true };
        var swapped = await old.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.True(old.HandedOver);
        Assert.Equal(UpdateStep.Swapped, swapped.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Empty(installation.Processes.Runs);

        // The hand-over: `jason update apply` typed into the file now at the carried path, which is the new build.
        var finished = await installation.Processes.RunAsync(installation.Paths, [installation.InstallPath, "update", "apply", "--feed", feed], installation.Error, Ct);

        Assert.Equal(ExitCodes.Success, finished.ExitCode);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(finished.StandardOutput, JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
        Assert.Equal([true], installation.StartsHandedOver);
        Assert.True(installation.Running, "the runtime the older build stopped was not started again");

        // Once that update is complete the file is still carried, and the new build refuses the next one.
        installation.Out.GetStringBuilder().Clear();
        var again = await UpdateCommands.ApplyAsync(installation.Env, feed, null, 0, human: false, Ct);
        Assert.Equal(ExitCodes.ApiError, again);
        Assert.Equal("update_carried_by_desktop", RuntimeVerbs.Envelope(installation.Out.ToString()).Code);
    }

    /// <summary>The check and the update's own record answer as they always have: neither changes a file.</summary>
    [Fact]
    public async Task Check_and_status_answer_as_before_for_a_carried_executable()
    {
        using var installation = new FakeInstallation();
        CarriedRecord.Beside(installation.InstallPath);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--feed", installation.Feed.ToString()], installation.Env, Ct));
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "status"], installation.Env, Ct));
        Assert.DoesNotContain("update_carried_by_desktop", installation.Out.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The same installation without the record is Jason's own, and the same command installs the update.</summary>
    [Fact]
    public async Task Without_the_record_the_same_installation_updates()
    {
        using var installation = new FakeInstallation();

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(installation.To.ToString(), installation.Installed());
    }
}
