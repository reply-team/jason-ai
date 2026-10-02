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
/// </remarks>
public sealed class PluginNotificationChannel(
    string id,
    NotificationPluginEntry entry,
    PluginRegistry registry,
    IServiceScopeFactory scopes) : INotificationChannel
{
    public string Name => id;

    public string Kind => ChannelKinds.Plugin;

    public async Task<ChannelOutcome> DeliverAsync(NotificationMessage message, string notificationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var contract = DeliveryCatalog.Find("notification.send")
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
        if (BindingProblem(binding, plugin) is { } problem)
        {
            return NotAttempted(NotAttemptedReasons.BindingInvalid, problem);
        }

        var input = new JsonObject
        {
            ["title"] = message.Title,
            ["message"] = message.Text,
            ["counts"] = new JsonObject { ["approvals"] = message.Approvals, ["decisions"] = message.Decisions },
        };

        // The runtime's own half of the contract, checked before a child exists: an input outside it is final.
        if (contract.CheckInput(input) is { Count: > 0 } inputProblems)
        {
            return ChannelOutcome.Failed(
                id,
                Kind,
                "input_invalid",
                FailureClass.Validation,
                string.Join("; ", inputProblems.Select(p => $"{(p.Pointer.Length == 0 ? "/" : p.Pointer)} {p.Reason}")));
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
            InvocationOutcome.Succeeded succeeded
                when contract.CheckResult(succeeded.Result).Count == 0 && DeliveryContract.CheckExternalIds(succeeded.ExternalIds).Count == 0 =>
                ChannelOutcome.Delivered(id, Kind, (succeeded.Result as JsonObject)?["receipt"]?.GetValue<string>()),
            InvocationOutcome.Succeeded => ChannelOutcome.Failed(id, Kind, "result_invalid", FailureClass.Ambiguous, "the answer is outside the delivery contract's result"),
            InvocationOutcome.Failed failed => ChannelOutcome.Failed(id, Kind, failed.Error.Code, failed.Error.Class),
            InvocationOutcome.ProtocolFailure protocol => ChannelOutcome.Failed(id, Kind, protocol.Code, OutcomeClassification.ClassOf(protocol.Code)),
            _ => ChannelOutcome.Failed(id, Kind, "result_invalid", FailureClass.Ambiguous),
        };
    }

    /// <summary>What is wrong with the binding before anything starts: a credential, its size, or its own schema.</summary>
    private static string? BindingProblem(JsonObject binding, LoadedPlugin plugin)
    {
        if (FirstSecretLikePointer(binding, string.Empty) is { } secret)
        {
            return $"{secret} is named like a credential; a binding names an account, and a secret is read from the plugin's environment";
        }

        var bytes = Encoding.UTF8.GetByteCount(binding.ToJsonString());
        if (bytes > PluginProtocol.MaxBindingBytes)
        {
            return string.Create(CultureInfo.InvariantCulture, $"a binding is at most {PluginProtocol.MaxBindingBytes} bytes; this one is {bytes}");
        }

        if (plugin.Manifest.Binding is { } schema && SchemaValidator.Validate(binding, schema) is { Count: > 0 } problems)
        {
            return string.Join("; ", problems.Select(problem => $"{(problem.Pointer.Length == 0 ? "/" : problem.Pointer)} {problem.Reason}"));
        }

        return null;
    }

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
