using Jason.Cli;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The walkthrough a person follows, typed. A command that stopped existing — renamed, moved under a
/// different noun, given a required option — would leave the page quietly wrong, so every printed command is
/// handed to the real parser here: not executed against anything, only parsed, and a usage error is a failed
/// test.
/// </summary>
/// <remarks>
/// <para>
/// The check is "does this CLI understand it", which is exactly what exit code 2 answers. A command that parses
/// and then cannot reach a runtime answers 3 instead, and that is a pass: what is being guarded is the spelling
/// of the vocabulary, not whether a runtime happens to be running while the tests are.
/// </para>
/// <para>
/// The skills pack used to be guarded here too, three files named one by one. It moved to
/// <c>Jason.App.Tests</c> and is enumerated there instead: a skill may print a line the CLI never sees —
/// <c>runtime run</c> is answered by the runtime service before the CLI is reached — and a guard that knew
/// only this library would call such a line a usage error. What stays here is the page, which types nothing
/// the CLI does not answer.
/// </para>
/// </remarks>
[Collection(WorkingDirectoryCollection.Name)]
public class DocumentedCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_command_the_walkthrough_prints_is_a_command_this_cli_parses() =>
        await AssertEveryCommandParsesAsync(Page("Documentation", "golden-path.md"));

    /// <summary>
    /// The machine these lines are typed against. Nothing here may act on it: the data directory is this
    /// test's own and holds no descriptor, the one command that would start a runtime is handed a process
    /// table that launches nothing, and the one that would register something at logon is handed a registrar
    /// that records rather than registers — the day the walkthrough prints that verb, this guard types it for
    /// real on a developer's machine and on three CI runners.
    /// </summary>
    internal static CliEnvironment Machine(TempPaths dir, StringWriter error) =>
        new(
            new StringWriter(),
            error,
            dir.Paths,
            Processes: new FakeProcessControl
            {
                OnLaunch = _ => new FakeProcessHandle(0) { HasExited = true, ExitCode = 1 },
            },
            Autostart: new Autostart.RecordingRegistrar(),
            Harnesses: Jason.Cli.Skills.HarnessLocators.At(dir.Paths.Root),
            Programs: new FakeProgramRunner());

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
            var exit = await CliApp.RunAsync([.. Arguments(command)], Machine(dir, error), Ct);

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
                // Every option that names a document the CLI reads while it parses. The placeholder differs by
                // option because the CLI checks the shape as it reads: a list of contacts is an array, and a
                // note or a result is an object.
                var content = arguments[index] switch
                {
                    "--file" => "[]",
                    "--note-file" or "--result-file" => "{}",
                    _ => null,
                };

                if (content is null || File.Exists(arguments[index + 1]))
                {
                    continue;
                }

                var placeholder = Path.Combine(root, arguments[index + 1]);
                File.WriteAllText(placeholder, content);
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

    /// <summary>
    /// Every <c>jason …</c> line inside a fenced block, which is where a page prints what to type. The
    /// skills pack has one of these too, in <c>Jason.App.Tests</c>, and deliberately not this one: this
    /// project reads the pages under <c>docs/</c> and knows nothing about the pack, which is the separation
    /// that keeps a CLI test from depending on what ships beside the runtime.
    /// </summary>
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

    /// <summary>The line as a shell would hand it over, by the one splitter every such guard here uses.</summary>
    private static IReadOnlyList<string> Arguments(string command) => ShellWords.Split(command);
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
