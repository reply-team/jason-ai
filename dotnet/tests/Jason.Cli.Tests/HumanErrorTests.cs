using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Cli.Http;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;
using static Jason.Cli.Tests.HumanErrorPins;

namespace Jason.Cli.Tests;

/// <summary>
/// Under <c>--human</c> an error is said as a sentence a person reads, with its code in parentheses so it can be quoted,
/// on the stream the envelope goes to; without the flag the envelope and the exit code are what they always were. These
/// rows hold the paths every business verb shares, <c>runtime status</c>, <c>runtime stop</c> and <c>journal watch</c>.
/// </summary>
public sealed class HumanErrorTests : IDisposable
{
    private const string NotFound = "{\"error\":{\"code\":\"campaign_not_found\",\"message\":\"The campaign cmp_X was not found.\",\"retryable\":false}}";

    private const string Token = "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}";

    private const string Unreachable = "{\"error\":{\"code\":\"runtime_unreachable\",\"message\":\"The runtime at http://127.0.0.1:5000 did not answer (connection refused). The descriptor may be stale.\",\"retryable\":true}}";

    private const string UnreachableSaid = "Not reached (runtime_unreachable): The runtime at http://127.0.0.1:5000 did not answer (connection refused). The descriptor may be stale. It can be tried again.";

    private const string Unauthorized = "{\"error\":{\"code\":\"unauthorized\",\"message\":\"The runtime rejected the capability token from the descriptor; the descriptor is stale.\",\"retryable\":true}}";

    private const string UnauthorizedSaid = "Not reached (unauthorized): The runtime rejected the capability token from the descriptor; the descriptor is stale. It can be tried again.";

    private static readonly RuntimeDescriptor Descriptor = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private readonly TempPaths _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _dir.Dispose();

    private string NoDescriptor =>
        "{\"error\":{\"code\":\"no_descriptor\",\"message\":\"No runtime endpoint descriptor at \\u0027" + Json(_dir.Paths.DescriptorFile)
        + "\\u0027. Is the runtime running?\",\"retryable\":true}}";

    private string NoDescriptorSaid =>
        "Not reached (no_descriptor): No runtime endpoint descriptor at '" + _dir.Paths.DescriptorFile + "'. Is the runtime running? It can be tried again.";

    /// <summary>A path as the envelope's JSON spells it.</summary>
    private static string Json(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    // ---- without --human, nothing changed ----

    /// <summary>
    /// Without <c>--human</c>, the error on stdout is the JSON envelope as it was before the flag had sentences, byte for
    /// byte, with its exit code, on each failure road: a business verb's refusal, the same with details (a refused plugin
    /// reload), a business verb, <c>runtime status</c>, <c>runtime stop</c> and <c>journal watch</c> with no runtime. The
    /// rows of this class and of <see cref="HumanErrorVerbTests"/> hold every road both ways; this one stands for them in
    /// the critical tier.
    /// </summary>
    [Fact]
    [Critical(Family.R2, "without --human an error is the JSON envelope, byte for byte")]
    public async Task Without_human_every_failure_road_prints_the_envelope_byte_for_byte()
    {
        const string Rejected =
            "{\"error\":{\"code\":\"plugin_reload_rejected\",\"message\":\"2 problems in 1 package.\",\"retryable\":false,"
            + "\"details\":[{\"field\":\"broken/plugin.yaml#3:1\",\"code\":\"yaml_invalid\",\"message\":\"mapping values are not allowed here\"}]}}";
        var roads = new List<(string Road, Func<Task<(int Exit, string Out)>> Run, int Exit, string Envelope)>
        {
            ("campaign list, no runtime", () => Cli(Never, false, "campaign", "list"), ExitCodes.RuntimeUnavailable, NoDescriptor),
            ("runtime status, no runtime", () => Status(Never, false), ExitCodes.RuntimeUnavailable, NoDescriptor),
            ("runtime stop, no runtime", () => Stop(Never, false), ExitCodes.RuntimeUnavailable, NoDescriptor),
            ("journal watch, no runtime", () => Cli(Never, false, "journal", "watch"), ExitCodes.RuntimeUnavailable, NoDescriptor),
        };
        foreach (var (road, run, exit, envelope) in roads)
        {
            var (code, printed) = await run();
            Assert.Equal((road, exit), (road, code));
            Assert.Equal((road, envelope + Environment.NewLine), (road, printed));
        }

        _dir.WriteDescriptor(Descriptor);
        foreach (var (road, run, envelope) in new (string, Func<Task<(int Exit, string Out)>>, string)[]
        {
            ("campaign get, refused", () => Cli(Respond(HttpStatusCode.NotFound, NotFound), false, "campaign", "get", "cmp_X"), NotFound),
            ("plugin reload, refused with details", () => Cli(Respond(HttpStatusCode.Conflict, Rejected), false, "plugin", "reload"), Rejected),
        })
        {
            var (code, printed) = await run();
            Assert.Equal((road, ExitCodes.ApiError), (road, code));
            Assert.Equal((road, envelope + Environment.NewLine), (road, printed));
        }
    }

    // ---- the sentence ----

    [Fact]
    public void A_refusal_is_said_with_its_code()
    {
        Assert.Equal("Refused (campaign_not_found): The campaign cmp_X was not found.", CliErrors.Sentence(NotFound, ExitCodes.ApiError));
    }

    /// <summary>The label is the exit code's, never the code's: the same code is "Not reached" at 3 and "Refused" at 1.</summary>
    [Fact]
    public void The_label_follows_the_exit_code()
    {
        var envelope = CliErrors.Serialize(CliErrors.RuntimeUnreachable, "gone.", retryable: false);

        Assert.Equal("Not reached (runtime_unreachable): gone.", CliErrors.Sentence(envelope, ExitCodes.RuntimeUnavailable));
        Assert.Equal("Refused (runtime_unreachable): gone.", CliErrors.Sentence(envelope, ExitCodes.ApiError));
    }

    [Fact]
    public void A_refusal_worth_repeating_says_it_can_be_tried_again()
    {
        Assert.Equal("Refused (busy): The runtime is busy. It can be tried again.", CliErrors.Sentence(CliErrors.Serialize("busy", "The runtime is busy.", retryable: true), ExitCodes.ApiError));
        Assert.Equal("Refused (busy): The runtime is busy.", CliErrors.Sentence(CliErrors.Serialize("busy", "The runtime is busy.", retryable: false), ExitCodes.ApiError));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"error\":{\"message\":\"no code\"}}")]
    public void Text_that_is_not_an_envelope_is_left_as_it_is(string text)
    {
        Assert.Equal(text, CliErrors.Sentence(text, ExitCodes.ApiError));
    }

    /// <summary>Every detail the envelope carries follows the sentence on a line of its own, six of six.</summary>
    [Fact]
    public async Task A_refusal_with_details_says_every_one_of_them()
    {
        const string invalid =
            "{\"error\":{\"code\":\"validation_failed\",\"message\":\"The request is not valid.\",\"retryable\":false,\"details\":["
            + "{\"field\":\"/name\",\"code\":\"required\",\"message\":\"A name is required.\"},"
            + "{\"field\":\"/context/a\",\"code\":\"type\",\"message\":\"Must be a string.\"},"
            + "{\"field\":\"/context/b\",\"code\":\"type\",\"message\":\"Must be a number.\"},"
            + "{\"field\":\"/context/c\",\"code\":\"enum\",\"message\":\"Must be one of x, y.\"},"
            + "{\"field\":\"/context/d\",\"code\":\"max_length\",\"message\":\"At most 10 characters.\"},"
            + "{\"field\":\"/context/e\",\"code\":\"min_items\",\"message\":\"At least one item.\"}]}}";
        _dir.WriteDescriptor(Descriptor);
        var nl = Environment.NewLine;

        await BothAsync(
            human => Cli(Respond(HttpStatusCode.BadRequest, invalid), human, "campaign", "get", "cmp_X"),
            ExitCodes.ApiError,
            invalid,
            "Refused (validation_failed): The request is not valid." + nl
            + "  /name: required (A name is required.)" + nl
            + "  /context/a: type (Must be a string.)" + nl
            + "  /context/b: type (Must be a number.)" + nl
            + "  /context/c: enum (Must be one of x, y.)" + nl
            + "  /context/d: max_length (At most 10 characters.)" + nl
            + "  /context/e: min_items (At least one item.)");
    }

    // ---- every business verb: OperationRunner ----

    [Fact]
    public async Task A_business_error()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            human => Cli(Respond(HttpStatusCode.NotFound, NotFound), human, "campaign", "get", "cmp_X"),
            ExitCodes.ApiError,
            NotFound,
            "Refused (campaign_not_found): The campaign cmp_X was not found.");
    }

    [Fact]
    public async Task An_answer_the_runtime_did_not_compose()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            human => Cli(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(string.Empty) }, human, "journal", "list"),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"runtime_error\",\"message\":\"The runtime answered 500 with a body that is not JSON.\",\"retryable\":true}}",
            "Refused (runtime_error): The runtime answered 500 with a body that is not JSON. It can be tried again.");
    }

    [Fact]
    public async Task A_business_verb_with_no_runtime()
    {
        await BothAsync(human => Cli(Never, human, "campaign", "list"), ExitCodes.RuntimeUnavailable, NoDescriptor, NoDescriptorSaid);
    }

    [Fact]
    public async Task A_business_verb_whose_runtime_refuses_the_connection()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Cli(Refused, human, "campaign", "list"), ExitCodes.RuntimeUnavailable, Unreachable, UnreachableSaid);
    }

    [Fact]
    public async Task A_business_verb_whose_token_is_rejected()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Cli(Respond(HttpStatusCode.Unauthorized, Token), human, "campaign", "list"), ExitCodes.RuntimeUnavailable, Unauthorized, UnauthorizedSaid);
    }

    [Fact]
    public async Task A_request_the_runtime_took_and_did_not_answer()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            async human =>
            {
                var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
                var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var output = new StringWriter();
                var env = new CliEnvironment(output, new StringWriter(), _dir.Paths, new Waiting(asked), Clock: clock);
                var run = CliApp.RunAsync(human ? ["campaign", "list", "--human"] : ["campaign", "list"], env, Ct);
                await asked.Task.WaitAsync(Ct);
                clock.Advance(RuntimeClient.Budget("campaign.list") + TimeSpan.FromMinutes(1));
                return (await run.WaitAsync(Ct), output.ToString());
            },
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"request_timed_out\",\"message\":\"The runtime at http://127.0.0.1:5000 took \\u0027campaign.list\\u0027 and did not answer within 30 seconds, its budget and a margin; it may still be working. Asking again starts the work over.\",\"retryable\":false}}",
            "Refused (request_timed_out): The runtime at http://127.0.0.1:5000 took 'campaign.list' and did not answer within 30 seconds, its budget and a margin; it may still be working. Asking again starts the work over.");
    }

    // ---- runtime status ----

    [Fact]
    public async Task Runtime_status_with_no_runtime()
    {
        await BothAsync(human => Status(Never, human), ExitCodes.RuntimeUnavailable, NoDescriptor, NoDescriptorSaid);
    }

    [Fact]
    public async Task Runtime_status_whose_runtime_refuses_the_connection()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Status(Refused, human), ExitCodes.RuntimeUnavailable, Unreachable, UnreachableSaid);
    }

    [Fact]
    public async Task Runtime_status_whose_token_is_rejected()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Status(Respond(HttpStatusCode.Unauthorized, Token), human), ExitCodes.RuntimeUnavailable, Unauthorized, UnauthorizedSaid);
    }

    [Fact]
    public async Task Runtime_status_refused_by_the_runtime()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Status(Respond(HttpStatusCode.InternalServerError, NotFound), human), ExitCodes.ApiError, NotFound, "Refused (campaign_not_found): The campaign cmp_X was not found.");
    }

    [Fact]
    public async Task Runtime_status_answered_by_another_instance()
    {
        const string other = "{\"runtime_version\":\"0.1.0-dev\",\"api_version\":\"v1\",\"instance_id\":\"rt_OTHER\",\"pid\":77,\"started_at\":\"1970-01-01T00:00:00+00:00\",\"data_dir\":\"/d\",\"database\":{\"applied_migrations\":[]}}";
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            human => Status(Respond(HttpStatusCode.OK, other), human),
            ExitCodes.RuntimeUnavailable,
            "{\"error\":{\"code\":\"stale_descriptor\",\"message\":\"The descriptor names instance \\u0027rt_LIVE\\u0027 but the runtime answering at http://127.0.0.1:5000 is \\u0027rt_OTHER\\u0027.\",\"retryable\":true}}",
            "Not reached (stale_descriptor): The descriptor names instance 'rt_LIVE' but the runtime answering at http://127.0.0.1:5000 is 'rt_OTHER'. It can be tried again.");
    }

    // ---- runtime stop ----

    [Fact]
    public async Task Runtime_stop_with_no_runtime()
    {
        await BothAsync(human => Stop(Never, human), ExitCodes.RuntimeUnavailable, NoDescriptor, NoDescriptorSaid);
    }

    [Fact]
    public async Task Runtime_stop_whose_runtime_refuses_the_connection()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Stop(Refused, human), ExitCodes.RuntimeUnavailable, Unreachable, UnreachableSaid);
    }

    [Fact]
    public async Task Runtime_stop_refused_by_the_runtime()
    {
        const string refused = "{\"error\":{\"code\":\"shutdown_refused\",\"message\":\"Not now.\",\"retryable\":false}}";
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Stop(Respond(HttpStatusCode.Conflict, refused), human), ExitCodes.ApiError, refused, "Refused (shutdown_refused): Not now.");
    }

    [Fact]
    public async Task Runtime_stop_whose_runtime_outlives_its_acknowledgement()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            async human =>
            {
                var processes = new Process.FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
                var (env, output, _) = RuntimeVerbs.Environment(_dir, new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"))), processes);
                return (await RuntimeStopCommand.RunAsync(env, human, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll), output.ToString());
            },
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"shutdown_timeout\",\"message\":\"The runtime acknowledged the shutdown but had not exited after 0.3 s.\",\"retryable\":true}}",
            "Refused (shutdown_timeout): The runtime acknowledged the shutdown but had not exited after 0.3 s. It can be tried again.");
    }

    // ---- journal watch ----

    [Fact]
    public async Task A_journal_watch_with_no_runtime()
    {
        await BothAsync(human => Cli(Never, human, "journal", "watch"), ExitCodes.RuntimeUnavailable, NoDescriptor, NoDescriptorSaid);
    }

    [Fact]
    public async Task A_journal_watch_whose_runtime_refuses_the_connection()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            human => Cli(Refused, human, "journal", "watch"),
            ExitCodes.RuntimeUnavailable,
            "{\"error\":{\"code\":\"runtime_unreachable\",\"message\":\"The runtime at http://127.0.0.1:5000 did not open the journal\\u0027s stream (connection refused). The descriptor may be stale.\",\"retryable\":true}}",
            "Not reached (runtime_unreachable): The runtime at http://127.0.0.1:5000 did not open the journal's stream (connection refused). The descriptor may be stale. It can be tried again.");
    }

    [Fact]
    public async Task A_journal_watch_whose_token_is_rejected()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Cli(Respond(HttpStatusCode.Unauthorized, Token), human, "journal", "watch"), ExitCodes.RuntimeUnavailable, Unauthorized, UnauthorizedSaid);
    }

    [Fact]
    public async Task A_journal_watch_refused_by_the_runtime()
    {
        const string refused = "{\"error\":{\"code\":\"invalid_cursor\",\"message\":\"The cursor is not one this journal gave.\",\"retryable\":false}}";
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(human => Cli(Respond(HttpStatusCode.BadRequest, refused), human, "journal", "watch"), ExitCodes.ApiError, refused, "Refused (invalid_cursor): The cursor is not one this journal gave.");
    }

    [Fact]
    public async Task A_journal_watch_whose_stream_the_runtime_ends()
    {
        _dir.WriteDescriptor(Descriptor);

        await BothAsync(
            human => Cli(request => Closing(request, 1008, "the token was revoked"), human, "journal", "watch"),
            ExitCodes.RuntimeUnavailable,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"The runtime ended the journal\\u0027s stream: the token was revoked\",\"retryable\":true}}",
            "Not reached (unauthorized): The runtime ended the journal's stream: the token was revoked It can be tried again.");
    }

    // ---- the machine ----

    private static readonly Func<HttpRequestMessage, HttpResponseMessage> Never = _ => throw new InvalidOperationException("must not be called");

    private static readonly Func<HttpRequestMessage, HttpResponseMessage> Refused = _ => throw new HttpRequestException("connection refused");

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private CliEnvironment Env(Func<HttpRequestMessage, HttpResponseMessage> respond, StringWriter output) =>
        new(output, new StringWriter(), _dir.Paths, new FakeHandler(respond));

    private async Task<(int Exit, string Out)> Cli(Func<HttpRequestMessage, HttpResponseMessage> respond, bool human, params string[] words)
    {
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(human ? [.. words, "--human"] : words, Env(respond, output), Ct);
        return (exit, output.ToString());
    }

    private async Task<(int Exit, string Out)> Status(Func<HttpRequestMessage, HttpResponseMessage> respond, bool human)
    {
        var output = new StringWriter();
        return (await RuntimeStatusCommand.RunAsync(Env(respond, output), human, Ct), output.ToString());
    }

    private async Task<(int Exit, string Out)> Stop(Func<HttpRequestMessage, HttpResponseMessage> respond, bool human)
    {
        var output = new StringWriter();
        return (await RuntimeStopCommand.RunAsync(Env(respond, output), human, Ct), output.ToString());
    }

    /// <summary>The upgrade answered as the runtime answers it, and the stream then closed by the runtime with <paramref name="status"/>.</summary>
    private static HttpResponseMessage Closing(HttpRequestMessage request, int status, string reason)
    {
        var key = request.Headers.GetValues("Sec-WebSocket-Key").Single();
        var reasonBytes = Encoding.UTF8.GetBytes(reason);
        var frame = new List<byte> { 0x88, (byte)(2 + reasonBytes.Length), (byte)(status >> 8), (byte)(status & 0xFF) };
        frame.AddRange(reasonBytes);
        var response = new HttpResponseMessage(HttpStatusCode.SwitchingProtocols) { Content = new Upgraded([.. frame]) };
        response.Headers.TryAddWithoutValidation("Sec-WebSocket-Accept", Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))));
        response.Headers.TryAddWithoutValidation("Sec-WebSocket-Protocol", request.Headers.GetValues("Sec-WebSocket-Protocol").Single());
        return response;
    }

    /// <summary>The connection after the upgrade: what the runtime sent, read once, and whatever the CLI writes back, dropped.</summary>
    private sealed class Upgraded(byte[] sent) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(sent).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = sent.Length;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new Duplex(sent));
    }

    private sealed class Duplex(byte[] sent) : MemoryStream(sent, writable: false)
    {
        public override bool CanWrite => true;

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.CompletedTask;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
