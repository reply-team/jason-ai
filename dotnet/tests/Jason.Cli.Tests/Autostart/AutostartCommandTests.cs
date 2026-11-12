using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// The three verbs from the side they are typed on: what goes to the machine, what comes back on stdout, and
/// that it is exactly one document either way.
/// </summary>
/// <remarks>
/// Every one of these runs through the real command line against a recording registrar. Nothing here registers
/// anything: the machine is the recorder, and an environment that named no registrar would refuse rather than
/// reach for this one.
/// </remarks>
public class AutostartCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Installed = @"C:\Users\ada\AppData\Local\Programs\jason\jason.exe";

    /// <summary>
    /// Registering twice leaves one registration. A person who runs it again after an update, or a script that
    /// runs it every time, must not end up with two things starting at logon.
    /// </summary>
    [Fact]
    [Critical(Family.I4, "enable is idempotent")]
    public async Task Enabling_twice_leaves_one_registration()
    {
        using var machine = new Machine();

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "enable"], Ct));
        Assert.True(machine.Answer().Registered);

        machine.Clear();
        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "enable"], Ct));
        Assert.True(machine.Answer().Registered);

        // Applied twice, and a machine holds one registration per account: the second replaces the first.
        Assert.Equal(2, machine.Registrar.Applied.Count);
        Assert.Single(machine.Registrar.Applied.Select(registration => registration.Name).Distinct(StringComparer.Ordinal));
        Assert.True(machine.Registrar.State.Registered);
    }

    /// <summary>
    /// D33, and the reason autostart is a verb rather than a documented `schtasks` line: the registration
    /// carries the data directory it was made under. An operator working under a JASON_DATA_DIR who registered
    /// without it would get a background runtime on ~/.jason and two runtimes disagreeing about which data is
    /// real.
    /// </summary>
    [Fact]
    public async Task The_registration_carries_the_data_directory_it_was_made_under()
    {
        using var machine = new Machine();

        await machine.RunAsync(["runtime", "autostart", "enable"], Ct);

        var registered = Assert.Single(machine.Registrar.Applied);
        Assert.Equal(
            [Installed, "runtime", "run", "--detached", "--data-dir", machine.Paths.Root],
            registered.Run);
        Assert.Equal(Path.GetFullPath(machine.Paths.Root), Path.GetFullPath(registered.Run[^1]));

        var answer = machine.Answer();
        Assert.Equal(machine.Paths.Root, answer.DataDirectory);
        Assert.Equal(Installed, answer.Executable);
        Assert.Equal("windows", answer.Platform);
    }

    /// <summary>What a machine holds is what `status` prints, even where this installation would register something else.</summary>
    [Fact]
    public async Task Status_prints_the_registration_the_machine_holds_and_not_the_one_this_session_would_make()
    {
        using var machine = new Machine();
        machine.Registrar.State = new AutostartState(
            true,
            ["/opt/jason/jason", "runtime", "run", "--detached", "--data-dir", "/srv/jason"],
            "/etc/somewhere/jason.service");

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "status"], Ct));

        var answer = machine.Answer();
        Assert.True(answer.Registered);
        Assert.Equal("/srv/jason", answer.DataDirectory);
        Assert.Equal("/opt/jason/jason", answer.Executable);
        Assert.Equal("/etc/somewhere/jason.service", answer.ArtifactPath);
        Assert.Empty(machine.Registrar.Applied);
    }

    /// <summary>
    /// And whether that executable is still there, which is how a registration rots: an update that moved the
    /// binary, or a directory somebody deleted, leaves a logon task that fails every morning in silence.
    /// </summary>
    [Fact]
    public async Task Status_says_whether_the_registered_executable_still_exists()
    {
        using var machine = new Machine();
        machine.Registrar.State = new AutostartState(
            true,
            [Path.Combine(machine.Paths.Root, "gone", "jason.exe"), "runtime", "run"],
            null);

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "status"], Ct));
        Assert.False(machine.Answer().ExecutableExists);

        // And the other way round, against a file that is really there.
        var real = Path.Combine(machine.Paths.Root, "jason.exe");
        Directory.CreateDirectory(machine.Paths.Root);
        await File.WriteAllTextAsync(real, "a program", Ct);
        machine.Registrar.State = new AutostartState(true, [real, "runtime", "run"], null);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status"], Ct);
        Assert.True(machine.Answer().ExecutableExists);
    }

    /// <summary>
    /// Windows keeps no document once the task is made — the Task Scheduler took a copy — so the registrar
    /// names none. The file that was handed over is still under this data directory, and that is worth printing
    /// while it is there: the page names that path, and a person looking at a task wants to read what was
    /// registered. A registration made from another data directory left its file somewhere else, and this says
    /// nothing rather than pointing at a file that is not the one in force.
    /// </summary>
    [Fact]
    public async Task Status_prints_the_document_that_was_handed_over_while_it_is_there()
    {
        using var machine = new Machine();
        machine.Registrar.State = new AutostartState(true, [Installed, "runtime", "run"], null);

        // Nothing under this data directory yet: another installation's registration, as far as this one knows.
        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "status"], Ct));
        Assert.Null(machine.Answer().ArtifactPath);

        // And now the file this data directory's own `enable` would have handed over.
        var handed = AutostartArtifacts.ArtifactPath(AutostartPlatform.Windows, "/home/unused", machine.Paths.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(handed)!);
        await File.WriteAllTextAsync(handed, "<Task />", Ct);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status"], Ct);
        Assert.Equal(handed, machine.Answer().ArtifactPath);
    }

    [Fact]
    public async Task Status_on_a_machine_with_nothing_registered_says_so_and_is_not_an_error()
    {
        using var machine = new Machine();

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "status"], Ct));

        var answer = machine.Answer();
        Assert.False(answer.Registered);
        Assert.Null(answer.DataDirectory);
        Assert.Null(answer.Executable);
        Assert.Null(answer.ExecutableExists);
    }

    [Fact]
    public async Task Disabling_something_that_was_never_registered_is_not_an_error()
    {
        using var machine = new Machine();

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "disable"], Ct));

        Assert.False(machine.Answer().Registered);
        Assert.Equal(1, machine.Registrar.Removals);
    }

    [Fact]
    public async Task Disabling_twice_is_not_an_error()
    {
        using var machine = new Machine();
        await machine.RunAsync(["runtime", "autostart", "enable"], Ct);

        machine.Clear();
        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "disable"], Ct));
        Assert.False(machine.Answer().Registered);

        machine.Clear();
        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "disable"], Ct));
        Assert.False(machine.Answer().Registered);
        Assert.Equal(2, machine.Registrar.Removals);
    }

    /// <summary>A machine this product cannot register anything on says so, with a code, and registers nothing.</summary>
    [Fact]
    public async Task An_unsupported_platform_refuses_with_a_code()
    {
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.Unsupported));

        Assert.Equal(ExitCodes.ApiError, await machine.RunAsync(["runtime", "autostart", "enable"], Ct));

        var refusal = machine.Only<ErrorResponse>();
        Assert.Equal(AutostartCodes.Unsupported, refusal.Error.Code);
        Assert.Contains("jason runtime start", refusal.Error.Message, StringComparison.Ordinal);
        Assert.Empty(machine.Registrar.Applied);
    }

    /// <summary>
    /// And an environment that names no registrar at all gets the same answer, which is the fail-closed half of
    /// this seam: the two guards in this repository that type documented command lines for real would otherwise
    /// register a logon task on whatever machine the tests are running on.
    /// </summary>
    [Fact]
    public async Task An_environment_that_names_no_registrar_refuses_rather_than_asking_this_machine()
    {
        using var directory = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["runtime", "autostart", "enable"],
            new CliEnvironment(output, new StringWriter(), directory.Paths),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = JsonSerializer.Deserialize<ErrorResponse>(output.ToString().Trim(), JasonJson.Options)!;
        Assert.Equal(AutostartCodes.Unsupported, refusal.Error.Code);
    }

    /// <summary>A tool that refuses is reported with a code and with what the tool itself said.</summary>
    [Fact]
    public async Task A_registrar_that_refuses_is_reported_with_the_tool_s_own_words()
    {
        using var machine = new Machine();
        machine.Registrar.Refuses = new AutostartException(
            AutostartCodes.Refused,
            "schtasks refused: ERROR: Access is denied.");

        Assert.Equal(ExitCodes.ApiError, await machine.RunAsync(["runtime", "autostart", "enable"], Ct));

        var refusal = machine.Only<ErrorResponse>();
        Assert.Equal(AutostartCodes.Refused, refusal.Error.Code);
        Assert.Contains("Access is denied", refusal.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And `disable` after a refused `enable` still succeeds. It is what a person does next — the registration
    /// was refused, so they check that nothing was left half-done — and a second failure would tell them the
    /// opposite of what is true. Nothing here is red: it is the sentence on the page held in place.
    /// </summary>
    [Fact]
    public async Task Disabling_after_a_refused_enable_still_succeeds()
    {
        using var machine = new Machine();
        machine.Registrar.Refuses = new AutostartException(
            AutostartCodes.Refused,
            "this machine's Task Scheduler refused to register a logon task for this account. Nothing was registered or removed.");

        Assert.Equal(ExitCodes.ApiError, await machine.RunAsync(["runtime", "autostart", "enable"], Ct));

        machine.Clear();
        machine.Registrar.Refuses = null;

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "disable"], Ct));
        Assert.False(machine.Answer().Registered);
    }

    /// <summary>Every verb prints one document, whatever happened: the CLI's whole contract with a script.</summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("status")]
    public async Task Every_verb_prints_exactly_one_document(string verb)
    {
        using var machine = new Machine();

        await machine.RunAsync(["runtime", "autostart", verb], Ct);

        Assert.Single(machine.Lines());
    }

    /// <summary>And renders for people instead when it is asked to, with no JSON in it.</summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("status")]
    public async Task Every_verb_renders_for_people_when_it_is_asked_to(string verb)
    {
        using var machine = new Machine();

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", verb, "--human"], Ct));

        var printed = Assert.Single(machine.Lines());
        Assert.DoesNotContain("{", printed, StringComparison.Ordinal);
        Assert.Contains("logon", printed, StringComparison.Ordinal);
    }

    /// <summary>With nothing registered, `status` says "No logon registration." in the same words `jason status` uses.</summary>
    [Fact]
    public async Task Status_with_nothing_registered_says_no_logon_registration()
    {
        using var machine = new Machine();

        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);

        Assert.Equal("No logon registration.", Assert.Single(machine.Lines()));
    }

    /// <summary>A machine that cannot register anything says that too, in the same vocabulary.</summary>
    [Fact]
    public async Task Status_on_a_machine_that_cannot_register_says_so()
    {
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.Unsupported));

        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);

        Assert.Equal("No logon registration, and this machine has no way to register one.", Assert.Single(machine.Lines()));
    }

    /// <summary>
    /// On macOS the registration carries the search path of the process that made it, since launchd's own holds no program
    /// a package manager installed; the body says which it carries, and the human line says it too.
    /// </summary>
    [Fact]
    public async Task On_macos_the_registration_carries_the_search_path_it_was_made_with()
    {
        const string Searched = "/opt/homebrew/bin:/Users/ada/.local/bin:/usr/bin:/bin";
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.MacOs), Searched);

        await machine.RunAsync(["runtime", "autostart", "enable"], Ct);

        Assert.Equal(Searched, Assert.Single(machine.Registrar.Applied).SearchPath);
        Assert.Equal(Searched, machine.Answer().Path);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);

        // The executable named here is not on this machine, so the line goes on to say so after the search path.
        Assert.StartsWith(
            $"The runtime starts at logon from {Installed}, on the data directory {machine.Paths.Root}, with the search path {Searched}. ",
            Assert.Single(machine.Lines()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A LaunchAgent that carries no search path, one an earlier version registered, starts the runtime on launchd's own,
    /// and the human line names it, which is what tells the person to register again.
    /// </summary>
    [Fact]
    public async Task A_launch_agent_with_no_search_path_is_said_to_start_on_launchd_s_own()
    {
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.MacOs));
        machine.Registrar.State = new AutostartState(true, [Installed, "runtime", "run", "--detached", "--data-dir", machine.Paths.Root], null);

        await machine.RunAsync(["runtime", "autostart", "status"], Ct);
        Assert.Null(machine.Answer().Path);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);

        Assert.Contains(
            $", on the data directory {machine.Paths.Root}, with launchd's default search path, /usr/bin:/bin:/usr/sbin:/sbin.",
            Assert.Single(machine.Lines()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// On Linux the user unit carries the search path of the process that made it too, since the user manager's own
    /// environment reads no shell profile; the body says which it carries, and the human line says it.
    /// </summary>
    [Fact]
    public async Task On_linux_the_registration_carries_the_search_path_it_was_made_with()
    {
        const string Searched = "/home/ada/.local/bin:/home/ada/.npm-global/bin:/usr/bin:/bin";
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.Linux), Searched);

        await machine.RunAsync(["runtime", "autostart", "enable"], Ct);

        Assert.Equal(Searched, Assert.Single(machine.Registrar.Applied).SearchPath);
        Assert.Equal(Searched, machine.Answer().Path);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);

        Assert.StartsWith(
            $"The runtime starts at logon from {Installed}, on the data directory {machine.Paths.Root}, with the search path {Searched}. ",
            Assert.Single(machine.Lines()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A user unit that carries no search path, one an earlier version registered, starts the runtime with the user
    /// manager's environment, and the human line says so, which is what tells the person to register again.
    /// </summary>
    [Fact]
    public async Task A_user_unit_with_no_search_path_is_said_to_start_with_the_user_manager_s()
    {
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.Linux));
        machine.Registrar.State = new AutostartState(true, [Installed, "runtime", "run", "--detached", "--data-dir", machine.Paths.Root], null);

        await machine.RunAsync(["runtime", "autostart", "status"], Ct);
        Assert.Null(machine.Answer().Path);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);

        Assert.Contains(
            $", on the data directory {machine.Paths.Root}, with the systemd user manager's search path.",
            Assert.Single(machine.Lines()),
            StringComparison.Ordinal);
    }

    /// <summary>On Windows nothing carries a search path, and the body says so with a null rather than leaving the field out.</summary>
    [Fact]
    public async Task On_windows_the_body_s_path_is_null()
    {
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.Windows), "/opt/homebrew/bin:/usr/bin");

        await machine.RunAsync(["runtime", "autostart", "enable"], Ct);

        Assert.Null(Assert.Single(machine.Registrar.Applied).SearchPath);
        using var body = JsonDocument.Parse(Assert.Single(machine.Lines()));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("path").ValueKind);

        machine.Clear();
        await machine.RunAsync(["runtime", "autostart", "status", "--human"], Ct);
        Assert.DoesNotContain("search path", Assert.Single(machine.Lines()), StringComparison.Ordinal);
    }

    /// <summary>The help of the two verbs that print a registration says what the macOS and Linux ones carry.</summary>
    [Theory]
    [InlineData("enable", "and on macOS and Linux the search path (PATH) this command runs with, since launchd's own is /usr/bin:/bin:/usr/sbin:/sbin and the systemd user manager's reads no shell profile.")]
    [InlineData("status", "and on macOS and Linux the search path it carries, under path.")]
    public async Task The_help_says_what_a_macos_or_linux_registration_carries(string verb, string said)
    {
        using var machine = new Machine();

        await machine.RunAsync(["runtime", "autostart", verb, "--help"], Ct);

        Assert.Contains(said, System.Text.RegularExpressions.Regex.Replace(string.Join(" ", machine.Lines()), @"\s+", " "), StringComparison.Ordinal);
    }

    /// <summary>An installation with a recording machine behind it, and its own temporary data directory.</summary>
    /// <summary>
    /// A PATH the unit cannot carry refuses `enable` only. A registration is known by its file, not by what a new one
    /// would hold, so `disable` composes it with no search path and takes away what is there, or says nothing is,
    /// whatever the terminal's PATH holds: the advice an uninstall gives on a refusal is `disable`, and it must work.
    /// </summary>
    [Theory]
    [InlineData("/home/ada/\"q\"/bin:/usr/bin", true)]
    [InlineData("/home/ada/\"q\"/bin:/usr/bin", false)]
    [InlineData("/home/ada/a\\b/bin:/usr/bin", true)]
    [InlineData("/home/ada/a\\b/bin:/usr/bin", false)]
    public async Task On_linux_disable_takes_the_unit_away_whatever_the_search_path_holds(string searched, bool registered)
    {
        using var machine = new Machine(new RecordingRegistrar(AutostartPlatform.Linux), searched);
        if (registered)
        {
            machine.Registrar.State = new AutostartState(true, [Installed, "runtime", "run", "--detached", "--data-dir", machine.Paths.Root], null);
        }

        Assert.Equal(ExitCodes.Success, await machine.RunAsync(["runtime", "autostart", "disable"], Ct));
        Assert.False(machine.Answer().Registered);
        Assert.Equal(1, machine.Registrar.Removals);

        machine.Clear();
        Assert.Equal(ExitCodes.ApiError, await machine.RunAsync(["runtime", "autostart", "enable"], Ct));
        Assert.Empty(machine.Registrar.Applied);
    }

    private sealed class Machine : IDisposable
    {
        private readonly TempPaths _directory = new();
        private readonly StringWriter _out = new();

        public Machine(RecordingRegistrar? registrar = null, string? searchPath = null)
        {
            Registrar = registrar ?? new RecordingRegistrar();
            Environment = new CliEnvironment(
                _out,
                new StringWriter(),
                _directory.Paths,
                InstallPath: Installed,
                Autostart: Registrar,
                SearchPath: searchPath);
        }

        public RecordingRegistrar Registrar { get; }

        public CliEnvironment Environment { get; }

        public JasonPaths Paths => _directory.Paths;

        public Task<int> RunAsync(string[] args, CancellationToken ct) => CliApp.RunAsync(args, Environment, ct);

        public void Clear() => _out.GetStringBuilder().Clear();

        public string[] Lines() => _out.ToString().Split(System.Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        public AutostartStatusResponse Answer() => Only<AutostartStatusResponse>();

        public T Only<T>() => JsonSerializer.Deserialize<T>(Assert.Single(Lines()), JasonJson.Options)!;

        public void Dispose() => _directory.Dispose();
    }
}
