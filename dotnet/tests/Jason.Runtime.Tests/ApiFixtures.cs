using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Json;
using Jason.Runtime.Tests.Documentation;

namespace Jason.Runtime.Tests;

/// <summary>
/// The Runtime API's recorded exchanges under <c>docs/api/fixtures/</c>: what a consumer may render, held to what
/// the runtime produced. A recording test posts through <see cref="RuntimeApiFixture.RecordAsync{T}"/>, asserts
/// on the answer as any test does, and the exchange is compared with the committed file; a difference fails here,
/// and only <c>JASON_UPDATE_FIXTURES=1</c> rewrites. Ids, digests, the data directory and the few values that are
/// not the clock's are scrubbed so a file is the same on every machine.
/// </summary>
public static class ApiFixtures
{
    public const string UpdateVariable = "JASON_UPDATE_FIXTURES";

    /// <summary>Every recorded case, as <c>operation/case</c>. A recording not listed here fails; a file not listed here fails the catalog test.</summary>
    public static readonly string[] Cases =
    [
        "approval.approve/already-decided",
        "approval.approve/approved",
        "approval.approve/from-campaign-row",
        "approval.approve/from-campaign-row-again",
        "approval.get/email-message",
        "approval.get/for-campaign-row",
        "approval.get/linkedin-message",
        "approval.get/other-pending",
        "approval.get/pending-with-preview",
        "approval.list/by-campaign-pending",
        "approval.list/by-work-item",
        "approval.list/decided",
        "approval.list/empty",
        "approval.list/messages-pending",
        "approval.list/pending",
        "approval.list/summer-pending",
        "approval.list/unauthorized",
        "approval.list_standing/listed",
        "approval.reject/from-work-item",
        "approval.reject/rejected",
        "approval.set_standing/archived-campaign",
        "approval.set_standing/operation-not-coverable",
        "approval.set_standing/role-refused",
        "approval.set_standing/set",
        "approval.unset_standing/unset",
        "block.clear/cleared",
        "block.clear/not-open",
        "block.get/one",
        "block.get/unknown",
        "block.list/cleared",
        "block.list/open",
        "campaign.activity/all-7-days",
        "campaign.activity/all-today",
        "campaign.activity/last-7-days",
        "campaign.activity/many",
        "campaign.activity/many-one",
        "campaign.activity/one",
        "campaign.activity/summer",
        "campaign.activity/today",
        "campaign.activity/two",
        "campaign.activity_by_period/contacted-7-days",
        "campaign.activity_by_period/contacted-one",
        "campaign.activity_by_period/contacted-today",
        "campaign.activity_by_period/contacted-two",
        "campaign.activity_by_period/replied-7-days",
        "campaign.activity_by_period/replied-one",
        "campaign.activity_by_period/replied-today",
        "campaign.activity_by_period/replied-two",
        "campaign.add_contacts/already-member",
        "campaign.add_contacts/archived-campaign",
        "campaign.add_contacts/move-in",
        "campaign.add_contacts/move-in-rejected",
        "campaign.add_contacts/one-person",
        "campaign.add_contacts/re-enrol-excluded",
        "campaign.add_contacts/several",
        "campaign.add_contacts/to-archived",
        "campaign.get/archived",
        "campaign.get/details",
        "campaign.get/gone",
        "campaign.get/gone-here",
        "campaign.get/in-project-with-folder",
        "campaign.get/inbox",
        "campaign.get/inbox-quiet",
        "campaign.get/many",
        "campaign.get/one",
        "campaign.get/summer",
        "campaign.get/unknown",
        "campaign.get/unlinked",
        "campaign.list/by-project",
        "campaign.list/campaigns-for-picker",
        "campaign.list/first-page",
        "campaign.list/for-picker",
        "campaign.list/inbox",
        "campaign.list/include-archived",
        "campaign.list/move-targets",
        "campaign.list/query",
        "campaign.list/screen",
        "campaign.list/screen-many",
        "campaign.list/screen-many-next",
        "campaign.list/screen-query",
        "campaign.list_contacts/activity-many",
        "campaign.list_contacts/activity-many-next",
        "campaign.list_contacts/activity-people",
        "campaign.list_contacts/activity-people-all",
        "campaign.list_contacts/activity-removed",
        "campaign.list_contacts/activity-replied",
        "campaign.list_contacts/activity-replied-all",
        "campaign.list_contacts/activity-replied-day",
        "campaign.list_contacts/activity-replied-today",
        "campaign.list_contacts/everyone",
        "campaign.list_contacts/query",
        "campaign.list_contacts/query-too-long",
        "campaign.list_contacts/summary-failed",
        "campaign.list_contacts/summary-removed",
        "campaign.list_contacts/summary-waiting",
        "campaign.list_contacts/summary-waiting-after",
        "campaign.list_contacts/unlinked",
        "campaign.pause/with-reason",
        "campaign.remove_contacts/exclude-archived",
        "campaign.remove_contacts/exclude-one",
        "campaign.remove_contacts/move-out",
        "campaign.remove_contacts/move-out-archived",
        "campaign.start/after-pause",
        "campaign.summary/all",
        "campaign.summary/all-after",
        "campaign.summary/archived",
        "campaign.summary/inbox-all",
        "campaign.summary/inbox-one",
        "campaign.summary/inbox-quiet",
        "campaign.summary/many",
        "campaign.summary/many-next",
        "campaign.summary/some",
        "campaign.summary/some-after",
        "campaign.summary/summer",
        "campaign.summary/unlinked",
        "campaign.summary/with-archived",
        "contact.get/archived",
        "contact.get/imported",
        "contact.get/no-first-name",
        "contact.get/oli",
        "contact.get/one",
        "contact.get/person-after-exclude",
        "contact.get/person-after-half-move",
        "contact.get/person-after-move",
        "contact.get/person-finished",
        "contact.get/person-panel",
        "contact.get/unrouted",
        "contact.get/with-campaigns",
        "contact.list/archived",
        "contact.list/by-activity",
        "contact.list/by-activity-query",
        "contact.list/by-activity-query-aguilar",
        "contact.list/by-campaign",
        "contact.list/by-campaign-query",
        "contact.list/by-campaign-query-archived",
        "contact.list/by-campaign-query-none",
        "contact.list/campaign-picker",
        "contact.list/campaign-picker-zoe",
        "contact.list/first-page",
        "contact.list/gone-campaign",
        "contact.list/many",
        "contact.list/many-next",
        "contact.list/query-email",
        "contact.list/query-phone",
        "contact.list/query-too-long",
        "contact.thread/has-more",
        "contact.thread/messages",
        "contact.thread/next",
        "contact.thread/not-this-contact",
        "contact.threads/has-more",
        "contact.threads/inbox-mateo",
        "contact.threads/inbox-paula",
        "contact.threads/next",
        "contact.threads/no-route",
        "contact.threads/not-linked",
        "contact.threads/provider-failed",
        "contact.threads/two-threads",
        "contact.update/person-channels",
        "contact.update/person-cleared",
        "contact.update/person-primaries",
        "contact.update/person-refused",
        "contact.update/person-saved",
        "decision.answer/answered",
        "decision.get/for-campaign-row",
        "decision.get/pending-with-options",
        "decision.list/by-campaign-pending",
        "decision.list/decided",
        "decision.list/empty",
        "decision.list/pending",
        "decision.list/summer-pending",
        "journal.kinds/first-page",
        "journal.list/by-awaiting-item",
        "journal.list/by-campaign",
        "journal.list/by-campaign-changes",
        "journal.list/by-campaign-replies",
        "journal.list/by-campaign-replies-none",
        "journal.list/by-contact",
        "journal.list/by-contact-changes",
        "journal.list/by-contact-decisions",
        "journal.list/by-contact-empty",
        "journal.list/by-contact-failed",
        "journal.list/by-contact-replied-twice",
        "journal.list/by-contact-replies",
        "journal.list/by-contact-work",
        "journal.list/by-expired-item",
        "journal.list/by-failed-item",
        "journal.list/by-succeeded-item",
        "journal.list/by-work-item",
        "journal.list/oli",
        "journal.list/summer",
        "plugin.list/none",
        "preference.list/none",
        "preference.list/set",
        "preference.set/invalid-value",
        "preference.set/no-actor",
        "preference.set/not-human",
        "preference.set/sender",
        "preference.set/tone",
        "preference.set/unknown-key",
        "preference.unset/removed",
        "project.create/created",
        "project.create/exists",
        "project.create/invalid-slug",
        "project.create/with-slug",
        "project.get/by-id",
        "project.get/by-slug",
        "project.get/unknown",
        "project.link/made",
        "project.link/present",
        "project.link/relinked",
        "project.list/all",
        "project.list/query",
        "project.update/renamed",
        "reply.record/confidence-out-of-range",
        "reply.record/recorded",
        "reply.record/recorded-with-decision",
        "reply.record/unknown-goal",
        "role.list/all",
        "rolenote.get/client",
        "rolenote.get/client-empty",
        "rolenote.get/project",
        "rolenote.get/unknown-project",
        "rolenote.list/client",
        "rolenote.list/project",
        "rolenote.set/campaign",
        "rolenote.set/client",
        "rolenote.set/project",
        "rolenote.set/two-levels",
        "route.list/empty",
        "subscription.list/one-kind",
        "subscription.rewind/not-human",
        "subscription.rewind/since-without-offset",
        "subscription.rewind/to-a-line",
        "subscription.runs/succeeded",
        "suppression.add/person-email",
        "suppression.add/person-linkedin",
        "suppression.list/aguilar-email-clear",
        "suppression.list/grace-email-clear",
        "suppression.list/mateo-email-clear",
        "suppression.list/mateo-linkedin-clear",
        "suppression.list/mateo-phone-clear",
        "suppression.list/person-email-clear",
        "suppression.list/person-email-listed",
        "suppression.list/person-linkedin-clear",
        "suppression.list/person-linkedin-listed",
        "suppression.list/person-phone-clear",
        "suppression.list/person-second-email-clear",
        "suppression.remove/person-email",
        "suppression.remove/person-linkedin",
        "system.info/released-plugin-behind",
        "system.info/released-plugin-current",
        "system.info/released-plugin-differs",
        "system.info/released-plugin-not-installed",
        "system.info/with-ui",
        "system.info/without-ui",
        "ui.revoke/never-used",
        "ui.revoke/one",
        "ui.revoke/stale",
        "ui.tokens/aged",
        "ui.tokens/none",
        "ui.tokens/two",
        "workitem.cancel/reason",
        "workitem.count/ada-done",
        "workitem.count/ada-now",
        "workitem.count/ada-planned",
        "workitem.count/done",
        "workitem.count/failed",
        "workitem.count/failed-on-16",
        "workitem.count/failed-on-17",
        "workitem.count/filtered",
        "workitem.count/many-done",
        "workitem.count/many-now",
        "workitem.count/many-planned",
        "workitem.count/now",
        "workitem.count/planned",
        "workitem.count/planned-on-16",
        "workitem.count/planned-on-17",
        "workitem.count/smb-done",
        "workitem.count/smb-manager-done",
        "workitem.count/smb-manager-now",
        "workitem.count/smb-now",
        "workitem.count/smb-operations-done",
        "workitem.count/smb-operations-now",
        "workitem.count/smb-operations-planned",
        "workitem.count/smb-planned",
        "workitem.count/smb-roles-done",
        "workitem.count/smb-roles-now",
        "workitem.count/smb-roles-planned",
        "workitem.count/tomas-done",
        "workitem.count/tomas-now",
        "workitem.count/tomas-planned",
        "workitem.count/unknown-campaign-done",
        "workitem.count/unknown-campaign-now",
        "workitem.count/unknown-campaign-planned",
        "workitem.count/zero",
        "workitem.create/provider-archived",
        "workitem.create/provider-contact-update",
        "workitem.create/researcher-archived",
        "workitem.create/researcher-asked",
        "workitem.create/researcher-asked-plain",
        "workitem.get/awaiting",
        "workitem.get/expired",
        "workitem.get/failed",
        "workitem.get/open",
        "workitem.get/researcher-finding",
        "workitem.get/researcher-finding-second",
        "workitem.get/succeeded",
        "workitem.list/ada-now",
        "workitem.list/by-block",
        "workitem.list/by-campaign-planned",
        "workitem.list/by-campaign-running",
        "workitem.list/by-contact",
        "workitem.list/by-contact-open",
        "workitem.list/by-contact-open-imported",
        "workitem.list/by-contact-open-none",
        "workitem.list/done",
        "workitem.list/empty",
        "workitem.list/failed",
        "workitem.list/failed-on-16",
        "workitem.list/failed-on-17",
        "workitem.list/now",
        "workitem.list/oli-open",
        "workitem.list/planned",
        "workitem.list/planned-many",
        "workitem.list/planned-many-next",
        "workitem.list/researcher-findings",
        "workitem.list/researcher-findings-two",
        "workitem.list/smb-now",
        "workitem.list/smb-operations-planned",
        "workitem.list/smb-planned",
        "workitem.list/smb-roles-planned",
        "workitem.list/summer-planned",
        "workitem.list/summer-running",
        "workitem.list/tomas-now",
        "workitem.list/unknown-campaign",
        "workitem.update/dates",
        "workitem.update/finished-meanwhile",
    ];

    public static string Root => Path.Combine(RepositoryTree.Root(), "docs", "api", "fixtures");

    public static string PathOf(string @case) => Path.Combine(Root, @case.Replace('/', Path.DirectorySeparatorChar) + ".json");

    public static bool Updating => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    // Relaxed escaping so a mark like <data> reads as written; the files are documents, never wire bytes.
    private static readonly JsonSerializerOptions Pretty = new(JasonJson.Options) { WriteIndented = true, IndentSize = 2, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The file's text for an exchange: indented, LF, one final newline, so a diff reads as a document.</summary>
    public static string Text(JsonObject exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        return exchange.ToJsonString(Pretty).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>Compares, or writes under the variable, and says what to do otherwise.</summary>
    public static void Hold(string @case, JsonObject exchange)
    {
        Assert.True(Cases.Contains(@case, StringComparer.Ordinal), $"'{@case}' is recorded but not catalogued in ApiFixtures.Cases; add it there so the catalog test sees it.");
        var path = PathOf(@case);
        var text = Text(exchange);
        var existing = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
        if (existing == text)
        {
            return;
        }

        if (Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return;
        }

        var first = FirstDifference(existing, text);
        Assert.Fail($"The recorded exchange '{@case}' differs from {path}{Environment.NewLine}{first}{Environment.NewLine}"
            + $"If the runtime's answer changed on purpose, rerun this test with {UpdateVariable}=1 and commit the file; the UI's own checks then say what to update on its side.");
    }

    private static string FirstDifference(string? existing, string produced)
    {
        if (existing is null)
        {
            return "The file does not exist yet.";
        }

        var a = existing.Split('\n');
        var b = produced.Split('\n');
        for (var line = 0; line < Math.Max(a.Length, b.Length); line++)
        {
            var left = line < a.Length ? a[line] : "<end>";
            var right = line < b.Length ? b[line] : "<end>";
            if (left != right)
            {
                return string.Create(CultureInfo.InvariantCulture, $"line {line + 1}: committed '{left}', produced '{right}'");
            }
        }

        return "The texts differ only in line endings or a final newline.";
    }
}

/// <summary>
/// Replaces what varies between runs with stable words: ids by prefix in order of first appearance, digests in order of
/// first appearance, the data directory by a mark, the home the runtime was given by <c>~</c> — so a folder under the harness root
/// reads as it would on a person's machine, <c>~/Jason/projects/…</c> — and the values the clock does not control by
/// fixed ones.
/// </summary>
public sealed partial class FixtureScrubber(string dataDir, string? home = null)
{
    public const string FixedMoment = "2026-10-16T09:00:00.000Z";

    private static readonly string[] MomentKeys = ["started_at", "loaded_at", "activated_at", "checked_at"];

    private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _countByPrefix = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _digests = new(StringComparer.Ordinal);

    public JsonNode? Scrub(JsonNode? node, string? key = null)
    {
        switch (node)
        {
            case JsonObject obj:
                // An attempt's moments were seeded by the recording test, not read off a running clock; only the
                // runtime's own moments (when it started, when a plugin loaded) are volatile.
                var seeded = obj.ContainsKey("claimed_at");
                var scrubbed = new JsonObject();
                foreach (var (name, child) in obj)
                {
                    scrubbed[name] = seeded && MomentKeys.Contains(name, StringComparer.Ordinal) ? child?.DeepClone() : Scrub(child, name);
                }

                return scrubbed;
            case JsonArray array:
                return new JsonArray([.. array.Select(child => Scrub(child))]);
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(Text(text, key));
            case JsonValue value when key == "pid" && value.TryGetValue<int>(out _):
                return JsonValue.Create(4242);
            case JsonValue value when key is "port" or "configured_port" && value.TryGetValue<int>(out _):
                return JsonValue.Create(52766);
            default:
                return node?.DeepClone();
        }
    }

    private string Text(string text, string? key)
    {
        if (key is not null && MomentKeys.Contains(key, StringComparer.Ordinal))
        {
            return FixedMoment;
        }

        // The build stamp carries a commit hash, so it would change with every commit.
        if (key == "runtime_version")
        {
            return "0.0.0-recorded";
        }

        if (Ulid().IsMatch(text) && Ulid().Match(text).Length == text.Length)
        {
            return Id(text);
        }

        if (Digest().IsMatch(text))
        {
            // Not hex on purpose: the catalog test holds every file free of real digests. Numbered by first appearance, as
            // ids are, so two equal digests read as one word: a journal key is a digest wherever the runtime makes it one,
            // and the screens tell two lines that say one thing by their equal keys.
            if (!_digests.TryGetValue(text, out var word))
            {
                word = "sha256:recorded-" + (_digests.Count + 1).ToString(CultureInfo.InvariantCulture);
                _digests[text] = word;
            }

            return word;
        }

        // The home first: it is named after the data directory, so the data directory's mark would take its beginning.
        if (home is not null && text.Contains(home, StringComparison.Ordinal))
        {
            text = text.Replace(home, "~", StringComparison.Ordinal).Replace('\\', '/');
        }

        if (text.Contains(dataDir, StringComparison.Ordinal))
        {
            return text.Replace(dataDir, "<data>", StringComparison.Ordinal).Replace('\\', '/');
        }

        // An id inside a longer text, such as a URL or a message, is mapped the same way.
        return Ulid().Replace(text, match => Id(match.Value));
    }

    private string Id(string id)
    {
        if (_ids.TryGetValue(id, out var known))
        {
            return known;
        }

        var prefix = id[..id.IndexOf('_')];
        var n = _countByPrefix.GetValueOrDefault(prefix) + 1;
        _countByPrefix[prefix] = n;
        return _ids[id] = $"{prefix}_{n.ToString(CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex("\\b([a-z]{2,8})_[0-9A-HJKMNP-TV-Z]{26}\\b")]
    private static partial Regex Ulid();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$")]
    private static partial Regex Digest();
}
