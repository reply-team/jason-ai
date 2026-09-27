using Jason.Cli;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// Everything an update refuses to do, one test per code. A code alone tells a person nothing they can act on,
/// so each test also reads the message: the version, the path, or the thing to type next.
/// </summary>
public class UpdateRefusalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An update renames files rather than copying them, because a rename is atomic and a half-copied
    /// executable is the one state worth avoiding at any price. Two volumes make a rename impossible, and the
    /// refusal names both paths, because which of the two to move is the person's decision and not this
    /// program's.
    /// </summary>
    /// <remarks>
    /// Driven through the check rather than through a second disk: the check compares the two paths' roots, so
    /// an install path on another volume is one this test can name. It is named on Windows only — on Unix every
    /// absolute path has the root <c>/</c>, so there is no pair of paths this check can be given that differs.
    /// </remarks>
    [Fact]
    public async Task An_install_path_on_another_volume_is_refused_with_both_paths_named()
    {
        var volume = OtherVolume();
        Assert.SkipWhen(volume is null, "This platform has no second volume a path can name.");

        using var installation = new FakeInstallation();
        var elsewhere = Path.Combine(volume!, "Program Files", "Jason", ReleaseAssets.ExecutableName);
        var applier = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, elsewhere);

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.CrossVolume, refused.Code);
        Assert.False(refused.Retryable, "the two paths will be on the same two volumes next time as well");

        var staged = installation.Update.StagedExecutable(installation.To);
        Assert.Contains(staged, refused.Message, StringComparison.Ordinal);
        Assert.Contains(elsewhere, refused.Message, StringComparison.Ordinal);
        Assert.Contains("JASON_DATA_DIR", refused.Message, StringComparison.Ordinal);

        // Nothing crossed, and nothing was done on the way to finding out. Which two volumes are in play is
        // known from the paths alone, before a byte is fetched — so this refusal costs no download, no drained
        // runtime and no stopped one.
        Assert.False(File.Exists(staged), "something was staged for an update that cannot be applied");
        Assert.False(File.Exists(elsewhere), "something was written to the other volume");
        Assert.DoesNotContain(
            installation.Fetched,
            address => address.EndsWith(".zip", StringComparison.Ordinal) || address.EndsWith(".tar.gz", StringComparison.Ordinal));
        Assert.DoesNotContain("system.shutdown", installation.Operations);
        Assert.DoesNotContain("system.drain", installation.Operations);
    }

    /// <summary>
    /// An installation started as <c>dotnet jason.dll</c> is not one file, so there is nothing to replace and
    /// the refusal says how to move that build forward instead of guessing which file was meant.
    /// </summary>
    /// <remarks>
    /// The shape of such a command is asked of <see cref="SelfExecutable"/> rather than spelled here: what a
    /// muxed installation looks like is that type's answer, and a test that wrote <c>["dotnet", "jason.dll"]</c>
    /// by hand would keep passing after that answer changed.
    /// </remarks>
    [Fact]
    public void An_installation_run_through_the_muxer_says_so_rather_than_guessing_a_file()
    {
        var muxer = SelfExecutable.Resolve(
            OperatingSystem.IsWindows() ? @"C:\Program Files\dotnet\dotnet.exe" : "/usr/share/dotnet/dotnet",
            Path.Combine("opt", "jason", "jason.dll"));

        var refused = Assert.Throws<UpdateException>(() => UpdateApplier.ResolveInstallPath(muxer, singleFile: false));

        Assert.Equal(UpdateCodes.NotUpdatable, refused.Code);
        Assert.False(refused.Retryable, "the same installation will be the same installation next time");
        Assert.Contains("dotnet", refused.Message, StringComparison.Ordinal);
        Assert.Contains("one-liner", refused.Message, StringComparison.Ordinal);

        // And a published installation is exactly the file it is running, which is the file an update replaces.
        var published = Path.Combine("opt", "jason", ReleaseAssets.ExecutableName);
        Assert.Equal(published, UpdateApplier.ResolveInstallPath(SelfExecutable.Resolve(published, "jason.dll"), singleFile: true));
    }

    /// <summary>
    /// Nor is a build's own launcher one file: <c>dotnet run --project runtime/src/Jason.App</c> starts
    /// <c>bin/Debug/net10.0/jason</c>, one file of many, whose process path is that launcher. An update that
    /// replaced it would be replacing a build output.
    /// </summary>
    [Fact]
    public void A_build_run_from_its_own_launcher_says_so_rather_than_replacing_it()
    {
        var launcher = SelfExecutable.Resolve(Path.Combine("repo", "runtime", "src", "Jason.App", "bin", "Debug", "net10.0", ReleaseAssets.ExecutableName), "jason.dll");

        var refused = Assert.Throws<UpdateException>(() => UpdateApplier.ResolveInstallPath(launcher, singleFile: false));

        Assert.Equal(UpdateCodes.NotUpdatable, refused.Code);
    }

    /// <summary>
    /// A release may say it cannot be reached from any version, and the refusal names the one to install
    /// first — the whole purpose of the field, which until now was parsed and read by nothing.
    /// </summary>
    [Fact]
    public async Task A_release_that_cannot_be_applied_directly_names_the_version_to_pass_through()
    {
        using var installation = new FakeInstallation();
        installation.Release.Says(installation.Release.Manifest().Replace(
            "\"min_upgrade_from\": null",
            "\"min_upgrade_from\": \"9.0.0\"",
            StringComparison.Ordinal));

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.NotDirectlyApplicable, refused.Code);
        Assert.False(refused.Retryable, "the release will say the same thing next time");
        Assert.Contains(installation.To.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Contains(SemanticVersion.Current.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Contains("9.0.0", refused.Message, StringComparison.Ordinal);

        // Refused before anything was downloaded, and before an update existed to finish.
        Assert.Equal(["/releases/latest/download/" + ReleaseAssets.Manifest], installation.Fetched);
        Assert.Null(installation.Ledger());
    }

    /// <summary>
    /// Nothing newer is nothing to do — unless a person asked for a particular version by name, which is how
    /// an installation is put back onto a release it has already passed.
    /// </summary>
    [Fact]
    public async Task A_feed_with_nothing_newer_refuses_only_when_no_version_was_asked_for()
    {
        var old = SemanticVersion.Parse("0.0.0");
        using var installation = new FakeInstallation(to: old);

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.UpToDate, refused.Code);
        Assert.False(refused.Retryable, "the feed will offer the same release next time");
        Assert.Contains(SemanticVersion.Current.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Contains(old.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Null(installation.Ledger());

        // The same feed, the same release, and a person who named it: that is an instruction rather than an
        // advertisement, and it is carried out.
        var ledger = await installation.Applier()
            .ApplyAsync(new UpdateRequest(installation.Feed, old, TimeSpan.Zero), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(old.ToString(), installation.Installed());
    }

    /// <summary>
    /// A ledger in flight is finished rather than abandoned, so asking for a different version while one is
    /// under way is refused — and the message says what is in flight, where it stands and what to type.
    /// </summary>
    [Fact]
    public async Task An_update_to_another_version_while_one_is_in_flight_is_refused()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var inFlight = installation.Killed(UpdateStep.Stopped);
        var other = SemanticVersion.Parse("1.2.3");

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, other, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.InProgress, refused.Code);
        Assert.False(refused.Retryable, "the update in flight will still be in flight next time");
        Assert.Contains(installation.To.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Contains("1.2.3", refused.Message, StringComparison.Ordinal);
        Assert.Contains("stopped", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jason update apply", refused.Message, StringComparison.Ordinal);
        Assert.Contains("jason update status", refused.Message, StringComparison.Ordinal);

        // A refusal changes nothing: the update in flight is exactly where it was, and no feed was asked.
        Assert.Equal(inFlight.ToJson(), installation.Ledger()!.ToJson());
        Assert.Empty(installation.Fetched);
    }

    /// <summary>
    /// The same version is the same update, so a second <c>apply</c> carries it on from where the ledger stands
    /// rather than beginning again — which is what writing the ledger before each step is for.
    /// </summary>
    [Fact]
    public async Task A_second_apply_for_the_same_version_carries_on_from_the_step_it_reached()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var killed = installation.Killed(UpdateStep.Kept);
        var applier = installation.Applier();

        var ledger = await applier.ApplyAsync(new UpdateRequest(installation.Feed, installation.To, TimeSpan.Zero), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());

        // The same update and not a new one: the moment it began is the moment the killed run wrote down.
        Assert.Equal(killed.StartedAt, ledger.StartedAt);

        // And it began at the step the ledger named: nothing was fetched again, no runtime was drained again,
        // and what it says it did starts at the swap.
        Assert.Empty(installation.Fetched);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.Equal(["swapped", "started", "healthy"], applier.Steps.Select(Word).Take(3));
    }

    /// <summary>
    /// A pinned version is read from the release's own directory — the address both install scripts compose by
    /// hand for <c>--version</c> — and so is the archive that manifest names.
    /// </summary>
    /// <remarks>
    /// <c>…/releases/latest/download/</c> is a moving address: what arrives from it describes whatever is
    /// newest. Asking it for a particular version's manifest and then taking the archive from the moving
    /// address would compare one release's digest against another release's bytes.
    /// </remarks>
    [Fact]
    public async Task A_pinned_version_is_read_from_the_address_the_install_scripts_use()
    {
        using var installation = new FakeInstallation().WithRuntime();

        await installation.Applier()
            .ApplyAsync(new UpdateRequest(installation.Feed, installation.To, TimeSpan.Zero), Ct);

        var release = $"/releases/download/v{installation.To}/";
        Assert.Contains(release + ReleaseAssets.Manifest, installation.Fetched);
        Assert.Contains(release + installation.Release.Asset, installation.Fetched);
        Assert.All(installation.Fetched, address => Assert.StartsWith(release, address, StringComparison.Ordinal));
    }

    /// <summary>The first word of a step the applier reported, which is that step's own name.</summary>
    private static string Word(string step) => step.Split(' ', ':')[0];

    /// <summary>
    /// A path on a volume this machine's data directory is not on, or null where there is no such thing. On
    /// Windows a drive letter nothing is mounted on is one; on Unix there is no second root to name.
    /// </summary>
    private static string? OtherVolume()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var mounted = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        foreach (var letter in "ZYXWVUT")
        {
            if (!mounted.Contains(letter))
            {
                return letter + @":\";
            }
        }

        return null;
    }

    /// <summary>
    /// Which volume a path is on, decided from the machine's mount points rather than from the first character
    /// of the path — which is what a check built on the path root amounts to on Linux and macOS.
    /// </summary>
    /// <remarks>
    /// The mount points are given here rather than read from the machine, because a test that needs two volumes
    /// to prove a rule about two volumes can only run on a machine that happens to have them. The choosing is
    /// the part that was wrong; it is a pure function and this is it.
    /// </remarks>
    [Fact]
    public void Two_paths_under_different_mount_points_are_on_different_volumes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows names its volumes in the path itself, and the drive letter is the answer.");

        string[] mounts = ["/", "/mnt/data"];

        Assert.NotEqual(
            UpdateApplier.VolumeOf("/home/ada/.jason/update/staged/0.2.0/jason", mounts),
            UpdateApplier.VolumeOf("/mnt/data/programs/jason", mounts));

        // The longest mount point that really is a parent wins, and a name that merely begins with one does not.
        Assert.Equal("/mnt/data", UpdateApplier.VolumeOf("/mnt/data/programs/jason", mounts));
        Assert.Equal("/", UpdateApplier.VolumeOf("/mnt/database/programs/jason", mounts));
        Assert.Equal("/", UpdateApplier.VolumeOf("/home/ada/jason", mounts));
    }

    /// <summary>A file that is not a ledger is refused with its own code by every verb that reads one.</summary>
    /// <remarks>
    /// <c>jason update status</c> already answered this way; <c>apply</c> and <c>rollback</c> let the exception
    /// out, so the same broken file got an envelope from one verb and one bare line from the others.
    /// </remarks>
    [Fact]
    public async Task A_file_that_is_not_a_ledger_is_refused_with_its_code_by_apply_and_rollback()
    {
        using var installation = new FakeInstallation().WithRuntime();
        Directory.CreateDirectory(installation.Update.Root);
        File.WriteAllText(installation.Update.Ledger, "half a fi");

        foreach (var verb in (string[][])[["update", "apply", "--feed", installation.Feed.ToString()], ["update", "rollback"]])
        {
            installation.Out.GetStringBuilder().Clear();
            var exit = await CliApp.RunAsync(verb, installation.Env, Ct);

            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.Contains(UpdateLedgerException.Invalid, installation.Out.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The ledger is written before every step, and it is a file like any other: a directory in its place, a
    /// read-only update directory, and the write fails. Raw, that arrives as one line with no code — and the
    /// write before <c>swapped</c> happens while the install path is empty.
    /// </summary>
    [Fact]
    public async Task A_ledger_that_cannot_be_written_is_refused_with_a_code_that_names_it()
    {
        using var installation = new FakeInstallation().WithRuntime();

        // Something at the ledger's path that is not a file it can replace.
        Directory.CreateDirectory(installation.Update.Ledger);

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("ledger.json", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The window where the install path holds no executable is one step wide, and the write before
    /// <c>swapped</c> is the one that happens inside it. "Run <c>jason update apply</c> again" is useless
    /// advice there: <c>jason</c> is exactly what is missing. The applier's own copy is the program that
    /// exists, which is what the swap's own remedy has always said and what this one did not.
    /// </summary>
    [Theory]
    [InlineData(UpdateStep.Staged, false)]
    [InlineData(UpdateStep.Drained, false)]
    [InlineData(UpdateStep.Stopped, false)]
    [InlineData(UpdateStep.Kept, false)]
    [InlineData(UpdateStep.Swapped, true)]
    [InlineData(UpdateStep.Started, false)]
    [InlineData(UpdateStep.Healthy, false)]
    [InlineData(UpdateStep.Complete, false)]
    public void The_remedy_for_a_ledger_write_offers_the_applier_copy_only_inside_the_empty_window(UpdateStep step, bool offersTheCopy)
    {
        const string Root = "/home/ada/.jason/update";
        const string Copy = "/home/ada/.jason/update/applier/jason";

        var remedy = UpdateApplier.RemedyForRecording(step, Root, Copy);

        Assert.Equal(offersTheCopy, remedy.Contains(Copy, StringComparison.Ordinal));
        Assert.Contains(Root, remedy, StringComparison.Ordinal);
        Assert.Contains("jason update apply", remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The copy of the applier is put where a resumed update can run it — and that, too, is a directory to
    /// create and a file to copy, in the step before the install path is emptied.
    /// </summary>
    [Fact]
    public async Task An_applier_copy_that_cannot_be_written_is_refused_with_a_code()
    {
        using var installation = new FakeInstallation().WithRuntime();

        // A file where the directory has to be: creating it fails, and so would the copy into it.
        Directory.CreateDirectory(installation.Update.Root);
        File.WriteAllText(installation.Update.Applier, "not a directory");

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);

        // And it failed before anything was replaced, which is what the message promises.
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// A database that cannot be put back is a refusal with a code, and the runtime still comes up. The copy
    /// itself lands beside the database and is renamed onto it, so there is no state in which half a database
    /// sits at that path — which no putting-back could undo.
    /// </summary>
    [Fact]
    public async Task A_database_that_cannot_be_restored_is_refused_with_a_code_and_the_runtime_comes_back()
    {
        using var installation = new FakeInstallation { Migrates = true };
        installation.WithRuntime();
        await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        // Something at the database's path that no copy can replace.
        File.Delete(installation.Paths.DatabaseFile);
        Directory.CreateDirectory(installation.Paths.DatabaseFile);

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("state/jason.db", refused.Message.Replace('\\', '/'), StringComparison.Ordinal);

        // The half that is always safe happened, and the machine is not left dead by a refusal about a file.
        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.True(installation.Running, "a refusal about the database left the machine with nothing running");
        Assert.False(File.Exists(installation.Paths.DatabaseFile + ".restoring"), "half a database was left beside the real one");
    }
}
