using Jason.Cli;
using Jason.Cli.Tests.Documentation;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
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
/// <para>
/// A page under <c>skills/runtime/roles/</c> is a role's page: a role's agent reads it inside a run, so the lines it
/// prints are typed under a run's environment, an attempt and a work item named by made-up ids. Every other page is
/// typed outside a run. The folder decides, never a list of pages; so a role's page that prints a line the CLI refuses
/// inside a run fails here, as a person's page that prints a line the CLI refuses outside one does.
/// </para>
/// </remarks>
public class DocumentedSkillCommandsTests
{
    private const string Attempt = "att_01K7QN0000000000000000000R";

    private const string WorkItem = "wi_01K7QN0000000000000000000W";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.D1, "a skill's printed command parses")]
    public async Task Every_command_the_pack_prints_is_a_command_this_program_understands()
    {
        var skills = SkillPack.All();
        Assert.NotEmpty(skills);

        var printed = 0;
        foreach (var skill in skills)
        {
            foreach (var command in SkillPack.PrintedCommands(await File.ReadAllLinesAsync(skill.File, Ct)))
            {
                await AssertUnderstoodAsync(command, skill);
                printed++;
            }
        }

        Assert.True(printed > 0, "The pack printed no commands at all, so this guard asserted nothing.");
    }

    /// <summary>
    /// The manager's page prints lifting a suppression without <c>--actor</c>, which the CLI refuses outside a run and
    /// sends as the attempt inside one. The page is a role's, so its line is typed inside a run and understood; the
    /// same line typed outside a run is refused, which is what this guard said of every page before it read the folder.
    /// </summary>
    [Fact]
    public async Task A_role_page_s_line_is_typed_inside_a_run_where_the_attempt_names_who_acts()
    {
        var manager = SkillPack.Find("manager");
        Assert.True(manager.IsRole);
        var line = Assert.Single(
            SkillPack.PrintedCommands(manager.File),
            command => command.StartsWith("jason suppression remove ", StringComparison.Ordinal));
        Assert.DoesNotContain("--actor", line, StringComparison.Ordinal);

        await AssertUnderstoodAsync(line, manager);

        var outside = await Assert.ThrowsAsync<Xunit.Sdk.TrueException>(
            () => AssertUnderstoodAsync(line, manager.File));
        Assert.Contains("suppression remove needs --actor", outside.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page outside <c>roles/</c> is a person's, and its lines are typed outside a run: a line that needs an actor
    /// and names none is refused there, whatever a run would have made of it.
    /// </summary>
    [Fact]
    public async Task A_page_outside_roles_is_typed_outside_a_run_where_a_line_without_an_actor_is_refused()
    {
        var page = SkillPack.Find("troubleshooting-jason");
        Assert.False(page.IsRole);
        const string Line =
            "jason suppression remove --channel email --value someone@example.com --reason \"a made-up case\"";

        var refused = await Assert.ThrowsAsync<Xunit.Sdk.TrueException>(() => AssertUnderstoodAsync(Line, page));
        Assert.Contains("suppression remove needs --actor", refused.Message, StringComparison.Ordinal);

        await AssertUnderstoodAsync("jason --actor human:ada " + Line["jason ".Length..], page);
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

    /// <summary>
    /// A file a printed command writes lands in this test's own directory, never where the suite happens to run: these
    /// lines are typed for real, and a page that printed <c>jason doctor --output bundle.json</c> would otherwise leave a
    /// bundle in the repository, or in the test host's own directory, every time the suite ran. Every spelling of the
    /// option the parser accepts is typed, and the bundle has to be written — inside this test's own directory — for the
    /// test to pass: a line the verb refused would leave no stray either.
    /// </summary>
    [Theory]
    [InlineData("jason doctor --output {0}")]
    [InlineData("jason doctor --output={0}")]
    [InlineData("jason doctor --output:{0}")]
    public async Task A_file_a_printed_command_writes_lands_in_this_tests_own_directory(string line)
    {
        var name = $"guard-{Guid.NewGuid():N}.json";
        var stray = Path.Combine(Directory.GetCurrentDirectory(), name);
        try
        {
            using var tree = new TempTree();
            var error = new StringWriter();

            var exit = await CliApp.RunAsync(
                Named([.. ShellWords.Split(string.Format(System.Globalization.CultureInfo.InvariantCulture, line, name))], tree.Root),
                Machine(tree, error),
                Ct);

            Assert.True(exit == ExitCodes.Success, $"Typing '{line}' exited {exit}: {error}");
            Assert.True(File.Exists(Path.Combine(tree.Root, name)), $"Typing '{line}' wrote nothing inside this test's own directory.");
            Assert.False(File.Exists(stray), $"Typing '{line}' wrote {stray}, outside this test's own directory.");
        }
        finally
        {
            File.Delete(stray);
        }
    }

    /// <summary>
    /// Typed at this program, with four seams in place: its own data directory, a process table that launches
    /// nothing, a release-feed handler that throws, and no autostart registrar at all — which production
    /// answers with a refusal. Internal because the business pack's guard types its lines through this same
    /// method: a second copy with its own environment is how a documentation guard starts a process on three
    /// CI runners.
    /// </summary>
    internal static Task AssertUnderstoodAsync(string command, string source) =>
        AssertUnderstoodAsync(command, source, variables: null);

    /// <summary>
    /// A line a page of the pack prints, typed as that page's reader types it: inside a run for a role's page.
    /// </summary>
    private static Task AssertUnderstoodAsync(string command, PackSkill page) =>
        AssertUnderstoodAsync(command, page.File, page.IsRole ? ARun() : null);

    /// <summary>
    /// The environment of a run, as the launcher hands it to a role's agent: this process's own variables, which a
    /// child inherits, and the attempt and the work item it runs for.
    /// </summary>
    private static Dictionary<string, string> ARun()
    {
        var variables = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        variables[ExecutionEnvironment.AttemptIdVariable] = Attempt;
        variables[ExecutionEnvironment.WorkItemIdVariable] = WorkItem;
        return variables;
    }

    private static async Task AssertUnderstoodAsync(
        string command, string source, IReadOnlyDictionary<string, string>? variables)
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
                    var machine = Machine(tree, error) with { Variables = variables };
                    var exit = await CliApp.RunAsync(Named(arguments, tree.Root), machine, Ct);

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
    /// <para>
    /// The name is rewritten to an absolute path inside this test's own directory rather than the process
    /// being moved to it. A working directory is process-wide, and a guard that moved it would have to be
    /// kept away from everything else running beside it.
    /// </para>
    /// <para>
    /// A file a command writes is moved there the same way, whatever the page printed: <c>--output</c>, which
    /// <c>jason doctor</c> takes, in each of the spellings the parser accepts. These lines are typed for real,
    /// and a relative name would otherwise be written wherever the suite runs — the repository, or the test
    /// host's own directory. It is not made to exist, because the verb refuses a file that is already there.
    /// </para>
    /// </remarks>
    private static string[] Named(string[] arguments, string root)
    {
        const string Output = "--output";
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index].Length > Output.Length + 1
                && arguments[index].StartsWith(Output, StringComparison.Ordinal)
                && arguments[index][Output.Length] is '=' or ':')
            {
                arguments[index] = arguments[index][..(Output.Length + 1)] + Path.Combine(root, Path.GetFileName(arguments[index][(Output.Length + 1)..]));
                continue;
            }

            if (index == arguments.Length - 1)
            {
                break;
            }

            if (arguments[index] == Output)
            {
                arguments[index + 1] = Path.Combine(root, Path.GetFileName(arguments[index + 1]));
                continue;
            }

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
            new JasonPaths(tree.Root, 1),
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
