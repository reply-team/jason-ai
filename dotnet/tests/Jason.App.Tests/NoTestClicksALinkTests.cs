using System.Text.RegularExpressions;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

/// <summary>
/// No test clicks a <c>jason:</c> link for real: none writes this account's registry, none writes the clipboard, none
/// opens a browser through the environment a click is given. Held over the source of every test project.
/// </summary>
/// <remarks>
/// <para>
/// Three rules. No test names the real registry or the real clipboard: either would act on the machine running the
/// suite, whatever the data directory says. No file reaches the link handler and the real CLI environment together,
/// which carries the real opener and the real clipboard. And no file runs the router beside a <c>jason:</c> link,
/// since the router hands a link the real environment: choosing the mode is held by <c>ModeRouter.Select</c> alone.
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
    [InlineData("handler with the real environment", "await Link" + "Handler.RunAsync([link], CliEnvironment.Default(), ct);")]
    [InlineData("router with a link", "await ModeRouter.RunAsync([\"jason://attention\"], ct);")]
    [InlineData("router with a link in pieces", "await ModeRouter.RunAsync([\"JASON\" + \"://attention\"], ct);")]
    [InlineData("router with the table's link", "await ModeRouter.RunAsync([AttentionLinks.List], ct);")]
    public void The_rule_finds_each_form(string form, string text) =>
        Assert.True(Offence("Mutant.cs", text) is not null, $"{form} passes the rule: {text}");

    /// <summary>A test may run the router for another mode, and may choose the link mode without running it.</summary>
    [Theory]
    [InlineData("await ModeRouter.RunAsync([\"--version\"], ct);")]
    [InlineData("Assert.Equal(Mode.Link, ModeRouter.Select([\"jason://attention\"]));")]
    [InlineData("await Link" + "Handler.RunAsync([link], cli.Environment, ct);")]
    public void What_acts_on_nothing_is_no_offence(string text) => Assert.Null(Offence("Fine.cs", text));

    private static string? Offence(string file, string source)
    {
        var text = Joined(source);
        var registry = string.Concat("Registry", "UserRegistry");
        var clipboard = string.Concat("Windows", "Clipboard");
        if (text.Contains(registry, StringComparison.Ordinal))
        {
            return $"{file} names {registry}, which writes this account's registry";
        }

        if (text.Contains(clipboard, StringComparison.Ordinal))
        {
            return $"{file} names {clipboard}, which writes the person's clipboard";
        }

        if (text.Contains(string.Concat("Link", "Handler"), StringComparison.Ordinal) && text.Contains("CliEnvironment.Default", StringComparison.Ordinal))
        {
            return $"{file} reaches the link handler and the real CLI environment together";
        }

        if (text.Contains("ModeRouter.RunAsync", StringComparison.Ordinal)
            && (LinkLiteral().IsMatch(text) || text.Contains("AttentionLinks", StringComparison.Ordinal)))
        {
            return $"{file} runs the router beside a jason: link";
        }

        return null;
    }

    private static string Joined(string text) => Concatenation().Replace(Uncommented(text), string.Empty);

    private static string Uncommented(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [GeneratedRegex(@"""\s*\+\s*""")]
    private static partial Regex Concatenation();

    [GeneratedRegex("\"jason:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkLiteral();

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
