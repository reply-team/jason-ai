using System.CommandLine;
using System.Net;
using System.Text;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

/// <summary>
/// Under <c>JASON_CONFINED</c>, an option of an operation verb that reads a file's content into a request takes only
/// <c>-</c>: the content comes by standard input, so whatever reads the file is the shell the caller's host already
/// checks, never this CLI. Anything else is refused before the file is opened and before any request, exit 1, naming
/// the cure. Unset or empty, nothing changes.
/// </summary>
public class ConfinedFileOptionsTests
{
    private const string Variable = "JASON_CONFINED";

    /// <summary>
    /// The options the command tree says read a file into a request, each with the words its verb needs besides it. A
    /// file option added later is a difference here before it is a path the confinement does not cover.
    /// </summary>
    internal static readonly Dictionary<string, string[]> Rows = new(StringComparer.Ordinal)
    {
        ["campaign archive --file"] = ["campaign", "archive", "cmp_A"],
        ["campaign create --file"] = ["campaign", "create"],
        ["campaign pause --file"] = ["campaign", "pause", "cmp_A"],
        ["campaign start --file"] = ["campaign", "start", "cmp_A"],
        ["campaign update --file"] = ["campaign", "update", "cmp_A"],
        ["campaign update-context --file"] = ["campaign", "update-context", "cmp_A"],
        ["campaign add-contacts --file"] = ["campaign", "add-contacts", "cmp_A"],
        ["campaign remove-contacts --file"] = ["campaign", "remove-contacts", "cmp_A"],
        ["contact create --file"] = ["contact", "create"],
        ["contact update --file"] = ["contact", "update", "cnt_A"],
        ["contact archive --file"] = ["contact", "archive", "cnt_A"],
        ["journal append --file"] = ["journal", "append", "cmp_A", "--kind", "decision"],
        ["preference set --value-file"] = ["preference", "set", "tone_of_voice", "--actor", "human:ada"],
        ["profile create --file"] = ["profile", "create", "local-claude"],
        ["profile update --file"] = ["profile", "update", "local-claude"],
        ["project create --file"] = ["project", "create"],
        ["project update --file"] = ["project", "update", "prj_A"],
        ["report submit --file"] = ["report", "submit", "--actor", "human:ada"],
        ["role add --file"] = ["role", "add", "fake"],
        ["rolenote set --note-file"] = ["rolenote", "set", "cmp_A", "researcher"],
        ["workitem create --file"] = ["workitem", "create", "cmp_A"],
        ["workitem update --file"] = ["workitem", "update", "wi_A"],
        ["workitem cancel --file"] = ["workitem", "cancel", "wi_A"],
        ["workitem resume --file"] = ["workitem", "resume", "wi_A", "--actor", "human:ada"],
        ["workitem set-result --result-file"] = ["workitem", "set-result", "wi_A", "--attempt", "att_A"],
        ["workitem complete --result-file"] = ["workitem", "complete", "wi_A", "--attempt", "att_A", "--status", "succeeded"],
    };

    /// <summary>
    /// Options that name a path and are not under the rule, each for its own reason: <c>project link --path</c> names a
    /// folder the runtime adopts and sends no file's content; <c>doctor --output</c> writes, and is asked each time;
    /// export and import run under the variable as without it, and the desktop's chat never runs them; <c>logs</c>
    /// takes no path.
    /// </summary>
    private static readonly string[] NotUnderTheRule = ["project link --path", "doctor --output"];

    [Fact]
    public void Every_option_that_reads_a_file_into_a_request_has_a_row()
    {
        var derived = Derived();
        Assert.True(
            Rows.Keys.Order(StringComparer.Ordinal).SequenceEqual(derived.Order(StringComparer.Ordinal)),
            $"The tree derives: {string.Join(", ", derived.Order(StringComparer.Ordinal))}");
    }

    /// <summary>
    /// The options spelt as file options are the options of the type the one reader serves, both ways round: a file option
    /// made as a plain option would read a path outside the rule.
    /// </summary>
    [Fact]
    public void Every_such_option_is_one_the_one_reader_serves()
    {
        var byType = Options().Where(pair => pair.Value is RequestFileOption).Select(pair => pair.Key).Order(StringComparer.Ordinal);
        Assert.Equal(Derived().Order(StringComparer.Ordinal), byType);
    }

    [Fact]
    public void The_options_that_name_a_path_for_another_reason_are_not_under_the_rule()
    {
        var options = Options().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        foreach (var key in NotUnderTheRule)
        {
            Assert.True(options.ContainsKey(key), $"'{key}' is no longer in the command tree; the rule's exceptions name it.");
            Assert.DoesNotContain(key, Derived());
        }
    }

    public static TheoryData<string> Keys() => [.. Rows.Keys.Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Confined_a_path_is_refused_before_the_file_is_opened_and_before_any_request(string key)
    {
        using var file = new HeldFile("{}");
        var run = await RunAsync(key, file.Path, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Equal(0, run.Requests);
        Assert.Equal(string.Empty, run.Output);
        var option = Option(key);
        Assert.Contains($"{option} - < ", run.Error, StringComparison.Ordinal);
        Assert.Contains("here-document", run.Error, StringComparison.Ordinal);
        Assert.Contains(Variable, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Confined_a_dash_reads_standard_input_as_before(string key)
    {
        var run = await RunAsync(key, "-", new Dictionary<string, string> { [Variable] = "yes" }, standardInput: "{}");

        Assert.True(run.Exit == ExitCodes.Success && run.Requests == 1, $"exit {run.Exit}, {run.Requests} requests: {run.Error}");
    }

    /// <summary>Unset and empty are today's behaviour, byte for byte: the same exit, the same words, the same request.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Unset_or_empty_a_path_is_read_as_before(string key)
    {
        using var file = new TempFile("{}");
        var unset = await RunAsync(key, file.Path, new Dictionary<string, string>());
        var empty = await RunAsync(key, file.Path, new Dictionary<string, string> { [Variable] = string.Empty });

        Assert.True(unset.Exit == ExitCodes.Success && unset.Requests == 1, $"exit {unset.Exit}, {unset.Requests} requests: {unset.Error}");
        Assert.Equal(unset, empty);
    }

    /// <summary>Unset and empty, a file that is not there is the usage error it was, word for word.</summary>
    [Fact]
    public async Task Unset_or_empty_a_missing_file_says_what_it_said()
    {
        var missing = Path.Combine(Path.GetTempPath(), "jason-no-such-" + Guid.NewGuid().ToString("N") + ".json");
        foreach (var variables in new[] { new Dictionary<string, string>(), new Dictionary<string, string> { [Variable] = string.Empty } })
        {
            var body = await RunAsync("campaign create --file", missing, variables);
            Assert.Equal(ExitCodes.Usage, body.Exit);
            Assert.Equal($"No such file: '{missing}'.{Environment.NewLine}Run 'jason --help' for usage.{Environment.NewLine}", body.Error);

            var result = await RunAsync("workitem set-result --result-file", missing, variables);
            Assert.Equal(ExitCodes.Usage, result.Exit);
            Assert.Equal($"--result-file: no such file: '{missing}'.{Environment.NewLine}Run 'jason --help' for usage.{Environment.NewLine}", result.Error);
        }
    }

    internal static string Option(string key) => key[(key.LastIndexOf(' ') + 1)..];

    private static async Task<Run> RunAsync(string key, string value, Dictionary<string, string> variables, string? standardInput = null)
    {
        using var paths = new TempPaths();
        paths.WriteDescriptor(new RuntimeDescriptor("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch));
        var requests = 0;
        var output = new StringWriter();
        var error = new StringWriter();
        var environment = new CliEnvironment(
            output,
            error,
            paths.Paths,
            new FakeHandler(_ =>
            {
                requests++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }),
            standardInput is null ? null : new StringReader(standardInput),
            Variables: variables);

        var exit = await CliApp.RunAsync([.. Rows[key], Option(key), value], environment, TestContext.Current.CancellationToken);
        return new Run(exit, requests, output.ToString(), error.ToString());
    }

    /// <summary>The options the tree gives an operation verb that read a file's content into its request.</summary>
    private static List<string> Derived() =>
        [.. Options().Where(pair => pair.Value.Name == "--file" || pair.Value.Name.EndsWith("-file", StringComparison.Ordinal)).Select(pair => pair.Key)];

    private static IEnumerable<KeyValuePair<string, Option>> Options()
    {
        using var paths = new TempPaths();
        var found = new List<KeyValuePair<string, Option>>();
        Walk(CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths)), string.Empty);
        return found;

        void Walk(Command command, string path)
        {
            if (command.Subcommands.Count == 0)
            {
                found.AddRange(command.Options.Select(option => new KeyValuePair<string, Option>($"{path} {option.Name}", option)));
            }

            foreach (var sub in command.Subcommands)
            {
                Walk(sub, $"{path} {sub.Name}".Trim());
            }
        }
    }

    internal sealed record Run(int Exit, int Requests, string Output, string Error);

    /// <summary>A file this test holds open with no sharing, so a CLI that opened it would say it could not be read.</summary>
    private sealed class HeldFile : IDisposable
    {
        private readonly FileStream _held;

        public HeldFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jason-held-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(Path, content);
            _held = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        public string Path { get; }

        public void Dispose()
        {
            _held.Dispose();
            File.Delete(Path);
        }
    }
}
