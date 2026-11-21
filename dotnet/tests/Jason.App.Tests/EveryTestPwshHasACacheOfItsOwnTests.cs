using System.Text.RegularExpressions;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

/// <summary>
/// No test starts PowerShell 7 but through <c>ChildPwsh.StartInfo</c>, the one start that gives each child a start cache
/// of its own. Held over the source of every test project.
/// </summary>
/// <remarks>
/// <para>
/// PowerShell 7 reads and writes a cache as it starts, under the account's home unless told otherwise, and children that
/// share it share whatever one of them wrote there. A start of a test's own would hand its child the runner's cache, so
/// the rule is about the pair: a file that names PowerShell 7 as a program — a literal path that ends in <c>pwsh</c> or
/// <c>pwsh.exe</c>, or the bare name where a program goes: in a path it combines, a start info, a start, a look-up on the
/// PATH, a file name — and also makes or starts a process is an offence. The bare name as data, a workflow step's shell
/// say, is not a program. The helper's own file and this one are the rule's exceptions.
/// </para>
/// <para>
/// Its reach is the source text with whole-line comments removed and adjacent string literals joined, as
/// <see cref="NothingHereRegistersAnythingTests"/> reads it. A program name built at run time, a program a script the test
/// runs starts in turn, and reflection are beyond it.
/// </para>
/// </remarks>
public sealed partial class EveryTestPwshHasACacheOfItsOwnTests
{
    /// <summary>This file names everything it forbids, because it is the rule about them.</summary>
    private const string Self = "EveryTestPwshHasACacheOfItsOwnTests.cs";

    /// <summary>The one start of a test's PowerShell 7.</summary>
    private const string Helper = "ChildPwsh.cs";

    /// <summary>
    /// The files that start PowerShell 7 at the product's child-process door, which a test of the door must, and name it as a
    /// program beside that, by file name, with why. Each is held instead to give its start the cache through
    /// <c>ChildPwsh.WithOwnCache</c>.
    /// </summary>
    private static readonly Dictionary<string, string> AtTheDoor = new(StringComparer.Ordinal)
    {
        ["ChildProcessModulePathTests.cs"] = "names PowerShell 7's program as data for the door's rule about Windows PowerShell, and asks PowerShell 7 its module path through the door",
    };

    [Fact]
    [Critical(Family.B9, "the suite's PowerShell 7 children never share the runner's start cache")]
    public void No_test_starts_PowerShell_7_but_through_the_one_start_that_gives_it_a_cache_of_its_own()
    {
        var tests = TestsDirectory();
        var offences = Sources(tests)
            .Where(file => Path.GetFileName(file) is not Self and not Helper)
            .Where(file => AtTheDoor.ContainsKey(Path.GetFileName(file))
                ? !File.ReadAllText(file).Contains("ChildPwsh.WithOwnCache(", StringComparison.Ordinal)
                : Offends(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(tests, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offences.Count == 0,
            $"These {offences.Count} files start PowerShell 7 with a start of their own rather than ChildPwsh.StartInfo, so each child shares the runner's start cache:"
            + Environment.NewLine + string.Join(Environment.NewLine, offences));
    }

    /// <summary>The rule holds the forms a test would reach for.</summary>
    [Theory]
    [InlineData("start info by the bare name", "var start = new ProcessStartInfo(\"pwsh\") { UseShellExecute = false };")]
    [InlineData("start of its own by the Windows name", "using var started = Process.Start(\"pwsh.exe\", arguments);")]
    [InlineData("found on the PATH", "var pwsh = dirs.Select(d => Path.Combine(d, \"pwsh\")).First(File.Exists); var start = new ProcessStartInfo(pwsh);")]
    [InlineData("a full path at the product's door", "var start = ChildProcess.StartInfo(@\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\");")]
    [InlineData("a process of its own", "using var p = new Process { StartInfo = { FileName = \"/usr/local/bin/pwsh\" } };")]
    [InlineData("the name in pieces", "var start = new ProcessStartInfo(\"pw\" + \"sh\");")]
    public void The_rule_finds_each_form(string form, string text) =>
        Assert.True(Offends(text), $"{form} passes the rule: {text}");

    /// <summary>The helper's start, a workflow's shell named as data, and other programs are no offence.</summary>
    [Theory]
    [InlineData("var start = ChildPwsh.StartInfo(tree.Root); using var process = Process.Start(start)!;")]
    [InlineData("Assert.Equal(\"pwsh\", Scalar(step, \"shell\")); var start = new ProcessStartInfo(\"bash\");")]
    [InlineData("Assert.Equal([\"bash\", \"pwsh\"], installs.Keys); var pwsh = installs[\"pwsh\"]; using var process = Process.Start(start)!;")]
    [InlineData("[InlineData(\"pwsh\", \"dotnet\", false)] var info = new ProcessStartInfo(\"dotnet\");")]
    [InlineData("var start = new ProcessStartInfo(\"git\"); Assert.SkipWhen(pwsh is null, \"pwsh is not on this machine's PATH\");")]
    [InlineData("var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, \"WindowsPowerShell\", \"v1.0\", \"powershell.exe\"));")]
    public void What_does_not_start_PowerShell_7_of_its_own_is_no_offence(string text) => Assert.False(Offends(text), text);

    private static bool Offends(string source)
    {
        var text = Joined(source);
        return PowerShell7Program().IsMatch(text) && ProcessStarts.Any(start => text.Contains(start, StringComparison.Ordinal));
    }

    /// <summary>What a test file makes or starts a process through.</summary>
    private static readonly string[] ProcessStarts = ["ProcessStartInfo", "ChildProcess.Start", "Process.Start(", "new Process"];

    private static string Joined(string text) => Concatenation().Replace(Uncommented(text), string.Empty);

    private static string Uncommented(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [GeneratedRegex(@"""\s*\+\s*""")]
    private static partial Regex Concatenation();

    /// <summary>
    /// PowerShell 7 as a program, with or without its extension: a literal path that ends in it, or the bare name in a
    /// path combined on its line, as a start info's or a start's first argument, looked up on the PATH, or as a file name.
    /// </summary>
    [GeneratedRegex(
        @"""[^""\n]*[/\\]pwsh(?:\.exe)?""|(?:Path\.Combine\([^;\n]*?|ProcessStartInfo\(\s*|Process\.Start\(\s*|StartInfo\(\s*|OnPath\(\s*|FileName\s*=\s*)""pwsh(?:\.exe)?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShell7Program();

    private static string TestsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "dotnet", "tests");
    }

    /// <summary>Every test project's sources, found from the repository root the way every guard here finds it.</summary>
    private static IEnumerable<string> Sources(string tests)
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory
            .EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal));
    }
}
