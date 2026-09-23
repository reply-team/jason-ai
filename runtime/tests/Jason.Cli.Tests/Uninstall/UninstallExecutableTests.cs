using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The last step, and the one that can honestly half succeed.
/// </summary>
/// <remarks>
/// Windows will not delete the image of a running process, and this verb is ordinarily run from the very file
/// it is removing. Whatever happens, the report says what really became of it — a verb that claimed a clean
/// uninstall it did not perform would be worse than one that left the file behind and said so.
/// </remarks>
public class UninstallExecutableTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_executable_and_its_directory_go()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);

        var (exit, report) = await RunAsync(dir, executable, new RecordingRemover { ReallyRemoves = true });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(executable));
        Assert.False(Directory.Exists(Path.GetDirectoryName(executable)!), "an empty install directory was left behind.");
        Assert.Contains(report.Done, line => line.Contains("Removed the executable", StringComparison.Ordinal));
    }

    /// <summary>
    /// A file in the install directory that the installer did not put there keeps the directory. Somebody put
    /// it there on purpose, and an uninstall is not the moment to decide it did not matter.
    /// </summary>
    [Fact]
    public async Task A_directory_holding_something_else_is_kept_and_reported()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var theirs = Path.Combine(Path.GetDirectoryName(executable)!, "notes.txt");
        File.WriteAllText(theirs, "mine");

        var (exit, report) = await RunAsync(dir, executable, new RecordingRemover { ReallyRemoves = true });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(executable));
        Assert.True(File.Exists(theirs), "a file the installer never wrote went with the directory.");
        Assert.Contains(report.Kept, line => line.Contains("did not write", StringComparison.Ordinal));
    }

    /// <summary>
    /// Where the file is the image of the running process it is moved out of the install directory instead —
    /// so the installation is off the machine, the directory goes, and the report names where the one
    /// remaining copy went.
    /// </summary>
    [Fact]
    public async Task A_running_image_is_moved_aside_and_the_report_names_where_it_went()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var aside = Path.Combine(dir.Paths.Root, "elsewhere", "jason");

        var (exit, report) = await RunAsync(
            dir,
            executable,
            new RecordingRemover { ReallyRemoves = true, CanRemoveRunningImage = false, MovesTo = aside });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(executable), "the install directory still holds the executable.");
        Assert.True(File.Exists(aside), "it was reported moved and is not there.");
        Assert.False(Directory.Exists(Path.GetDirectoryName(executable)!));
        Assert.Contains(report.Kept, line => line.Contains(aside, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// And where it cannot go at all, the verb says so, names the file, and exits 1. Everything else is
    /// already gone, and saying otherwise would be the one lie this verb must not tell.
    /// </summary>
    [Fact]
    public async Task A_file_that_cannot_be_removed_is_named_and_the_verb_does_not_claim_success()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var remover = new RecordingRemover { ReallyRemoves = true };
        remover.Refuses.Add(executable);

        var (exit, report) = await RunAsync(dir, executable, remover);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(report.Completed);
        Assert.True(File.Exists(executable));
        Assert.Contains(report.Problems, problem => problem.Contains(executable, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(report.Done, line => line.Contains("Removed the executable", StringComparison.Ordinal));
    }

    /// <summary>An executable somebody has already removed is not an error: there is nothing to do.</summary>
    [Fact]
    public async Task An_executable_that_is_already_gone_is_not_a_failure()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        File.Delete(executable);

        var (exit, _) = await RunAsync(dir, executable, new RecordingRemover { ReallyRemoves = true });

        Assert.Equal(ExitCodes.Success, exit);
    }

    private static string Install(TempPaths dir)
    {
        var directory = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "bin")).FullName;
        var executable = Path.Combine(directory, "jason");
        File.WriteAllText(executable, "not really a program");
        return executable;
    }

    private static async Task<(int Exit, UninstallReport Report)> RunAsync(TempPaths dir, string executable, RecordingRemover remover)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: executable,
            Removes: remover);

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false),
            Ct);

        return (exit, JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!);
    }
}
