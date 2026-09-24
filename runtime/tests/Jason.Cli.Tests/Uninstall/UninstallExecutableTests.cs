using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Uninstall;
using Jason.Contracts.Discovery;
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

    /// <summary>
    /// Which file this installation <em>is</em>, which is the question asked before the remover seam is
    /// reached — so no substituted seam can make a wrong answer safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build started as <c>dotnet jason.dll</c> has the muxer for its process path. Read raw, this verb
    /// planned to remove the muxer, take its directory off this account's PATH and — on Windows, where the
    /// image of a running process cannot be deleted but can be renamed — move it aside, which succeeds. On a
    /// developer's machine that is <c>C:\Program Files\dotnet\dotnet.exe</c>, and running from source
    /// through <c>dotnet</c> is what this repository's README tells them to do.
    /// </para>
    /// <para>
    /// The shape of such a command is asked of <see cref="SelfExecutable"/> rather than spelled here, for the
    /// reason the update verb's own muxer test gives: a test that wrote <c>["dotnet", "jason.dll"]</c> by
    /// hand would keep passing after that answer changed. The reproduction through the shipped program is in
    /// <c>Jason.App.Tests</c>, which can be a muxed process rather than describe one.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_build_run_through_the_muxer_is_installed_as_no_file_at_all()
    {
        var muxer = SelfExecutable.Resolve(
            OperatingSystem.IsWindows() ? @"C:\Program Files\dotnet\dotnet.exe" : "/usr/share/dotnet/dotnet",
            Path.Combine("opt", "jason", "jason.dll"));

        Assert.Null(SelfExecutable.Image(muxer));

        // And a published installation is exactly the file it is running, which is the file this verb removes.
        var published = Path.Combine("opt", "jason", "jason");
        Assert.Equal(published, SelfExecutable.Image(SelfExecutable.Resolve(published, "jason.dll")));
    }

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

    /// <summary>
    /// What a single-file build unpacked on its first run goes with it, after the executable — and the directory
    /// every build unpacks into goes too, once nothing is left in it.
    /// </summary>
    /// <remarks>
    /// Every uninstall on Windows left this behind, one directory per build under the system's temporary
    /// directory, and a Windows left to itself never clears that. Which directory is this build's own only the
    /// running process can say; that half is proved against a published executable, where there is one.
    /// </remarks>
    [Fact]
    public async Task What_the_build_unpacked_goes_after_the_executable()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var extracted = Unpacked(dir, "Xq7tzCSumrk");
        var log = new StepLog();

        var (exit, report) = await RunAsync(dir, executable, new RecordingRemover(log) { ReallyRemoves = true, ExtractedLibraries = extracted });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(extracted));
        Assert.False(Directory.Exists(Path.GetDirectoryName(extracted)!), "the directory builds unpack into was left empty.");
        Assert.Equal(extracted, report.Plan.ExtractedLibraries);
        Assert.Contains(report.Done, line => line.Contains(extracted, StringComparison.OrdinalIgnoreCase));
        Assert.True(
            log.Steps.IndexOf("executable.remove") < log.Steps.IndexOf("extracted.remove"),
            $"the unpacked libraries went before the executable: {string.Join(", ", log.Steps)}");
    }

    /// <summary>
    /// And another build's directory beside it is not this build's to remove: it stays, and so does the
    /// directory holding both, with the reason.
    /// </summary>
    [Fact]
    public async Task Another_builds_unpacked_libraries_are_kept()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var extracted = Unpacked(dir, "Xq7tzCSumrk");
        var other = Unpacked(dir, "L5GxzH7oZwh");

        var (exit, report) = await RunAsync(dir, executable, new RecordingRemover { ReallyRemoves = true, ExtractedLibraries = extracted });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(extracted));
        Assert.True(Directory.Exists(other), "another build's unpacked libraries were removed.");
        Assert.Contains(report.Kept, line => line.Contains("other builds", StringComparison.Ordinal));
    }

    /// <summary>
    /// A directory the remover named that would take the data directory with it is refused by the reader, and
    /// nothing is removed for it.
    /// </summary>
    /// <remarks>
    /// The belt on a recursive delete: a plan that could carry <c>~/.jason</c> in this field would be one bug
    /// away from purging it without the word.
    /// </remarks>
    [Theory]
    [InlineData("the data directory")]
    [InlineData("the install directory")]
    public async Task A_named_directory_holding_the_data_or_the_installation_is_never_planned(string holding)
    {
        // Two trees, so that each rule is tested alone: a directory holding one of them does not hold the other.
        using var dir = new TempPaths();
        using var elsewhere = new TempPaths();
        var executable = Install(elsewhere);
        var named = holding == "the data directory" ? dir.Paths.Root : Path.GetDirectoryName(executable)!;
        var remover = new RecordingRemover { ReallyRemoves = true, ExtractedLibraries = named };

        var (_, report) = await RunAsync(dir, executable, remover);

        Assert.Null(report.Plan.ExtractedLibraries);
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("extracted.remove", StringComparison.Ordinal));
    }

    /// <summary>
    /// The report is rendered once before the executable goes, and not only at the end.
    /// </summary>
    /// <remarks>
    /// A single-file build reads each assembly it has not loaded yet out of its own file, by the path it started
    /// from, and on Windows that path is gone once the running image is moved aside. A published build answered
    /// <c>--purge-data --yes</c> with nothing but "The type initializer for 'System.Text.Json.JsonSerializer'
    /// threw an exception." — after it had removed everything — because its report was the first JSON it wrote.
    /// The published executable is where that is proved end to end; this holds the order.
    /// </remarks>
    [Fact]
    public async Task The_report_is_rendered_before_the_executable_goes()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var log = new StepLog();
        var env = Environment(dir, executable, new RecordingRemover(log) { ReallyRemoves = true }, new StringWriter());
        var plan = UninstallReader.Read(env, purgeData: false);

        await UninstallRunner.RunAsync(
            env,
            plan,
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false),
            Ct,
            beforeTheImageGoes: _ => log.Add("rendered"));

        Assert.True(log.Steps.IndexOf("rendered") >= 0, "the report was never rendered before the executable step.");
        Assert.True(
            log.Steps.IndexOf("rendered") < log.Steps.IndexOf("executable.remove"),
            $"the report was rendered after the executable went: {string.Join(", ", log.Steps)}");
    }

    /// <summary>
    /// Something nobody planned for, part-way through, becomes the last problem of a report that still names
    /// every step that did happen — never an exception out of a verb that has already removed things.
    /// </summary>
    [Fact]
    public async Task A_failure_nobody_planned_for_is_reported_with_every_step_that_did_happen()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var remover = new RecordingRemover { ReallyRemoves = true };
        remover.Breaks.Add(executable);

        var (exit, report) = await RunAsync(dir, executable, remover);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(report.Completed);
        Assert.Contains(report.Done, line => line.Contains("logon registration", StringComparison.Ordinal));
        var problem = Assert.Single(report.Problems);
        Assert.Contains("stopped part-way", problem, StringComparison.Ordinal);
        Assert.Contains("could not load 'Some.Assembly'", problem, StringComparison.Ordinal);

        // Nothing after the step that failed was attempted: not even the line about the data directory.
        Assert.DoesNotContain(report.Kept, line => line.Contains("data directory", StringComparison.Ordinal));
    }

    /// <summary>
    /// And a report that cannot be written is written instead as lines on stderr, naming what was done, with
    /// exit 1: the document a script reads is not on stdout.
    /// </summary>
    [Fact]
    public async Task A_report_that_cannot_be_written_is_listed_on_stderr_instead()
    {
        using var dir = new TempPaths();
        var executable = Install(dir);
        var error = new StringWriter();
        var env = Environment(dir, executable, new RecordingRemover { ReallyRemoves = true }, new BrokenWriter()) with { Error = error };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(File.Exists(executable));
        var said = error.ToString();
        Assert.Contains("The report could not be written", said, StringComparison.Ordinal);
        Assert.Contains($"done: Removed the executable at '{executable}'.", said, StringComparison.Ordinal);
    }

    private static string Unpacked(TempPaths dir, string bundle)
    {
        var extracted = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "tmp", ".net", "jason", bundle)).FullName;
        File.WriteAllText(Path.Combine(extracted, "e_sqlite3.dll"), "not really a library");
        return extracted;
    }

    /// <summary>A standard output that fails on the first write, the way a report that cannot render does.</summary>
    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("this stream is gone.");
    }

    private static CliEnvironment Environment(TempPaths dir, string executable, RecordingRemover remover, TextWriter output) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: executable,
            Removes: remover);

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
        var env = Environment(dir, executable, remover, output);

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false),
            Ct);

        return (exit, JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!);
    }
}
