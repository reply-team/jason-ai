using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Scripting;
using Jason.PluginHost.Sdk;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Modules;

namespace Jason.PluginHost;

/// <summary>
/// One engine, one call into the plugin, one answer. Every way an invocation can end — a result, a declared
/// failure, a thrown error, a spent limit, a refused capability — leaves through here as an outcome, so the
/// protocol's promise (exactly one outcome, always) has a single place to keep.
/// </summary>
public sealed class InvocationRunner : IInvocationRunner
{
    private const string ResultProperty = "result";
    private const string ExternalIdsProperty = "external_ids";
    private const string DeadlineFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Never zero: a call made in the last instant of the budget still needs a timeout it can use.</summary>
    private static readonly TimeSpan MinimumRemaining = TimeSpan.FromMilliseconds(1);

    public PluginOutcome Run(PluginInvocation invocation, HostDiagnostics diagnostics, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var limits = invocation.Limits;
        var watch = Stopwatch.StartNew();
        var budget = new CallBudget(limits.Exec.MaxCalls, limits.Http.MaxCalls);
        var engine = EngineFactory.Create(limits, new PackageModuleLoader(invocation.Plugin.Root), deadline);
        var services = new HostServices(
            invocation,
            diagnostics,
            budget,
            () => Remaining(limits, watch),
            Directory.GetCurrentDirectory(),
            deadline);

        using var http = new HttpService(services);
        engine.SetValue(
            HostObject.Name,
            HostObject.Build(engine, services, new ExecService(services), http, new EnvService(services), new LogService(services)));

        var end = new Ending(invocation, diagnostics, watch, budget);
        try
        {
            var module = engine.Modules.Import("./" + invocation.Plugin.Entry.Module.Replace('\\', '/'));
            var entry = module.Get(invocation.Plugin.Entry.Function);
            if (!entry.IsCallable())
            {
                entry = module.Get("default");
            }

            if (!entry.IsCallable())
            {
                return end.Failed(
                    FailureClass.Permanent,
                    OutcomeCodes.EntryFunctionMissing,
                    $"The package does not export a function named '{invocation.Plugin.Entry.Function}'.");
            }

            var input = JsJson.FromJson(engine, invocation.Input);
            var context = JsJson.FromJson(engine, ContextObject(invocation));
            var returned = engine
                .Invoke(entry, JsValue.Undefined, [invocation.Operation, input, context])
                .UnwrapIfPromise(deadline);

            return Answer(engine, end, returned);
        }
        catch (JavaScriptException ex) when (HostFailure.TryRead(engine, ex.Error, out var declared))
        {
            return end.Failed(declared);
        }
        catch (PromiseRejectedException ex) when (HostFailure.TryRead(engine, ex.RejectedValue, out var declared))
        {
            return end.Failed(declared);
        }
        catch (JavaScriptException ex) when (IsSyntaxError(ex))
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.PluginSyntaxError, ex.Message);
        }
        catch (JavaScriptException ex)
        {
            return end.Failed(
                FailureClass.Permanent,
                OutcomeCodes.PluginException,
                ex.Message,
                new JsonObject { ["stack"] = ex.JavaScriptStackTrace ?? string.Empty });
        }
        catch (PromiseRejectedException ex)
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.PluginException, Describe(engine, ex.RejectedValue));
        }
        catch (HostRuleException ex)
        {
            return end.Failed(FailureClass.Permanent, ex.Code, ex.Message, ex.Details);
        }
        catch (ModuleResolutionException ex)
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.ModuleNotAllowed, ex.Message);
        }
        catch (ScriptPreparationException ex)
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.PluginSyntaxError, ex.Message);
        }
        catch (MemoryLimitExceededException ex)
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.PluginMemoryExceeded, ex.Message);
        }
        catch (StatementsCountOverflowException ex)
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.PluginStatementLimit, ex.Message);
        }
        catch (RecursionDepthOverflowException ex)
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.PluginRecursionLimit, ex.Message);
        }
        catch (Exception ex) when (ex is TimeoutException or ExecutionCanceledException or OperationCanceledException)
        {
            // Decision 12's rule: if anything left this process, the provider may already have acted, and only
            // the host knows whether it did. The plugin never has to reason about it.
            return end.Failed(
                budget.ExternalCallsStarted > 0 ? FailureClass.Ambiguous : FailureClass.Permanent,
                OutcomeCodes.PluginTimeout,
                string.Create(CultureInfo.InvariantCulture, $"The plugin did not finish within {limits.TimeoutMs} ms."));
        }
    }

    private static PluginOutcome Answer(Engine engine, Ending end, JsValue returned)
    {
        const string Contract = "invoke must return { result, external_ids? }";

        if (returned is not ObjectInstance answer || returned.IsArray() || returned.IsCallable() || !answer.HasOwnProperty(ResultProperty))
        {
            return end.Failed(FailureClass.Permanent, OutcomeCodes.BadReturn, Contract + ".");
        }

        var result = JsJson.ToJson(engine, answer.Get(ResultProperty));
        if (result is not null && Encoding.UTF8.GetByteCount(result.ToJsonString()) > PluginProtocol.MaxResultBytes)
        {
            return end.Failed(
                FailureClass.Permanent,
                OutcomeCodes.ResultTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"A result is at most {PluginProtocol.MaxResultBytes} bytes."));
        }

        if (!HostFailure.TryReadExternalIds(JsJson.ToJson(engine, answer.Get(ExternalIdsProperty)), out var externalIds))
        {
            return end.Failed(
                FailureClass.Permanent,
                OutcomeCodes.BadReturn,
                Contract + ", where external_ids is an object of short strings.");
        }

        return end.Succeeded(result, externalIds);
    }

    /// <summary>
    /// What the plugin is allowed to know about why it is running: identifiers, the caller's opaque binding and
    /// how long it has. Never a secret, never anything about the machine.
    /// </summary>
    private static JsonObject ContextObject(PluginInvocation invocation)
    {
        var context = invocation.Context;
        return new JsonObject
        {
            ["invocation_id"] = invocation.InvocationId,
            ["correlation_id"] = invocation.CorrelationId,
            ["attempt_id"] = context.AttemptId,
            ["attempt_number"] = context.AttemptNumber,
            ["work_item_id"] = context.WorkItemId,
            ["campaign_id"] = context.CampaignId,
            ["binding"] = context.Binding?.DeepClone(),
            ["timeout_ms"] = invocation.Limits.TimeoutMs,
            ["deadline"] = DateTimeOffset.UtcNow
                .AddMilliseconds(invocation.Limits.TimeoutMs)
                .UtcDateTime
                .ToString(DeadlineFormat, CultureInfo.InvariantCulture),
            ["plugin"] = new JsonObject
            {
                ["id"] = invocation.Plugin.Id,
                ["version"] = invocation.Plugin.Version,
            },
            ["runtime_version"] = context.RuntimeVersion,
        };
    }

    private static TimeSpan Remaining(InvocationLimits limits, Stopwatch watch)
    {
        var left = TimeSpan.FromMilliseconds(limits.TimeoutMs) - watch.Elapsed;
        return left < MinimumRemaining ? MinimumRemaining : left;
    }

    private static bool IsSyntaxError(JavaScriptException exception) =>
        exception.Error is ObjectInstance error
        && error.Get("name") is JsValue name
        && name.IsString()
        && string.Equals(name.AsString(), "SyntaxError", StringComparison.Ordinal);

    private static string Describe(Engine engine, JsValue rejected)
    {
        var described = JsJson.ToJson(engine, rejected)?.ToJsonString();
        return described ?? rejected.ToString();
    }

    /// <summary>Everything an outcome needs whichever way the invocation ended, in one place.</summary>
    private sealed class Ending(PluginInvocation invocation, HostDiagnostics diagnostics, Stopwatch watch, CallBudget budget)
    {
        public PluginOutcome Succeeded(JsonNode? result, JsonObject? externalIds) =>
            new(PluginProtocol.CurrentVersion, invocation.InvocationId, OutcomeStatus.Succeeded, result, externalIds, null, Cost());

        public PluginOutcome Failed(FailureClass failureClass, string code, string message, JsonNode? details = null) =>
            Failed(new OutcomeError(failureClass, code, message, details, null));

        public PluginOutcome Failed(OutcomeError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            var message = diagnostics.Redact(error.Message);
            return new PluginOutcome(
                PluginProtocol.CurrentVersion,
                invocation.InvocationId,
                OutcomeStatus.Failed,
                null,
                null,
                error with
                {
                    Message = message.Length > PluginProtocol.MaxErrorMessageLength
                        ? message[..PluginProtocol.MaxErrorMessageLength]
                        : message,
                    Details = Redact(error.Details),
                },
                Cost());
        }

        private OutcomeDiagnostics Cost() =>
            new(watch.ElapsedMilliseconds, budget.ExecCalls, budget.HttpCalls, diagnostics.LogLines);

        /// <summary>
        /// The details are the plugin's own words and may quote a value the host knows to be a secret; the
        /// runtime stores this text, so it is masked here rather than at every place that writes it down.
        /// </summary>
        private JsonNode? Redact(JsonNode? details)
        {
            if (details is null)
            {
                return null;
            }

            var redacted = diagnostics.Redact(details.ToJsonString());
            try
            {
                return JsonNode.Parse(redacted);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
