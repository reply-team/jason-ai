using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Journal;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Documentation;

namespace Jason.Runtime.Tests.Journal;

/// <summary>
/// A line of a kind in the shape its document under <c>docs/contracts/journal/</c> states, for a test that needs a line
/// of that kind rather than what the line says: the key, old and new of the kind's first valid body fixture. The tests'
/// runtime holds every save to the documents, so a line a test writes by hand is one the runtime could have written;
/// the columns a document names as always present stay the test's to give.
/// </summary>
internal static class DocumentedLines
{
    private static readonly ConcurrentDictionary<string, JsonObject> Bodies = new(StringComparer.Ordinal);

    /// <summary>The body of the kind's first valid fixture, as copies the caller may change.</summary>
    public static (string? Key, JsonNode? Old, JsonNode? New) Body(string kind)
    {
        var body = Bodies.GetOrAdd(kind, Read);
        return ((string?)body["key"], body["old"]?.DeepClone(), body["new"]?.DeepClone());
    }

    /// <summary>
    /// The kind's line through the runtime's writer, its body the fixture's: <paramref name="key"/> and
    /// <paramref name="updated"/>, when given, in place of its key and its <c>new</c>, and the members of
    /// <paramref name="with"/> written over that <c>new</c>.
    /// </summary>
    public static JournalEntry AppendDocumented(
        this JournalWriter writer,
        JasonDbContext db,
        ActorRef actor,
        string kind,
        Campaign? campaign,
        string? key = null,
        JsonNode? updated = null,
        JsonObject? with = null,
        WorkItem? workItem = null,
        Attempt? attempt = null,
        Contact? contact = null,
        int? contactId = null)
    {
        var (documentedKey, old, documented) = Body(kind);
        return writer.Append(
            db,
            actor,
            kind,
            campaign,
            key: key ?? documentedKey,
            old: old,
            updated: Over(updated ?? documented, with),
            workItem: workItem,
            attempt: attempt,
            contact: contact,
            contactId: contactId);
    }

    /// <summary>
    /// An attempt's failure as <c>workitem_failed</c> carries it, for a line that says work failed and nothing about how.
    /// </summary>
    public static JsonObject Failure() => new()
    {
        ["code"] = "role_failed",
        ["message"] = "The role did not finish its work.",
        ["retriable"] = false,
    };

    /// <summary>
    /// What a line of the kind must name and a test has no reason to make: a draft campaign, unless the test gives one,
    /// when its document names a campaign on every line, with a finished role item in it when it names a work item, and
    /// the item's attempt when it names an attempt or <paramref name="attempt"/> asks for one — saved, so the line can
    /// take the item's campaign from its key.
    /// </summary>
    public static async Task<(Campaign? Campaign, WorkItem? Item, Attempt? Attempt)> StandInsAsync(
        JasonDbContext db,
        string kind,
        DateTime now,
        CancellationToken ct,
        Campaign? campaign = null,
        bool attempt = false)
    {
        var contract = JournalKindCatalog.Find(kind) ?? throw new InvalidOperationException($"Kind {kind} has no document.");
        bool Names(string at) => contract.Entities.Values.Any(entity => entity.At == at && entity.Presence == JournalEntityPresence.Always);
        var needsAttempt = attempt || Names("/attempt_id");
        var needsItem = needsAttempt || Names("/work_item_id");
        if (!needsItem && !Names("/campaign_id"))
        {
            return (campaign, null, null);
        }

        if (campaign is null)
        {
            campaign = WorkItemFactory.NewCampaign("Stand-in", CampaignStatus.Draft, now);
            db.Campaigns.Add(campaign);
        }

        WorkItem? item = null;
        Attempt? ran = null;
        if (needsItem)
        {
            item = WorkItemFactory.NewAiRole(campaign, now: now);
            item.Status = WorkItemStatus.Succeeded;
            item.FinishedAt = now;
            db.WorkItems.Add(item);
            if (needsAttempt)
            {
                ran = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, now);
            }
        }

        await db.SaveChangesAsync(ct);
        return (campaign, item, ran);
    }

    /// <summary>The members of <paramref name="with"/> written over <paramref name="updated"/>, an object.</summary>
    public static JsonNode? Over(JsonNode? updated, JsonObject? with)
    {
        if (with is null)
        {
            return updated;
        }

        var target = Assert.IsType<JsonObject>(updated);
        foreach (var (name, value) in with)
        {
            target[name] = value?.DeepClone();
        }

        return target;
    }

    private static JsonObject Read(string kind)
    {
        var directory = Path.Combine(RepositoryTree.Root(), "docs", "contracts", "fixtures", "journal", kind);
        foreach (var file in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var fixture = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(file)));
            if (fixture["expect"] is JsonValue expect && (string?)expect == "valid")
            {
                return Assert.IsType<JsonObject>(fixture["body"]);
            }
        }

        throw new InvalidOperationException($"No valid body fixture of kind {kind} under {directory}.");
    }
}
