using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Plugins;

/// <summary>What one answer did to Jason's record of a provider's identifiers.</summary>
/// <param name="Pinned">At least one identifier was written down for the first time.</param>
/// <param name="Diverged">At least one identifier disagreed with a pin that already stood.</param>
/// <param name="UndeclaredKind">
/// The first kind the answer offered that the operation's contract does not declare, or null when every kind was
/// declared. Nothing of that kind was written: what the contract does not name, nothing later knows how to read.
/// </param>
public sealed record PinOutcome(bool Pinned, bool Diverged, string? UndeclaredKind);

/// <summary>
/// Jason's record of what each plugin calls each of our entities. A pin is written once, by the attempt that
/// learned it, and is never overwritten: when a plugin later answers with something else the disagreement is
/// recorded beside the pin, both values stay visible, and the attempt still succeeds. Resolving the disagreement
/// is reconciliation, and nothing here can do it — so the honest thing is to keep the evidence and let a person
/// decide.
/// </summary>
/// <remarks>
/// Nothing here saves: entries join the caller's change set, so a pin and the outcome it was learned from commit
/// together or not at all. The contact and the campaign are expected to arrive with their pins loaded — the same
/// precondition the read paths already have, since a contact answered without its pins would claim it has none.
/// </remarks>
public sealed class ExternalIdStore(JournalWriter journal, TimeProvider clock)
{
    /// <summary>
    /// Applies a plugin's returned identifiers, mapped through the kinds the operation's own contract declares:
    /// the declared entity decides whether a value pins the work item's contact or its campaign.
    /// </summary>
    public PinOutcome Apply(
        JasonDbContext db,
        OperationContract contract,
        string pluginId,
        WorkItem item,
        Campaign campaign,
        Contact? contact,
        Attempt attempt,
        JsonObject? externalIds)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(attempt);

        if (externalIds is null || externalIds.Count == 0)
        {
            // Returning nothing is an ordinary answer: an operation pins what it learned, and it may learn nothing.
            return new PinOutcome(false, false, null);
        }

        var pinned = false;
        var diverged = false;
        string? undeclared = null;

        foreach (var (kind, node) in externalIds)
        {
            if (!contract.ExternalIds.TryGetValue(kind, out var declared))
            {
                undeclared ??= kind;
                continue;
            }

            if (!Identifier(node, out var value))
            {
                continue;
            }

            var pins = declared.Entity == PinnedEntity.Contact ? contact?.ExternalIds : campaign.ExternalIds;
            if (pins is null)
            {
                // The operation declares a contact identifier and this item names no contact. Nothing to pin it to.
                continue;
            }

            switch (Record(db, pins, pluginId, kind, value, declared.Entity, item, campaign, attempt))
            {
                case Applied.Pinned:
                    pinned = true;
                    break;
                case Applied.Diverged:
                    diverged = true;
                    break;
                default:
                    break;
            }
        }

        return new PinOutcome(pinned, diverged, undeclared);
    }

    /// <summary>
    /// What a plugin is allowed to see of a contact: its own pins, as <c>{kind: value}</c>. A diverged pin passes
    /// the value Jason recorded, never the one it disputes — otherwise the next attempt would work by the value
    /// Jason refused to accept.
    /// </summary>
    public IReadOnlyDictionary<string, string> PinsFor(Contact contact, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return Projection(contact.ExternalIds, pluginId);
    }

    /// <summary>The same for a campaign: one plugin's pins, under the kinds it knows them by.</summary>
    public IReadOnlyDictionary<string, string> PinsFor(Campaign campaign, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return Projection(campaign.ExternalIds, pluginId);
    }

    /// <summary>The wire shape of an entity's pins, in one stable order so two reads of an unchanged entity match.</summary>
    public static IReadOnlyList<ExternalIdDto> ToDtos(IReadOnlyCollection<ExternalId> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);

        return
        [
            .. pins
                .OrderBy(pin => pin.PluginId, StringComparer.Ordinal)
                .ThenBy(pin => pin.Kind, StringComparer.Ordinal)
                .Select(pin => new ExternalIdDto(
                    pin.PluginId,
                    pin.Kind,
                    pin.Value,
                    Utc(pin.RecordedAt),
                    pin.RecordedByAttemptId,
                    pin.DivergedValue,
                    pin.DivergedAt is { } diverged ? Utc(diverged) : null,
                    pin.DivergedByAttemptId)),
        ];
    }

    /// <summary>Everything in the database is UTC; SQLite hands the kind back unset.</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static IReadOnlyDictionary<string, string> Projection(IReadOnlyCollection<ExternalId> pins, string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pin in pins)
        {
            if (string.Equals(pin.PluginId, pluginId, StringComparison.Ordinal))
            {
                seen[pin.Kind] = pin.Value;
            }
        }

        return seen;
    }

    private Applied Record(
        JasonDbContext db,
        List<ExternalId> pins,
        string pluginId,
        string kind,
        string value,
        PinnedEntity entity,
        WorkItem item,
        Campaign campaign,
        Attempt attempt)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var actor = Actors.ForAttempt(attempt);
        var key = Key(entity, pluginId, kind);
        var standing = pins.Find(pin => string.Equals(pin.PluginId, pluginId, StringComparison.Ordinal)
            && string.Equals(pin.Kind, kind, StringComparison.Ordinal));

        if (standing is null)
        {
            pins.Add(new ExternalId
            {
                PluginId = pluginId,
                Kind = kind,
                Value = value,
                RecordedAt = now,
                RecordedByAttemptId = attempt.PublicId,
            });

            journal.Append(db, actor, JournalKinds.ExternalIdPinned, campaign, key, updated: JsonValue.Create(value), workItem: item, attempt: attempt);
            return Applied.Pinned;
        }

        if (string.Equals(standing.Value, value, StringComparison.Ordinal))
        {
            // The provider still agrees with itself. There is nothing to write and nothing to tell anyone.
            return Applied.Unchanged;
        }

        // The pin stands. What the plugin said is recorded beside it, with the attempt that said it, so that both
        // values remain visible until somebody decides which of them is right.
        standing.DivergedValue = value;
        standing.DivergedAt = now;
        standing.DivergedByAttemptId = attempt.PublicId;

        journal.Append(
            db,
            actor,
            JournalKinds.ExternalIdDiverged,
            campaign,
            key,
            old: JsonValue.Create(standing.Value),
            updated: JsonValue.Create(value),
            workItem: item,
            attempt: attempt);
        return Applied.Diverged;
    }

    private static string Key(PinnedEntity entity, string pluginId, string kind) =>
        (entity == PinnedEntity.Contact ? "contact/" : "campaign/") + pluginId + "/" + kind;

    /// <summary>
    /// An identifier is a non-empty string of at most the length the protocol accepts. Anything else — a number,
    /// an object, an empty string — identifies nothing, so it is not written down.
    /// </summary>
    private static bool Identifier(JsonNode? node, out string value)
    {
        value = string.Empty;
        if (node is not JsonValue text || node.GetValueKind() != JsonValueKind.String || !text.TryGetValue(out string? identifier))
        {
            return false;
        }

        if (identifier is null || identifier.Length == 0 || identifier.Length > OutcomeContract.MaxExternalIdLength)
        {
            return false;
        }

        value = identifier;
        return true;
    }

    private enum Applied
    {
        Unchanged,
        Pinned,
        Diverged,
    }
}
