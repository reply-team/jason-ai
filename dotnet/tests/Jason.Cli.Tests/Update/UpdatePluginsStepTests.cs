using System.Net;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Files;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Files;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;
using static Jason.Cli.Tests.Update.FakeInstallation;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The update's <c>plugins</c> step: a plugin package installed as an earlier version the release records is replaced by
/// the release's own before the new runtime starts, and the one it replaced is kept for a rollback. Anything else is left
/// as it is with a line of its own, and nothing here ever fails the update.
/// </summary>
public sealed class UpdatePluginsStepTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PluginPackage Earlier { get; } = Plugin("reply", "0.8.0");

    private static PluginPackage Latest { get; } = Plugin("reply", "0.9.0");

    private const string Replaced = "plugins: reply 0.8.0 -> 0.9.0";

    private const string RunInstall = "; run: jason plugin install reply --replace";

    /// <summary>The replacement every row's ledger names: <c>reply</c> from 0.8.0 to 0.9.0.</summary>
    private static UpdatePluginReplacement Replacement { get; } = new("reply", Earlier.Version, Earlier.Digest, Latest.Version, Latest.Digest);

    /// <summary>
    /// A package behind the release is replaced, the one it replaced is kept under the update's directory, the ledger
    /// names the replacement, and the step says it in one line.
    /// </summary>
    [Fact]
    public async Task A_package_behind_the_release_is_replaced_and_the_one_it_replaced_is_kept()
    {
        using var installation = Behind();
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
        Assert.Equal(
            [new UpdatePluginReplacement("reply", Earlier.Version, Earlier.Digest, Latest.Version, Latest.Digest)],
            installation.Ledger()!.Plugins);
        Assert.Contains(Replaced, applier.Steps);
    }

    /// <summary>
    /// The new runtime's first start finds the release's package already there: the step is taken before <c>started</c>,
    /// so the digest the runtime freezes at that start is the new package's and no reload is needed.
    /// </summary>
    [Fact]
    public async Task The_runtime_the_update_starts_finds_the_new_package_in_place()
    {
        using var installation = Behind();
        var atStart = new List<string?>();
        installation.OnStart = () => atStart.Add(installation.PluginDigest("reply"));

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal([Latest.Digest], atStart);
    }

    /// <summary>
    /// What an earlier update kept of a package gives way to what this one keeps: the copy under the update's directory is
    /// the one this update replaced, so a rollback of this update puts back the version it found.
    /// </summary>
    [Fact]
    public async Task An_earlier_updates_kept_package_gives_way_to_the_one_this_update_replaces()
    {
        using var installation = Behind();
        var kept = installation.Update.PreviousPlugin("reply");
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "main.js"), "// kept by an earlier update\n");
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(kept));
        Assert.Contains(Replaced, applier.Steps);
    }

    /// <summary>The release's own package, already installed, is left as it is: nothing is fetched for it and nothing kept.</summary>
    [Fact]
    public async Task A_current_package_is_left_as_it_is()
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Latest).PublishingPlugin(Latest, Earlier);
        var applier = installation.Applier();

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Contains("plugins: reply 0.9.0 is current", applier.Steps);
        Assert.Empty(installation.Ledger()!.Plugins);
        Assert.DoesNotContain(installation.FetchedForPlugins, fetched => fetched.Path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
    }

    /// <summary>
    /// A package edited on this machine has a digest no release recorded, so it is somebody's work: it is left byte for
    /// byte as it was, and its line says why.
    /// </summary>
    [Fact]
    public async Task A_package_edited_here_is_left_byte_for_byte()
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier).PublishingPlugin(Latest, Earlier);
        var edited = Path.Combine(installation.Paths.PluginPackageDirectory("reply"), "main.js");
        File.AppendAllText(edited, "// tuned on this machine\n");
        var before = File.ReadAllBytes(edited);
        var applier = installation.Applier();

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(before, File.ReadAllBytes(edited));
        Assert.Contains("plugins: reply differs from every package a release published, so it was left as it is", applier.Steps);
        Assert.Empty(installation.Ledger()!.Plugins);
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
    }

    /// <summary>A package the release publishes and nobody installed is not installed by an update; the line names the install.</summary>
    [Fact]
    public async Task A_package_not_installed_is_not_placed()
    {
        using var installation = new FakeInstallation().PublishingPlugin(Latest, Earlier);
        var applier = installation.Applier();

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.False(Path.Exists(installation.Paths.PluginPackageDirectory("reply")));
        Assert.Contains("plugins: reply is not installed, so none was placed; run: jason plugin install reply", applier.Steps);
    }

    /// <summary>The journal forwarder's package as the release before this one published it, and as this one publishes it.</summary>
    private static PluginPackage ForwarderEarlier { get; } = Plugin("journal-forwarder", "1.0.0");

    private static PluginPackage ForwarderLatest { get; } = Plugin("journal-forwarder", "1.0.1");

    /// <summary>
    /// A release publishes two packages, Reply's and the journal forwarder's, and the step knows neither by name: each
    /// installed copy is read against its own id's record, so both behind are both replaced, each kept for a rollback,
    /// each named in the ledger and in a line of its own.
    /// </summary>
    [Fact]
    public async Task Each_released_package_is_read_against_its_own_record_and_both_behind_are_replaced()
    {
        using var installation = new FakeInstallation()
            .WithInstalledPlugin(Earlier).WithInstalledPlugin(ForwarderEarlier)
            .PublishingPlugin(Latest, Earlier).PublishingPlugin(ForwarderLatest, ForwarderEarlier);
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(ForwarderLatest.Digest, installation.PluginDigest("journal-forwarder"));
        Assert.Equal(ForwarderEarlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("journal-forwarder")));
        Assert.Equal(
            [
                new UpdatePluginReplacement("journal-forwarder", ForwarderEarlier.Version, ForwarderEarlier.Digest, ForwarderLatest.Version, ForwarderLatest.Digest),
                Replacement,
            ],
            installation.Ledger()!.Plugins.OrderBy(plugin => plugin.Id, StringComparer.Ordinal));
        Assert.Contains(Replaced, applier.Steps);
        Assert.Contains("plugins: journal-forwarder 1.0.0 -> 1.0.1", applier.Steps);
    }

    /// <summary>
    /// The forwarder's copy a person edited to name another receiver has a digest no release recorded: it is theirs, so it
    /// is left byte for byte with its own line, while Reply's package behind the release is replaced beside it; and a
    /// forwarder nobody installed is named with the command that installs it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_forwarder_edited_here_or_not_installed_is_left_while_reply_s_package_is_replaced(bool installed)
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier).PublishingPlugin(Latest, Earlier).PublishingPlugin(ForwarderLatest, ForwarderEarlier);
        var manifest = Path.Combine(installation.Paths.PluginPackageDirectory("journal-forwarder"), "plugin.yaml");
        byte[]? before = null;
        if (installed)
        {
            installation.WithInstalledPlugin(ForwarderEarlier);
            File.AppendAllText(manifest, "# receiver: another host\n");
            before = File.ReadAllBytes(manifest);
        }

        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal([Replacement], installation.Ledger()!.Plugins);
        Assert.Contains(Replaced, applier.Steps);
        if (installed)
        {
            Assert.Equal(before, File.ReadAllBytes(manifest));
            Assert.Contains("plugins: journal-forwarder differs from every package a release published, so it was left as it is", applier.Steps);
        }
        else
        {
            Assert.False(Path.Exists(installation.Paths.PluginPackageDirectory("journal-forwarder")));
            Assert.Contains("plugins: journal-forwarder is not installed, so none was placed; run: jason plugin install journal-forwarder", applier.Steps);
        }
    }

    /// <summary>A release that publishes no plugin package says so in one line, and the update goes on to its end.</summary>
    [Fact]
    public async Task A_release_with_no_plugin_package_says_so_and_completes()
    {
        using var installation = new FakeInstallation();
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal([$"plugins: {installation.To} publishes no plugin package"], applier.Steps.Where(step => step.StartsWith("plugins:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A package whose archive is not the one checksums.txt lists is not placed: the installed one stays as it was, its line
    /// says why and names the install that places it, and the update completes with exit 0.
    /// </summary>
    [Fact]
    public async Task An_archive_checksums_txt_does_not_list_leaves_the_package_and_the_update_completes()
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        installation.Release.WithPlugin("reply", Latest.Version, Latest.Zip, Latest.Digest, checksum: new string('0', 64), earlier: [(Earlier.Version, Earlier.Digest)]);

        await AssertLeftAndCompletedAsync(installation);
    }

    /// <summary>
    /// A release the step cannot read again — the network gone after the swap — gives one line for the step, which names
    /// the check that tells which package is behind, and the update completes with every package as it was.
    /// </summary>
    [Fact]
    public async Task A_release_the_step_cannot_read_again_gives_one_line_and_the_update_completes()
    {
        using var installation = Behind();
        installation.Killed(UpdateStep.Swapped);
        installation.Release.FailWith = HttpStatusCode.ServiceUnavailable;
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        var line = Assert.Single(applier.Steps, step => step.StartsWith("plugins:", StringComparison.Ordinal));
        Assert.StartsWith("plugins: not checked — ", line, StringComparison.Ordinal);
        Assert.EndsWith("; run: jason status", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// What an earlier update kept cannot be deleted — a file in it held open — so the ledger cannot name this update's
    /// replacement: nothing the ledger does not name is moved, and the package's line says why.
    /// </summary>
    /// <remarks>Windows only: elsewhere an open file stops no delete.</remarks>
    [Fact]
    public async Task A_package_the_ledger_cannot_name_is_not_moved()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file held open.");

        using var installation = Behind();
        var held = Path.Combine(installation.Update.PreviousPlugin("reply"), "main.js");
        Directory.CreateDirectory(Path.GetDirectoryName(held)!);
        File.WriteAllText(held, "// kept by an earlier update\n");
        var applier = installation.Applier();

        UpdateLedger ledger;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ledger = await applier.ApplyAsync(Request(installation), Ct);
        }

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.Empty(installation.Ledger()!.Plugins);
        var line = Assert.Single(applier.Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.StartsWith("plugins: reply 0.8.0 not replaced — ", line, StringComparison.Ordinal);
        Assert.Contains("main.js", line, StringComparison.Ordinal);
        Assert.EndsWith(RunInstall, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal that ends its own sentence is said in the line without its full stop, so the line reads "…; run:" and
    /// never ".;": the refusal itself still reaches standard error whole.
    /// </summary>
    [Fact]
    public async Task A_refusal_that_ends_in_a_full_stop_is_said_without_it_before_the_command()
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        installation.Release.WithPlugin("reply", Latest.Version, Latest.Zip, Latest.Digest, checksum: new string('0', 64), earlier: [(Earlier.Version, Earlier.Digest)]);

        await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        var refusal = installation.Error.ToString().Split(Environment.NewLine).Single(said => said.Contains("checksums.txt", StringComparison.Ordinal));
        Assert.EndsWith(".", refusal, StringComparison.Ordinal);
        var line = Assert.Single(Answer(installation).Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.Equal($"plugins: reply 0.8.0 not replaced — {refusal.TrimEnd('.')}{RunInstall}", line);
    }

    /// <summary>
    /// The second compare: an archive that matches its checksums.txt line and unpacks to a tree other than the content
    /// digest the manifest gives is not placed either.
    /// </summary>
    [Fact]
    public async Task An_archive_that_unpacks_to_another_content_digest_leaves_the_package_and_the_update_completes()
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        installation.Release.WithPlugin("reply", Latest.Version, Latest.Zip, "sha256:" + new string('a', 64), earlier: [(Earlier.Version, Earlier.Digest)]);

        await AssertLeftAndCompletedAsync(installation);
    }

    /// <summary>
    /// Killed after the installed package went aside and before the release's came in: the next apply finds nothing in
    /// place beside the kept copy, and places the release's package without keeping anything twice.
    /// </summary>
    [Fact]
    public async Task A_kill_after_the_package_went_aside_is_finished_by_the_next_apply()
    {
        using var installation = Behind();
        KilledInPlugins(installation, placed: false);
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
        Assert.Contains(Replaced, applier.Steps);
    }

    /// <summary>
    /// Killed after the installed package went aside, and the release's then refused: the kept copy goes back into its
    /// empty place, and where that rename fails too the update still completes, its line saying both refusals.
    /// </summary>
    [Fact]
    public async Task A_kept_package_that_cannot_be_put_back_is_said_and_the_update_completes()
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        installation.Release.WithPlugin("reply", Latest.Version, Latest.Zip, Latest.Digest, checksum: new string('0', 64), earlier: [(Earlier.Version, Earlier.Digest)]);
        KilledInPlugins(installation, placed: false);
        var applier = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, installation.InstallPath)
        {
            MovePackageBack = (_, _) => throw new IOException("The disk would not rename it."),
        };

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.False(Path.Exists(installation.Paths.PluginPackageDirectory("reply")));
        Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
        var line = Assert.Single(applier.Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.StartsWith("plugins: reply 0.8.0 not replaced — ", line, StringComparison.Ordinal);
        Assert.Contains("checksums.txt", line, StringComparison.Ordinal);
        Assert.Contains($"{installation.Update.PreviousPlugin("reply")} could not be put back as {installation.Paths.PluginPackageDirectory("reply")} either: The disk would not rename it", line, StringComparison.Ordinal);
        Assert.EndsWith(RunInstall, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The kept copy's way back into its empty place is refused for a permission, not a hold: the files in it show that, and
    /// the put-back is given up at once, without a pause of the held-file rule, the line saying that waiting would not help.
    /// </summary>
    /// <remarks>Windows only: the files are refused by a deny of their own; the rename is the test's to refuse.</remarks>
    [Fact]
    public async Task A_kept_package_this_account_is_refused_is_not_waited_for_when_it_is_put_back()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny on the file is Windows' own way to refuse an open the account could otherwise make.");

        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        installation.Release.WithPlugin("reply", Latest.Version, Latest.Zip, Latest.Digest, checksum: new string('0', 64), earlier: [(Earlier.Version, Earlier.Digest)]);
        KilledInPlugins(installation, placed: false);
        var kept = installation.Update.PreviousPlugin("reply");
        var clock = new PausedClock();
        var pauses = new List<TimeSpan>();
        var applier = new UpdateApplier(installation.Env, installation.Update, clock, installation.InstallPath)
        {
            HeldFiles = InstallationRemovers.HeldFiles,
            MovePackageBack = (_, _) => throw new UnauthorizedAccessException("The rename was refused."),
            Pause = (delay, _) =>
            {
                pauses.Add(delay);
                clock.Pass(delay);
            },
        };

        UpdateLedger ledger;
        var undo = OperatingSystem.IsWindows() ? RefusedPackage.Deny(kept) : throw new PlatformNotSupportedException();
        try
        {
            ledger = await applier.ApplyAsync(Request(installation), Ct);
        }
        finally
        {
            undo();
        }

        Assert.Empty(pauses);
        Assert.Equal(UpdateStep.Complete, ledger.Step);
        var line = Assert.Single(applier.Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.Contains($"{kept} could not be put back as {installation.Paths.PluginPackageDirectory("reply")} either: {AccessRefused.Clause}", line, StringComparison.Ordinal);
        Assert.DoesNotContain("The rename was refused", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// Killed after the ledger named the replacement and before anything moved: the next apply places the release's
    /// package and keeps the one it replaced, as the killed run would have.
    /// </summary>
    [Fact]
    public async Task A_kill_after_the_record_and_before_the_first_move_is_finished_by_the_next_apply()
    {
        using var installation = Behind();
        RecordedInPlugins(installation);
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
        Assert.Contains(Replaced, applier.Steps);
    }

    /// <summary>
    /// Killed while what an earlier update kept was being deleted, before the ledger named anything: the next apply plans
    /// the step again from the plugins directory, finishes the delete, and replaces the package.
    /// </summary>
    [Fact]
    public async Task A_kill_during_the_delete_before_the_record_is_planned_again()
    {
        using var installation = Behind();
        installation.Killed(UpdateStep.Plugins);
        var half = Path.Combine(installation.Update.PreviousPlugin("reply"), "lib", "half.js");
        Directory.CreateDirectory(Path.GetDirectoryName(half)!);
        File.WriteAllText(half, "// half deleted\n");
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
        Assert.Equal([Replacement], installation.Ledger()!.Plugins);
        Assert.Contains(Replaced, applier.Steps);
    }

    /// <summary>
    /// A resumed step works from its ledger alone: a package the ledger names is replaced, and another one, behind the
    /// release as well but not named there, is neither moved nor given a line.
    /// </summary>
    [Fact]
    public async Task A_resumed_step_says_only_the_packages_its_ledger_names()
    {
        var other = Plugin("other", "1.0.0");
        using var installation = Behind().WithInstalledPlugin(other).PublishingPlugin(Plugin("other", "1.1.0"), other);
        RecordedInPlugins(installation);
        var applier = installation.Applier();

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(other.Digest, installation.PluginDigest("other"));
        Assert.Equal([Replaced], applier.Steps.Where(step => step.StartsWith("plugins:", StringComparison.Ordinal)));
    }

    /// <summary>A package edited after the ledger named it is somebody's since: the resumed step leaves it and says so.</summary>
    [Fact]
    public async Task A_package_changed_since_the_ledger_named_it_is_left()
    {
        using var installation = Behind();
        RecordedInPlugins(installation);
        File.AppendAllText(Path.Combine(installation.Paths.PluginPackageDirectory("reply"), "main.js"), "// tuned on this machine\n");
        var edited = installation.PluginDigest("reply");
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(edited, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
        Assert.Contains("plugins: reply changed since this update read it, so it was left as it is", applier.Steps);
    }

    /// <summary>
    /// The release read again no longer publishes the package the ledger names — none at all, or another one — so the
    /// resumed step places nothing it did not record, and the line names the install.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_release_that_no_longer_publishes_the_recorded_package_leaves_it(bool publishesAnother)
    {
        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        if (publishesAnother)
        {
            installation.PublishingPlugin(Plugin("reply", "1.0.0"), Earlier);
        }

        RecordedInPlugins(installation);
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
        Assert.Equal(
            [$"plugins: reply 0.8.0 not replaced — The release no longer publishes reply 0.9.0 as this update read it. Nothing was changed{RunInstall}"],
            applier.Steps.Where(step => step.StartsWith("plugins:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A person's interrupt during the put-back's wait does not leave the place empty: the kept copy goes back into it all
    /// the same, since a package left aside is worse than the wait, and the apply stops after it.
    /// </summary>
    /// <remarks>Windows only: elsewhere a held file is no hold, so nothing waits.</remarks>
    [Fact]
    public async Task A_kept_package_is_put_back_even_when_the_apply_is_cancelled_during_its_wait()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows counts a held file as a hold to wait out.");

        using var installation = new FakeInstallation().WithInstalledPlugin(Earlier);
        installation.Release.WithPlugin("reply", Latest.Version, Latest.Zip, Latest.Digest, checksum: new string('0', 64), earlier: [(Earlier.Version, Earlier.Digest)]);
        KilledInPlugins(installation, placed: false);
        using var interrupt = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var tries = 0;
        var applier = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, installation.InstallPath)
        {
            MovePackageBack = (from, to) =>
            {
                if (tries++ == 0)
                {
                    interrupt.Cancel();
                    throw new IOException("A file in it is held open.", unchecked((int)0x80070020));
                }

                Directory.Move(from, to);
            },
            Pause = (_, token) => token.ThrowIfCancellationRequested(),
        };

        var stopped = await Record.ExceptionAsync(() => applier.ApplyAsync(Request(installation), interrupt.Token));

        Assert.True(stopped is null or OperationCanceledException, stopped?.ToString());
        Assert.Equal(2, tries);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
    }

    /// <summary>Killed after the release's package came in: the next apply finds it in place and moves nothing again.</summary>
    [Fact]
    public async Task A_kill_after_the_package_was_placed_moves_nothing_again()
    {
        using var installation = Behind();
        KilledInPlugins(installation, placed: true);
        var applier = installation.Applier();

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
        Assert.DoesNotContain(installation.FetchedForPlugins, fetched => fetched.Path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.Contains(Replaced, applier.Steps);
    }

    /// <summary>
    /// The step is the new build's: an apply hands the rest over at <c>swapped</c>, and the executable it was handed to is
    /// the one that fetches and places the package.
    /// </summary>
    [Fact]
    public async Task The_step_is_taken_by_the_executable_the_update_hands_over_to()
    {
        using var installation = Behind();

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.NotEmpty(installation.FetchedForPlugins);
        Assert.All(installation.FetchedForPlugins, fetched => Assert.True(fetched.HandedOver, fetched.Path));
        Assert.Contains(Replaced, Answer(installation).Steps);
    }

    /// <summary>
    /// A rollback puts the kept package back where the update's own is still in place, and leaves a package that changed
    /// since — somebody's install or edit — with the kept copy beside it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rollback_puts_the_kept_package_back_only_over_the_one_the_update_placed(bool changedSince)
    {
        using var installation = Behind();
        installation.WithRuntime();
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        var target = installation.Paths.PluginPackageDirectory("reply");
        if (changedSince)
        {
            File.AppendAllText(Path.Combine(target, "main.js"), "// tuned on this machine\n");
        }

        var changed = installation.PluginDigest("reply");
        var rollback = installation.Rollback();

        var back = await rollback.RollBackAsync(Ct);

        Assert.Empty(back.Plugins);
        if (changedSince)
        {
            Assert.Equal(changed, installation.PluginDigest("reply"));
            Assert.Equal(Earlier.Digest, PackageDigestOf(installation.Update.PreviousPlugin("reply")));
            Assert.Contains(rollback.Steps, step => step.StartsWith("the plugin reply is not the 0.9.0 this update placed", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
            Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
            Assert.Contains("put the plugin reply 0.8.0 back", rollback.Steps);
        }
    }

    /// <summary>
    /// A rollback also puts the kept package back where nothing is installed beside it: the ledger names that package, so
    /// an empty place there is the update's own move left half made.
    /// </summary>
    [Fact]
    public async Task A_rollback_puts_the_kept_package_back_into_an_empty_place()
    {
        using var installation = Behind();
        KilledInPlugins(installation, placed: false);
        var rollback = installation.Rollback();

        await rollback.RollBackAsync(Ct);

        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
        Assert.Contains("put the plugin reply 0.8.0 back", rollback.Steps);
    }

    /// <summary>
    /// The kept package cannot be put back and the update's own cannot be moved back into its place either: the line says
    /// the first refusal, then the second, where the update's package was left and that the place is empty.
    /// </summary>
    [Fact]
    public async Task A_rollback_that_can_put_neither_package_in_place_says_where_each_is()
    {
        using var installation = Behind();
        installation.WithRuntime();
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        var target = installation.Paths.PluginPackageDirectory("reply");
        var kept = installation.Update.PreviousPlugin("reply");
        var aside = kept + ".rolling";
        var rollback = new UpdateRollback(installation.Env, installation.Update, TimeProvider.System)
        {
            MovePackage = (from, to) =>
            {
                if (to == target)
                {
                    throw new IOException(from == kept ? "The kept package would not move." : "The update's package would not move back.");
                }

                Directory.Move(from, to);
            },
        };

        await rollback.RollBackAsync(Ct);

        Assert.False(Path.Exists(target));
        Assert.Equal(Latest.Digest, PackageDigestOf(aside));
        Assert.Equal(Earlier.Digest, PackageDigestOf(kept));
        Assert.Contains(
            $"the plugin reply 0.8.0 could not be put back from {kept}: The kept package would not move. The 0.9.0 this update placed could not be moved back either: The update's package would not move back. It is in {aside}, and {target} is empty.",
            rollback.Steps);
    }

    /// <summary>
    /// A file of the update's package held open while a rollback restores is waited out for the whole of the held-file
    /// rule, Windows' access denied counted as the hold it is, and then the restore is given up with a line that says so.
    /// </summary>
    /// <remarks>Windows only: elsewhere an open file stops no rename.</remarks>
    [Fact]
    public async Task A_rollback_waits_out_a_held_file_of_the_updates_package_and_then_says_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a directory with a file in it held open.");

        using var installation = Behind();
        installation.WithRuntime();
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        var target = installation.Paths.PluginPackageDirectory("reply");
        var kept = installation.Update.PreviousPlugin("reply");
        var clock = new PausedClock();
        var pauses = new List<TimeSpan>();
        var rollback = new UpdateRollback(installation.Env, installation.Update, clock)
        {
            HeldFiles = InstallationRemovers.HeldFiles,
            Pause = (delay, _) =>
            {
                pauses.Add(delay);
                clock.Pass(delay);
            },
        };

        using (new FileStream(Path.Combine(target, "main.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await rollback.RollBackAsync(Ct);
        }

        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(kept));
        Assert.True(pauses.Aggregate(TimeSpan.Zero, (sum, pause) => sum + pause) >= InstallationRemovers.HeldFiles.Wait);
        Assert.Contains(rollback.Steps, step => step.StartsWith($"the plugin reply 0.8.0 could not be put back from {kept}: ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A package of the update this account is refused, as one placed by an elevated process is, is not one held open: a
    /// rollback that restores finds the same access-denied, the files in it show a permission, and the restore is given up
    /// at once, without a pause of the held-file rule, with a line that says waiting would not help.
    /// </summary>
    /// <remarks>Windows only: a deny on the directory is its own way to refuse a rename.</remarks>
    [Fact]
    public async Task A_rollback_does_not_wait_for_a_package_of_the_update_this_account_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny on the directory is Windows' own way to refuse its rename.");

        using var installation = Behind();
        installation.WithRuntime();
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        var target = installation.Paths.PluginPackageDirectory("reply");
        var kept = installation.Update.PreviousPlugin("reply");
        var clock = new PausedClock();
        var pauses = new List<TimeSpan>();
        var rollback = new UpdateRollback(installation.Env, installation.Update, clock)
        {
            HeldFiles = InstallationRemovers.HeldFiles,
            Pause = (delay, _) =>
            {
                pauses.Add(delay);
                clock.Pass(delay);
            },
        };

        var undo = OperatingSystem.IsWindows() ? RefusedPackage.Deny(target) : throw new PlatformNotSupportedException();
        try
        {
            await rollback.RollBackAsync(Ct);
        }
        finally
        {
            undo();
        }

        Assert.Empty(pauses);
        Assert.Equal(Latest.Digest, installation.PluginDigest("reply"));
        Assert.Equal(Earlier.Digest, PackageDigestOf(kept));
        var line = Assert.Single(rollback.Steps, step => step.StartsWith($"the plugin reply 0.8.0 could not be put back from {kept}: ", StringComparison.Ordinal));
        Assert.Contains($": {AccessRefused.Clause}", line, StringComparison.Ordinal);
        Assert.EndsWith($": {AccessRefused.Clause}", line, StringComparison.Ordinal);
        Assert.DoesNotContain("held open", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// An installed package this account is refused, as one placed by an elevated process is, is not one held open: the
    /// update's move of it comes back with the same access-denied, the files in it show a permission, and the move is given
    /// up at once, without a pause of the held-file rule. The package stays as it was, the line names the install, says that
    /// waiting would not help, and the update completes.
    /// </summary>
    /// <remarks>Windows only: a deny on the directory is its own way to refuse a rename.</remarks>
    [Fact]
    public async Task A_package_this_account_is_refused_is_left_at_once_and_the_update_completes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny on the directory is Windows' own way to refuse its rename.");

        using var installation = Behind();
        var clock = new PausedClock();
        var pauses = new List<TimeSpan>();
        var applier = new UpdateApplier(installation.Env, installation.Update, clock, installation.InstallPath)
        {
            HeldFiles = InstallationRemovers.HeldFiles,
            Pause = (delay, _) =>
            {
                pauses.Add(delay);
                clock.Pass(delay);
            },
        };

        var target = installation.Paths.PluginPackageDirectory("reply");
        UpdateLedger ledger;
        var undo = OperatingSystem.IsWindows() ? RefusedPackage.Deny(target) : throw new PlatformNotSupportedException();
        try
        {
            ledger = await applier.ApplyAsync(Request(installation), Ct);
        }
        finally
        {
            undo();
        }

        Assert.Empty(pauses);
        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
        var line = Assert.Single(applier.Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.StartsWith("plugins: reply 0.8.0 not replaced — ", line, StringComparison.Ordinal);
        Assert.Contains("this account is refused access to it, so waiting would not help", line, StringComparison.Ordinal);
        Assert.DoesNotContain("held open", line, StringComparison.Ordinal);
        Assert.EndsWith(RunInstall, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check and the step read the plugins directory through one plan: with one package behind and one edited, the
    /// check names exactly the one the apply replaces, and the apply leaves the other with its line.
    /// </summary>
    [Fact]
    public async Task The_check_names_exactly_the_packages_the_apply_replaces()
    {
        var other = Plugin("other", "1.0.0");
        using var installation = Behind().WithInstalledPlugin(other).PublishingPlugin(Plugin("other", "1.1.0"), other);
        File.AppendAllText(Path.Combine(installation.Paths.PluginPackageDirectory("other"), "main.js"), "// tuned on this machine\n");

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--feed", installation.Feed.ToString()], installation.Env, Ct));
        var check = JsonSerializer.Deserialize<UpdateCheckResponse>(installation.Out.ToString(), JasonJson.Options)!;
        installation.Out.GetStringBuilder().Clear();

        var applier = installation.Applier();
        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal([new PluginUpdate("reply", "0.8.0", "0.9.0")], check.PluginUpdates);
        Assert.Equal(["reply"], installation.Ledger()!.Plugins.Select(plugin => plugin.Id));
        Assert.Equal(
            ["plugins: other differs from every package a release published, so it was left as it is", Replaced],
            applier.Steps.Where(step => step.StartsWith("plugins:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A file of the installed package held open — a plugin host still reading it, or a scanner — is waited out for the
    /// whole of the update's held-file rule, Windows' access denied counted as the hold it is, and then the move is given
    /// up: the runtime the update starts finds the package as it was, its line names the install, and the update completes.
    /// </summary>
    /// <remarks>Windows only: elsewhere an open file stops no rename.</remarks>
    [Fact]
    public async Task A_package_held_past_the_wait_is_left_and_the_update_completes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a directory with a file in it held open.");

        using var installation = Behind();
        var clock = new PausedClock();
        var pauses = new List<TimeSpan>();
        var applier = new UpdateApplier(installation.Env, installation.Update, clock, installation.InstallPath)
        {
            HeldFiles = InstallationRemovers.HeldFiles,
            Pause = (delay, _) =>
            {
                pauses.Add(delay);
                clock.Pass(delay);
            },
        };

        var atStart = new List<string?>();
        installation.OnStart = () => atStart.Add(installation.PluginDigest("reply"));

        UpdateLedger ledger;
        using (new FileStream(Path.Combine(installation.Paths.PluginPackageDirectory("reply"), "main.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ledger = await applier.ApplyAsync(Request(installation), Ct);
        }

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
        Assert.Equal([Earlier.Digest], atStart);
        Assert.True(pauses.Aggregate(TimeSpan.Zero, (sum, pause) => sum + pause) >= InstallationRemovers.HeldFiles.Wait);
        var line = Assert.Single(applier.Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.StartsWith("plugins: reply 0.8.0 not replaced — ", line, StringComparison.Ordinal);
        Assert.EndsWith(RunInstall, line, StringComparison.Ordinal);
    }

    /// <summary>An installation with <c>reply</c> 0.8.0 installed and a release publishing 0.9.0, its record holding both.</summary>
    private static FakeInstallation Behind() => new FakeInstallation().WithInstalledPlugin(Earlier).PublishingPlugin(Latest, Earlier);

    /// <summary>
    /// The state a kill inside the step leaves: the ledger names the replacement, the installed package is kept, and the
    /// release's is in place when <paramref name="placed"/>.
    /// </summary>
    private static void KilledInPlugins(FakeInstallation installation, bool placed)
    {
        RecordedInPlugins(installation);
        Directory.CreateDirectory(installation.Update.PreviousPlugins);
        Directory.Move(installation.Paths.PluginPackageDirectory("reply"), installation.Update.PreviousPlugin("reply"));
        if (placed)
        {
            installation.WithInstalledPlugin(Latest);
        }
    }

    /// <summary>The state a kill after the step's record leaves: the ledger names the replacement, and nothing has moved.</summary>
    private static void RecordedInPlugins(FakeInstallation installation) =>
        (installation.Killed(UpdateStep.Plugins) with { Plugins = [Replacement] }).Write(installation.Update.Ledger);

    /// <summary>The apply typed as a person types it: the package left as it was, its line, and the update complete with exit 0.</summary>
    private static async Task AssertLeftAndCompletedAsync(FakeInstallation installation)
    {
        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = Answer(installation);
        Assert.Equal("complete", answer.Step);
        Assert.Equal(Earlier.Digest, installation.PluginDigest("reply"));
        Assert.False(Path.Exists(installation.Update.PreviousPlugin("reply")));
        var line = Assert.Single(answer.Steps, step => step.StartsWith("plugins: reply", StringComparison.Ordinal));
        Assert.StartsWith("plugins: reply 0.8.0 not replaced — ", line, StringComparison.Ordinal);
        Assert.EndsWith(RunInstall, line, StringComparison.Ordinal);
    }

    private static UpdateApplyResponse Answer(FakeInstallation installation) =>
        JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;

    private static string PackageDigestOf(string directory) => Jason.Contracts.Plugins.PackageDigest.Compute(directory).Digest;

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.FromSeconds(5));
}
