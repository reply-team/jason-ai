using Jason.Cli.Session;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// The console host Windows starts a session in replaces every <c>%NAME%</c> of a variable it was started with across the
/// whole command line it hands on, before the host reads a word of it. So a word it would change is refused before
/// anything starts: the scan is pure, modelled on the system's own expansion call, and held to that call on Windows.
/// </summary>
public sealed class ConsoleHostExpansionTests
{
    /// <summary>A block of variables as the console host would be started with them: names whatever their case.</summary>
    private static readonly IReadOnlyDictionary<string, string> Block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PATH"] = "C:/bin",
        ["X"] = "x",
    };

    private static readonly OwnJason Own = new("C:/Users/marta/.jason", "C:/Users/marta/AppData/Local/Programs/jason", "C:/bin");

    [Theory]
    [InlineData("%PATH%", "C:/bin")]
    [InlineData("%path%", "C:/bin")]
    [InlineData("a%PATH%b", "aC:/binb")]
    [InlineData("%NOPE%", "%NOPE%")]
    [InlineData("100%", "100%")]
    [InlineData("50% off and 20% more", "50% off and 20% more")]
    [InlineData("%%", "%%")]
    [InlineData("%%PATH%%", "%C:/bin%")]
    [InlineData("% PATH%", "% PATH%")]
    [InlineData("%NOPE%X%", "%NOPEx")]
    [InlineData("%NOPE%PATH%", "%NOPEC:/bin")]
    [InlineData("%X%%X%", "xx")]
    [InlineData("", "")]
    public void A_word_expands_by_the_rule_the_console_host_reads_it_by(string word, string expanded)
    {
        Assert.Equal(expanded, TerminalPlan.Expand(word, Block));
    }

    /// <summary>One pass: a value that holds a name of its own is not read again.</summary>
    [Fact]
    public void A_value_is_not_expanded_again()
    {
        var block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "%B%", ["B"] = "b" };

        Assert.Equal("%B%", TerminalPlan.Expand("%A%", block));
    }

    /// <summary>
    /// A word the console host would change starts nothing on Windows: the reason names the word, the variable as it is
    /// written there, and what the console host does with it.
    /// </summary>
    [Fact]
    public void Windows_refuses_a_word_the_console_host_would_change_and_names_it()
    {
        var inherited = new Dictionary<string, string> { ["JASON_TEST_DEFINED"] = "elsewhere" };

        var reason = TerminalPlan.Uncarried(TerminalPlatform.Windows, [@"C:\to%Jason_Test_Defined%ols\claude.exe", "Go."], Own, inherited);

        Assert.Equal(
            @"the word 'C:\to%Jason_Test_Defined%ols\claude.exe' holds %Jason_Test_Defined%, which the console host replaces with the value of the variable Jason_Test_Defined before the program it starts reads it",
            reason);
    }

    /// <summary>A brief is many lines: the one that holds the name is the one the reason shows.</summary>
    [Fact]
    public void A_word_of_many_lines_is_shown_by_the_line_that_holds_the_name()
    {
        var inherited = new Dictionary<string, string> { ["USERNAME"] = "marta" };
        var brief = "Jason is waiting.\r\n\nCampaign folder: D:\\Clients\\%USERNAME%\\Jason\\q4\nRead its memory file.";

        var reason = TerminalPlan.Uncarried(TerminalPlatform.Windows, ["C:/Tools/claude.exe", brief], Own, inherited);

        Assert.NotNull(reason);
        Assert.StartsWith(@"the line 'Campaign folder: D:\Clients\%USERNAME%\Jason\q4' holds %USERNAME%, ", reason, StringComparison.Ordinal);
    }

    /// <summary>The block is the launch's too: the two variables the console host is started with expand like any other.</summary>
    [Fact]
    public void A_name_only_the_launch_sets_is_a_change_too()
    {
        var reason = TerminalPlan.Uncarried(TerminalPlatform.Windows, ["C:/Tools/claude.exe", "Look in %JASON_DATA_DIR%."], Own, new Dictionary<string, string>());

        Assert.NotNull(reason);
        Assert.Contains("%JASON_DATA_DIR%", reason, StringComparison.Ordinal);
    }

    /// <summary>The confinement variable is taken off the console host's start, so its name is not one it replaces.</summary>
    [Fact]
    public void The_confinement_variable_is_not_in_the_block()
    {
        var inherited = new Dictionary<string, string> { [Confinement.Variable] = "1", [Confinement.ChatTurnVariable] = "1" };

        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.Windows, ["C:/Tools/claude.exe", $"%{Confinement.Variable}%"], Own, inherited));
        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.Windows, ["C:/Tools/claude.exe", $"%{Confinement.ChatTurnVariable}%"], Own, inherited));
    }

    /// <summary>
    /// Every other character a shell or a parser might read is no layer's business on this chain: quotes and backslashes
    /// are escaped and unescaped by the same rule twice, and no shell is in it.
    /// </summary>
    [Fact]
    public void A_brief_with_every_other_special_character_is_not_refused()
    {
        var inherited = new Dictionary<string, string> { ["PATH"] = "C:/bin" };
        const string Brief = "Run ^ & | < > ! !PATH! \"quoted\" C:\\a\\ ;\n\tnext line 100% PATH%";

        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.Windows, ["C:/Tools/claude.exe", Brief], Own, inherited));
    }

    /// <summary>No other system's terminal expands the command line, so only Windows refuses by it.</summary>
    [Theory]
    [InlineData(TerminalPlatform.Linux)]
    [InlineData(TerminalPlatform.MacOs)]
    public void Only_windows_refuses_by_the_expansion(TerminalPlatform os)
    {
        var inherited = new Dictionary<string, string> { ["PATH"] = "/usr/bin" };

        Assert.Null(TerminalPlan.Uncarried(os, ["/opt/claude", "%PATH%"], Own with { ExecutableFolder = "/opt/jason" }, inherited));
    }

    /// <summary>With no block to read, nothing is refused by it: <see cref="TerminalPlan.Compose"/> composes, it does not scan.</summary>
    [Fact]
    public void Without_a_block_nothing_is_refused_by_the_expansion()
    {
        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.Windows, ["C:/Tools/claude.exe", "%PATH%"], Own));
    }

    /// <summary>
    /// The block the scan reads is the one the start is given: this process's variables less the confinement variable, less
    /// PSModulePath where the host is Windows PowerShell, and the launch's two on top. Making a start info starts nothing.
    /// </summary>
    [Theory]
    [InlineData("C:/Tools/claude.exe")]
    [InlineData("C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe")]
    [InlineData("C:/Program Files/PowerShell/7/pwsh.exe")]
    public void The_block_is_the_environment_the_console_host_is_started_with(string host)
    {
        // The console host is Windows' own, named by a Windows path, which is no full path on macOS or Linux.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The console host is Windows' own; its path is no full path elsewhere.");

        var variables = new Dictionary<string, string> { [JasonPaths.DataDirectoryVariable] = Own.DataDirectory, ["PATH"] = TerminalPlan.SearchPathFirst(TerminalPlatform.Windows, Own.ExecutableFolder!, Own.SearchPath) };
        var start = TerminalStartInfo.For(TerminalPlatform.Windows, ["C:/Windows/System32/conhost.exe", "--", host], "C:/Jason", variables);

        var block = TerminalPlan.Block(TerminalPlatform.Windows, Process(), Own, [host]);

        var started = start.Environment.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(started.Count, block.Count);
        foreach (var (name, value) in started)
        {
            Assert.True(block.TryGetValue(name, out var held), $"The block has no {name}.");
            Assert.Equal(value, held);
        }
    }

    /// <summary>
    /// The console host hands its own environment to the host it starts, so where that host is Windows PowerShell -
    /// <c>jason session open --shell</c>'s on Windows - the console host is started without the PSModulePath this process
    /// inherited, which a PowerShell 7 session fills with folders Windows PowerShell cannot load its own commands from;
    /// every other host, PowerShell 7 among them, keeps it, and so does a terminal elsewhere.
    /// </summary>
    [Theory]
    [InlineData(TerminalPlatform.Windows, "C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", false)]
    [InlineData(TerminalPlatform.Windows, "C:/Windows/System32/WindowsPowerShell/v1.0/POWERSHELL.EXE", false)]
    [InlineData(TerminalPlatform.Windows, "C:/Program Files/PowerShell/7/pwsh.exe", true)]
    [InlineData(TerminalPlatform.Windows, "C:/Tools/claude.exe", true)]
    [InlineData(TerminalPlatform.Linux, "/usr/bin/powershell", true)]
    public void The_console_host_starts_Windows_PowerShell_without_the_inherited_module_path(TerminalPlatform os, string host, bool keeps)
    {
        var inherited = Environment.GetEnvironmentVariable(ChildProcess.ModulePathVariable);
        // A system tool is started by its full path, and a terminal's path is full only on its own kind of system: the console
        // host's on Windows, a macOS or Linux terminal's elsewhere. Each row runs where its path is full.
        Assert.SkipWhen(OperatingSystem.IsWindows() != (os == TerminalPlatform.Windows), "A terminal's path is full only on its own kind of system: the console host's on Windows, a macOS or Linux terminal's elsewhere.");
        Assert.SkipWhen(inherited is null, "This process has no PSModulePath to hand on.");
        IReadOnlyList<string> argv = os == TerminalPlatform.Windows ? ["C:/Windows/System32/conhost.exe", "--", host] : ["/usr/bin/x-terminal-emulator", "-e", host];

        var start = TerminalStartInfo.For(os, argv, os == TerminalPlatform.Windows ? "C:/Jason" : "/home/marta/Jason");

        Assert.Equal(keeps, start.Environment.ContainsKey(ChildProcess.ModulePathVariable));
        Assert.Equal(!keeps, TerminalPlan.HostsWindowsPowerShell(os, [host]));
    }

    /// <summary>
    /// The model is held to the system's own expansion call, on the one system that has it, over this process's variables:
    /// names surely defined there and names surely not. Nothing in the environment is changed.
    /// </summary>
    [Fact]
    public void On_windows_the_scan_agrees_with_the_systems_own_expansion()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has the console host's expansion call.");

        var block = Process();
        var nope = "N" + Guid.NewGuid().ToString("N");
        var other = "O" + Guid.NewGuid().ToString("N");
        string[] battery =
        [
            "%SystemRoot%", "%systemroot%", "%PATH%", "a%PATH%b", $"%{nope}%", "100%", "50% off and 20% more", "%%", "%%PATH%%",
            "% PATH%", $"%{nope}%PATH%", $"%{nope}%SystemRoot%", $"%{nope}%{other}%", "%SystemRoot%%SystemRoot%", "%%%", "%SystemRoot",
            "SystemRoot%", $"x%{nope}", "D:\\Clients\\%SystemRoot%\\Jason\nline two %PATH%", string.Empty,
        ];

        foreach (var word in battery)
        {
            Assert.Equal(Environment.ExpandEnvironmentVariables(word), TerminalPlan.Expand(word, block));
        }
    }

    /// <summary>This process's variables, as a start info reads them.</summary>
    private static Dictionary<string, string> Process()
    {
        var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                read[name] = value;
            }
        }

        return read;
    }
}
