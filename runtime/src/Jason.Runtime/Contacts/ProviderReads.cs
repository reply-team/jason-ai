using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>How a read on request reaches a plugin: the plugin host through <see cref="PluginInvoker"/>, and a test's own answers in a test.</summary>
public interface IProviderReadInvoker
{
    Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken);
}

public sealed class PluginProviderReadInvoker(PluginInvoker invoker) : IProviderReadInvoker
{
    public Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken) =>
        invoker.InvokeAsync(request, cancellationToken);
}

/// <summary>
/// A person's conversations, read from the provider when somebody asks and answered in the request. It is information,
/// not work: no work item, no attempt, no approval, no retry, nothing journaled and no identifier pinned from what comes
/// back. The person's provider identifier says which plugin to ask, and the account it was learned in says which route:
/// a current route to that plugin whose binding has the identity the learning attempt recorded, so one account's
/// identifier is never sent to another.
/// </summary>
public sealed class ContactThreadService(JasonDbContext db, PluginRegistry plugins, RouteRegistry routes, IProviderReadInvoker invoker)
{
    private const string ContactPin = "contact";

    private const string ListOperation = "conversation.list";

    private const string GetOperation = "conversation.get";

    private const int MaxThreadIdLength = 256;

    public async Task<ContactThreadsResponse> ThreadsAsync(ContactThreadsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (limit, offset) = Page(request.Limit, request.Offset, maxLimit: 100);
        var target = await TargetAsync(request.ContactId, request.PluginId, ListOperation, cancellationToken).ConfigureAwait(false);
        var args = new JsonObject { ["limit"] = limit, ["offset"] = offset, ["source"] = "all" };

        var result = await ReadAsync(target, args, cancellationToken).ConfigureAwait(false);
        return new ContactThreadsResponse(target.PluginId, result["items"]!.DeepClone().AsArray(), result["has_more"]!.GetValue<bool>());
    }

    public async Task<ContactThreadResponse> ThreadAsync(ContactThreadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw DomainErrors.Required("thread_id");
        }

        // The contract's own bound on a thread's identifier: refused here as the caller's mistake, not composed into an
        // input the schema check would then refuse as the runtime's.
        if (request.ThreadId.Trim().Length > MaxThreadIdLength)
        {
            throw new ValidationException([new ErrorDetail("thread_id", "too_long", $"thread_id must be at most {MaxThreadIdLength} characters.")]);
        }

        var (limit, offset) = Page(request.Limit, request.Offset, maxLimit: 20);
        var target = await TargetAsync(request.ContactId, request.PluginId, GetOperation, cancellationToken).ConfigureAwait(false);
        var args = new JsonObject { ["thread"] = new JsonObject { ["external_id"] = request.ThreadId.Trim() }, ["limit"] = limit, ["offset"] = offset };

        var result = await ReadAsync(target, args, cancellationToken).ConfigureAwait(false);
        var thread = result["thread"]!.AsObject();
        if (!string.Equals((string?)thread["contact"]?["external_id"], target.Pin, StringComparison.Ordinal))
        {
            throw new NotFoundException("thread_not_this_contact", $"Thread '{request.ThreadId.Trim()}' at '{target.PluginId}' is not this person's.");
        }

        return new ContactThreadResponse(target.PluginId, thread.DeepClone().AsObject(), result["messages"]!.DeepClone().AsArray(), result["has_more"]!.GetValue<bool>());
    }

    private static (int Limit, int Offset) Page(int? limit, int? offset, int maxLimit)
    {
        var errors = new ValidationErrors();
        if (limit is < 1 || limit > maxLimit)
        {
            errors.Add("limit", "invalid", $"limit must be between 1 and {maxLimit}.");
        }

        if (offset is < 0)
        {
            errors.Add("offset", "invalid", "offset must not be negative.");
        }

        errors.ThrowIfAny();
        return (limit ?? 20, offset ?? 0);
    }

    private async Task<Target> TargetAsync(string? contactId, string? pluginId, string operation, CancellationToken ct)
    {
        var contact = await ContactService.LoadAsync(db, contactId, ct).ConfigureAwait(false);
        var pins = contact.ExternalIds
            .Where(pin => pin.Kind == ContactPin && (string.IsNullOrWhiteSpace(pluginId) || pin.PluginId == pluginId.Trim()))
            .ToList();
        if (pins.Count == 0)
        {
            throw new NotFoundException("contact_not_linked", $"Contact '{contact.PublicId}' is known to no provider{(string.IsNullOrWhiteSpace(pluginId) ? string.Empty : $" as '{pluginId.Trim()}'")}.");
        }

        if (pins.Count > 1)
        {
            throw new ValidationException([new ErrorDetail("plugin_id", "required", "This person is known to several providers; name the plugin to read from.")]);
        }

        var pin = pins[0];
        var contract = OperationCatalog.Find(operation)!;
        var learned = await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.PublicId == pin.RecordedByAttemptId)
            .Select(attempt => new { attempt.Provenance })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var pluginSnapshot = plugins.Snapshot;
        var routeSnapshot = routes.Snapshot;

        var identity = learned?.Provenance?.BindingIdentity;
        PreflightVerdict? refusal = null;
        foreach (var route in learned is null ? [] : Candidates(routeSnapshot, operation))
        {
            if (route.PluginId != pin.PluginId || !string.Equals(route.BindingIdentity, identity, StringComparison.Ordinal))
            {
                continue;
            }

            if (ProviderOpPreflight.Routable(pluginSnapshot, contract, route) is { } refused)
            {
                refusal ??= refused;
                continue;
            }

            var personsPins = contact.ExternalIds.Where(other => other.PluginId == pin.PluginId).ToDictionary(other => other.Kind, other => other.Value, StringComparer.Ordinal);
            return new Target(pin.PluginId, pin.Value, contract, route, new PinnedPlugin(pluginSnapshot.Find(pin.PluginId)!, pluginSnapshot.Id), contact, personsPins);
        }

        throw refusal is { Code: { } code, Message: { } message }
            ? new ConflictException(code, message)
            : new NotFoundException(AttemptErrors.NoRoute, $"No route sends '{operation}' to '{pin.PluginId}' in the account this person's identifier was learned in.");
    }

    /// <summary>What the operation resolves to in every campaign the routes name, then globally.</summary>
    private static IEnumerable<Route> Candidates(RouteSnapshot snapshot, string operation)
    {
        foreach (var campaign in snapshot.Campaigns.Keys.Order(StringComparer.Ordinal))
        {
            if (RouteResolver.Resolve(snapshot, campaign, operation) is { } resolution)
            {
                yield return resolution.Route;
            }
        }

        // A campaign id no route names resolves to the global routes.
        if (RouteResolver.Resolve(snapshot, string.Empty, operation) is { } global)
        {
            yield return global.Route;
        }
    }

    private async Task<JsonObject> ReadAsync(Target target, JsonObject args, CancellationToken ct)
    {
        var input = CanonicalInput.ComposeRead(target.Contract, target.Contact, target.Pins, args);
        if (SchemaValidator.Validate(input, target.Contract.InputSchema, PatternTiming.Default) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException($"The input composed for '{target.Contract.Id}' does not satisfy its schema: {problems.Count} problem(s).");
        }

        var request = new PluginInvocationRequest(
            target.PluginId,
            target.Contract.Id,
            input,
            target.Route.Binding?.DeepClone().AsObject(),
            PublicId.New("ctr"),
            Timeout: TimeSpan.FromMilliseconds(target.Contract.TimeoutMs),
            Pinned: target.Plugin);
        var invoked = await invoker.InvokeAsync(request, ct).ConfigureAwait(false);

        switch (invoked.Outcome)
        {
            case InvocationOutcome.Succeeded { Result: JsonObject result }
                when SchemaValidator.Verdicts(OutcomeContract.CheckResult(target.Contract, result)).Count == 0:
                return result;
            case InvocationOutcome.Succeeded:
                throw Failed(AttemptErrors.ResultInvalid, $"The provider's answer to '{target.Contract.Id}' does not satisfy the schema that operation publishes.", FailureClass.Ambiguous);
            case InvocationOutcome.Failed { Error: var error }:
                // A blocked read is final, as a permanent one is, under the code the plugin gave. The block itself does
                // not travel: an API error carries a code, a message and whether to retry, and nothing else. The code is
                // held to the operation's list by the one check a work item's failure meets, host endings included.
                throw OutcomeContract.CheckFailure(target.Contract, error) is null
                    ? Failed(error.Code, error.Message, OutcomeContract.Recorded(error, out _))
                    : Failed(AttemptErrors.ResultInvalid, $"The provider failed '{target.Contract.Id}' outside what that operation declares.", FailureClass.Ambiguous);
            case InvocationOutcome.ProtocolFailure protocol:
                throw Failed("provider_read_failed", protocol.Message, OutcomeClassification.ClassOf(protocol.Code));
            default:
                throw new InvalidOperationException($"An invocation of '{target.Contract.Id}' ended in an outcome nothing reads.");
        }
    }

    /// <summary>The provider could not answer: the request is answered with what it said, and only a transient class says try again.</summary>
    private static DomainException Failed(string code, string message, FailureClass failureClass) =>
        new(StatusCodes.Status502BadGateway, code, message, retryable: failureClass == FailureClass.Transient);

    private sealed record Target(
        string PluginId,
        string Pin,
        OperationContract Contract,
        Route Route,
        PinnedPlugin Plugin,
        Contact Contact,
        IReadOnlyDictionary<string, string> Pins);
}
