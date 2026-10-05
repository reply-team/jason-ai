using System.Reflection;
using Jason.Cli.Doctor;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Journal;

/// <summary>
/// Every kind of journal entry the runtime writes has been decided: either it carries values, and the text that says
/// what <c>jason doctor --include-journal</c> adds names them, or it carries only metadata. The flag carries the
/// journal's entries whole, so a kind nobody decided is a value that could leave the machine without the warning
/// naming it. A new kind fails here, naming itself, until it is placed in one list or the other.
/// </summary>
/// <remarks>
/// The lists are a decision made by reading what each kind's entries hold. This test does not read the places that
/// write them: it holds the decision to exist for every kind and to quote the words the bundle itself says.
/// </remarks>
public sealed class JournalKindInventoryTests
{
    /// <summary>The kinds whose entries hold values, each with the words of <see cref="DoctorComposer.JournalCarries"/> that name them.</summary>
    private static readonly Dictionary<string, string> CarriesValues = new(StringComparer.Ordinal)
    {
        [JournalKinds.CampaignCreated] = "campaign names and fields as they changed",
        [JournalKinds.CampaignUpdated] = "campaign names and fields as they changed",
        [JournalKinds.ContextUpdated] = "campaign and work-item contexts as they changed",
        [JournalKinds.WorkItemContextUpdated] = "campaign and work-item contexts as they changed",
        [JournalKinds.WorkItemUpdated] = "result formats",
        [JournalKinds.SuppressionAdded] = "the addresses and numbers put on or taken off the do-not-contact register",
        [JournalKinds.SuppressionRemoved] = "the addresses and numbers put on or taken off the do-not-contact register",
        [JournalKinds.RoutesUpdated] = "the accounts a campaign's routes bind",
        [JournalKinds.DecisionAnswered] = "the options decisions were answered with",
        [JournalKinds.WorkItemFailed] = "the errors attempts ended with",
        [JournalKinds.ExternalIdPinned] = "provider identifiers",
        [JournalKinds.ExternalIdDiverged] = "provider identifiers",
        [JournalKinds.ExternalEffectReported] = "the effect, tool and operation a report names",
        [JournalKinds.NotificationSent] = "the failure text and receipts notification channels reported",
    };

    /// <summary>
    /// The kinds whose entries hold only metadata: ids, counts, statuses, attempt numbers, moments, codes, sizes and
    /// hashes, the names of fields that changed, and the names of roles, profiles and plugins.
    /// </summary>
    private static readonly HashSet<string> MetadataOnly = new(StringComparer.Ordinal)
    {
        JournalKinds.CampaignStarted,
        JournalKinds.CampaignPaused,
        JournalKinds.CampaignArchived,
        JournalKinds.ContactsAdded,
        JournalKinds.ContactsRemoved,
        JournalKinds.ContactCreated,
        JournalKinds.ContactUpdated,
        JournalKinds.ContactArchived,
        JournalKinds.WorkItemCreated,
        JournalKinds.WorkItemScheduled,
        JournalKinds.WorkItemProcessing,
        JournalKinds.WorkItemSucceeded,
        JournalKinds.WorkItemCancelled,
        JournalKinds.WorkItemExpired,
        JournalKinds.WorkItemReleased,
        JournalKinds.WorkItemAwaitingApproval,
        JournalKinds.ApprovalRequested,
        JournalKinds.ApprovalApproved,
        JournalKinds.ApprovalRejected,
        JournalKinds.ApprovalSuperseded,
        JournalKinds.ApprovalCancelled,
        JournalKinds.WorkItemReopened,
        JournalKinds.RoleAdded,
        JournalKinds.RoleUpdated,
        JournalKinds.RoleNoteSet,
        JournalKinds.ProfileCreated,
        JournalKinds.ProfileRevised,
        JournalKinds.ProfileDisabled,
        JournalKinds.ProfileEnabled,
        JournalKinds.PluginsReloaded,
        JournalKinds.DecisionRaised,
        JournalKinds.DecisionCancelled,
    };

    /// <summary>
    /// What every entry can hold whatever its kind — who it says acted and why — and what the entries people and agents
    /// append under kinds of their own hold. No list of the runtime's kinds can name these, so the text has to.
    /// </summary>
    private static readonly string[] EveryEntry =
    [
        "entries people and agents wrote themselves",
        "the names actors gave",
        "the reasons they typed",
    ];

    [Fact]
    public void Every_kind_the_runtime_writes_is_decided_once_and_a_kind_that_carries_values_is_named_by_the_bundles_words()
    {
        var defined = typeof(JournalKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Concat(JournalKinds.Reserved)
            .ToHashSet(StringComparer.Ordinal);

        var undecided = defined.Where(kind => !CarriesValues.ContainsKey(kind) && !MetadataOnly.Contains(kind)).Order(StringComparer.Ordinal).ToList();
        Assert.True(
            undecided.Count == 0,
            $"{undecided.Count} journal kind(s) are in neither list; decide whether each carries values (and which words of the bundle's warning name them) or only metadata: {string.Join(", ", undecided)}");

        var twice = CarriesValues.Keys.Where(MetadataOnly.Contains).Order(StringComparer.Ordinal).ToList();
        Assert.True(twice.Count == 0, $"{twice.Count} journal kind(s) are in both lists: {string.Join(", ", twice)}");

        var stale = CarriesValues.Keys.Concat(MetadataOnly).Where(kind => !defined.Contains(kind)).Order(StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, $"{stale.Count} listed kind(s) are not kinds the runtime defines: {string.Join(", ", stale)}");

        var unnamed = CarriesValues
            .Where(pair => !DoctorComposer.JournalCarries.Contains(pair.Value, StringComparison.Ordinal))
            .Select(pair => $"{pair.Key}: \"{pair.Value}\"")
            .Concat(EveryEntry.Where(words => !DoctorComposer.JournalCarries.Contains(words, StringComparison.Ordinal)).Select(words => $"every entry: \"{words}\""))
            .ToList();
        Assert.True(
            unnamed.Count == 0,
            $"What the journal flag adds does not say what {unnamed.Count} carrier(s) hold:{Environment.NewLine}{string.Join(Environment.NewLine, unnamed)}");
    }
}
