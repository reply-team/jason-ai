using System.Net;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

/// <summary>
/// Under <c>--human</c> an error is said as a sentence a person reads, with its code in parentheses so it can be quoted,
/// on the stream the envelope goes to; without the flag the envelope and the exit code are what they always were.
/// </summary>
public sealed class HumanErrorTests : IDisposable
{
    private readonly List<TempPaths> _dirs = [];

    private static readonly RuntimeDescriptor Descriptor = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private const string NotFound = "{\"error\":{\"code\":\"campaign_not_found\",\"message\":\"The campaign cmp_X was not found.\",\"retryable\":false}}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_refusal_is_said_with_its_code()
    {
        Assert.Equal("Refused (campaign_not_found): The campaign cmp_X was not found.", CliErrors.Sentence(NotFound));
    }

    [Theory]
    [InlineData(CliErrors.NoDescriptor)]
    [InlineData(CliErrors.RuntimeUnreachable)]
    [InlineData(CliErrors.Unauthorized)]
    [InlineData(CliErrors.StaleDescriptor)]
    public void A_runtime_that_could_not_be_asked_is_said_as_not_reached(string code)
    {
        Assert.Equal($"Not reached ({code}): gone.", CliErrors.Sentence(CliErrors.Serialize(code, "gone.", retryable: true)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"error\":{\"message\":\"no code\"}}")]
    public void Text_that_is_not_an_envelope_is_left_as_it_is(string text)
    {
        Assert.Equal(text, CliErrors.Sentence(text));
    }

    [Fact]
    public async Task A_business_error_under_human_is_a_sentence_and_without_it_the_envelope_byte_for_byte()
    {
        var (human, humanOut, humanErr) = Run(Respond(HttpStatusCode.NotFound, NotFound));
        var (plain, plainOut, plainErr) = Run(Respond(HttpStatusCode.NotFound, NotFound));

        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["campaign", "get", "cmp_X", "--human"], human, Ct));
        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["campaign", "get", "cmp_X"], plain, Ct));

        Assert.Equal("Refused (campaign_not_found): The campaign cmp_X was not found." + Environment.NewLine, humanOut.ToString());
        Assert.Equal(NotFound + Environment.NewLine, plainOut.ToString());
        Assert.Equal(string.Empty, humanErr.ToString());
        Assert.Equal(string.Empty, plainErr.ToString());
    }

    [Fact]
    public async Task An_answer_the_runtime_did_not_compose_is_said_by_the_envelope_that_stands_for_it()
    {
        static HttpResponseMessage Empty(HttpRequestMessage _) => new(HttpStatusCode.InternalServerError) { Content = new StringContent(string.Empty) };
        var (human, humanOut, _) = Run(Empty);
        var (plain, plainOut, _) = Run(Empty);

        Assert.Equal(ExitCodes.ApiError, await OperationRunner.RunAsync(human, "journal.list", RequestBody.Empty(), new RunOptions(Human: true), Ct));
        Assert.Equal(ExitCodes.ApiError, await OperationRunner.RunAsync(plain, "journal.list", RequestBody.Empty(), new RunOptions(Human: false), Ct));

        Assert.Equal("Refused (runtime_error): The runtime answered 500 with a body that is not JSON." + Environment.NewLine, humanOut.ToString());
        Assert.Equal(
            "{\"error\":{\"code\":\"runtime_error\",\"message\":\"The runtime answered 500 with a body that is not JSON.\",\"retryable\":true}}" + Environment.NewLine,
            plainOut.ToString());
    }

    public static TheoryData<string, string> Unreached => new()
    {
        { "none", CliErrors.NoDescriptor },
        { "refused", CliErrors.RuntimeUnreachable },
        { "token", CliErrors.Unauthorized },
    };

    [Theory]
    [MemberData(nameof(Unreached))]
    public async Task A_runtime_that_could_not_be_asked_is_exit_3_said_as_a_sentence_under_human(string how, string code)
    {
        var ((human, humanOut, _), (plain, plainOut, _)) = Unreachable(how);

        Assert.Equal(ExitCodes.RuntimeUnavailable, await CliApp.RunAsync(["campaign", "list", "--human"], human, Ct));
        Assert.Equal(ExitCodes.RuntimeUnavailable, await CliApp.RunAsync(["campaign", "list"], plain, Ct));

        Assert.StartsWith($"Not reached ({code}): ", humanOut.ToString(), StringComparison.Ordinal);
        Assert.Equal(CliErrors.Sentence(plainOut.ToString().TrimEnd()) + Environment.NewLine, humanOut.ToString());
        Assert.StartsWith("{\"error\":{\"code\":\"" + code + "\",", plainOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_the_runtime_took_and_did_not_answer_is_exit_1_said_as_a_sentence_under_human()
    {
        async Task<(int Exit, string Out)> TimeOut(bool human)
        {
            using var dir = new TempPaths();
            dir.WriteDescriptor(Descriptor);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
            var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var output = new StringWriter();
            var env = new CliEnvironment(output, new StringWriter(), dir.Paths, new Waiting(asked), Clock: clock);
            var run = OperationRunner.RunAsync(env, "campaign.list", RequestBody.Empty(), new RunOptions(Human: human), Ct);
            await asked.Task.WaitAsync(Ct);
            clock.Advance(RuntimeClient.Budget("campaign.list") + TimeSpan.FromMinutes(1));
            return (await run.WaitAsync(Ct), output.ToString());
        }

        var human = await TimeOut(human: true);
        var plain = await TimeOut(human: false);

        Assert.Equal(ExitCodes.ApiError, human.Exit);
        Assert.Equal(ExitCodes.ApiError, plain.Exit);
        Assert.StartsWith("{\"error\":{\"code\":\"request_timed_out\",", plain.Out, StringComparison.Ordinal);
        Assert.StartsWith("Refused (request_timed_out): The runtime at http://127.0.0.1:5000 took 'campaign.list'", human.Out, StringComparison.Ordinal);
        Assert.Equal(CliErrors.Sentence(plain.Out.TrimEnd()) + Environment.NewLine, human.Out);
    }

    [Theory]
    [InlineData("none", CliErrors.NoDescriptor)]
    [InlineData("refused", CliErrors.RuntimeUnreachable)]
    [InlineData("token", CliErrors.Unauthorized)]
    public async Task Runtime_status_that_could_not_ask_says_a_sentence_under_human(string how, string code)
    {
        var ((human, humanOut, _), (plain, plainOut, _)) = Unreachable(how);

        Assert.Equal(ExitCodes.RuntimeUnavailable, await RuntimeStatusCommand.RunAsync(human, human: true, Ct));
        Assert.Equal(ExitCodes.RuntimeUnavailable, await RuntimeStatusCommand.RunAsync(plain, human: false, Ct));

        Assert.StartsWith($"Not reached ({code}): ", humanOut.ToString(), StringComparison.Ordinal);
        Assert.Equal(CliErrors.Sentence(plainOut.ToString().TrimEnd()) + Environment.NewLine, humanOut.ToString());
        Assert.StartsWith("{\"error\":{\"code\":\"" + code + "\",", plainOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_status_refused_by_the_runtime_says_a_sentence_under_human()
    {
        var (human, humanOut, _) = Run(Respond(HttpStatusCode.InternalServerError, NotFound));
        var (plain, plainOut, _) = Run(Respond(HttpStatusCode.InternalServerError, NotFound));

        Assert.Equal(ExitCodes.ApiError, await RuntimeStatusCommand.RunAsync(human, human: true, Ct));
        Assert.Equal(ExitCodes.ApiError, await RuntimeStatusCommand.RunAsync(plain, human: false, Ct));

        Assert.Equal("Refused (campaign_not_found): The campaign cmp_X was not found." + Environment.NewLine, humanOut.ToString());
        Assert.Equal(NotFound + Environment.NewLine, plainOut.ToString());
    }

    [Fact]
    public async Task Runtime_status_answered_by_another_instance_says_a_sentence_under_human()
    {
        const string other = "{\"runtime_version\":\"0.1.0-dev\",\"api_version\":\"v1\",\"instance_id\":\"rt_OTHER\",\"pid\":77,\"started_at\":\"1970-01-01T00:00:00+00:00\",\"data_dir\":\"/d\",\"database\":{\"applied_migrations\":[]}}";
        var (human, humanOut, _) = Run(Respond(HttpStatusCode.OK, other));
        var (plain, plainOut, _) = Run(Respond(HttpStatusCode.OK, other));

        Assert.Equal(ExitCodes.RuntimeUnavailable, await RuntimeStatusCommand.RunAsync(human, human: true, Ct));
        Assert.Equal(ExitCodes.RuntimeUnavailable, await RuntimeStatusCommand.RunAsync(plain, human: false, Ct));

        Assert.StartsWith("Not reached (stale_descriptor): The descriptor names instance 'rt_LIVE'", humanOut.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("{\"error\":{\"code\":\"stale_descriptor\",", plainOut.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none", CliErrors.NoDescriptor)]
    [InlineData("refused", CliErrors.RuntimeUnreachable)]
    public async Task Runtime_stop_that_could_not_ask_says_a_sentence_under_human(string how, string code)
    {
        var ((human, humanOut, _), (plain, plainOut, _)) = Unreachable(how);

        Assert.Equal(ExitCodes.RuntimeUnavailable, await RuntimeStopCommand.RunAsync(human, human: true, Ct));
        Assert.Equal(ExitCodes.RuntimeUnavailable, await RuntimeStopCommand.RunAsync(plain, human: false, Ct));

        Assert.StartsWith($"Not reached ({code}): ", humanOut.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("{\"error\":{\"code\":\"" + code + "\",", plainOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_stop_refused_by_the_runtime_says_a_sentence_under_human()
    {
        const string refused = "{\"error\":{\"code\":\"shutdown_refused\",\"message\":\"Not now.\",\"retryable\":false}}";
        var (human, humanOut, _) = Run(Respond(HttpStatusCode.Conflict, refused));
        var (plain, plainOut, _) = Run(Respond(HttpStatusCode.Conflict, refused));

        Assert.Equal(ExitCodes.ApiError, await RuntimeStopCommand.RunAsync(human, human: true, Ct));
        Assert.Equal(ExitCodes.ApiError, await RuntimeStopCommand.RunAsync(plain, human: false, Ct));

        Assert.Equal("Refused (shutdown_refused): Not now." + Environment.NewLine, humanOut.ToString());
        Assert.Equal(refused + Environment.NewLine, plainOut.ToString());
    }

    [Fact]
    public async Task A_journal_watch_without_a_runtime_says_a_sentence_under_human()
    {
        var ((human, humanOut, _), (plain, plainOut, _)) = Unreachable("none");

        Assert.Equal(ExitCodes.RuntimeUnavailable, await CliApp.RunAsync(["journal", "watch", "--human"], human, Ct));
        Assert.Equal(ExitCodes.RuntimeUnavailable, await CliApp.RunAsync(["journal", "watch"], plain, Ct));

        Assert.StartsWith("Not reached (no_descriptor): ", humanOut.ToString(), StringComparison.Ordinal);
        Assert.Equal(CliErrors.Sentence(plainOut.ToString().TrimEnd()) + Environment.NewLine, humanOut.ToString());
        Assert.StartsWith("{\"error\":{\"code\":\"no_descriptor\",", plainOut.ToString(), StringComparison.Ordinal);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            dir.Dispose();
        }
    }

    /// <summary>A run on a data directory of its own, or on <paramref name="shared"/>, so two runs say the same paths.</summary>
    private (CliEnvironment Env, StringWriter Out, StringWriter Error) Run(Func<HttpRequestMessage, HttpResponseMessage> respond, bool descriptor = true, TempPaths? shared = null)
    {
        var dir = shared ?? new TempPaths();
        if (shared is null)
        {
            _dirs.Add(dir);
        }

        if (descriptor)
        {
            dir.WriteDescriptor(Descriptor);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliEnvironment(output, error, dir.Paths, new FakeHandler(respond)), output, error);
    }

    /// <summary>Two runs on one data directory where no runtime can be asked: the first for <c>--human</c>, the second without.</summary>
    private ((CliEnvironment Env, StringWriter Out, StringWriter Error) Human, (CliEnvironment Env, StringWriter Out, StringWriter Error) Plain) Unreachable(string how)
    {
        var dir = new TempPaths();
        _dirs.Add(dir);
        (CliEnvironment, StringWriter, StringWriter) One() => how switch
        {
            "none" => Run(_ => throw new InvalidOperationException("must not be called"), descriptor: false, shared: dir),
            "refused" => Run(_ => throw new HttpRequestException("connection refused"), shared: dir),
            _ => Run(Respond(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}"), shared: dir),
        };

        return (One(), One());
    }

    private sealed class Waiting(TaskCompletionSource asked) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            asked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
