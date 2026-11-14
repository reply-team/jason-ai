using System.CommandLine;
using System.Text.Json;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

/// <summary>
/// The layout gate: every verb that touches a part of the data directory that moves between layouts, or starts a runtime
/// that would, is refused where the stamp names a layout newer than this build runs or cannot be read — locally, before
/// anything is read, written, stopped, started or asked; and nothing else is.
/// </summary>
public sealed class LayoutGateTests : IDisposable
{
    private const string Version = "0.42.0";

    private const string Newer = """{"layout":2,"written_by":"0.43.0","at":"2026-11-20T10:00:00Z"}""";

    private const string NewerSentence =
        "This data directory was set up by Jason 0.43.0, newer than this jason (0.42.0): update this jason, or use the one that set it up.";

    /// <summary>What the update verbs say instead: updating this jason is what they are, so they name the install line.</summary>
    private const string UpdateNewerSentence =
        "This data directory was set up by Jason 0.43.0, newer than this jason (0.42.0), and this jason's update cannot follow it: install Jason 0.43.0 or newer by running the install line again, or use the one that set it up.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-cli-tests", "layout-gate-" + Guid.NewGuid().ToString("N"));

    public LayoutGateTests() => Directory.CreateDirectory(_root);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Every gated command line, in the shape a person or the desktop types it.</summary>
    private static readonly string[] GatedLines =
    [
        "runtime start",
        "runtime restart",
        "plugin install reply",
        "plugin reload",
        "skills install",
        "skills update",
        "ui install",
        "update apply",
        "update rollback",
        "export",
        "export --stop",
        "import jason-export-20261108T093000Z.zip",
        "uninstall --purge-data",
        "uninstall --purge-data --yes",
        "uninstall --purge-data --dry-run",
        "session open --setup",
        "session open --setup --dry-run",
    ];

    public static TheoryData<string> Gated() => new(GatedLines);

    [Fact]
    public void The_table_is_these_commands_and_each_is_in_the_command_tree_with_its_flag()
    {
        Assert.Equal(
            [
                ("runtime start", null), ("runtime restart", null), ("plugin install", null), ("plugin reload", null),
                ("skills install", null), ("skills update", null), ("ui install", null), ("update apply", null),
                ("update rollback", null), ("export", null), ("import", null), ("uninstall", "--purge-data"),
                ("session open", "--setup"),
            ],
            LayoutGatedVerbs.Table);

        var root = CliApp.BuildRootCommand(Env());
        foreach (var (command, flag) in LayoutGatedVerbs.Table)
        {
            var found = command.Split(' ').Aggregate<string, Command?>(root, (parent, name) => parent?.Subcommands.FirstOrDefault(sub => sub.Name == name));
            Assert.True(found is not null, $"'{command}' is not a command.");
            Assert.True(flag is null || found.Options.Any(option => option.Name == flag), $"'{command}' has no {flag}.");
            Assert.Contains(flag is null ? LayoutGatedVerbs.Help : "With " + flag + ":", found.Description, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Gated))]
    public async Task A_newer_layout_refuses_each_gated_verb_before_anything_is_done(string line)
    {
        var args = line.Split(' ');
        File.WriteAllText(Path.Combine(_root, "layout.json"), Newer);

        var (exit, output, error, processes, asked, before) = await RunAsync(args);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("layout_newer", envelope.GetProperty("code").GetString());
        Assert.Equal(line.StartsWith("update ", StringComparison.Ordinal) ? UpdateNewerSentence : NewerSentence, envelope.GetProperty("message").GetString());
        Assert.False(envelope.GetProperty("retryable").GetBoolean());
        Assert.Equal(string.Empty, error);
        Assert.Empty(processes.Launches);
        Assert.Empty(asked);
        Assert.Equal(before, Listing());
    }

    /// <summary>
    /// The update verbs never say "update this jason", which is what they were asked to do: they name the install line, which
    /// takes any version, and keep the code. Every other gated verb says the sentence every refusal of a newer layout says.
    /// </summary>
    [Theory]
    [InlineData("update apply", UpdateNewerSentence)]
    [InlineData("update rollback", UpdateNewerSentence)]
    [InlineData("plugin reload", NewerSentence)]
    public async Task The_update_verbs_name_the_install_line_and_every_other_verb_says_update_this_jason(string line, string sentence)
    {
        File.WriteAllText(Path.Combine(_root, "layout.json"), """{"layout":2,"written_by":" 0.43.0+\u001b[31mx","at":"2026-11-20T10:00:00Z"}""");

        var (exit, output, _, _, _, _) = await RunAsync(line.Split(' '));

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("layout_newer", envelope.GetProperty("code").GetString());
        Assert.Equal(sentence, envelope.GetProperty("message").GetString());
    }

    [Theory]
    [MemberData(nameof(Gated))]
    public async Task An_unreadable_stamp_refuses_each_gated_verb_before_anything_is_done(string line)
    {
        var args = line.Split(' ');
        File.WriteAllText(Path.Combine(_root, "layout.json"), """{"layout":0,"written_by":"0.43.0"}""");

        var (exit, output, _, processes, asked, before) = await RunAsync(args);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("layout_unreadable", envelope.GetProperty("code").GetString());
        Assert.Equal(
            $"This data directory's layout stamp at {Path.Combine(_root, "layout.json")} cannot be read (its layout is 0, and layouts count from 1), so this jason cannot tell how the directory is laid out: use the Jason that set it up, or update this jason.",
            envelope.GetProperty("message").GetString());
        Assert.Empty(processes.Launches);
        Assert.Empty(asked);
        Assert.Equal(before, Listing());
    }

    [Fact]
    public async Task Under_human_the_sentence_goes_to_standard_error_and_nothing_to_standard_output()
    {
        File.WriteAllText(Path.Combine(_root, "layout.json"), Newer);

        var (exit, output, error, _, _, _) = await RunAsync(["runtime", "start", "--human"]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(string.Empty, output);
        Assert.Equal(NewerSentence, error.TrimEnd());
    }

    [Fact]
    public async Task Help_is_not_a_run_of_the_verb_and_is_not_refused()
    {
        File.WriteAllText(Path.Combine(_root, "layout.json"), Newer);

        var (exit, output, _, _, _, _) = await RunAsync(["export", "--help"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(LayoutGatedVerbs.Help, output, StringComparison.Ordinal);
    }

    /// <summary>With no stamp, or one that says layout 1, the gate lets every gated verb through to its own answer.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("""{"layout":1,"written_by":"0.42.0"}""")]
    public void A_layout_this_build_runs_lets_every_gated_verb_through(string? stamp)
    {
        if (stamp is not null)
        {
            File.WriteAllText(Path.Combine(_root, "layout.json"), stamp);
        }

        var env = Env();
        var root = CliApp.BuildRootCommand(env);
        foreach (var args in GatedLines.Select(line => line.Split(' ')))
        {
            var parsed = root.Parse(args);
            Assert.Empty(parsed.Errors);
            Assert.Null(LayoutGatedVerbs.Refusal(env, parsed));
        }
    }

    /// <summary>
    /// Only the purge of <c>uninstall</c> touches the data directory; the rest of it, the diagnosing verbs and a verb that
    /// only asks the runtime, are never refused by the stamp.
    /// </summary>
    [Theory]
    [InlineData("uninstall")]
    [InlineData("uninstall --dry-run")]
    [InlineData("status")]
    [InlineData("doctor --dry-run")]
    [InlineData("logs")]
    [InlineData("plugin list")]
    [InlineData("runtime status")]
    [InlineData("runtime stop")]
    [InlineData("session open --here")]
    [InlineData("session open --campaign cmp_01")]
    [InlineData("session open --approval apr_01")]
    [InlineData("session open --question dec_01")]
    public void Verbs_outside_the_table_are_never_refused_by_the_stamp(string line)
    {
        File.WriteAllText(Path.Combine(_root, "layout.json"), Newer);
        var env = Env();
        var args = line.Split(' ');

        var parsed = CliApp.BuildRootCommand(env).Parse(args);

        Assert.Empty(parsed.Errors);
        Assert.Null(LayoutGatedVerbs.Refusal(env, parsed));
    }

    private CliEnvironment Env(TextWriter? output = null, TextWriter? error = null, FakeProcessControl? processes = null, Asked? asked = null) =>
        new(output ?? new StringWriter(), error ?? new StringWriter(), new JasonPaths(_root), asked ?? new Asked(), Processes: processes ?? new FakeProcessControl(), BuildVersion: Version, Variables: new Dictionary<string, string>());

    private async Task<(int Exit, string Output, string Error, FakeProcessControl Processes, List<string> Asked, List<string> Before)> RunAsync(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var processes = new FakeProcessControl();
        var asked = new Asked();
        var before = Listing();
        var exit = await CliApp.RunAsync(args, Env(output, error, processes, asked), Ct);
        return (exit, output.ToString(), error.ToString(), processes, asked.Requests, before);
    }

    /// <summary>Every entry under the data directory with its size, so a write of any kind shows.</summary>
    private List<string> Listing() =>
        [.. Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(_root, entry) + (File.Exists(entry) ? " " + new FileInfo(entry).Length : "/"))
            .Order(StringComparer.Ordinal)];

    /// <summary>A runtime door that records every request and answers none.</summary>
    private sealed class Asked : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            throw new HttpRequestException("connection refused");
        }
    }
}
