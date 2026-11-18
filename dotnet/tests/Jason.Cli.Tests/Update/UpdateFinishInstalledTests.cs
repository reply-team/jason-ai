using System.Net;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Skills;
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
        Assert.True(first.InstallationFound);
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
        Assert.False(report.InstallationFound);
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

    /// <summary>
    /// Jason Desktop's first start: the data directory holds its own folder, <c>desktop/mode</c>, and maybe its log, and
    /// nothing of an installation. The real verb reads no release, takes no lock and makes nothing: the listing after is the
    /// listing before, with no <c>update/</c>, and the feed was asked nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Critical(Family.I2, "the finishing step reads no release and makes nothing where the data directory holds only the Desktop's own")]
    public async Task With_only_the_desktops_own_folder_nothing_is_read_or_made(bool withItsLog)
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        Directory.Delete(installation.Paths.Root, recursive: true);
        Directory.CreateDirectory(Path.Combine(installation.Paths.Root, "desktop"));
        File.WriteAllText(Path.Combine(installation.Paths.Root, "desktop", "mode"), "demo");
        if (withItsLog)
        {
            Directory.CreateDirectory(installation.Paths.LogsDirectory);
            File.WriteAllText(Path.Combine(installation.Paths.LogsDirectory, "desktop-20261117.jsonl"), "{}\n");
        }

        var before = Listing(installation.Paths.Root);

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = JsonSerializer.Deserialize<UpdateFinishResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.True(report.DataDirFound);
        Assert.False(report.InstallationFound);
        Assert.False(report.Complete);
        Assert.All(report.Parts, part =>
        {
            Assert.Equal(UpdateFinishStates.NotDone, part.State);
            Assert.Contains("holds nothing of an installation but Jason Desktop's own", part.Reason, StringComparison.Ordinal);
        });
        Assert.Equal(before, Listing(installation.Paths.Root));
        Assert.False(Directory.Exists(Path.Combine(installation.Paths.Root, "update")), "the finishing step made the update folder");
        Assert.Empty(installation.Release.Requested);
    }

    /// <summary>
    /// An installation whose data directory has no <c>update/</c> folder yet: the finishing step writes into the installation,
    /// so it takes the update's lock all the same, making the folder for it, and holds it while it reads the release; an update
    /// beside it could not take it in that moment. Only a data directory holding nothing but the Desktop's own is left as it is.
    /// </summary>
    [Fact]
    public async Task An_installation_with_no_update_folder_is_finished_under_the_lock()
    {
        var (installation, desktop) = FakeDesktop.Carried(to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        var update = new UpdatePaths(installation.Paths);
        if (Directory.Exists(update.Root))
        {
            Directory.Delete(update.Root, recursive: true);
        }

        var heldWhileAsked = new List<bool>();
        installation.Release.OnRequest = _ => heldWhileAsked.Add(Held(update.Lock));

        var report = await FinishAsync(installation);

        Assert.True(report.DataDirFound);
        Assert.True(report.InstallationFound);
        Assert.NotEmpty(heldWhileAsked);
        Assert.All(heldWhileAsked, held => Assert.True(held, "the release was read with the update's lock not held"));
        Assert.False(Held(update.Lock), "the lock outlived the finishing step");
    }

    /// <summary>
    /// A data directory that holds nothing but the Desktop's own, as a fresh machine's first start leaves it: <c>desktop/mode</c>
    /// and nothing else.
    /// </summary>
    private static void DesktopOnly(FakeInstallation installation)
    {
        Directory.Delete(installation.Paths.Root, recursive: true);
        Directory.CreateDirectory(Path.Combine(installation.Paths.Root, "desktop"));
        File.WriteAllText(Path.Combine(installation.Paths.Root, "desktop", "mode"), "demo");
    }

    /// <summary>
    /// The update's lock file is the tool's own by-product: an <c>update/</c> holding it and nothing else, beside the Desktop's
    /// own, is no installation. Nothing is read or made, the lock file is not taken, and the mark says no installation.
    /// </summary>
    [Fact]
    public async Task A_lone_update_lock_beside_the_desktops_own_is_no_installation_and_nothing_is_read_or_made()
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        DesktopOnly(installation);
        var update = new UpdatePaths(installation.Paths);
        Directory.CreateDirectory(update.Root);
        File.WriteAllText(update.Lock, string.Empty);
        var before = Listing(installation.Paths.Root);

        var report = await FinishAsync(installation);

        Assert.True(report.DataDirFound);
        Assert.False(report.InstallationFound);
        Assert.All(report.Parts, part => Assert.Contains("holds nothing of an installation but Jason Desktop's own", part.Reason, StringComparison.Ordinal));
        Assert.Equal(before, Listing(installation.Paths.Root));
        Assert.Empty(installation.Release.Requested);
    }

    /// <summary>
    /// The one definition of "an installation is here", entry by entry: the Desktop's own, a lone lock file and the system's
    /// litter at the top and in <c>logs/</c> are none; a record under <c>update/</c>, an entry nobody named, litter anywhere
    /// else, or a folder in the place of a log, is one.
    /// </summary>
    [Theory]
    [InlineData("desktop/mode", false)]
    [InlineData("desktop/rollback/0.48.3/Jason.exe", false)]
    [InlineData("logs/desktop-20261117.jsonl", false)]
    [InlineData("update/update.lock", false)]
    [InlineData("update/update.lock.holder", false)]
    [InlineData(".DS_Store", false)]
    [InlineData("desktop.ini", false)]
    [InlineData("Thumbs.db", false)]
    [InlineData("logs/.DS_Store", false)]
    [InlineData("logs/desktop.ini", false)]
    [InlineData("logs/Thumbs.db", false)]
    [InlineData("update/ledger.json", true)]
    [InlineData("update/desktop-req_01JA.json", true)]
    [InlineData("update/.DS_Store", true)]
    [InlineData("logs/runtime-20261117.jsonl", true)]
    [InlineData("logs/desktop-20261117.jsonl/inside", true)]
    [InlineData("notes.txt", true)]
    [InlineData("thumbs.db", true)]
    [InlineData("jason.db", true)]
    [InlineData("skills/roles/researcher/SKILL.md", true)]
    public void An_installation_is_anything_but_the_desktops_own_a_lone_lock_and_the_systems_litter(string entry, bool installed)
    {
        using var paths = new TempPaths();
        var root = paths.Paths.Root;
        Directory.CreateDirectory(Path.Combine(root, "desktop"));
        File.WriteAllText(Path.Combine(root, "desktop", "mode"), "demo");
        var file = Path.Combine(root, entry);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Empty);
        if (!OperatingSystem.IsWindows() && entry == "thumbs.db")
        {
            // Only a system whose names differ by case can hold this beside the one the litter names; the name is never folded.
            Assert.Equal(UpdateCommands.DataDirHolds.Here, UpdateCommands.InstallationAt(root));
            return;
        }

        Assert.Equal(installed ? UpdateCommands.DataDirHolds.Here : UpdateCommands.DataDirHolds.DesktopsOwnOnly, UpdateCommands.InstallationAt(root));
    }

    /// <summary>
    /// Fail closed: a data directory, or its <c>logs/</c> or <c>update/</c>, this account cannot list is refused before the
    /// lock, retryable, and never taken for an installation: nothing is made, no release read.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("logs")]
    [InlineData("update")]
    public async Task A_data_directory_that_cannot_be_listed_is_refused_before_the_lock_and_nothing_is_read_or_made(string folder)
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        DesktopOnly(installation);
        var denied = Path.Combine(installation.Paths.Root, folder);
        Directory.CreateDirectory(denied);
        if (folder == "update")
        {
            File.WriteAllText(Path.Combine(denied, "update.lock"), string.Empty);
        }

        var before = Listing(installation.Paths.Root);
        using var unlistable = Unlistable.Make(denied);
        Assert.SkipWhen(unlistable is null, "This account lists a folder whatever its mode says (root, say).");

        var exit = await CliApp.RunAsync(["update", "apply", "--finish", "--feed", installation.Feed.AbsoluteUri], installation.Machine!, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal(UpdateCodes.DataDirUnreadable, refusal.Code);
        Assert.True(refusal.Retryable);
        Assert.Contains(installation.Paths.Root, refusal.Message, StringComparison.Ordinal);
        unlistable!.Dispose();
        Assert.Equal(before, Listing(installation.Paths.Root));
        Assert.Empty(installation.Release.Requested);
    }

    /// <summary>
    /// A machine whose person typed <c>jason install --roles</c> after the Desktop's first start holds an installation: the
    /// role skills. The finishing step then finds it, runs, and completes, so the set-up does not run again at every start.
    /// </summary>
    [Fact]
    public async Task After_install_with_roles_on_the_desktops_own_directory_the_finishing_step_completes()
    {
        var (installation, desktop) = FakeDesktop.Carried(withRuntime: false, to: SemanticVersion.Current);
        using var _ = installation;
        using var __ = desktop;
        DesktopOnly(installation);
        // The skills the application carries beside its jason, as a release lays them out.
        var bundle = Path.GetDirectoryName(installation.InstallPath)!;
        foreach (var (file, text) in SkillsPackage.Files($"v{SemanticVersion.Current}").Where(entry => entry.Name.StartsWith("skills/", StringComparison.Ordinal)))
        {
            var path = Path.Combine(bundle, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        var installed = await CliApp.RunAsync(["install", "--roles"], installation.Machine!, Ct);
        Assert.True(installed == ExitCodes.Success, installation.Out.ToString());
        Assert.Equal(UpdateCommands.DataDirHolds.Here, UpdateCommands.InstallationAt(installation.Paths.Root));
        installation.Out.GetStringBuilder().Clear();

        var report = await FinishAsync(installation);

        Assert.True(report.InstallationFound);
        Assert.True(report.Complete, string.Join(" | ", report.Parts.Select(part => $"{part.Part}: {part.State} {part.Reason}")));
        Assert.All(report.Parts, part => Assert.NotEqual(UpdateFinishStates.NotDone, part.State));
    }

    /// <summary>A folder this account cannot list, for as long as it lives; null where the account lists it all the same.</summary>
    private sealed class Unlistable : IDisposable
    {
        private readonly Action _undo;
        private bool _undone;

        private Unlistable(Action undo) => _undo = undo;

        public static Unlistable? Make(string folder)
        {
            var made = OperatingSystem.IsWindows() ? Denied(folder) : Closed(folder);
            try
            {
                _ = Directory.EnumerateFileSystemEntries(folder).Any();
            }
            catch (UnauthorizedAccessException)
            {
                return made;
            }

            made.Dispose();
            return null;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static Unlistable Denied(string folder)
        {
            var info = new DirectoryInfo(folder);
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.ListDirectory,
                System.Security.AccessControl.AccessControlType.Deny);
            var security = info.GetAccessControl();
            security.AddAccessRule(rule);
            info.SetAccessControl(security);
            return new Unlistable(() => Allowed(info, rule));
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void Allowed(DirectoryInfo info, System.Security.AccessControl.FileSystemAccessRule rule)
        {
            var security = info.GetAccessControl();
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static Unlistable Closed(string folder)
        {
            var mode = File.GetUnixFileMode(folder);
            File.SetUnixFileMode(folder, UnixFileMode.None);
            return new Unlistable(() => Opened(folder, mode));
        }

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static void Opened(string folder, UnixFileMode mode) => File.SetUnixFileMode(folder, mode);

        public void Dispose()
        {
            if (!_undone)
            {
                _undone = true;
                _undo();
            }
        }
    }

    /// <summary>Whether another process holds the lock: an open that shares nothing is refused while it does.</summary>
    private static bool Held(string file)
    {
        if (!File.Exists(file))
        {
            return false;
        }

        try
        {
            using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static string[] Listing(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(entry => Path.GetRelativePath(root, entry)).Order(StringComparer.Ordinal)];

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
        Assert.Equal(["version", "data_dir", "data_dir_found", "installation_found", "complete", "parts"], document.RootElement.EnumerateObject().Select(member => member.Name));
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
