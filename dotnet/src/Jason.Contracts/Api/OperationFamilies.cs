using System.Collections.Frozen;
using System.Reflection;

namespace Jason.Contracts.Api;

/// <summary>
/// What a plugin's <c>api</c> grant can open for its handler's <c>host.call</c>: every operation of the Runtime API is
/// a read, in exactly one write family named by its noun, or never grantable. The families a grant names resolve here,
/// once per load, to the operation set the runtime mints a handler's capability with; the plugin-host refuses an
/// operation outside that set before anything leaves it, and the API refuses it again.
/// </summary>
/// <remarks>
/// A read is known by its verb, so an operation that asks a question is a read without a second list to keep: the verb
/// is one of <see cref="ReadVerbs"/> and the noun is not one of <see cref="ClosedNouns"/>, the installation's and the
/// UI's own doors and the provider's accounts. A conversation read is no read here, and neither is a read a campaign's route answers — an audience's
/// preview, a filter's values, whether the account may search — since answering each spends a provider call. Everything that
/// changes a person's decisions, the installation, its routes and grants, or an executor's fenced attempt is in
/// <see cref="Never"/>, and an operation added to the API is unclassified until it is a read, in a family or never,
/// which a test holds.
/// </remarks>
public static class OperationFamilies
{
    public const string Read = "read";
    public const string Campaign = "campaign";
    public const string Contact = "contact";
    public const string WorkItem = "workitem";
    public const string Suppression = "suppression";
    public const string Report = "report";
    public const string JournalAppend = "journal.append";

    /// <summary>Every family a grant may name, in the order the documentation lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Read, Campaign, Contact, WorkItem, Suppression, Report, JournalAppend];

    /// <summary>The verbs of an operation that only reads.</summary>
    public static IReadOnlySet<string> ReadVerbs { get; } = new[]
    {
        "get", "list", "count", "summary", "activity", "activity_by_period", "list_contacts", "list_standing", "resolve",
        "sources", "kinds", "describe", "audience",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The nouns no read of a handler's reaches: the installation's own, the UI's own, and the provider's accounts, a noun
    /// whose every operation spends a provider call.
    /// </summary>
    public static IReadOnlySet<string> ClosedNouns { get; } = new[] { "system", "ui", "sender" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The write families, each the operations of its noun a handler may be granted.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Writes { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [Campaign] =
        [
            Operations.CampaignCreate, Operations.CampaignUpdate, Operations.CampaignStart, Operations.CampaignPause,
            Operations.CampaignArchive, Operations.CampaignUpdateContext, Operations.CampaignAddContacts,
            Operations.CampaignRemoveContacts,
        ],
        [Contact] = [Operations.ContactCreate, Operations.ContactUpdate, Operations.ContactArchive],
        [WorkItem] = [Operations.WorkItemCreate, Operations.WorkItemUpdate, Operations.WorkItemCancel],
        [Suppression] = [Operations.SuppressionAdd],
        [Report] = [Operations.ReportSubmit],
        [JournalAppend] = [Operations.JournalAppend],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The operations no grant opens: a person's own doors, the installation's, and an executor's fenced verbs. Lifting a
    /// do-not-contact entry is a decision about a person, so a plugin may add to the register and never take from it.
    /// Moving a subscribed kind's cursor back is the installation's, beside asking for an account's events again. A
    /// subscription run's record carries the envelopes other plugins' handlers made: a handler in the chain already holds
    /// the envelope handed to it, and a person reads the final one through the run token, so no plugin reads another
    /// chain's envelopes.
    /// </summary>
    public static IReadOnlySet<string> Never { get; } = new[]
    {
        Operations.SystemInfo, Operations.SystemShutdown, Operations.SystemDrain, Operations.SystemResume,
        Operations.UiOpen, Operations.UiTokens, Operations.UiRevoke,
        Operations.ProjectCreate, Operations.ProjectUpdate, Operations.ProjectLink,
        Operations.PreferenceSet, Operations.PreferenceUnset,
        Operations.ContactThreads, Operations.ContactThread,
        Operations.AudiencePreview, Operations.AudienceFilterValues, Operations.AudienceAvailability,
        Operations.SenderList,
        Operations.WorkItemHeartbeat, Operations.WorkItemSetResult, Operations.WorkItemComplete, Operations.WorkItemWait,
        Operations.WorkItemResume,
        Operations.ApprovalApprove, Operations.ApprovalReject, Operations.ApprovalSetStanding, Operations.ApprovalUnsetStanding,
        Operations.BlockClear,
        Operations.DecisionRaise, Operations.DecisionAnswer,
        Operations.ReplyRecord,
        Operations.SuppressionRemove,
        Operations.RoleAdd, Operations.RoleSetProfile, Operations.RoleNoteSet,
        Operations.ProfileCreate, Operations.ProfileUpdate, Operations.ProfileDisable, Operations.ProfileEnable,
        Operations.PluginReload,
        Operations.RouteSet, Operations.RouteUnset,
        Operations.EventsBackfill, Operations.EventsSignal,
        Operations.SubscriptionRuns, Operations.SubscriptionRewind,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Every operation the Runtime API publishes: the constants of <see cref="Operations"/>.</summary>
    public static IReadOnlyList<string> Catalog { get; } =
        [.. typeof(Operations)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    private static readonly FrozenDictionary<string, string> FamilyByWrite = Writes
        .SelectMany(family => family.Value.Select(operation => KeyValuePair.Create(operation, family.Key)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Reads = Catalog.Where(IsRead).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="operation"/> is a read by the verb rule; the catalog is not consulted.</summary>
    public static bool IsRead(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var dot = operation.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot != operation.LastIndexOf('.'))
        {
            return false;
        }

        return ReadVerbs.Contains(operation[(dot + 1)..]) && !ClosedNouns.Contains(operation[..dot]);
    }

    /// <summary>The family that opens <paramref name="operation"/>, or null where none does.</summary>
    public static string? FamilyOf(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Never.Contains(operation))
        {
            return null;
        }

        return IsRead(operation) ? Read : FamilyByWrite.GetValueOrDefault(operation);
    }

    /// <summary>Whether <paramref name="family"/> is a family a grant may name.</summary>
    public static bool IsFamily(string family) => All.Contains(family, StringComparer.Ordinal);

    /// <summary>The operations the published catalog holds under <paramref name="families"/>, which must all be families.</summary>
    public static IReadOnlySet<string> OperationsOf(IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in families)
        {
            if (string.Equals(family, Read, StringComparison.Ordinal))
            {
                operations.UnionWith(Reads);
            }
            else if (Writes.TryGetValue(family, out var writes))
            {
                operations.UnionWith(writes);
            }
            else
            {
                throw new ArgumentException($"'{family}' is not an operation family.", nameof(families));
            }
        }

        return operations.ToFrozenSet(StringComparer.Ordinal);
    }
}
