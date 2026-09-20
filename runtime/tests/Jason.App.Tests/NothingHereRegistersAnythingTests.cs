namespace Jason.App.Tests;

/// <summary>
/// Nothing in this project types the verb that registers something at logon.
/// </summary>
/// <remarks>
/// <see cref="ModeRouter.RunAsync"/> builds <see cref="Jason.Cli.CliEnvironment.Default"/>, which is the one
/// place this machine's own registrar is constructed. Every test here that runs a command line runs it through
/// that — so a test typing <c>runtime autostart enable</c> would register a logon task on the machine running
/// the suite, and on three CI runners, with nothing in the way. The CLI's own tests substitute the seam; this
/// project cannot, so it does not type the verb at all, and that is read off the sources rather than
/// remembered.
/// </remarks>
public class NothingHereRegistersAnythingTests
{
    [Fact]
    public void No_test_in_this_project_types_the_verb_that_registers_something()
    {
        // Spelled in halves so this file is not its own counter-example.
        const string Verb = "auto" + "start";

        var offenders = Sources()
            .Where(source => File.ReadAllText(source).Contains(Verb, StringComparison.OrdinalIgnoreCase))
            .Select(source => Path.GetFileName(source))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"these files name the verb that registers something at logon, and everything here runs through the real environment: {string.Join(", ", offenders)}");
    }

    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(Project(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Generated(file) && Path.GetFileName(file) != Path.GetFileName(Self()));

    private static bool Generated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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
