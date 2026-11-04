using System.Text.RegularExpressions;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

/// <summary>
/// No test clicks a <c>jason:</c> link for real: none writes this account's registry, none writes the clipboard, none
/// starts a terminal, none opens a browser through the environment a click is given, none hands a link to a process it
/// starts. Held over the source of every test project.
/// </summary>
/// <remarks>
/// <para>
/// Six rules. No test names the real registry, the real clipboard or the real terminal starter: each would act on the
/// machine running the suite, whatever the data directory says, the last by opening a window with an agent host in it.
/// No file reaches the link handler and the real CLI environment together, which carries the real opener, the real
/// clipboard and the real terminal starter; nor names the <c>session</c> verb or its opener beside that environment. No
/// file runs the router beside a <c>jason:</c> link, since the router hands a link the real environment: choosing the
/// mode is held by <c>ModeRouter.Select</c> alone. And no file that starts a process — by a start info of its own, by
/// <c>Process.Start</c>, by a <c>Process</c> of its own, through <c>ChildProcess</c>, or through the executable runner
/// the end-to-end tests share — holds a <c>jason:</c> literal, names <c>AttentionLinks</c> or holds a literal that is
/// or begins with the <c>session</c> verb, since the executable it starts runs a link as a click does and the session
/// verb as a person does.
/// </para>
/// <para>
/// Its reach is the source text with whole-line comments removed and adjacent string literals joined, as
/// <see cref="NothingHereRegistersAnythingTests"/> reads it. A name built from pieces at run time and reflection are
/// beyond it.
/// </para>
/// </remarks>
public sealed partial class NoTestClicksALinkTests
{
    /// <summary>This file names everything it forbids, because it is the rule about them.</summary>
    private const string Self = "NoTestClicksALinkTests.cs";

    [Fact]
    [Critical(Family.B9, "the suite never clicks a link")]
    public void No_test_writes_the_registry_or_the_clipboard_or_runs_a_link_for_real()
    {
        var offences = Sources()
            .Where(file => Path.GetFileName(file) != Self)
            .Select(file => Offence(Path.GetFileName(file), File.ReadAllText(file)))
            .OfType<string>()
            .ToList();

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    /// <summary>The rule holds the forms a test would reach for, each in a file of its own.</summary>
    [Theory]
    [InlineData("real registry", "var registry = new Registry" + "UserRegistry();")]
    [InlineData("real clipboard", "IClipboard clipboard = new Windows" + "Clipboard();")]
    [InlineData("real terminal", "ITerminalStarter terminals = new PlatformTerminal" + "Starter();")]
    [InlineData("handler with the real environment", "await Link" + "Handler.RunAsync([link], CliEnvironment.Default(), ct);")]
    [InlineData("session verb with the real environment", "await CliApp.RunAsync([\"session\", \"open\", \"--approval\", id], CliEnvironment.Default(), ct);")]
    [InlineData("session opener with the real environment", "await Session" + "Opener.OpenAsync(CliEnvironment.Default(), kind, id, false, false, ct);")]
    [InlineData("router with a link", "await ModeRouter.RunAsync([\"jason://attention\"], ct);")]
    [InlineData("router with a link in pieces", "await ModeRouter.RunAsync([\"JASON\" + \"://attention\"], ct);")]
    [InlineData("router with the table's link", "await ModeRouter.RunAsync([AttentionLinks.List], ct);")]
    [InlineData("process start with a link", "var start = new ProcessStartInfo(exe) { ArgumentList = { \"jason://attention\" } };")]
    [InlineData("child process with a link", "using var child = ChildProcess.Start(ChildProcess.StartInfo(exe, [\"jason://attention\"]));")]
    [InlineData("executable runner with a link", "await GoldenPath.JasonAsync(it, \"jason://attention/approvals\");")]
    [InlineData("launcher runner with a link in pieces", "await GoldenPath.LauncherAsync(it, \"JASON\" + \":attention\");")]
    [InlineData("executable runner with the session verb", "await GoldenPath.JasonAsync(it, \"session\", \"open\", \"--approval\", id);")]
    [InlineData("process start with the table's link", "var start = new ProcessStartInfo(exe) { ArgumentList = { AttentionLinks.Item(id) } };")]
    [InlineData("process start of its own with a link", "using var started = Process.Start(exe, \"jason://attention\");")]
    [InlineData("process start of its own with the session verb in one string", "using var started = Process.Start(exe, \"session open --approval apr_1\");")]
    [InlineData("start info with the session verb in one string", "var start = new ProcessStartInfo(exe, \"session open --approval apr_1\");")]
    [InlineData("process of its own with a link", "using var started = new Process { StartInfo = { FileName = exe, ArgumentList = { \"jason://attention\" } } };")]
    public void The_rule_finds_each_form(string form, string text) =>
        Assert.True(Offence("Mutant.cs", text) is not null, $"{form} passes the rule: {text}");

    /// <summary>A test may run the router for another mode, and may choose the link mode without running it.</summary>
    [Theory]
    [InlineData("await ModeRouter.RunAsync([\"--version\"], ct);")]
    [InlineData("Assert.Equal(Mode.Link, ModeRouter.Select([\"jason://attention\"]));")]
    [InlineData("await Link" + "Handler.RunAsync([link], cli.Environment, ct);")]
    [InlineData("await _run.Cli(\"session\", \"open\", \"--approval\", \"apr_1\");")]
    [InlineData("var session = Read(body); var env = CliEnvironment.Default();")]
    [InlineData("await GoldenPath.JasonAsync(it, \"--version\");")]
    public void What_acts_on_nothing_is_no_offence(string text) => Assert.Null(Offence("Fine.cs", text));

    private static string? Offence(string file, string source)
    {
        var text = Joined(source);
        var registry = string.Concat("Registry", "UserRegistry");
        var clipboard = string.Concat("Windows", "Clipboard");
        var terminal = string.Concat("PlatformTerminal", "Starter");
        if (text.Contains(registry, StringComparison.Ordinal))
        {
            return $"{file} names {registry}, which writes this account's registry";
        }

        if (text.Contains(clipboard, StringComparison.Ordinal))
        {
            return $"{file} names {clipboard}, which writes the person's clipboard";
        }

        if (text.Contains(terminal, StringComparison.Ordinal))
        {
            return $"{file} names {terminal}, which starts a terminal window on this machine";
        }

        if (text.Contains(string.Concat("Link", "Handler"), StringComparison.Ordinal) && text.Contains("CliEnvironment.Default", StringComparison.Ordinal))
        {
            return $"{file} reaches the link handler and the real CLI environment together";
        }

        if ((text.Contains("\"session\"", StringComparison.Ordinal) || text.Contains(string.Concat("Session", "Opener"), StringComparison.Ordinal))
            && text.Contains("CliEnvironment.Default", StringComparison.Ordinal))
        {
            return $"{file} names the session verb beside the real CLI environment, which starts a terminal window on this machine";
        }

        if (text.Contains("ModeRouter.RunAsync", StringComparison.Ordinal)
            && (LinkLiteral().IsMatch(text) || text.Contains("AttentionLinks", StringComparison.Ordinal)))
        {
            return $"{file} runs the router beside a jason: link";
        }

        if (ProcessStarts.FirstOrDefault(start => text.Contains(start, StringComparison.Ordinal)) is { } how)
        {
            if (LinkLiteral().IsMatch(text) || text.Contains("AttentionLinks", StringComparison.Ordinal))
            {
                return $"{file} starts a process through {how} beside a jason: link";
            }

            if (SessionLiteral().IsMatch(text))
            {
                return $"{file} starts a process through {how} beside the session verb, which starts a terminal window on this machine";
            }
        }

        return null;
    }

    /// <summary>
    /// What a test file starts a process through: a start info, the shared helper, the end-to-end runner, a start of its
    /// own, a process of its own.
    /// </summary>
    private static readonly string[] ProcessStarts =
        ["ProcessStartInfo", "ChildProcess", "JasonAsync(", "LauncherAsync(", "Process.Start(", "new Process"];

    private static string Joined(string text) => Concatenation().Replace(Uncommented(text), string.Empty);

    private static string Uncommented(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [GeneratedRegex(@"""\s*\+\s*""")]
    private static partial Regex Concatenation();

    [GeneratedRegex("\"jason:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkLiteral();

    /// <summary>A literal that is the session verb or begins with it, as one command line written in one string does.</summary>
    [GeneratedRegex(@"""session\b", RegexOptions.CultureInvariant)]
    private static partial Regex SessionLiteral();

    /// <summary>Every test project's sources, found from the repository root the way every guard here finds it.</summary>
    private static IEnumerable<string> Sources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var separator = Path.DirectorySeparatorChar;
        return Directory
            .EnumerateFiles(Path.Combine(directory.FullName, "dotnet", "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal));
    }
}
