using System.Text.Json;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The <c>jason:</c> link registration, taken away by <c>jason uninstall</c> in the step that takes the notification
/// registration: only where it carries Jason's marker and its command names this installation's executable. Another
/// installation's is named and kept, for its own uninstall; another program's is never touched.
/// </summary>
public sealed class UninstallSchemeRegistrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly UninstallOptions Run = new(Human: false, DryRun: false, PurgeData: false, Yes: false, Force: false);

    private static readonly UninstallOptions DryRun = new(Human: true, DryRun: true, PurgeData: false, Yes: false, Force: false);

    private const string Elsewhere = "\"C:\\Elsewhere\\jason.exe\" \"%1\"";

    private static string InstallPath(TempPaths dir) => Path.Combine(dir.Paths.Root + "-install", "jason");

    /// <summary>The command this installation's toast host writes.</summary>
    private static string Ours(TempPaths dir) => LinkRegistration.Command([InstallPath(dir)]);

    private static CliEnvironment Machine(TempPaths dir, TextWriter output, RecordingRemover remover) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: InstallPath(dir),
            Processes: new FakeProcessControl(),
            Removes: remover);

    private static UninstallReport Report(StringWriter output) =>
        JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;

    [Fact]
    public async Task A_dry_run_names_this_installation_s_link_and_removes_nothing()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, Link = new LinkRegistrationRead(LinkRegistration.Marker, Ours(dir)) };

        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), DryRun, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains($@"  the jason: link registration at HKCU\{LinkRegistration.Key}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT touched: the jason:", text, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.NotNull(remover.Link);
    }

    [Fact]
    public async Task This_installation_s_link_is_removed_and_reported()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, Link = new LinkRegistrationRead(LinkRegistration.Marker, Ours(dir)) };

        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains($@"Removed the jason: link registration at HKCU\{LinkRegistration.Key}.", Report(output).Done);
        Assert.Equal([Ours(dir)], remover.LinkRemovedFor);
        Assert.Null(remover.Link);
    }

    /// <summary>A key with Jason's marker and no command is one the toast host did not finish writing: it is ours.</summary>
    [Fact]
    public async Task Jason_s_link_with_no_command_is_removed()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, Link = new LinkRegistrationRead(LinkRegistration.Marker, null) };

        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains($@"Removed the jason: link registration at HKCU\{LinkRegistration.Key}.", Report(output).Done);
        Assert.Null(remover.Link);
    }

    /// <summary>
    /// Another installation on this account wrote the key last. A command other than this installation's is kept, since
    /// it may be another installation's or a spelling of this one.
    /// </summary>
    [Fact]
    public async Task Another_installation_s_link_is_named_and_kept()
    {
        using var dir = new TempPaths();
        var planned = new StringWriter();
        await UninstallCommand.RunAsync(
            Machine(dir, planned, new RecordingRemover { Link = new LinkRegistrationRead(LinkRegistration.Marker, Elsewhere) }),
            DryRun,
            Ct);
        Assert.Contains(
            $@"  NOT touched: the jason: link registration at HKCU\{LinkRegistration.Key}, which holds a command other than this installation's: {Elsewhere}",
            planned.ToString(),
            StringComparison.Ordinal);

        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, Link = new LinkRegistrationRead(LinkRegistration.Marker, Elsewhere) };
        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = Report(output);
        Assert.Contains($@"Kept the jason: link registration at HKCU\{LinkRegistration.Key}: it holds a command other than this installation's: {Elsewhere}", report.Kept);
        Assert.DoesNotContain(report.Done, line => line.Contains("jason: link", StringComparison.Ordinal));
        Assert.Empty(remover.LinkRemovedFor);
        Assert.NotNull(remover.Link);
    }

    /// <summary>A <c>jason</c> class without Jason's marker is another program's, and is never touched.</summary>
    [Fact]
    public async Task Another_program_s_link_is_never_touched()
    {
        using var dir = new TempPaths();
        var planned = new StringWriter();
        await UninstallCommand.RunAsync(
            Machine(dir, planned, new RecordingRemover { Link = new LinkRegistrationRead(null, Ours(dir)) }),
            DryRun,
            Ct);
        Assert.Contains(
            $@"  NOT touched: the jason: link registration at HKCU\{LinkRegistration.Key}, which is another program's",
            planned.ToString(),
            StringComparison.Ordinal);

        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, Link = new LinkRegistrationRead("another.program", Ours(dir)) };
        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(Report(output).Kept, line => line.StartsWith($@"Left the jason: link registration at HKCU\{LinkRegistration.Key}", StringComparison.Ordinal));
        Assert.Empty(remover.LinkRemovedFor);
    }

    [Fact]
    public async Task No_link_registration_is_never_mentioned()
    {
        using var dir = new TempPaths();
        var planned = new StringWriter();
        await UninstallCommand.RunAsync(Machine(dir, planned, new RecordingRemover()), DryRun, Ct);
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };

        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("jason: link", planned.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("jason: link", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.LinkRemovedFor);
    }

    /// <summary>The step reads the key again once the runtime has stopped: its last toast may have written it after the plan.</summary>
    [Fact]
    public async Task The_step_reads_the_link_again_after_the_runtime_has_stopped()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover
        {
            ReallyRemoves = true,
            LinkWrittenAfterTheFirstRead = new LinkRegistrationRead(LinkRegistration.Marker, Ours(dir)),
        };

        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains($@"Removed the jason: link registration at HKCU\{LinkRegistration.Key}.", Report(output).Done);
        Assert.Null(remover.Link);
    }

    [Fact]
    public async Task A_link_registration_that_cannot_be_read_is_a_problem_not_an_absence()
    {
        using var dir = new TempPaths();
        var planned = new StringWriter();
        await UninstallCommand.RunAsync(Machine(dir, planned, new RecordingRemover { LinkUnreadable = true }), DryRun, Ct);
        Assert.Contains("  the jason: link registration, which could not be read: the classes refused to be read", planned.ToString(), StringComparison.Ordinal);

        var output = new StringWriter();
        var exit = await UninstallCommand.RunAsync(Machine(dir, output, new RecordingRemover { ReallyRemoves = true, LinkUnreadable = true }), Run, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(Report(output).Problems, problem => problem.StartsWith("The jason: link registration could not be read: the classes refused to be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_link_registration_that_would_not_go_is_a_problem()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true, Link = new LinkRegistrationRead(LinkRegistration.Marker, Ours(dir)) };
        remover.Refuses.Add(LinkRegistration.Key);

        var exit = await UninstallCommand.RunAsync(Machine(dir, output, remover), Run, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(Report(output).Problems, problem => problem.StartsWith($@"'HKCU\{LinkRegistration.Key}' could not be removed:", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one decision, made by the plan and made again by the remover just before the delete: Jason's marker and this
    /// installation's command, or no command at all, is removed; Jason's marker with another command is kept; anything
    /// without the marker is untouched; no key is nothing.
    /// </summary>
    [Theory]
    [InlineData(LinkRegistration.Marker, Ours_, LinkRegistrationFate.Removed)]
    [InlineData(LinkRegistration.Marker, null, LinkRegistrationFate.Removed)]
    [InlineData(LinkRegistration.Marker, Elsewhere, LinkRegistrationFate.Kept)]
    [InlineData(LinkRegistration.Marker, "\"C:\\INSTALL\\JASON.EXE\" \"%1\"", LinkRegistrationFate.Removed)]
    [InlineData(null, Ours_, LinkRegistrationFate.Untouched)]
    [InlineData("AI.JASON.RUNTIME", Ours_, LinkRegistrationFate.Untouched)]
    [InlineData("another.program", null, LinkRegistrationFate.Untouched)]
    public void The_fate_of_a_link_registration_is_one_decision(string? marker, string? command, LinkRegistrationFate fate) =>
        Assert.Equal(fate, LinkRegistrationPlan.Of(new LinkRegistrationRead(marker, command), Ours_)?.Fate);

    private const string Ours_ = "\"C:\\Install\\jason.exe\" \"%1\"";

    [Fact]
    public void No_key_is_no_plan() => Assert.Null(LinkRegistrationPlan.Of(null, Ours_));

    /// <summary>
    /// The remover's own check just before the delete: the same decision, read again, and anything but Removed refused, so
    /// a key another installation or another program wrote between the plan and the step is never deleted.
    /// </summary>
    [Theory]
    [InlineData(true, LinkRegistration.Marker, Ours_, true)]
    [InlineData(true, LinkRegistration.Marker, Elsewhere, false)]
    [InlineData(true, "another.program", Ours_, false)]
    [InlineData(false, null, null, false)]
    public void The_remover_deletes_only_what_the_decision_removes(bool key, string? marker, string? command, bool removes)
    {
        var read = key ? new LinkRegistrationRead(marker, command) : null;

        var refused = Record.Exception(() => LinkRegistrationPlan.Require(read, Ours_));

        if (removes)
        {
            Assert.Null(refused);
        }
        else
        {
            Assert.Equal(CliErrors.UninstallRefused, Assert.IsType<RemovalRefused>(refused).Code);
        }
    }

    /// <summary>
    /// An installation whose path holds a <c>%</c> writes no link, so a key of ours naming any command is another
    /// installation's, and one naming none is a half-written key of ours.
    /// </summary>
    [Theory]
    [InlineData(Ours_, LinkRegistrationFate.Kept)]
    [InlineData(null, LinkRegistrationFate.Removed)]
    public void An_installation_that_writes_no_link_keeps_a_command_and_removes_none(string? command, LinkRegistrationFate fate) =>
        Assert.Equal(fate, LinkRegistrationPlan.Of(new LinkRegistrationRead(LinkRegistration.Marker, command), null)?.Fate);

    [Fact]
    public void This_installation_s_command_is_none_where_its_path_holds_a_percent() =>
        Assert.Null(UninstallReader.LinkCommand(new CliEnvironment(new StringWriter(), new StringWriter(), new JasonPaths(Path.GetTempPath()), InstallPath: @"C:\Users\Ann%1x\Jason\jason.exe")));

    [Theory]
    [InlineData("on Windows, the notification registration: the AppUserModelID key the toast was shown under, the two entries Windows made for it, and the jason: link registration where its command names this executable")]
    [InlineData("a jason: link registration another program owns, and one whose command is other than this installation's: it may be another installation's or a spelling of this one.")]
    [InlineData("This account's, not this installation's: the logon registration, the notification registration and the agent harnesses. Uninstalling a second installation on one account takes the first one's registrations and harness skills too. The jason: link goes only where its command names this executable.")]
    public async Task The_help_says_what_it_takes_away_and_what_it_leaves(string published)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(published, text, StringComparison.Ordinal);
    }
}
