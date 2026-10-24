using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Notifications.Plugins;

/// <summary>
/// A notification plugin an operator named in the settings, as a channel. Everything that can be decided before a child
/// starts is decided first — is the plugin loaded and usable here, does it speak the delivery contract, does the binding
/// satisfy its own schema — and a channel that cannot reach anybody is recorded with its reason, never skipped in silence.
/// Then it is invoked through the plugin host for <c>notification.send</c>, with the pointer as its input.
/// </summary>
/// <remarks>
/// The binding is read live from the settings and checked against the snapshot in force at each send, so an edit of
/// either applies to the next notification. A binding names an account and never a credential: the plugin reads its
/// secret with <c>host.env</c> from what the operator granted it.
/// <para>
/// The timing and the delivery contract are given only by a test: the runtime always matches under the default timing
/// and always holds a delivery to the contract this build publishes. A test hands in a contract whose schemas carry a
/// pattern, because the published one carries none, and nothing else could show its input and result being measured
/// under the timing.
/// </para>
/// </remarks>
public sealed class PluginNotificationChannel(
    string id,
    NotificationPluginEntry entry,
    PluginRegistry registry,
    IServiceScopeFactory scopes,
    PatternTiming? timing = null,
    DeliveryContract? delivery = null) : INotificationChannel
{
    /// <summary>How a binding, an input and an answer are matched against their patterns: the runtime's one default, which a test replaces.</summary>
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    public string Name => id;

    public string Kind => ChannelKinds.Plugin;

    public async Task<ChannelOutcome> DeliverAsync(NotificationMessage message, string notificationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var contract = delivery
            ?? DeliveryCatalog.Find("notification.send")
            ?? throw new InvalidOperationException("This build publishes no notification.send contract.");

        var snapshot = registry.Snapshot;
        if (snapshot.Find(id) is not { } plugin)
        {
            return NotAttempted(NotAttemptedReasons.Unavailable, "not loaded");
        }

        if (plugin.Status != PluginStatus.Valid)
        {
            return NotAttempted(NotAttemptedReasons.Unavailable, string.Join(", ", plugin.Problems.Select(problem => problem.Code).Distinct(StringComparer.Ordinal)));
        }

        if (plugin.Manifest.Kind != PluginKind.Notification || !plugin.Manifest.Contracts.Operations.Contains(DeliveryContract.CurrentVersion))
        {
            return NotAttempted(
                NotAttemptedReasons.NotDeclared,
                string.Create(CultureInfo.InvariantCulture, $"a {plugin.Manifest.Kind.ToString().ToLowerInvariant()} plugin declaring no version {DeliveryContract.CurrentVersion} of the delivery contract"));
        }

        // A binding that is not an object is refused by name: read as an empty one, it would be checked as if nothing
        // had been written, and its own schema's refusal would name the wrong thing.
        if (entry.Binding is { } given and not JsonObject)
        {
            return NotAttempted(NotAttemptedReasons.BindingInvalid, $"the binding is an object; this one is a {given.GetValueKind().ToString().ToLowerInvariant()}");
        }

        var binding = entry.Binding?.DeepClone() as JsonObject ?? [];
        if (BindingProblem(binding) is { } problem)
        {
            return NotAttempted(NotAttemptedReasons.BindingInvalid, problem);
        }

        // The binding's own schema. A pattern there that did not finish matching, twice, finds nothing wrong with the
        // binding, so it is never `binding_invalid`; nothing has been sent, so the channel fails transient and is tried
        // again like any transient failure.
        if (plugin.Manifest.Binding is { } schema && SchemaValidator.Validate(binding, schema, _timing) is { Count: > 0 } bindingProblems)
        {
            return SchemaValidator.Verdicts(bindingProblems) is { Count: > 0 } wrong
                ? NotAttempted(NotAttemptedReasons.BindingInvalid, Listed(wrong))
                : ChannelOutcome.Failed(id, Kind, AttemptErrors.ValidationTimedOut, FailureClass.Transient, Listed(bindingProblems));
        }

        var input = new JsonObject
        {
            ["title"] = message.Title,
            ["message"] = message.Text,
            ["counts"] = new JsonObject { ["approvals"] = message.Approvals, ["decisions"] = message.Decisions },
        };

        // The runtime's own half of the contract, checked before a child exists.
        if (InputOutcome(id, contract.CheckInput(input, _timing)) is { } refused)
        {
            return refused;
        }

        var request = new PluginInvocationRequest(
            id,
            contract.Id,
            input,
            binding,
            notificationId,
            Timeout: TimeSpan.FromMilliseconds(contract.TimeoutMs),
            Pinned: new PinnedPlugin(plugin, snapshot.Id),
            Purpose: InvocationPurpose.Delivery);

        PluginInvocationResult result;
        await using (var scope = scopes.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<PluginInvoker>().InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return result.Outcome switch
        {
            InvocationOutcome.Succeeded succeeded => ResultOutcome(id, succeeded, contract.CheckResult(succeeded.Result, _timing)),
            // The delivery family declares no block, so a channel that answers blocked has answered outside its contract.
            InvocationOutcome.Failed { Error.Class: FailureClass.Blocked } => ChannelOutcome.Failed(
                id,
                Kind,
                "result_invalid",
                FailureClass.Ambiguous,
                "the answer is blocked, which the delivery contract does not declare"),
            InvocationOutcome.Failed failed => ChannelOutcome.Failed(id, Kind, failed.Error.Code, failed.Error.Class),
            InvocationOutcome.ProtocolFailure protocol => ChannelOutcome.Failed(id, Kind, protocol.Code, OutcomeClassification.ClassOf(protocol.Code)),
            _ => ChannelOutcome.Failed(id, Kind, "result_invalid", FailureClass.Ambiguous),
        };
    }

    /// <summary>
    /// What the runtime's own half of the delivery contract makes of the input it is about to hand a plugin, or null
    /// when it holds. An input it refuses is final; one a pattern could not finish measuring is not refused — nothing
    /// has been sent, so the channel fails transient and is tried again like any transient failure.
    /// </summary>
    private static ChannelOutcome? InputOutcome(string id, IReadOnlyList<SchemaProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (SchemaValidator.Verdicts(problems) is { Count: > 0 } wrong)
        {
            return ChannelOutcome.Failed(id, ChannelKinds.Plugin, "input_invalid", FailureClass.Validation, Listed(wrong));
        }

        return problems.Count > 0
            ? ChannelOutcome.Failed(id, ChannelKinds.Plugin, AttemptErrors.ValidationTimedOut, FailureClass.Transient, Listed(problems))
            : null;
    }

    /// <summary>
    /// What a plugin's answer comes to. The plugin has run whichever way it went, so an answer outside the contract
    /// is a pointer that may have arrived — ambiguous, and never repeated — and an answer a pattern could not finish
    /// measuring is the same, under its own code rather than <c>result_invalid</c>, which would blame the answer: a
    /// pointer delivered twice costs nothing, and one never delivered is what a later notification repairs.
    /// </summary>
    private static ChannelOutcome ResultOutcome(string id, InvocationOutcome.Succeeded succeeded, IReadOnlyList<SchemaProblem> resultProblems)
    {
        ArgumentNullException.ThrowIfNull(succeeded);
        ArgumentNullException.ThrowIfNull(resultProblems);

        IReadOnlyList<SchemaProblem> problems = [.. resultProblems, .. DeliveryContract.CheckExternalIds(succeeded.ExternalIds)];
        if (problems.Count == 0)
        {
            return ChannelOutcome.Delivered(id, ChannelKinds.Plugin, (succeeded.Result as JsonObject)?["receipt"]?.GetValue<string>());
        }

        return SchemaValidator.Unmeasured(problems)
            ? ChannelOutcome.Failed(id, ChannelKinds.Plugin, AttemptErrors.ValidationTimedOut, FailureClass.Ambiguous, "the answer could not be measured against the delivery contract's result")
            : ChannelOutcome.Failed(id, ChannelKinds.Plugin, "result_invalid", FailureClass.Ambiguous, "the answer is outside the delivery contract's result");
    }

    /// <summary>What is wrong with the binding before its schema is read: a credential, or its size.</summary>
    private static string? BindingProblem(JsonObject binding)
    {
        if (FirstSecretLikePointer(binding, string.Empty) is { } secret)
        {
            return $"{secret} is named like a credential; a binding names an account, and a secret is read from the plugin's environment";
        }

        var bytes = Encoding.UTF8.GetByteCount(binding.ToJsonString());
        return bytes > PluginProtocol.MaxBindingBytes
            ? string.Create(CultureInfo.InvariantCulture, $"a binding is at most {PluginProtocol.MaxBindingBytes} bytes; this one is {bytes}")
            : null;
    }

    /// <summary>Problems as a person reading the line is shown them: each place, and the reason it was refused.</summary>
    private static string Listed(IReadOnlyList<SchemaProblem> problems) =>
        string.Join("; ", problems.Select(problem => $"{(problem.Pointer.Length == 0 ? "/" : problem.Pointer)} {problem.Reason}"));

    private static string? FirstSecretLikePointer(JsonNode? node, string pointer)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (name, value) in members)
                {
                    var at = pointer + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                    if (SecretLikeNames.Matches(name))
                    {
                        return at;
                    }

                    if (FirstSecretLikePointer(value, at) is { } nested)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonArray entries:
                for (var index = 0; index < entries.Count; index++)
                {
                    if (FirstSecretLikePointer(entries[index], pointer + "/" + index.ToString(CultureInfo.InvariantCulture)) is { } nested)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private ChannelOutcome NotAttempted(string reason, string detail) => ChannelOutcome.NotAttempted(id, Kind, reason, detail);
}
