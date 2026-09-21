using Jason.Cli;
using Jason.Cli.Tests.Documentation;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// Everything the pack teaches somebody to type, typed — against the argument router of the program that
/// ships, not against one library inside it. The distinction is not academic: <c>runtime run</c> never
/// reaches the CLI, so a guard that only knew the CLI would call the command line a published installation
/// runs in the background a usage error.
/// </summary>
/// <remarks>
/// Nothing here may act on the machine. The data directory is this test's own; the process table launches
/// nothing, because a null seam falls back to the one that starts real processes; the release feed is a
/// handler that throws, because one documented verb reads it; and the seam that would write something at
/// logon is left null, which the production code answers with a refusal rather than by reaching for the
/// machine. It is therefore never <see cref="ModeRouter.RunAsync"/>: that builds the real environment,
/// which is the one path that would register something on a developer's machine and on three CI runners.
/// </remarks>
public class DocumentedSkillCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_command_the_pack_prints_is_a_command_this_program_understands()
    {
        var skills = SkillPack.All();
        Assert.NotEmpty(skills);

        var printed = 0;
        foreach (var skill in skills)
        {
            foreach (var command in SkillPack.PrintedCommands(await File.ReadAllLinesAsync(skill.File, Ct)))
            {
                await AssertUnderstoodAsync(command, skill.File);
                printed++;
            }
        }

        Assert.True(printed > 0, "The pack printed no commands at all, so this guard asserted nothing.");
    }

    /// <summary>
    /// The command line a published installation runs in the background, which is the shape that proves this
    /// guard goes through the router: it is answered by the runtime service and never by the CLI.
    /// </summary>
    [Fact]
    public async Task The_command_line_that_starts_a_background_runtime_is_understood() =>
        await AssertUnderstoodAsync("jason runtime run --detached --data-dir /tmp/jason-somewhere", "this test");

    /// <summary>The one line the version mode answers, typed, so the branch below is exercised by name
    /// whatever the pack happens to print.</summary>
    [Fact]
    public async Task The_version_mode_answers_the_bare_line_and_nothing_longer() =>
        await AssertUnderstoodAsync("jason --version", "this test");

    private static async Task AssertUnderstoodAsync(string command, string source)
    {
        // The splitter drops the program's own name, so what is left is what the program is handed — which
        // is what ModeRouter reads to decide which of its modes answers at all.
        var arguments = ShellWords.Split(command).ToArray();

        switch (ModeRouter.Select(arguments))
        {
            case Mode.RuntimeService:
                // Never ModeRouter.RunAsync: this only asks whether the line would be accepted. Parse throws
                // RuntimeRunUsageException on a line this program would refuse.
                RuntimeRunArguments.Parse(arguments);
                return;

            case Mode.Version:
                // The router answers only the bare line, so this branch may never bless a longer one:
                // anything after --version falls through to the CLI and is judged there. Asserted
                // rather than assumed, because a branch that returns in silence is indistinguishable
                // from one that blesses whatever it is handed, and the difference shows up the day
                // the router's pattern changes.
                Assert.True(
                    arguments is ["--version"],
                    $"'{command}' in {source} is answered by the version mode although it is longer "
                    + $"than the line that mode answers: [{string.Join(", ", arguments)}].");
                return;

            case Mode.PluginHost:
                Assert.Fail($"'{command}' in {source} starts the plugin host, which no page teaches anybody to type.");
                return;

            default:
                using (var tree = new TempTree())
                {
                    var error = new StringWriter();
                    var exit = await CliApp.RunAsync(Named(arguments, tree.Root), Machine(tree, error), Ct);

                    Assert.True(
                        exit != ExitCodes.Usage,
                        $"'{command}' is printed in {source} and this program does not understand it:"
                        + $"{Environment.NewLine}{error}");
                }

                return;
        }
    }

    /// <summary>
    /// The documents a printed command names, made to exist. The CLI reads such a file while it parses, so
    /// without this the guard would be reporting that a page's example data is not on this machine rather
    /// than that its command has the wrong spelling. The placeholder differs by option because the CLI checks
    /// the shape as it reads: a list of contacts is an array, a note or a result is an object.
    /// </summary>
    /// <remarks>
    /// The name is rewritten to an absolute path inside this test's own directory rather than the process
    /// being moved to it. A working directory is process-wide, and a guard that moved it would have to be
    /// kept away from everything else running beside it.
    /// </remarks>
    private static string[] Named(string[] arguments, string root)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
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

            Directory.CreateDirectory(root);
            var placeholder = Path.Combine(root, Path.GetFileName(arguments[index + 1]));
            File.WriteAllText(placeholder, content);
            arguments[index + 1] = placeholder;
        }

        return arguments;
    }

    /// <summary>The machine these lines are typed against, and every way in which it is not this machine.</summary>
    internal static CliEnvironment Machine(TempTree tree, StringWriter error) =>
        new(
            new StringWriter(),
            error,
            new JasonPaths(tree.Root),
            HttpHandler: new Unreachable(),
            Processes: new FakeProcessControl
            {
                OnLaunch = _ => new FakeProcessHandle(0) { HasExited = true, ExitCode = 1 },
            });

    /// <summary>
    /// Every request refused before it leaves the process: one documented verb reads the release feed rather
    /// than the runtime, and typing it for real would be the first test here to open a socket.
    /// </summary>
    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network in tests");
    }
}
