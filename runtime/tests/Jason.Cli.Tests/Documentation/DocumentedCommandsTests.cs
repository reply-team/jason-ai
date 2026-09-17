using System.Text;
using Jason.Cli;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// Everything this repository teaches somebody to type, typed. The skill an agent reads and the walkthrough a
/// person follows both print commands, and a command that stopped existing — renamed, moved under a different
/// noun, given a required option — would leave both of them quietly wrong. So every printed command is handed
/// to the real parser here: not executed against anything, only parsed, and a usage error is a failed test.
/// </summary>
/// <remarks>
/// The check is "does this CLI understand it", which is exactly what exit code 2 answers. A command that parses
/// and then cannot reach a runtime answers 3 instead, and that is a pass: what is being guarded is the spelling
/// of the vocabulary, not whether a runtime happens to be running while the tests are.
/// </remarks>
[Collection(WorkingDirectoryCollection.Name)]
public class DocumentedCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_command_the_skill_prints_is_a_command_this_cli_parses() =>
        await AssertEveryCommandParsesAsync(Page("Skills", "runtime", "managed-campaign-work", "SKILL.md"));

    [Fact]
    public async Task Every_command_the_walkthrough_prints_is_a_command_this_cli_parses() =>
        await AssertEveryCommandParsesAsync(Page("Documentation", "golden-path.md"));

    [Fact]
    public void The_skill_says_what_it_is_and_does_not_oversell_it()
    {
        var skill = File.ReadAllText(Page("Skills", "runtime", "managed-campaign-work", "SKILL.md"));

        // An honest status, because a first draft that called itself finished would be the one claim in it a
        // reader could not check.
        Assert.Contains("status: draft", skill, StringComparison.Ordinal);

        // And no promise the product does not make: no provider is configured out of the box, and the skill has
        // to say a route is somebody's explicit act rather than assume one exists.
        Assert.Contains("route", skill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("out of the box", skill, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The file as MSBuild copied it beside these tests, so nothing depends on the working directory.</summary>
    private static string Page(params string[] parts)
    {
        var path = Path.Combine([AppContext.BaseDirectory, .. parts]);
        Assert.True(File.Exists(path), $"The page these tests guard is not beside them: '{path}'.");
        return path;
    }

    private static async Task AssertEveryCommandParsesAsync(string page)
    {
        var commands = Commands(await File.ReadAllLinesAsync(page, Ct));
        Assert.NotEmpty(commands);

        foreach (var command in commands)
        {
            using var dir = new TempPaths();
            using var named = new NamedFiles(Arguments(command), dir.Paths.Root);
            var error = new StringWriter();

            // Nothing here may act on the machine. The data directory is this test's own and holds no
            // descriptor, so every command that needs a runtime ends at "nothing is running" — and the one
            // command that would start one is handed a process table that launches nothing.
            var environment = new CliEnvironment(
                new StringWriter(),
                error,
                dir.Paths,
                Processes: new FakeProcessControl
                {
                    OnLaunch = _ => new FakeProcessHandle(0) { HasExited = true, ExitCode = 1 },
                });

            var exit = await CliApp.RunAsync([.. Arguments(command)], environment, Ct);

            Assert.True(
                exit != ExitCodes.Usage,
                $"'{command}' is printed in {Path.GetFileName(page)} and this CLI does not understand it:{Environment.NewLine}{error}");
        }
    }

    /// <summary>
    /// The files a printed command names with <c>--file</c>, made to exist while it is parsed and taken away
    /// afterwards. The CLI reads such a file as it parses, so without this the guard would be reporting that a
    /// page's example data is not on this machine rather than that its command has the wrong spelling.
    /// </summary>
    /// <remarks>
    /// A page prints a relative name, and the CLI resolves one against the working directory — so the working
    /// directory becomes this test's own for as long as the command is parsed, and the placeholder is written
    /// there. The guard writes nothing outside the directory it owns, and this class is in a collection of its
    /// own so that nothing else in the assembly runs while the working directory is somewhere else.
    /// </remarks>
    private sealed class NamedFiles : IDisposable
    {
        private readonly List<string> _created = [];
        private readonly string _previous = Directory.GetCurrentDirectory();

        public NamedFiles(IReadOnlyList<string> arguments, string root)
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);

            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (arguments[index] != "--file" || File.Exists(arguments[index + 1]))
                {
                    continue;
                }

                var placeholder = Path.Combine(root, arguments[index + 1]);
                File.WriteAllText(placeholder, "[]");
                _created.Add(placeholder);
            }
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previous);
            foreach (var file in _created)
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>Every <c>jason …</c> line inside a fenced block, which is where a page prints what to type.</summary>
    private static IReadOnlyList<string> Commands(IReadOnlyList<string> lines)
    {
        var commands = new List<string>();
        var fenced = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            var text = line.Trim();
            if (fenced && text.StartsWith("jason ", StringComparison.Ordinal))
            {
                commands.Add(text);
            }
        }

        return commands;
    }

    /// <summary>
    /// The line as a shell would hand it over: quoted runs stay together, and the program's own name is dropped.
    /// A JSON argument is the reason this exists — it is one argument however many spaces it contains.
    /// </summary>
    private static IReadOnlyList<string> Arguments(string command)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var started = false;
        var escaped = false;

        foreach (var character in command)
        {
            if (quote != '\0')
            {
                // A quoted JSON argument carries quotes of its own, escaped the way a shell requires — so the
                // escape has to be understood here too, or every such example would look like a broken command.
                if (escaped)
                {
                    current.Append(character);
                    escaped = false;
                }
                else if (character == '\\' && quote == '"')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '\'' or '"':
                    quote = character;
                    started = true;
                    break;

                case ' ':
                    if (started)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                        started = false;
                    }

                    break;

                default:
                    current.Append(character);
                    started = true;
                    break;
            }
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return [.. arguments.Skip(1)];
    }
}

/// <summary>
/// The tests that move the process's working directory, kept away from everything else in this assembly while
/// they do: a directory is process-wide, and a test that read the wrong one would fail for a reason that has
/// nothing to do with what it is about.
/// </summary>
[CollectionDefinition(WorkingDirectoryCollection.Name, DisableParallelization = true)]
public class WorkingDirectoryCollection
{
    public const string Name = "working directory";
}
