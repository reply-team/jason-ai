using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The inbound read's page, held to the claims an operator and a manager's author will act on: where it runs in a scan,
/// which campaigns it skips and why, what a line carries, where the next read starts, what a failure does, the settings,
/// and what it costs. A page that drifted from any of them would teach someone a read they do not have.
/// </summary>
public partial class InboundSyncDocTests
{
    [Fact]
    public void The_page_and_the_work_execution_contract_name_the_scan_s_six_steps_in_the_order_the_scan_runs_them()
    {
        const string steps = "six short steps: **expire**, **enforce**, **sync**, **summon**, **claim**, **notify**";

        Assert.Contains(steps, Flattened(Read("inbound-sync.md")), StringComparison.Ordinal);
        Assert.Contains(steps, Flattened(Read("work-execution.md")), StringComparison.Ordinal);

        // And the order the page states is the order the scan runs them in.
        var scan = File.ReadAllText(Path.Combine(SourceDirectory(), "Dispatch", "ScanRunner.cs"));
        var order = new[] { "<Expirer,", "<LeaseEnforcer,", "<InboundSync,", "<Summoner,", "<Claimer,", "<Notifier," }
            .Select(step => scan.IndexOf(step, StringComparison.Ordinal))
            .ToList();
        Assert.All(order, at => Assert.True(at >= 0));
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public void The_settings_the_page_publishes_are_the_ones_the_runtime_enforces()
    {
        var page = Flattened(Read("inbound-sync.md"));
        var defaults = new InboundOptions();

        Assert.Contains($"`Inbound:Enabled` | `{(defaults.Enabled ? "true" : "false")}` |", page, StringComparison.Ordinal);
        Assert.Contains(
            $"`Inbound:SyncSeconds` | `{defaults.SyncSeconds}` | {InboundOptions.MinimumSyncSeconds}..{InboundOptions.MaximumSyncSeconds} |",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            $"`Inbound:LookbackSeconds` | `{defaults.LookbackSeconds}` | 0..{InboundOptions.MaximumLookbackSeconds} |",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The three reasons a campaign is skipped, in the order the step asks them, by the codes it logs: the order is read
    /// off the step's own source, so asking them in another order is a page that has to change.
    /// </summary>
    [Fact]
    public void The_skip_reasons_are_the_step_s_codes_in_its_order()
    {
        var page = Flattened(Read("inbound-sync.md"));
        var source = File.ReadAllText(Path.Combine(SourceDirectory(), "Dispatch", "InboundSync.cs"));
        var codes = new Dictionary<string, string>
        {
            [nameof(InboundSkipCodes.NoRoute)] = InboundSkipCodes.NoRoute,
            [nameof(InboundSkipCodes.PluginOperationUnsupported)] = InboundSkipCodes.PluginOperationUnsupported,
            [nameof(InboundSkipCodes.CampaignNotLinked)] = InboundSkipCodes.CampaignNotLinked,
        };

        var asked = codes.OrderBy(code => source.IndexOf($"SkipAsync(db, campaign, InboundSkipCodes.{code.Key}", StringComparison.Ordinal))
            .Select(code => code.Value)
            .ToList();
        Assert.All(codes.Keys, name => Assert.Contains($"SkipAsync(db, campaign, InboundSkipCodes.{name}", source, StringComparison.Ordinal));

        var printed = codes.Values.OrderBy(code => page.IndexOf($"| `{code}` |", StringComparison.Ordinal)).ToList();
        Assert.All(codes.Values, code => Assert.Contains($"| `{code}` |", page, StringComparison.Ordinal));
        Assert.Equal(asked, printed);
    }

    [Fact]
    public void The_line_the_page_describes_is_the_line_the_step_writes()
    {
        var page = Flattened(Read("inbound-sync.md"));

        Assert.Contains($"| `kind` | `{JournalKinds.InboundReplyObserved}` |", page, StringComparison.Ordinal);
        Assert.Contains(
            "| `new` | `plugin`, `thread`, `channel`, `last_activity_at`, `unread`, `has_meeting_intent`, `contact_id` |",
            page,
            StringComparison.Ordinal);

        // Read off the step's own source, so a member added to the line is a member the page has to name.
        var source = File.ReadAllText(Path.Combine(SourceDirectory(), "Dispatch", "InboundSync.cs"));
        var written = Member().Matches(source[source.IndexOf("updated: new JsonObject", StringComparison.Ordinal)..source.IndexOf("workItem: item,", StringComparison.Ordinal)])
            .Select(match => match.Groups[1].Value)
            .ToList();
        Assert.Equal(["plugin", "thread", "channel", "last_activity_at", "unread", "has_meeting_intent", "contact_id"], written);
    }

    /// <summary>The sentences an operator will hold the read to: where a campaign that gains a route starts, and what a failure does.</summary>
    [Fact]
    public void The_page_states_where_a_read_starts_and_what_a_failure_does()
    {
        var page = Flattened(Read("inbound-sync.md"));

        Assert.Contains(
            "a campaign that gains its route or link later starts from then, less the look-back, and never before its go-live",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "A read that fails starts again from its first page with the same since, and the key keeps the re-listed threads from being said twice.",
            page,
            StringComparison.Ordinal);
        Assert.Contains("With a look-back of 0 the next read starts where the last one started, and a reply the provider lists that late is not said", page, StringComparison.Ordinal);
        Assert.Contains("never a subject, a preview or a name", Flattened(Read("architecture.md")), StringComparison.Ordinal);

        // The bound in the provider's terms, and the setting that moves it.
        Assert.Contains(
            "a reply the provider takes into its inbox more than `Inbound:LookbackSeconds` after the moment it stamps on the thread is not read by the step",
            page,
            StringComparison.Ordinal);
        Assert.Contains("A provider slower than that needs a longer look-back: raise `Inbound:LookbackSeconds`.", page, StringComparison.Ordinal);

        // What a line does not know, and what an installation upgrading has to do to be woken by replies.
        Assert.Contains("A line says that a thread the other side wrote into moved, not that the other side moved it last", page, StringComparison.Ordinal);
        Assert.Contains("is not woken by replies until it adds the kind", page, StringComparison.Ordinal);

        // And the numbers the step holds itself to.
        Assert.Contains($"`limit` {InboundSync.PageSize},", page, StringComparison.Ordinal);
        Assert.Equal(86_400, InboundSync.MaximumWaitSeconds);
        Assert.Contains("doubled for each failure in a row, at most a day", page, StringComparison.Ordinal);
        Assert.Contains("A read ends at the furthest offset `conversation.list` takes, a million threads", page, StringComparison.Ordinal);
        Assert.Equal(1_000_000, InboundSync.MaximumOffset);
    }

    /// <summary>What a cadence costs, in reads and lines a day, computed from the defaults rather than written twice.</summary>
    [Fact]
    public void The_page_prices_the_default_cadence()
    {
        var page = Flattened(Read("inbound-sync.md"));
        var reads = 86_400 / new InboundOptions().SyncSeconds;

        Assert.Contains($"about {reads.ToString(CultureInfo.InvariantCulture)} read work items a day", page, StringComparison.Ordinal);

        // Each read is made, scheduled, processing and succeeded: four lifecycle lines.
        Assert.Contains($"about {(reads * 4).ToString(CultureInfo.InvariantCulture)} lifecycle lines", page, StringComparison.Ordinal);

        // A read lists everything that moved within the look-back, so a busy campaign takes more than one page.
        Assert.Contains("a busy campaign may take two or three read work items a cadence rather than one", page, StringComparison.Ordinal);
    }

    /// <summary>The manager's page names the two settings that decide how soon a reply wakes a manager, at their defaults.</summary>
    [Fact]
    public void The_campaign_manager_page_names_the_inbound_read_s_defaults()
    {
        var page = Flattened(Read("campaign-manager.md"));
        var defaults = new InboundOptions();

        Assert.Contains(
            $"every `Inbound:SyncSeconds` ({defaults.SyncSeconds.ToString(CultureInfo.InvariantCulture)} by default)",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            $"`Inbound:LookbackSeconds` ({defaults.LookbackSeconds.ToString(CultureInfo.InvariantCulture)} by default)",
            page,
            StringComparison.Ordinal);
    }

    private static string Read(string page) => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", page));

    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    private static string SourceDirectory() => Path.Combine(RepositoryRoot(), "runtime", "src", "Jason.Runtime");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\[""([a-z_]+)""\]\s*=")]
    private static partial Regex Member();
}
