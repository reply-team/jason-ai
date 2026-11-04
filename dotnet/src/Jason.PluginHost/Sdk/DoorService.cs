using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.call</c>: one Runtime API operation, made by the host through the door the invocation was given. The plugin
/// names the operation and its arguments; the host holds the capability and the address, refuses an operation the door
/// does not open before anything leaves the process, says who is speaking and why, and hands back the answer, or a
/// failure the plugin can catch.
/// </summary>
public sealed class DoorService(HostServices services, InvocationDoor door) : IDisposable
{
    public const string Function = "host.call";

    /// <summary>The runtime did not answer: nothing was listening, or the connection broke before an answer came.</summary>
    public const string RuntimeUnreachable = "runtime_unreachable";

    /// <summary>The answer was larger than the door reads; the call may have acted all the same.</summary>
    public const string ResponseTooLarge = "response_too_large";

    /// <summary>No answer came within the call's bound; the call may have acted.</summary>
    public const string RuntimeTimeout = "runtime_timeout";

    /// <summary>What came back was not the Runtime API's answer or its error.</summary>
    public const string RuntimeAnswerInvalid = "runtime_answer_invalid";

    /// <summary>The members of a request the host sets, and a plugin may not.</summary>
    public static IReadOnlySet<string> HostSet { get; } = new HashSet<string>(StringComparer.Ordinal) { "actor", "reason" };

    /// <summary>How much of a refused operation's name a refusal repeats.</summary>
    private const int MaxNamedOperation = 64;

    private static readonly IReadOnlySet<string> Catalog = OperationFamilies.Catalog.ToHashSet(StringComparer.Ordinal);

    private HttpClient? _client;

    public JsValue Invoke(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);

        var named = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!named.IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the first argument is the operation's name, like 'campaign.list'.");
        }

        var operation = named.AsString();
        if (!Catalog.Contains(operation) || !door.Operations.Contains(operation, StringComparer.Ordinal))
        {
            var shown = operation.Length > MaxNamedOperation ? operation[..MaxNamedOperation] : operation;
            throw new HostRuleException(
                HostOutcomeCodes.CapabilityNotGranted,
                $"This handler's door does not open '{shown}'.",
                new JsonObject { ["capability"] = "api", ["operation"] = shown });
        }

        var body = Arguments(engine, args.Length > 1 ? args[1] : JsValue.Undefined);
        body["actor"] = new JsonObject { ["type"] = "plugin", ["id"] = services.Invocation.Plugin.Id };
        body["reason"] = "journal " + door.LineId;

        var text = body.ToJsonString();
        if (Encoding.UTF8.GetByteCount(text) > PluginProtocol.MaxResultBytes)
        {
            throw new HostRuleException(
                HostOutcomeCodes.ApiLimit,
                string.Create(CultureInfo.InvariantCulture, $"A Runtime API request is at most {PluginProtocol.MaxResultBytes} bytes."),
                new JsonObject { ["limit"] = "api.request_bytes", ["max"] = PluginProtocol.MaxResultBytes });
        }

        services.Budget.Api();
        return Send(engine, operation, text);
    }

    public void Dispose() => _client?.Dispose();

    private static JsonObject Arguments(Engine engine, JsValue argument)
    {
        if (argument.IsUndefined())
        {
            return [];
        }

        if (argument is not ObjectInstance || argument.IsArray() || argument.IsCallable()
            || JsJson.ToJson(engine, argument) is not JsonObject body)
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the second argument is an object of the operation's arguments.");
        }

        foreach (var member in HostSet)
        {
            if (body.ContainsKey(member))
            {
                throw ArgumentReader.TypeError(engine, $"{Function}: '{member}' is the host's to set; a handler speaks as its plugin about its journal line.");
            }
        }

        return body;
    }

    private JsValue Send(Engine engine, string operation, string body)
    {
        var watch = Stopwatch.StartNew();
        var client = _client ??= CreateClient();
        var remaining = services.Remaining();
        var bound = TimeSpan.FromMilliseconds(door.Limits.TimeoutMs);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(door.BaseUrl.TrimEnd('/') + Operations.Route(operation)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", door.Capability);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(services.Deadline);
        deadline.CancelAfter(bound < remaining ? bound : remaining);

        int status;
        var capture = new BoundedCapture(door.Limits.ResponseBytes);
        try
        {
            using var response = client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .GetAwaiter()
                .GetResult();

            status = (int)response.StatusCode;
            using var stream = response.Content.ReadAsStream(deadline.Token);
            capture.DrainAsync(stream, deadline.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (services.Deadline.IsCancellationRequested)
        {
            // The invocation itself is out of time: the runner ends it, and says it may have acted.
            Unanswered(operation, watch);
            throw;
        }
        catch (OperationCanceledException)
        {
            Unanswered(operation, watch);
            throw Failure(
                engine,
                "ambiguous",
                RuntimeTimeout,
                string.Create(CultureInfo.InvariantCulture, $"The runtime did not answer '{operation}' within {(long)(bound < remaining ? bound : remaining).TotalMilliseconds} ms; it may have acted."),
                Details(operation, null),
                retryable: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Unanswered(operation, watch);
            throw Failure(
                engine,
                "transient",
                RuntimeUnreachable,
                $"The runtime could not be reached for '{operation}'.",
                Details(operation, null),
                retryable: true);
        }

        services.Diagnostics.Host(
            "info",
            "api",
            new JsonObject { ["operation"] = operation, ["status"] = status, ["duration_ms"] = watch.ElapsedMilliseconds });

        if (capture.Truncated)
        {
            var details = Details(operation, status);
            details["max"] = door.Limits.ResponseBytes;
            throw Failure(
                engine,
                "permanent",
                ResponseTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"The answer to '{operation}' is larger than the {door.Limits.ResponseBytes} bytes a handler reads; the call may have acted."),
                details,
                retryable: false);
        }

        JsonNode? answer;
        try
        {
            answer = JsonNode.Parse(capture.Text);
        }
        catch (JsonException)
        {
            throw Invalid(engine, operation, status);
        }

        if (status == 200)
        {
            return JsJson.FromJson(engine, answer);
        }

        if (answer is JsonObject envelope
            && envelope["error"] is JsonObject error
            && error["code"] is JsonValue code && code.TryGetValue<string>(out var spelled) && HostFailure.IsCode(spelled)
            && error["message"] is JsonValue said && said.TryGetValue<string>(out var message))
        {
            var retryable = error["retryable"] is JsonValue flag && flag.TryGetValue<bool>(out var value) && value;
            var details = Details(operation, status);
            details["details"] = error["details"]?.DeepClone();
            throw Failure(engine, retryable ? "transient" : "permanent", spelled, message, details, retryable);
        }

        throw Invalid(engine, operation, status);
    }

    private JavaScriptException Invalid(Engine engine, string operation, int status) =>
        Failure(
            engine,
            "permanent",
            RuntimeAnswerInvalid,
            string.Create(CultureInfo.InvariantCulture, $"The answer to '{operation}' (status {status}) is not the Runtime API's."),
            Details(operation, status),
            retryable: false);

    private void Unanswered(string operation, Stopwatch watch) =>
        services.Diagnostics.Host(
            "warn",
            "api_failed",
            new JsonObject { ["operation"] = operation, ["duration_ms"] = watch.ElapsedMilliseconds });

    private static JsonObject Details(string operation, int? status) =>
        new() { ["status"] = status, ["operation"] = operation };

    private static JavaScriptException Failure(
        Engine engine,
        string failureClass,
        string code,
        string message,
        JsonObject details,
        bool retryable)
    {
        var text = message.Length > PluginProtocol.MaxErrorMessageLength ? message[..PluginProtocol.MaxErrorMessageLength] : message;
        var error = HostFailure.Build(engine, failureClass, code, text, details);
        error.FastSetDataProperty("retryable", retryable);
        return new JavaScriptException(error);
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            // Every call carries its own deadline; the client's own timeout would only fight with it.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", HttpService.UserAgent);
        return client;
    }
}
