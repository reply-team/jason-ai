using System.CommandLine;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Cli;
using Jason.Cli.Http;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

/// <summary>
/// A CLI launched inside an attempt says so on every request it sends, reads included, whatever actor it names: the
/// attempt's id goes in the <c>Jason-Attempt</c> header as the environment names it, trimmed and not checked. The
/// verbs that are a person's own word are refused by the runtime for it; the CLI sends them anyway, prints the refusal as
/// it came and exits 1.
/// </summary>
public class InsideARunTests
{
    private const string Attempt = "att_01K7QN0000000000000000000R";

    /// <summary>A Latin small e with an acute accent, written by its code so the source stays ASCII.</summary>
    private const char NonAscii = (char)0xE9;

    private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private static readonly string[][] Rows =
    [
        ["campaign", "list"],
        ["campaign", "create", "--name", "Spring outreach"],
        ["--actor", "human:ada", "approval", "approve", "apr_A"],
        ["--actor", "human:ada", "decision", "answer", "dec_A", "--answer", "Yes, go ahead."],
    ];

    public static TheoryData<string[]> Verbs() => [.. Rows];

    /// <summary>A read, a write and a person's own word each name the attempt, the last whatever person it claims.</summary>
    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task A_verb_run_inside_a_run_names_its_attempt_on_the_request(string[] args)
    {
        using var run = new Run(new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = Attempt });

        var exit = await run.RunAsync(args);

        Assert.Equal(ExitCodes.Success, exit);
        var request = Assert.Single(run.Requests);
        Assert.Equal(new[] { Attempt }, request.Attempt);
    }

    /// <summary>
    /// The value goes as the environment names it, trimmed, and is not checked against any shape: the runtime only reads
    /// that it is there.
    /// </summary>
    [Theory]
    [InlineData("  att_01K7QN0000000000000000000R\t", Attempt)]
    [InlineData("att_nothing-like-an-id", "att_nothing-like-an-id")]
    public async Task The_attempt_is_sent_as_the_environment_names_it_trimmed(string named, string sent)
    {
        using var run = new Run(new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = named });

        await run.RunAsync("campaign", "list");

        Assert.Equal(new[] { sent }, Assert.Single(run.Requests).Attempt);
        Assert.Equal(string.Empty, run.Error.ToString());
    }

    /// <summary>A non-ASCII letter, a line break that would start a second header, and a space.</summary>
    public static TheoryData<string> NotVisibleAscii =>
    [
        "att_01K7QN" + NonAscii + "0000000000000000R",
        "att_01K7QN0000000000000000000R" + (char)13 + (char)10 + "X-Other: 1",
        "att_A att_B",
    ];

    /// <summary>
    /// A value that is not all visible ASCII is not sent: a non-ASCII letter, a line break or a space inside it among
    /// them. The request goes without the header, as it would with the variable unset, and one line on stderr names the
    /// variable and never its value.
    /// </summary>
    [Theory]
    [MemberData(nameof(NotVisibleAscii))]
    public async Task A_value_that_is_not_all_visible_ASCII_is_not_sent_and_stderr_says_so(string named)
    {
        using var run = new Run(new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = named });

        var exit = await run.RunAsync("campaign", "list");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Null(Assert.Single(run.Requests).Attempt);
        var line = Assert.Single(run.Error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("JASON_ATTEMPT_ID", line, StringComparison.Ordinal);
        Assert.Contains("is not all visible ASCII, so it is not sent", line, StringComparison.Ordinal);
        Assert.DoesNotContain("att_", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A verb that makes more than one client — a human view that finds a campaign's folder, the diagnostic bundle — says
    /// it once on the stderr it was given, however many clients were made with it.
    /// </summary>
    [Fact]
    public async Task Two_clients_on_one_stderr_say_it_once()
    {
        var error = new StringWriter();
        var seen = new List<string[]?>();
        var handler = new FakeHandler(request =>
        {
            seen.Add(AttemptOf(request));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        });

        using (var first = new RuntimeClient(Live, "att_" + NonAscii, error, handler))
        using (var second = new RuntimeClient(Live, "att_" + NonAscii, error, handler))
        {
            await first.PostAsync("campaign.list", TestContext.Current.CancellationToken);
            await second.PostAsync("campaign.list", TestContext.Current.CancellationToken);
        }

        Assert.Equal(new string[]?[] { null, null }, seen);
        Assert.Single(error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Outside a run, or where the variable is empty, no request carries the header.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Outside_a_run_no_request_names_an_attempt(string? named)
    {
        var variables = new Dictionary<string, string>();
        if (named is not null)
        {
            variables["JASON_ATTEMPT_ID"] = named;
        }

        using var run = new Run(variables);

        var exit = await run.RunAsync("--actor", "human:ada", "approval", "approve", "apr_A");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Null(Assert.Single(run.Requests).Attempt);
    }

    /// <summary>A verb that asks the runtime more than once names the attempt every time.</summary>
    [Theory]
    [InlineData("runtime", "status")]
    [InlineData("status")]
    public async Task Every_request_a_verb_sends_names_the_attempt(params string[] args)
    {
        using var run = new Run(new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = Attempt });

        await run.RunAsync(args);

        Assert.NotEmpty(run.Requests);
        Assert.All(run.Requests, request => Assert.Equal(new[] { Attempt }, request.Attempt));
    }

    /// <summary>The client names the attempt it was given on every request, and none when it was given none.</summary>
    [Theory]
    [InlineData(Attempt, Attempt)]
    [InlineData(" att_X ", "att_X")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", null)]
    public async Task The_client_names_the_attempt_it_was_given(string? given, string? sent)
    {
        var seen = new List<string[]?>();
        using var client = new RuntimeClient(Live, given, TextWriter.Null, new FakeHandler(request =>
        {
            seen.Add(AttemptOf(request));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }));

        await client.PostAsync("campaign.list", TestContext.Current.CancellationToken);
        await client.PostAsync("campaign.create", "{\"name\":\"x\"}", TestContext.Current.CancellationToken);

        Assert.Equal(2, seen.Count);
        Assert.All(seen, attempt => Assert.Equal(sent is null ? null : new[] { sent }, attempt));
    }

    /// <summary>
    /// Every request names the attempt because every client is made with it: each <c>new RuntimeClient(</c> under
    /// <c>dotnet/src</c> passes the attempt the CLI's environment names, and the stderr that environment writes to. Most
    /// sites are held by no row of their own, and <c>null</c> would compile, so the source is read. The text alone would
    /// miss a construction it does not spell, <c>RuntimeClient client = new(…)</c> say, so the compiled CLI is read too:
    /// every construction it makes stands at a site the text holds, and there are as many of one as of the other.
    /// </summary>
    [Fact]
    public void Every_client_the_source_makes_is_given_the_environments_attempt()
    {
        var (sites, held) = ClientSites();

        // A reading that found no client would pass on nothing, so the sites are counted first.
        Assert.NotEmpty(sites);
        Assert.Equal(sites, held);

        var constructions = CompiledCalls.In(typeof(RuntimeClient).Assembly.Location)
            .Where(call => call.Target is { Namespace: "Jason.Cli.Http", Type: nameof(RuntimeClient), Member: ".ctor" })
            .ToList();
        Assert.All(constructions, call => Assert.True(
            held.Any(site => site.File == call.File && site.Line >= call.StartLine && site.Line <= call.EndLine),
            $"{call.File}:{call.StartLine} makes a RuntimeClient at no site the text holds to the environment's attempt."));
        Assert.Equal(held.Count, constructions.Count);
    }

    /// <summary>
    /// A confined call — the desktop's chat sets <c>JASON_CONFINED</c> on every turn — says so on every request it sends, in
    /// the <c>Jason-Confined</c> header, so the runtime can record that a send was written there; a call with the variable
    /// unset or empty sends no header at all.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task A_confined_call_says_so_on_every_request(string? variable, bool sent)
    {
        var variables = new Dictionary<string, string>();
        if (variable is not null)
        {
            variables["JASON_CONFINED"] = variable;
        }

        using var run = new Run(variables);

        Assert.Equal(ExitCodes.Success, await run.RunAsync("campaign", "list"));
        Assert.Equal(ExitCodes.Success, await run.RunAsync("campaign", "create", "--name", "Spring outreach"));

        Assert.Equal(2, run.Requests.Count);
        Assert.All(run.Requests, request => Assert.Equal(sent ? new[] { "1" } : null, request.Confined));
    }

    /// <summary>
    /// Every client the source makes is told whether the call is confined, from the environment, so no request leaves a
    /// confined call without the header: each <c>new RuntimeClient(</c> under <c>dotnet/src</c> passes <c>env.Confined()</c>.
    /// </summary>
    [Fact]
    public void Every_client_the_source_makes_is_told_whether_the_call_is_confined()
    {
        var (sites, _) = ClientSites();
        var told = ConfinedSites();

        Assert.NotEmpty(sites);
        Assert.Equal(sites.Count, told);
    }

    /// <summary>Each <c>new RuntimeClient(</c> under <c>dotnet/src</c> by file and line, and the ones of them that pass the environment's attempt and stderr.</summary>
    private static (List<(string File, int Line)> Sites, List<(string File, int Line)> Held) ClientSites()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var sources = Directory.EnumerateFiles(Path.Combine(root.FullName, "dotnet", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"));

        var sites = new List<(string File, int Line)>();
        var held = new List<(string File, int Line)>();
        foreach (var path in sources)
        {
            var text = File.ReadAllText(path);
            var name = Path.GetRelativePath(root.FullName, path).Replace('\\', '/');
            (string, int) At(Match match) => (name, text.AsSpan(0, match.Index).Count('\n') + 1);
            sites.AddRange(Regex.Matches(text, @"new\s+(?:[\w.]+\.)?RuntimeClient\s*\(").Select(At));
            held.AddRange(Regex.Matches(text, @"new\s+(?:[\w.]+\.)?RuntimeClient\s*\(\s*[^,()]+,\s*env\.AttemptFromEnvironment\(\),\s*env\.Error,").Select(At));
        }

        return (sites, held);
    }

    /// <summary>How many <c>new RuntimeClient(</c> calls under <c>dotnet/src</c> name <c>confined: env.Confined()</c>.</summary>
    private static int ConfinedSites()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return Directory.EnumerateFiles(Path.Combine(root.FullName, "dotnet", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Sum(path => Regex.Matches(File.ReadAllText(path), @"new\s+(?:[\w.]+\.)?RuntimeClient\s*\([^;]*?confined:\s*env\.Confined\(\)\)").Count);
    }

    /// <summary>The CLI does not decide the refusal: it sends, and prints the runtime's answer as it came, exiting 1.</summary>
    [Fact]
    public async Task A_refusal_from_inside_a_run_is_printed_as_it_came_and_exits_1()
    {
        const string envelope = "{\"error\":{\"code\":\"decided_inside_a_run\",\"message\":\"This request comes from inside attempt `att_01K7QN0000000000000000000R`.\",\"retryable\":false}}";
        using var run = new Run(new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = Attempt }, HttpStatusCode.Forbidden, envelope);

        var exit = await run.RunAsync("--actor", "human:ada", "approval", "reject", "apr_A");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Single(run.Requests);
        Assert.Equal(envelope + Environment.NewLine, run.Output.ToString());
    }

    /// <summary>Exactly the verbs that are a person's own word say in their help, word for word, that a run is refused them.</summary>
    [Fact]
    public void Exactly_the_persons_verbs_say_in_their_help_that_a_run_is_refused_them()
    {
        const string sentence = "From inside a run (the environment names an attempt) this is refused: it is a person's word.";
        using var paths = new TempPaths();
        var saying = new List<string>();
        Walk(CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths)), string.Empty);

        Assert.Equal(
            new[] { "approval approve", "approval reject", "approval set-standing", "approval unset-standing", "campaign set-send-mode", "decision answer", "preference set", "preference unset" },
            saying.Order(StringComparer.Ordinal));

        void Walk(Command command, string path)
        {
            if (command.Subcommands.Count == 0 && command.Description?.Contains(sentence, StringComparison.Ordinal) == true)
            {
                saying.Add(path);
            }

            foreach (var sub in command.Subcommands)
            {
                Walk(sub, $"{path} {sub.Name}".Trim());
            }
        }
    }

    /// <summary>The help of <c>--actor</c> says that a run is named by the header, whatever actor is claimed.</summary>
    [Fact]
    public void The_help_of_actor_names_the_header_beside_the_default()
    {
        var description = ActorOption.Create().Description;

        Assert.Contains("Jason-Attempt", description, StringComparison.Ordinal);
        Assert.Contains("whatever actor is named", description, StringComparison.Ordinal);
    }

    private static string[]? AttemptOf(HttpRequestMessage request) =>
        request.Headers.TryGetValues("Jason-Attempt", out var values) ? [.. values] : null;

    private static string[]? ConfinedOf(HttpRequestMessage request) =>
        request.Headers.TryGetValues("Jason-Confined", out var values) ? [.. values] : null;

    private sealed record Sent(string Operation, string[]? Attempt, string[]? Confined);

    private sealed class Run : IDisposable
    {
        private readonly TempPaths _paths = new();
        private readonly CliEnvironment _environment;

        public Run(IReadOnlyDictionary<string, string> variables, HttpStatusCode status = HttpStatusCode.OK, string response = "{}")
        {
            _paths.WriteDescriptor(Live);
            _environment = new CliEnvironment(
                Output,
                Error,
                _paths.Paths,
                new FakeHandler(request =>
                {
                    Requests.Add(new Sent(request.RequestUri!.AbsolutePath, AttemptOf(request), ConfinedOf(request)));
                    return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
                }),
                Variables: variables);
        }

        public StringWriter Output { get; } = new();

        public StringWriter Error { get; } = new();

        public List<Sent> Requests { get; } = [];

        public Task<int> RunAsync(params string[] args) => CliApp.RunAsync(args, _environment, TestContext.Current.CancellationToken);

        public void Dispose() => _paths.Dispose();
    }
}
