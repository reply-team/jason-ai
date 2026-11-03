using System.Text.Json;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// What a toast leaves in the account, taken away by <c>jason uninstall</c>: the AppUserModelID key the runtime's toast
/// was shown under, and the two entries Windows made for it at the first toast — after the runtime has stopped, so it
/// cannot write the key back. The notifications already in the notification center stay: they are the person's to
/// dismiss.
/// </summary>
public sealed class UninstallNotificationRegistrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CliEnvironment Machine(TempPaths dir, TextWriter output, RecordingRemover remover) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root + "-install", "jason"),
            Processes: new FakeProcessControl(),
            Removes: remover);

    [Fact]
    public async Task A_dry_run_names_the_keys_that_are_there_and_removes_nothing()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, NotificationKeys = [ToastRegistration.Key, ToastRegistration.WindowsSettingsKey] };

        var exit = await CliApp.RunAsync(["uninstall", "--dry-run", "--human"], Machine(dir, output, remover), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        var heading = text.IndexOf("This would be removed:", StringComparison.Ordinal);
        Assert.True(heading >= 0, text);
        Assert.Contains($@"the notification registration at HKCU\{ToastRegistration.Key}", text[heading..], StringComparison.Ordinal);
        Assert.Contains($@"the notification registration at HKCU\{ToastRegistration.WindowsSettingsKey}", text[heading..], StringComparison.Ordinal);
        Assert.DoesNotContain(ToastRegistration.BackupKey, text, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.Equal(2, remover.NotificationKeys.Count);
    }

    [Fact]
    public async Task Each_key_removed_is_reported_and_an_absent_one_is_not_mentioned()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, NotificationKeys = [ToastRegistration.Key, ToastRegistration.BackupKey] };

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.Contains($@"Removed the notification registration at HKCU\{ToastRegistration.Key}.", report.Done);
        Assert.Contains($@"Removed the notification registration at HKCU\{ToastRegistration.BackupKey}.", report.Done);
        Assert.DoesNotContain(ToastRegistration.WindowsSettingsKey, output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.NotificationKeys);
    }

    /// <summary>
    /// The step reads the keys again once the runtime has stopped: a runtime still running when the plan was read could
    /// show one more toast before it stopped, and Windows' entries for it are made then.
    /// </summary>
    [Fact]
    public async Task The_step_reads_the_keys_again_after_the_runtime_has_stopped()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover
        {
            ReallyRemoves = true,
            NotificationKeys = [ToastRegistration.Key],
            KeysWrittenAfterTheFirstRead = [ToastRegistration.WindowsSettingsKey],
        };

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.Contains($@"Removed the notification registration at HKCU\{ToastRegistration.WindowsSettingsKey}.", report.Done);
        Assert.Empty(remover.NotificationKeys);
    }

    /// <summary>A registry that cannot be read is said, in the plan and in the run: never taken for one with no keys.</summary>
    [Fact]
    public async Task A_registry_that_cannot_be_read_is_a_problem_not_an_absence()
    {
        using var dir = new TempPaths();
        var planned = new StringWriter();
        await UninstallCommand.RunAsync(
            Machine(dir, planned, new RecordingRemover { RegistryUnreadable = true }),
            new UninstallOptions(Human: true, DryRun: true, PurgeData: false, Yes: false, Force: false),
            Ct);
        Assert.Contains("the notification registration, which could not be read: the registry refused to be read", planned.ToString(), StringComparison.Ordinal);

        var output = new StringWriter();
        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, new RecordingRemover { ReallyRemoves = true, RegistryUnreadable = true }),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.Contains(report.Problems, problem => problem.StartsWith("The notification registration could not be read: the registry refused to be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_key_that_would_not_go_is_a_problem_and_the_verb_did_not_get_there()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, NotificationKeys = [ToastRegistration.Key, ToastRegistration.WindowsSettingsKey] };
        remover.Refuses.Add(ToastRegistration.Key);

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.Contains(report.Problems, problem => problem.Contains(ToastRegistration.Key, StringComparison.Ordinal));
        Assert.Contains($@"Removed the notification registration at HKCU\{ToastRegistration.WindowsSettingsKey}.", report.Done);
    }

    [Theory]
    [InlineData("on Windows, the notification registration: the AppUserModelID key the toast was shown under, the two entries Windows made for it")]
    [InlineData("the notifications already in the notification center, which are the person's to dismiss")]
    [InlineData("the logon registration, the notification registration and the agent harnesses")]
    public async Task The_help_says_what_it_takes_away_and_what_it_leaves(string published)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(published, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// One list of keys, named once: the runtime's toast host writes the first, and this verb reads all three from the
    /// same type. Read from the sources, because the two sides are in assemblies that never see each other.
    /// </summary>
    [Fact]
    public void The_runtime_and_the_uninstall_name_the_keys_from_one_list()
    {
        var root = RepositoryRoot();
        var remover = File.ReadAllText(Path.Combine(root, "dotnet", "src", "Jason.Cli", "Uninstall", "IInstallationRemover.cs"));
        var registration = File.ReadAllText(Path.Combine(root, "dotnet", "src", "Jason.Runtime", "Notifications", "Desktop", "AppUserModelIdRegistration.cs"));

        Assert.Contains("ToastRegistration.Keys", remover, StringComparison.Ordinal);
        Assert.Contains("ToastRegistration.Key,", registration, StringComparison.Ordinal);
        Assert.Equal(3, ToastRegistration.Keys.Count);
        Assert.Equal(ToastRegistration.Key, ToastRegistration.Keys[0]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
