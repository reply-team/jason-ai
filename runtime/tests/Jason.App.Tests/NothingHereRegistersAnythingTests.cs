using System.Text.RegularExpressions;

namespace Jason.App.Tests;

/// <summary>
/// Nothing in this project can register something at logon.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModeRouter.RunAsync"/> builds <see cref="Jason.Cli.CliEnvironment.Default"/>, which is the one
/// place this machine's own registrar is constructed. A test that typed <c>runtime autostart enable</c> through
/// either of them would register a logon task on the machine running the suite, and on three CI runners, with
/// nothing in the way.
/// </para>
/// <para>
/// So the rule is about the pair, not about the word: a file here may name the verb, and a file here may reach
/// the real environment, but no file may do both. That is stronger than the rule this replaced — "no file names
/// the verb" — which a file could satisfy while doing the dangerous thing, simply by spelling the verb in two
/// halves. This reads the sources with adjacent string literals joined back up, so <c>"auto" + "start"</c> is
/// the word it is.
/// </para>
/// </remarks>
public partial class NothingHereRegistersAnythingTests
{
    [Fact]
    public void No_file_that_names_the_verb_can_also_reach_the_real_environment()
    {
        const string Verb = "autostart";
        const string Router = "ModeRouter.RunAsync";
        const string RealEnvironment = "CliEnvironment.Default";

        var offenders = new List<string>();
        foreach (var source in Sources())
        {
            var text = Joined(File.ReadAllText(source));
            if (!text.Contains(Verb, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var reaches = new List<string>();
            if (text.Contains(Router, StringComparison.Ordinal))
            {
                reaches.Add(Router);
            }

            if (text.Contains(RealEnvironment, StringComparison.Ordinal))
            {
                reaches.Add(RealEnvironment);
            }

            if (reaches.Count > 0)
            {
                offenders.Add($"{Path.GetFileName(source)} (names the verb and {string.Join(" and ", reaches)})");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "these files name the verb that registers something at logon and can also reach the environment that "
            + $"would perform it: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The source with adjacent string literals joined: <c>"auto" + "start"</c> becomes <c>"autostart"</c>. A
    /// scan that did not do this is defeated by the one idiom somebody reaches for precisely when they are
    /// trying not to trip it.
    /// </summary>
    private static string Joined(string text) => Concatenation().Replace(text, string.Empty);

    [GeneratedRegex("\"\\s*\\+\\s*\"")]
    private static partial Regex Concatenation();

    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(Project(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Generated(file) && Path.GetFileName(file) != Path.GetFileName(Self()));

    private static bool Generated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>This file names the verb and both of the dangerous names, because it is the rule about them.</summary>
    private static string Self() => "NothingHereRegistersAnythingTests.cs";

    /// <summary>This test project's own directory, found from the repository root the way every guard here does.</summary>
    private static string Project()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "runtime", "tests", "Jason.App.Tests");
    }
}
