using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// No test shows a toast, writes the toast's registration, or sends a notification. Held over the source of every test,
/// in the way the suite holds itself offline, with the named exceptions spelled out: the one test that calls the interop
/// up to but never including Show, and the one that launches the toast host with a protocol it refuses.
/// </summary>
/// <remarks>
/// Its reach is the source text, read with its whitespace taken out, so an argument list split one per line reads as
/// one, a start information made by hand is a launch however the class that starts it is named here, and the class that
/// composes the toast host's command is named however it is aliased in the same file. A name built from pieces at run
/// time, a command read from elsewhere, an alias declared in another file, and reflection are beyond it.
/// </remarks>
public sealed class NoTestShowsAToastTests
{
    private const string DryTest = "WindowsToastDryTests.cs";

    private const string LauncherTest = "ProcessToastLauncherTests.cs";

    private const string HostTest = "ToastHostTests.cs";

    private const string CommandsTest = "ToastCommandsTests.cs";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void No_test_shows_a_toast_or_writes_the_registration()
    {
        var offences = Sources().Select(source => Offence(Path.GetFileName(source.File), source.Text)).OfType<string>().ToList();
        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    /// <summary>
    /// The guard's reach, held over forms it once missed: the arguments split one per line (E3), the composed Windows
    /// command started through the door (E4), the protocol taken from its constant beside a launch (E5), a start
    /// information made by hand and started through an alias of Process (E6), and the composed command reached through
    /// an alias of its class (E7).
    /// </summary>
    [Theory]
    [InlineData("E3", "var start = ChildProcess.StartInfo(exe);\nforeach (var argument in new[]\n{\n    \"toast-host\",\n    \"--protocol\",\n    \"1\",\n})\n{\n    start.ArgumentList.Add(argument);\n}")]
    [InlineData("E4", "var launch = ToastCommands.Windows(new JasonDllLocator().Command, message);\nvar start = ChildProcess.StartInfo(launch.Command[0]);")]
    [InlineData("E5", "var protocol = ToastCommands.Protocol.ToString(CultureInfo.InvariantCulture);\nawait launcher.LaunchAsync(new ToastLaunch([exe, \"toast-host\", \"--protocol\", protocol], payload, environment), bound, ct);")]
    [InlineData("E6", "using Starter = System.Diagnostics.Process;\nvar start = new ProcessStartInfo(exe) { ArgumentList = { \"toast-host\", \"--protocol\", version } };\nStarter.Start(start);")]
    [InlineData("E7", "using Commands = Jason.Runtime.Notifications.Desktop.ToastCommands;\nvar launch = Commands.Windows(new JasonDllLocator().Command, message);\nvar start = new ProcessStartInfo(launch.Command[0]);")]
    public void The_guard_finds_the_forms_it_once_missed(string form, string text) =>
        Assert.True(Offence("Mutant.cs", text) is not null, $"{form} passes the guard: {text}");

    /// <summary>
    /// A fake launcher is no launch: a test that composes the Windows command and hands it to its own fake, as the
    /// channel's tests do, starts nothing.
    /// </summary>
    [Fact]
    public void A_fake_launcher_that_composes_the_command_is_no_offence() =>
        Assert.Null(Offence(
            "Fake.cs",
            "public Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken)\n"
                + "{\n    var program = launch.Command[0] == \"C:\\\\jason\\\\jason.exe\" ? \"toast-host\" : launch.Command[0];\n}\n"
                + "var expected = ToastCommands.Windows(new Self().Command, Message);"));

    /// <summary>What a test source does that no test may, or null where it does none of it.</summary>
    internal static string? Offence(string name, string source)
    {
        // Whitespace is no disguise: the arguments split one per line, or spaced apart, read as one.
        var text = Regex.Replace(source, @"\s+", string.Empty);

        // Built from parts, so this file is not an offender of its own rule.
        if (text.Contains(string.Concat("Registry", "UserRegistry"), StringComparison.Ordinal))
        {
            return $"{name} reaches the real registry.";
        }

        if (text.Contains(string.Concat(".Sh", "ow()"), StringComparison.Ordinal))
        {
            return $"{name} shows a toast.";
        }

        if (name != DryTest && text.Contains(string.Concat("WindowsToast", ".Prepare"), StringComparison.Ordinal))
        {
            return $"{name} calls the interop; only {DryTest} may.";
        }

        if (name != LauncherTest && text.Contains(string.Concat("ProcessToast", "Launcher"), StringComparison.Ordinal))
        {
            return $"{name} launches a real toast; only {LauncherTest} may, and only with a protocol the host refuses.";
        }

        // The toast host itself, called in process: only its own tests, which never hand it a payload it would show
        // on the platform they run on.
        if (name != HostTest && text.Contains(string.Concat("ToastHost", ".RunAsync"), StringComparison.Ordinal))
        {
            return $"{name} runs the toast host; only {HostTest} may.";
        }

        // And the mode as a child, in the protocol it answers: a test that starts it so has asked for a toast, through
        // whichever door it went. A test that only states the composed command names the protocol by its constant.
        var mode = string.Concat("\"toast", "-host\"");
        if ((text.Contains(mode, StringComparison.Ordinal) && text.Contains(string.Concat("\"--protocol\",\"", "1\""), StringComparison.Ordinal))
            || text.Contains(string.Concat("toast-host--protocol", "1"), StringComparison.Ordinal))
        {
            return $"{name} names the toast host with the protocol it answers.";
        }

        // And through the door, whatever the protocol: outside the two files that state the command, a file that names
        // the toast host, or the class that composes its command and holds its protocol, and starts a child, may have
        // asked for a toast. The class counts by its own name, so an alias of it declared in the same file names it where
        // it is declared. A start information made by hand counts, whatever starts it: an alias of Process hides the
        // call, not what it was given. A fake launcher's own LaunchAsync is a definition, not a call, and starts nothing.
        if (name != CommandsTest && name != LauncherTest)
        {
            var toast = text.Contains(string.Concat("toast", "-host"), StringComparison.Ordinal)
                || text.Contains(string.Concat("Toast", "Commands"), StringComparison.Ordinal);
            var launch = text.Contains(string.Concat("ChildProcess.", "StartInfo"), StringComparison.Ordinal)
                || text.Contains(string.Concat("Process.", "Start("), StringComparison.Ordinal)
                || text.Contains(string.Concat(".Launch", "Async("), StringComparison.Ordinal)
                || text.Contains(string.Concat("ProcessStart", "Info"), StringComparison.Ordinal);
            if (toast && launch)
            {
                return $"{name} names the toast host and starts a child; only {CommandsTest} and {LauncherTest} may do both.";
            }
        }

        return null;
    }

    /// <summary>The one test that launches the toast host asks it in a protocol it refuses before it touches anything.</summary>
    [Fact]
    public void The_launcher_test_asks_the_toast_host_only_what_it_refuses()
    {
        var text = File.ReadAllText(Path.Combine(Folder(), LauncherTest));
        Assert.DoesNotContain("\"--protocol\", \"1\"", text, StringComparison.Ordinal);
        Assert.Contains("\"--protocol\", \"2\"", text, StringComparison.Ordinal);
    }

    /// <summary>A payload the toast host would show is handed to it only where it refuses to show anything.</summary>
    [Fact]
    public void The_host_tests_hand_over_a_showable_payload_only_where_the_host_cannot_show_it()
    {
        var text = File.ReadAllText(Path.Combine(Folder(), HostTest));
        Assert.Contains("Assert.SkipWhen(OperatingSystem.IsWindows()", text, StringComparison.Ordinal);
    }

    /// <summary>And the behaviour beside the source: a runtime the API fixture starts notifies nobody.</summary>
    [Fact]
    public async Task A_runtime_the_api_fixture_starts_notifies_nobody()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, paths => File.WriteAllText(
            paths.UserSettingsFile,
            """{"Dispatcher":{"Enabled":false},"Notifications":{"Enabled":true,"Toast":true}}"""));

        Assert.False(fixture.Resolve<LiveSettings<NotificationsOptions>>().Current.Enabled);
    }

    /// <summary>Every test source but this one, with its text.</summary>
    private static IEnumerable<(string File, string Text)> Sources() =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "runtime", "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !Path.GetFileName(file).Equals("NoTestShowsAToastTests.cs", StringComparison.Ordinal))
            .Select(file => (file, File.ReadAllText(file)));

    private static string Folder() => Path.Combine(RepositoryRoot(), "runtime", "tests", "Jason.Runtime.Tests", "Notifications");

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
