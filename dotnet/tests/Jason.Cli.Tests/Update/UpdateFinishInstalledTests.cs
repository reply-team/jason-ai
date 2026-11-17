using System.Net;
using System.Text.Json;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// <c>jason update apply --finish</c> on a jason Jason Desktop carries with no update in flight, the finishing step both doors
/// use (<c>docs/release-and-update.md</c> §7, "The finishing step"): what belongs to the installed version — its release's
/// plugin packages by the update's own plan, every skills deployment recorded, the UI — brought up to it, part by part, each
/// said done, already there, or not done with the reason; no runtime started or stopped; nothing made where no data
/// directory is; nothing of a part changed whose release cannot be read.
/// </summary>
public sealed class UpdateFinishInstalledTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.I2, "the finishing step brings what belongs to the installed version up to it, and run again changes nothing")]
    public async Task A_package_behind_is_replaced_the_runtime_reloads_and_a_second_run_finds_every_part_there()
    {
        var (installation, desktop) = FakeDesktop.Carried(to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        var behind = FakeInstallation.Plugin("reply", "0.17.0");
        var released = FakeInstallation.Plugin("reply", "0.18.0");
        installation.WithInstalledPlugin(behind).PublishingPlugin(released, behind);

        var first = await FinishAsync(installation);

        Assert.True(first.DataDirFound);
        Assert.True(first.Complete);
        Assert.Equal(SemanticVersion.Current.ToString(), first.Version);
        Assert.Equal(["plugins", "skills", "ui"], first.Parts.Select(part => part.Part));
        var plugins = first.Parts[0];
        Assert.Equal(UpdateFinishStates.Done, plugins.State);
        Assert.Null(plugins.Reason);
        Assert.Contains("reply 0.17.0 -> 0.18.0", plugins.Lines);
        Assert.Contains("reload: the runtime loaded the packages", plugins.Lines);
        Assert.Equal(released.Digest, installation.PluginDigest("reply"));
        Assert.Equal(UpdateFinishStates.AlreadyThere, first.Parts[1].State);
        Assert.Equal(UpdateFinishStates.Done, first.Parts[2].State);

        // No runtime started or stopped: the one asked of the runtime is the reload a package changed for.
        Assert.Empty(installation.Processes.Launches);
        Assert.True(installation.Running);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.DoesNotContain("system.shutdown", installation.Operations);
        Assert.Single(installation.Operations, operation => operation == "plugin.reload");
        Assert.Empty(desktop.Starts);
        Assert.Null(installation.Ledger());

        installation.Out.GetStringBuilder().Clear();
        var second = await FinishAsync(installation);

        Assert.True(second.Complete);
        Assert.All(second.Parts, part => Assert.Equal(UpdateFinishStates.AlreadyThere, part.State));
        Assert.Contains("reply 0.18.0 is current", second.Parts[0].Lines);
        Assert.Single(installation.Operations, operation => operation == "plugin.reload");
        Assert.Equal(released.Digest, installation.PluginDigest("reply"));
    }

    /// <summary>A package the person edited is left as an update's plugins step leaves it, and nothing is reloaded for it.</summary>
    [Fact]
    public async Task An_edited_package_is_left_as_the_update_leaves_it()
    {
        var (installation, desktop) = FakeDesktop.Carried(to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        var behind = FakeInstallation.Plugin("reply", "0.17.0");
        installation.WithInstalledPlugin(behind).PublishingPlugin(FakeInstallation.Plugin("reply", "0.18.0"), behind);
        File.AppendAllText(Path.Combine(installation.Paths.PluginPackageDirectory("reply"), "main.js"), "// mine\n");
        var edited = installation.PluginDigest("reply");

        var report = await FinishAsync(installation);

        var plugins = report.Parts[0];
        Assert.Equal(UpdateFinishStates.AlreadyThere, plugins.State);
        Assert.Contains("reply differs from every package a release published, so it was left as it is", plugins.Lines);
        Assert.Equal(edited, installation.PluginDigest("reply"));
        Assert.DoesNotContain("plugin.reload", installation.Operations);
    }

    /// <summary>With no runtime up, a package is replaced and none is started: it loads at the runtime's next start.</summary>
    [Fact]
    public async Task With_no_runtime_a_package_is_replaced_and_no_runtime_is_started()
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        var behind = FakeInstallation.Plugin("reply", "0.17.0");
        installation.WithInstalledPlugin(behind).PublishingPlugin(FakeInstallation.Plugin("reply", "0.18.0"), behind);

        var report = await FinishAsync(installation);

        Assert.Equal(UpdateFinishStates.Done, report.Parts[0].State);
        Assert.Contains("reload: no runtime answered, so the packages load at the runtime's next start", report.Parts[0].Lines);
        Assert.Empty(installation.Processes.Launches);
        Assert.False(installation.Running);
    }

    /// <summary>A machine that only ever saw the Demo has no data directory: nothing is done, nothing is made, and it says so.</summary>
    [Fact]
    [Critical(Family.I2, "the finishing step makes nothing where no data directory is")]
    public async Task With_no_data_directory_nothing_is_done_or_made_and_it_says_so()
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        Directory.Delete(installation.Paths.Root, recursive: true);

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = JsonSerializer.Deserialize<UpdateFinishResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.False(report.DataDirFound);
        Assert.False(report.Complete);
        Assert.Equal(installation.Paths.Root, report.DataDir);
        Assert.All(report.Parts, part =>
        {
            Assert.Equal(UpdateFinishStates.NotDone, part.State);
            Assert.Contains("There is no data directory at", part.Reason, StringComparison.Ordinal);
        });
        Assert.False(Directory.Exists(installation.Paths.Root), "the finishing step made the data directory");
        Assert.Empty(installation.Release.Requested);
    }

    /// <summary>A release that cannot be read changes nothing of the parts it is read for, and says what was not done.</summary>
    [Fact]
    public async Task A_release_that_cannot_be_read_changes_nothing_and_says_what_was_not_done()
    {
        var (installation, desktop) = FakeDesktop.Carried(to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        var behind = FakeInstallation.Plugin("reply", "0.17.0");
        installation.WithInstalledPlugin(behind).PublishingPlugin(FakeInstallation.Plugin("reply", "0.18.0"), behind);
        installation.Release.FailWith = HttpStatusCode.ServiceUnavailable;

        var report = await FinishAsync(installation);

        Assert.False(report.Complete);
        Assert.Equal(UpdateFinishStates.NotDone, report.Parts[0].State);
        Assert.Contains("could not be read, so no package was looked at or changed", report.Parts[0].Reason, StringComparison.Ordinal);
        Assert.Equal(behind.Digest, installation.PluginDigest("reply"));
        Assert.Equal(UpdateFinishStates.NotDone, report.Parts[2].State);
        Assert.False(Directory.Exists(installation.Paths.UiDefaultDirectory), "a UI was placed from a release that could not be read");
        Assert.DoesNotContain("plugin.reload", installation.Operations);
    }

    /// <summary>An update that stands before the swap, its application not the new version yet, has nothing for --finish.</summary>
    [Fact]
    public async Task An_update_before_the_swap_is_refused_in_progress()
    {
        var (installation, desktop) = FakeDesktop.Carried();
        using var _ = installation;
        using var __ = desktop;
        new UpdateLedger(SemanticVersion.Current, installation.To, UpdateStep.Stopped, FakeInstallation.Interrupted, installation.InstallPath, installation.Update.StagedExecutable(installation.To), installation.Update.PreviousExecutable)
            .Write(installation.Update.Ledger);
        var before = File.ReadAllText(installation.Update.Ledger);

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.InProgress, refusal.Code);
        Assert.Contains("`jason update apply` carries it on", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(installation.Update.Ledger));
        Assert.Empty(desktop.Starts);
    }

    /// <summary>For a jason the application does not carry, --finish stays a usage error.</summary>
    [Fact]
    public async Task For_a_jason_the_application_does_not_carry_finish_is_a_usage_error()
    {
        using var installation = new FakeInstallation();

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Env, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(installation.Ledger());
    }

    /// <summary>The document's members, in snake_case, as §7 writes them for the Desktop to read.</summary>
    [Fact]
    public async Task The_report_is_the_document_section_7_writes()
    {
        var (installation, desktop) = FakeDesktop.Carried(to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct));

        using var document = JsonDocument.Parse(installation.Out.ToString());
        Assert.Equal(["version", "data_dir", "data_dir_found", "complete", "parts"], document.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.All(document.RootElement.GetProperty("parts").EnumerateArray(), part =>
            Assert.Equal(["part", "state", "reason", "lines"], part.EnumerateObject().Select(member => member.Name)));
    }

    private static async Task<UpdateFinishResponse> FinishAsync(FakeInstallation installation)
    {
        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);
        Assert.Equal(ExitCodes.Success, exit);
        return JsonSerializer.Deserialize<UpdateFinishResponse>(installation.Out.ToString(), JasonJson.Options)!;
    }
}
