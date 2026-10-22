using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Api;

public class OperationEndpointsTests
{
    private const string Operation = "probe.echo";

    /// <summary>The text a request body carries that must never reach a log, in whatever form a failure quotes it.</summary>
    private const string SentinelBody = "SENTINEL_BODY";

    /// <summary>A key the sender typed into a free-form object: the parser's path names it, so the path is not logged.</summary>
    private const string SentinelKey = "SENTINEL_KEY";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// <paramref name="Context"/> is a node the door hands on without reading, the way a work item's context
    /// travels: whatever reads it later reads it inside the handler.
    /// </summary>
    public sealed record ProbeRequest(string? Name, int? Count, JsonNode? Context = null, Stamp? Stamp = null);

    /// <summary>
    /// A field whose converter fails the way the framework's own parsers do: with a <see cref="FormatException"/> that
    /// quotes what it could not read, and not with the <see cref="JsonException"/> the serializer would recognise.
    /// </summary>
    [JsonConverter(typeof(StampConverter))]
    public sealed record Stamp(DateTimeOffset At);

    public sealed class StampConverter : JsonConverter<Stamp>
    {
        public override Stamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(DateTimeOffset.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture));

        public override void Write(Utf8JsonWriter writer, Stamp value, JsonSerializerOptions options) => writer.WriteStringValue(value.At);
    }

    public sealed record ProbeResponse(string Echo, int TotalCount);

    /// <summary>
    /// A value a handler reads out of a nested node, with a converter that says what it could not read by quoting
    /// it — as parsers and converters do, which is why a log is not handed the exception they throw.
    /// </summary>
    [JsonConverter(typeof(CurrencyConverter))]
    public sealed record Currency(string Code);

    public sealed record Priced(Currency Currency);

    public sealed class CurrencyConverter : JsonConverter<Currency>
    {
        public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var code = reader.GetString();
            return code is "USD" or "EUR" ? new Currency(code) : throw new JsonException($"'{code}' is not a currency this runtime prices in.");
        }

        public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options) => writer.WriteStringValue(value.Code);
    }

    private static ProbeResponse Price(ProbeRequest request) =>
        new(request.Context.Deserialize<Priced>(JasonJson.Options)!.Currency.Code, 0);

    private static string ContextWithSentinel => "{\"context\":{\"currency\":\"" + SentinelBody + "\"}}";

    /// <summary>A free-form object: the keys under it are whatever the sender typed, and the parser's path names them.</summary>
    public sealed record Counted(Dictionary<string, int> Fields);

    private static ProbeResponse Count(ProbeRequest request) =>
        new("counted", request.Context.Deserialize<Counted>(JasonJson.Options)!.Fields.Count);

    private static string ContextWithSentinelKey => "{\"context\":{\"fields\":{\"" + SentinelKey + "\":\"many\"}}}";

    [Fact]
    public async Task An_empty_body_binds_to_a_request_whose_fields_are_all_absent()
    {
        await using var api = await ProbeApi.StartAsync(_ => new ProbeResponse("ok", 0), Ct);

        var (status, body) = await api.PostAsync(string.Empty, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"echo\":\"ok\",\"total_count\":0}", body);
        Assert.Equal(new ProbeRequest(null, null), api.LastRequest);
    }

    [Fact]
    [Critical(Family.R2, "200 with a bare DTO")]
    public async Task A_successful_response_is_the_bare_dto_in_the_api_dialect()
    {
        await using var api = await ProbeApi.StartAsync(request => new ProbeResponse(request.Name!, request.Count!.Value), Ct);

        var (status, body) = await api.PostAsync("{\"name\":\"latam\",\"count\":3}", Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"echo\":\"latam\",\"total_count\":3}", body);
    }

    [Fact]
    public async Task A_body_that_is_not_json_for_the_operation_is_an_invalid_request()
    {
        await using var api = await ProbeApi.StartAsync(_ => new ProbeResponse("ok", 0), Ct);

        var error = await api.PostErrorAsync("{not json", HttpStatusCode.BadRequest, Ct);

        Assert.Equal("invalid_request", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    [Critical(Family.R2, "snake_case codes and statuses")]
    public async Task Domain_exceptions_become_their_own_status_and_code()
    {
        await using var notFound = await ProbeApi.StartAsync(_ => throw new NotFoundException("campaign_not_found", "No campaign with id 'cmp_X'."), Ct);
        var missing = await notFound.PostErrorAsync("{}", HttpStatusCode.NotFound, Ct);
        Assert.Equal("campaign_not_found", missing.Code);
        Assert.Null(missing.Details);

        await using var conflict = await ProbeApi.StartAsync(_ => throw new ConflictException("invalid_transition", "A campaign cannot go from draft to paused."), Ct);
        Assert.Equal("invalid_transition", (await conflict.PostErrorAsync("{}", HttpStatusCode.Conflict, Ct)).Code);

        await using var invalid = await ProbeApi.StartAsync(_ => throw new InvalidRequestException("context_too_large", "The campaign context is too large."), Ct);
        Assert.Equal("context_too_large", (await invalid.PostErrorAsync("{}", HttpStatusCode.BadRequest, Ct)).Code);
    }

    [Fact]
    public async Task Validation_failures_carry_one_detail_per_field()
    {
        await using var api = await ProbeApi.StartAsync(_ => throw new ValidationException([new ErrorDetail("name", "required", "name is required.")]), Ct);

        var error = await api.PostErrorAsync("{}", HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("name", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task Losing_a_race_for_a_row_is_a_conflict_the_caller_can_retry()
    {
        await using var api = await ProbeApi.StartAsync(_ => throw new DbUpdateConcurrencyException("another writer got there first"), Ct);

        var error = await api.PostErrorAsync("{}", HttpStatusCode.Conflict, Ct);

        Assert.Equal("concurrent_update", error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain("another writer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.R2, "the 500 envelope")]
    public async Task An_unexpected_failure_is_a_retryable_internal_error_that_says_nothing_about_the_request()
    {
        await using var api = await ProbeApi.StartAsync(_ => throw new InvalidOperationException("a distinctive secret value"), Ct);

        var error = await api.PostErrorAsync("{\"name\":\"a distinctive secret value\"}", HttpStatusCode.InternalServerError, Ct);

        Assert.Equal("internal_error", error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain("distinctive", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A parser's message can quote the input it could not read, and a request body is exactly what the logs promise
    /// never to hold. So a failure that is a parse failure — here one a handler meets reading a node the door passed
    /// on unread — is logged by what kind it was and where the reader stopped, and the exception that carries the
    /// quote is not handed to the log at all, because a sink writes an exception whole.
    /// </summary>
    [Fact]
    public async Task A_handler_that_cannot_read_part_of_a_request_is_logged_by_where_it_stopped_and_not_by_what_the_parser_quoted()
    {
        var logs = new RecordingLogs();
        await using var api = await ProbeApi.StartAsync(Price, Ct, logs);

        var error = await api.PostErrorAsync(ContextWithSentinel, HttpStatusCode.InternalServerError, Ct);

        Assert.Equal("internal_error", error.Code);
        Assert.True(error.Retryable);
        var line = Assert.Single(logs.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Matches(@"^Operation probe\.echo failed: JsonException at line 1, byte \d+$", line.Message);
        Assert.Null(line.Exception);
        Assert.False(logs.Mentions(SentinelBody), "the parse failure's quote of the request reached the log");
    }

    /// <summary>
    /// The path the parser stopped at names every key it went through, and under a free-form object — a work item's
    /// context is one — those keys are text a person typed. Only names the code itself knows may travel into a log,
    /// so the line carries the kind and the position and not the path.
    /// </summary>
    [Fact]
    public async Task A_key_the_sender_typed_is_not_in_the_log_even_though_the_parsers_path_names_it()
    {
        var logs = new RecordingLogs();
        await using var api = await ProbeApi.StartAsync(Count, Ct, logs);

        await api.PostErrorAsync(ContextWithSentinelKey, HttpStatusCode.InternalServerError, Ct);

        var line = Assert.Single(logs.Lines);
        Assert.Matches(@"^Operation probe\.echo failed: JsonException at line 1, byte \d+$", line.Message);
        Assert.Null(line.Exception);
        Assert.False(logs.Mentions(SentinelKey), "a key the sender typed reached the log");
    }

    /// <summary>
    /// The framework wraps what a service throws more often than not, and a wrapper is no reason to write the
    /// parser's quote after all: the failure is found anywhere in the chain.
    /// </summary>
    [Fact]
    public async Task A_parse_failure_inside_another_exception_is_logged_the_same_way()
    {
        var logs = new RecordingLogs();
        await using var api = await ProbeApi.StartAsync(
            request =>
            {
                try
                {
                    return Price(request);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("The context could not be read.", ex);
                }
            },
            Ct,
            logs);

        await api.PostErrorAsync(ContextWithSentinel, HttpStatusCode.InternalServerError, Ct);

        var line = Assert.Single(logs.Lines);
        Assert.Matches(@"^Operation probe\.echo failed: JsonException at line 1, byte \d+$", line.Message);
        Assert.Null(line.Exception);
        Assert.False(logs.Mentions(SentinelBody));
    }

    /// <summary>The rule is about what a parser quotes, not a change in how the runtime reports its own faults.</summary>
    [Fact]
    public async Task A_failure_that_is_not_a_parse_failure_is_logged_with_its_exception()
    {
        var logs = new RecordingLogs();
        await using var api = await ProbeApi.StartAsync(_ => throw new InvalidOperationException("the fault"), Ct, logs);

        await api.PostErrorAsync("{}", HttpStatusCode.InternalServerError, Ct);

        var line = Assert.Single(logs.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("Operation probe.echo failed", line.Message);
        Assert.Equal("the fault", Assert.IsType<InvalidOperationException>(line.Exception).Message);
    }

    /// <summary>
    /// A body the door cannot read is the caller's mistake, answered as one — with the parser's words, to the
    /// caller who wrote the body — and it is not the runtime's failure, so it writes nothing to the log.
    /// </summary>
    [Fact]
    [Critical(Family.B3, "a request body never reaches the log")]
    public async Task A_body_the_door_cannot_read_is_answered_and_leaves_nothing_in_the_log()
    {
        var logs = new RecordingLogs();
        await using var api = await ProbeApi.StartAsync(_ => new ProbeResponse("ok", 0), Ct, logs);

        var error = await api.PostErrorAsync("{\"name\":\"" + SentinelBody + "\"", HttpStatusCode.BadRequest, Ct);

        Assert.Equal("invalid_request", error.Code);
        Assert.Empty(logs.Lines);
    }

    /// <summary>
    /// Not every failure to read a body is a <see cref="JsonException"/>: a converter can throw whatever its parser
    /// throws, quoting the value. Such a failure escaped the door once, and the host wrote it whole twice over — the
    /// request line's exception and the server's own "unhandled exception" — while the caller was answered with an
    /// empty 500. Whatever reading the body throws, the caller is answered as for any body the operation cannot take,
    /// the answer does not repeat the parser's words, and no category writes a line.
    /// </summary>
    [Fact]
    public async Task A_body_whose_reading_fails_with_any_exception_is_answered_as_invalid_and_no_line_of_any_category_carries_it()
    {
        var logs = new RecordingLogs();
        await using var api = await ProbeApi.StartAsync(_ => new ProbeResponse("ok", 0), Ct, logs, everyCategory: true);

        var (status, body) = await api.PostAsync("{\"stamp\":\"" + SentinelBody + "\"}", Ct);

        Assert.False(logs.Mentions(SentinelBody), "a value the door could not read reached the log");
        Assert.DoesNotContain(logs.Lines, line => line.Level >= LogLevel.Warning);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, JasonJson.Options)!.Error;
        Assert.Equal("invalid_request", error.Code);
        Assert.False(error.Retryable);
        Assert.DoesNotContain(SentinelBody, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_modules_are_wired_into_the_running_runtime()
    {
        await using var runtime = await RuntimeApiFixture.StartAsync(Ct);

        var info = await runtime.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal("v1", info.ApiVersion);

        var unknown = await runtime.PostErrorAsync("campaign.no_such_verb", null, HttpStatusCode.NotFound, Ct);
        Assert.Equal("not_found", unknown.Code);
    }

    /// <summary>One operation mapped over a service the test controls, hosted the way the runtime hosts its own.</summary>
    private sealed class ProbeApi : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _http = null!;

        public ProbeRequest? LastRequest { get; private set; }

        /// <param name="logs">
        /// Where the endpoints' own log lines go, when a test reads them. Only the category the endpoints write
        /// under is kept: the host's start-up lines are not what these tests are about.
        /// </param>
        /// <param name="everyCategory">
        /// Keep the lines of every category, the server's own included, for a test about what reaches a log from
        /// anywhere in the host and not only from the endpoints.
        /// </param>
        public static async Task<ProbeApi> StartAsync(Func<ProbeRequest, ProbeResponse> handler, CancellationToken cancellationToken, RecordingLogs? logs = null, bool everyCategory = false)
        {
            var api = new ProbeApi();
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            if (logs is not null && everyCategory)
            {
                builder.Logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
            }
            else if (logs is not null)
            {
                builder.Logging.SetMinimumLevel(LogLevel.None).AddFilter("Jason.Runtime.Api", LogLevel.Trace).AddProvider(logs);
            }

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(api);
            var app = builder.Build();
            app.MapOperation<ProbeApi, ProbeRequest, ProbeResponse>(Operation, (service, request, _) =>
            {
                service.LastRequest = request;
                return Task.FromResult(handler(request));
            });

            await app.StartAsync(cancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            api._app = app;
            api._http = new HttpClient { BaseAddress = new Uri(address) };
            return api;
        }

        public async Task<(HttpStatusCode Status, string Body)> PostAsync(string body, CancellationToken cancellationToken)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(Operations.Route(Operation), content, cancellationToken);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        public async Task<ErrorBody> PostErrorAsync(string body, HttpStatusCode expected, CancellationToken cancellationToken)
        {
            var (status, text) = await PostAsync(body, cancellationToken);
            Assert.Equal(expected, status);
            return JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync();
        }
    }
}
