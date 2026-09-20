using Jason.Cli;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The commands the published pages tell somebody to type, typed. A page is often the first thing a new operator
/// runs, and an example that answers "unrecognized option" teaches them the tool is broken before it teaches
/// them anything else.
/// <para>
/// Every line in the pages that begins with one of the nouns below is run against the real command line,
/// pointed at a data directory with no runtime in it. What is asserted is that it is not a **usage** error:
/// exit 2 is the CLI saying it does not understand what was typed, which is the way a documented command stops
/// working when an option is renamed. Exit 3 — no runtime listening — is the right answer here and is what a
/// passing line gives.
/// </para>
/// </summary>
public class DocumentedPageCommandsTests
{
    private const int UsageError = 2;

    /// <summary>
    /// The verb groups these pages print. Anything an operator is told to type belongs here; what keeps the
    /// list honest is that a noun added to the pages and not to this list is simply unguarded, which is how the
    /// profile pages came to document an option the CLI did not have.
    /// </summary>
    private static readonly string[] Nouns =
    [
        "jason profile ", "jason rolenote ", "jason campaign ", "jason workitem ", "jason decision ", "jason update ",
        "jason runtime autostart ",
    ];

    /// <summary>The pages whose printed commands are guarded.</summary>
    private static readonly string[] Pages =
    [
        "README.md",
        Path.Combine("docs", "execution-profiles.md"),
        Path.Combine("docs", "campaign-manager.md"),
        Path.Combine("docs", "release-and-update.md"),
    ];

    public static TheoryData<string, string> DocumentedCommands()
    {
        var data = new TheoryData<string, string>();
        foreach (var (page, line) in Printed())
        {
            data.Add(page, line);
        }

        return data;
    }

    private static IEnumerable<(string Page, string Command)> Printed() =>
        Pages.SelectMany(page => Commands(page).Select(line => (page, line)));

    [Theory]
    [MemberData(nameof(DocumentedCommands))]
    public async Task Every_documented_command_is_understood(string page, string command)
    {
        using var dir = new TempPaths();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            Named(Tokens(command), dir.Paths.Root),
            Machine(dir, error),
            TestContext.Current.CancellationToken);

        Assert.True(exit != UsageError, $"{page} documents `{command}`, and the CLI answers: {error}");
    }

    /// <summary>At least one line, or the theory above asserts nothing at all.</summary>
    [Fact]
    public void The_pages_carry_commands_to_check() => Assert.NotEmpty(DocumentedCommands());

    /// <summary>
    /// And every noun in that list is really printed by one of these pages. A noun nobody prints is a guard
    /// over nothing, which looks exactly like a guard: the list above is only honest if adding to it is what
    /// brings lines under the theory, and removing a documented command is what takes them out.
    /// </summary>
    [Theory]
    [MemberData(nameof(GuardedNouns))]
    public void Every_guarded_noun_is_printed_by_a_page(string noun) =>
        Assert.Contains(Printed(), printed => printed.Command.StartsWith(noun, StringComparison.Ordinal));

    public static TheoryData<string> GuardedNouns() => [.. Nouns];

    /// <summary>
    /// The machine every printed line is typed against: a data directory of this test's own, no network, and a
    /// registrar that records rather than registers.
    /// </summary>
    /// <remarks>
    /// The last one is not a nicety. These lines are typed <em>for real</em>, and one of the nouns above is
    /// <c>jason runtime autostart </c>: with this machine's own registrar behind it, running this guard would
    /// leave a logon task on the machine that ran it — a developer's, and three CI runners' — every time the
    /// suite ran.
    /// </remarks>
    internal static CliEnvironment Machine(TempPaths dir, StringWriter error) =>
        new(new StringWriter(), error, dir.Paths, new Unreachable(), Autostart: new Autostart.RecordingRegistrar());

    /// <summary>
    /// Every request refused before it leaves the process. Most of these commands never get this far — there is
    /// no runtime behind the data directory, so they stop at the missing descriptor — but <c>jason update check</c>
    /// reads the release feed rather than the runtime, and typing it for real would be the first test in this
    /// repository to open a socket to the internet. Refusing the request leaves the verb doing exactly what it
    /// does on a machine with no network: exit 1 with a code, which is not the usage error under test.
    /// </summary>
    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network in tests");
    }

    /// <summary>
    /// The documents a printed command names, made to exist. The CLI reads such a file while it parses, so
    /// without this the guard would be reporting that a page's example data is not on this machine rather than
    /// that its command has the wrong spelling. The placeholder differs by option because the CLI checks the
    /// shape as it reads: a list of contacts is an array, a note or a result is an object.
    /// </summary>
    private static string[] Named(string[] tokens, string root)
    {
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            var content = tokens[index] switch
            {
                "--file" => "[]",
                "--note-file" or "--result-file" => "{}",
                _ => null,
            };

            if (content is null || File.Exists(tokens[index + 1]))
            {
                continue;
            }

            Directory.CreateDirectory(root);
            var placeholder = Path.Combine(root, Path.GetFileName(tokens[index + 1]));
            File.WriteAllText(placeholder, content);
            tokens[index + 1] = placeholder;
        }

        return tokens;
    }

    private static IEnumerable<string> Commands(string page)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), page));

        // A shell continuation is one command spread over two lines; the reader types it as one.
        var joined = text.Replace("\\\r\n", " ", StringComparison.Ordinal).Replace("\\\n", " ", StringComparison.Ordinal);
        foreach (var line in joined.Split('\n'))
        {
            var trimmed = line.Trim();
            if (Nouns.Any(noun => trimmed.StartsWith(noun, StringComparison.Ordinal)))
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>The tokens a shell would hand the program, by the one splitter every such guard here uses.</summary>
    private static string[] Tokens(string command) => [.. ShellWords.Split(command)];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
