using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A feature page that describes a line the runtime writes links the kind's document for the line's fields, rather than
/// keeping a second list of them beside the one the runtime is held to. What the line means, when it is written and
/// what reads it stays on the page.
/// </summary>
public partial class JournalKindPageLinksTests
{
    [Theory]
    [InlineData("inbound-sync.md", "inbound_reply_observed", "`plugin`, `thread`, `channel`, `last_activity_at`, `unread`, `has_meeting_intent`, `contact_id`")]
    [InlineData("events.md", "inbound_reply_observed", "`plugin`, `thread`, `channel`, `last_activity_at`, `unread`, `has_meeting_intent`, `contact_id`")]
    [InlineData("events.md", "provider_event_observed", "`kind`, `plugin`, `event_id`, `occurred_at`, `channel`, `contact_id`")]
    [InlineData("events.md", "events_read", "the source, the plugin, the binding identity, how it read, the pages")]
    [InlineData("notifications.md", "notification_sent", "\"notification_id\": \"ntf_01K")]
    public void The_page_links_the_kinds_document_instead_of_listing_its_fields(string page, string kind, string list)
    {
        var text = Whitespace().Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", page)), " ");

        Assert.True(File.Exists(Path.Combine(RepositoryTree.Root(), "docs", "contracts", "journal", kind + ".json")), kind);
        Assert.Contains($"](contracts/journal/{kind}.json)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(list, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contract's own section and the decision that made the kinds a contract both say where a program reads the
    /// documents from a runtime, and in what shape a place comes back.
    /// </summary>
    [Theory]
    [InlineData("contracts/README.md", "The runtime answers these documents through `journal.kinds` (`docs/runtime-api.md`), each entity's `at` always a list there.")]
    [InlineData("architecture.md", "The runtime answers these documents through `journal.kinds`.")]
    public void The_contract_and_its_decision_say_the_runtime_answers_the_documents(string page, string sentence) =>
        Assert.Contains(sentence, Whitespace().Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", page)), " "), StringComparison.Ordinal);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
