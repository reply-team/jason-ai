using System.Text.Json;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The last step of an update: the new release's UI, placed as <c>jason ui install</c> places it. Like the skills, it
/// comes after <c>complete</c> and is no step of the ledger, so nothing in it fails the update: a UI that could not be
/// placed says why on its line and names the command that places it.
/// </summary>
public class UpdateUiStepTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_update_places_the_new_release_s_ui()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal($"ui: placed {installation.To}", applier.Steps[^1]);
        Assert.Equal(installation.To.ToString(), UiManifest.Read(installation.Paths.UiDefaultDirectory)!.Version);
    }

    /// <summary>
    /// A release that publishes no UI — one from before the UI was published, which <c>--version</c> can name — is not a
    /// failure, and the line names no command: the executable just installed may not have <c>jason ui install</c>.
    /// </summary>
    [Fact]
    public async Task A_release_that_publishes_no_ui_leaves_the_update_complete_and_says_so()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Release.WithoutUi();

        var exit = await CliApp.RunAsync(
            ["update", "apply", "--feed", installation.Feed.ToString(), "--drain-seconds", "0"],
            installation.Env,
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal($"ui: {installation.To} publishes no UI, so none was placed", answer.Steps[^1]);
        Assert.False(Directory.Exists(installation.Paths.UiDefaultDirectory));
    }

    /// <summary>A package that fails its checks is a failure of the step: it says why and names the command.</summary>
    [Fact]
    public async Task A_package_that_does_not_check_out_leaves_the_update_complete_and_names_the_repair()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Release.WithUi(Ui.UiZip.Build(installation.To.ToString()), digest: new string('0', 64));
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.StartsWith("ui: not updated — ", applier.Steps[^1], StringComparison.Ordinal);
        Assert.Contains("does not match checksums.txt", applier.Steps[^1], StringComparison.Ordinal);
        Assert.EndsWith("; run: jason ui install", applier.Steps[^1], StringComparison.Ordinal);
        Assert.Contains("does not match checksums.txt", installation.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.I2, "the UI matches the new API")]
    public async Task The_ui_step_holds_the_build_to_the_api_the_new_runtime_speaks()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.SpeaksApi = "v2";
        var applier = installation.Applier();

        await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.StartsWith("ui: not updated — The asset is built for API v1, and the runtime it is for speaks v2.", applier.Steps[^1], StringComparison.Ordinal);
        Assert.False(Directory.Exists(installation.Paths.UiDefaultDirectory));
    }

    /// <summary>An installation whose runtime was stopped is started by the update, and the started runtime says which API it speaks.</summary>
    [Fact]
    public async Task A_stopped_installation_gets_the_ui_of_the_release_it_updated_to()
    {
        using var installation = new FakeInstallation();
        var applier = installation.Applier();

        await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.Equal($"ui: placed {installation.To}", applier.Steps[^1]);
    }

    /// <summary>
    /// A runtime that went away after it was declared healthy says nothing about the API it speaks, so nothing holds a
    /// build to it: the step places nothing and names the command, which needs no runtime.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_stops_answering_after_it_was_healthy_leaves_the_ui_to_the_repair()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.GoesSilentOnceComplete = true;
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.StartsWith("ui: not updated — the new runtime did not say which API it speaks", applier.Steps[^1], StringComparison.Ordinal);
        Assert.EndsWith("; run: jason ui install", applier.Steps[^1], StringComparison.Ordinal);
        Assert.False(Directory.Exists(installation.Paths.UiDefaultDirectory));
    }

    [Fact]
    public async Task The_ui_is_placed_while_a_custom_build_is_served()
    {
        using var installation = new FakeInstallation().WithRuntime();
        Directory.CreateDirectory(installation.Paths.UiCustomDirectory);
        await File.WriteAllTextAsync(Path.Combine(installation.Paths.UiCustomDirectory, "index.html"), "mine", Ct);

        await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        Assert.Equal(installation.To.ToString(), UiManifest.Read(installation.Paths.UiDefaultDirectory)!.Version);
        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(installation.Paths.UiCustomDirectory, "index.html"), Ct));
        Assert.Equal(["index.html"], Directory.EnumerateFileSystemEntries(installation.Paths.UiCustomDirectory).Select(Path.GetFileName));
    }
}
