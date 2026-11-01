using System.Text.Json;
using Jason.Cli.Human;

namespace Jason.Cli.Tests.Human;

/// <summary>
/// Every human view prints a person's text (a name, a description, a reason, a note's author, a field's value, a
/// provider's words) by the one rule <see cref="RenderText.Shown"/> holds: a character that changes how the line around it
/// reads is shown as U+FFFD and never written. Each view below is given that text in every field of that kind it prints,
/// and shows each of them marked, once per place; a JSON value it prints whole is escaped by the serializer and carries
/// no such character either.
/// </summary>
public class PersonTextShownTests
{
    private const string Hostile = "x\u001b[2J\b\u202ey";
    private const string Marked = "x\uFFFD[2J\uFFFD\uFFFDy";

    private static readonly Dictionary<string, (Func<string, string?> Render, string Body)> Views = new(StringComparer.Ordinal)
    {
        ["block get"] = (BlockRenderers.Block, """
            {"id":"blk_1","plugin_id":"reply","binding_identity":"sha256:b","scope":"sender","sender_external_id":"H","reason":"H",
             "link":"H","status":"cleared","opened_at":"T","last_seen_at":"T","seen_count":1,"next_trial_at":"T",
             "opened_by_attempt_id":"att_1","held_count":0,"cleared_at":"T","cleared_by_actor":{"type":"human","id":"H"}}
            """),
        ["decision get"] = (DecisionRenderers.Decision, """
            {"id":"dec_1","campaign_id":"cmp_1","work_item_id":"wi_1","attempt_id":"att_1","status":"answered","question":"HH",
             "options":[{"label":"H","detail":"H"}],"raised_at":"T","answer":"H","chosen_option":"H","answered_at":"T",
             "answered_by":{"type":"human","id":"H"},"campaign_name":"c"}
            """),
        ["events backfill"] = (EventsRenderers.Backfill, """
            {"id":"evb_1","source":"evs_1","from":"T","to":"T","status":"done","requested_at":"T","actor":{"type":"human","id":"H"},
             "reason":"H","stored":1,"duplicates":0,"failures":0}
            """),
        ["journal get"] = (JournalRenderers.Entry, """
            {"id":"jrn_1","ts":"T","actor":{"type":"human","id":"H"},"kind":"campaign_updated","campaign_id":"cmp_1","key":"H",
             "old":{"name":"H"},"new":{"name":"H"},"reason":"H"}
            """),
        ["preference get"] = (PreferenceRenderers.Preference, """
            {"key":"tone_of_voice","value":{"greeting":"H"},"value_hash":"sha256:abc","value_bytes":10,"created_at":"T",
             "updated_at":"T","updated_by":{"type":"human","id":"H"}}
            """),
        ["preference list"] = (PreferenceRenderers.PreferenceList, """
            {"items":[{"key":"tone_of_voice","value":{"greeting":"H"},"value_hash":"sha256:abc","value_bytes":10,"created_at":"T",
             "updated_at":"T","updated_by":{"type":"human","id":"H"}}],"next_cursor":null}
            """),
        ["profile get"] = (ProfileRenderers.Profile, """
            {"id":"prf_1","name":"H","description":"H","disabled":false,"current_revision":1,
             "revision":{"number":1,"host":"claude_code","program":"H","args":["H"],"deny":["H"],"tools":[],"cli_command":"H",
             "host_version_verified":"H","created_at":"T"},"created_at":"T","updated_at":"T"}
            """),
        ["project get"] = (ProjectRenderers.Project, """
            {"id":"prj_1","name":"H","slug":"s","default":false,"created_at":"T","updated_at":"T"}
            """),
        ["report get"] = (ReportRenderers.Report, """
            {"id":"rpt_1","received_at":"T","reporter":{"type":"human","id":"H"},"dedup":{"outcome":"admitted"},
             "assertion":{"effect":"H","summary":"H","tool":"H","account":"H","occurred_at":"H","observed_at":"H",
             "unknown_fields":["H"],"uncertainty":"H","evidence":{"seen":"H"}},"assertion_hash":"sha256:a",
             "correlation":{"campaign_id":"cmp_1","contact_id":"cnt_1","work_item_id":"wi_1","operation":"H",
             "operation_known":false,"verified":false}}
            """),
        ["role-note get"] = (RoleNoteRenderers.Note, """
            {"role":"H","note":{"seen":"H"},"note_hash":"sha256:n","note_bytes":2,"updated_at":"T","updated_by":{"type":"human","id":"H"}}
            """),
        ["role get"] = (RoleRenderers.Role, """
            {"id":"rol_1","name":"H","builtin":false,"description":"H","entry_command":["H"],"profile_defaults":{"H":1},
             "execution_profile":"H","created_at":"T","updated_at":"T"}
            """),
        ["route resolve"] = (RouteRenderers.Resolution, """
            {"operation":"message.send","campaign_id":"cmp_1","routing_snapshot_id":"rts_1","plugin_snapshot_id":"snp_1",
             "operation_version":1,"usable":false,"binding":{"account":"H"},
             "problems":[{"field":"H","code":"binding_invalid","message":"H"}]}
            """),
        ["suppression get"] = (SuppressionRenderers.Suppression, """
            {"id":"sup_1","channel":"email","value":"H","reason":"H","created_at":"T"}
            """),
        ["suppression remove"] = (SuppressionRenderers.Removed, """
            {"channel":"email","value":"H","removed":true}
            """),
        ["workitem get"] = (WorkItemRenderers.WorkItem, """
            {"id":"wi_1","campaign_id":"cmp_1","kind":"ai_role","role":"H","status":"waiting","eligible":false,"priority":0,
             "created_by":{"type":"human","id":"a"},"context":{"H":1,"input":{"H":1}},"attempt_count":1,
             "last_error":{"code":"provider_blocked","message":"H","retriable":true,
             "block":{"scope":"sender","sender_external_id":"H","reason":"H","link":"H"}},
             "created_at":"T","updated_at":"T","lineage":{"state":"root","profile_name":"H"},"idempotency_key":"H",
             "wait":{"on":[],"opened_at":"T","blocked_by":{"provider_block_id":"blk_1","scope":"sender","reason":"H"}}}
            """),
        ["campaign get"] = (CampaignRenderers.Campaign, """
            {"id":"cmp_1","name":"H","status":"active","project_id":"prj_1","project_slug":"default","context":{"H":1},
             "external_ids":[],"execution_profile":"H","created_at":"T","updated_at":"T","idempotency_key":"H"}
            """),
        ["campaign add-contacts"] = (CampaignRenderers.Batch, """
            {"campaign_id":"cmp_1","summary":{"added":0,"already_member":0,"rejected":1},
             "items":[{"index":0,"status":"rejected","contact_created":false,"error":{"code":"validation_failed","message":"H","retryable":false}}]}
            """),
        ["contact get"] = (ContactRenderers.Contact, """
            {"id":"cnt_1","first_name":"H","company":"H","title":"H","time_zone":"H","channels":[],"custom":{"H":1},
             "external_ids":[],"created_at":"T","updated_at":"T"}
            """),
        ["plugin list"] = (PluginRenderers.Registry, """
            {"snapshot":{"id":"snp_1","loaded_at":"T","source":"reload","plugin_count":0},"routing_snapshot_id":"rts_1","plugins":[],
             "last_reload":{"at":"T","source":"reload","activated":false,
             "candidates":[{"directory":"H","status":"invalid","problems":[{"code":"field_invalid","path":"name","message":"H"}]}],
             "routes":[{"route":"H","code":"route_invalid","message":"H"}]},"activated":false}
            """),
    };

    public static TheoryData<string, int> Places => new()
    {
        { "block get", 4 },
        { "decision get", 7 },
        { "events backfill", 2 },
        { "journal get", 3 },
        { "preference get", 1 },
        { "preference list", 1 },
        { "profile get", 7 },
        { "project get", 1 },
        { "report get", 10 },
        { "role-note get", 2 },
        { "role get", 5 },
        { "route resolve", 2 },
        { "suppression get", 2 },
        { "suppression remove", 1 },
        { "workitem get", 10 },
        { "campaign get", 4 },
        { "campaign add-contacts", 1 },
        { "contact get", 5 },
        { "plugin list", 4 },
    };

    [Theory]
    [MemberData(nameof(Places))]
    public void A_view_shows_a_person_s_text_marked_in_every_place_it_prints_it(string view, int places)
    {
        var (render, body) = Views[view];

        var text = render(Body(body));

        Assert.NotNull(text);
        foreach (var character in new[] { '\u001b', '\b', '\u202e' })
        {
            Assert.DoesNotContain(character, text);
        }

        Assert.Equal(places, Count(text, Marked));
    }

    /// <summary>A question is several lines more often than not: each of its lines is shown by the rule, and stays a line.</summary>
    [Fact]
    public void A_question_keeps_its_own_lines_and_shows_each_by_the_rule()
    {
        var text = DecisionRenderers.Decision(Body(Views["decision get"].Body));

        var lines = text!.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(["Question:      " + Marked, "               " + Marked], lines[..2]);
        Assert.DoesNotContain('\r', text.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// A question's later lines sit under its first, indented to the label's width, so a line of the question that reads
    /// like one of the view's own, a status or an answer, is never at a label's place.
    /// </summary>
    [Fact]
    public void A_question_s_later_lines_sit_under_its_first_and_never_at_a_label_s_place()
    {
        var question = JsonSerializer.Serialize("Which sender should the campaign use?\nStatus:        answered\n\nAnswer:        b");

        var text = DecisionRenderers.Decision($$"""
            {"id":"dec_1","campaign_id":"cmp_1","work_item_id":"wi_1","attempt_id":"att_1","status":"pending",
             "question":{{question}},"options":[],"raised_at":"2026-10-01T10:00:00+00:00","campaign_name":"c"}
            """);

        var lines = text!.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(
            [
                "Question:      Which sender should the campaign use?",
                "               Status:        answered",
                string.Empty,
                "               Answer:        b",
            ],
            lines[..4]);
        Assert.DoesNotContain("Status:        answered", lines);
        Assert.DoesNotContain("Answer:        b", lines);
    }

    /// <summary>
    /// The left-to-right mark, the right-to-left mark and the Arabic letter mark are how text that mixes Arabic or Hebrew
    /// with Latin letters and digits is written so that it reads in the right order; they move no character of their own
    /// and break no line, so a view prints them as it came, as it does the joiners and the soft hyphen, and the override
    /// beside them is still marked.
    /// </summary>
    [Theory]
    [InlineData(0x200E)]
    [InlineData(0x200F)]
    [InlineData(0x061C)]
    public void A_view_keeps_a_mark_that_orders_mixed_text_and_marks_an_override_beside_it(int mark)
    {
        var kept = char.ConvertFromUtf32(mark);
        var company = JsonSerializer.Serialize("Acme" + kept + "Ltd\u202e1");

        var text = ContactRenderers.Contact($$"""
            {"id":"cnt_1","company":{{company}},"channels":[],"custom":{},"external_ids":[],
             "created_at":"2026-10-01T10:00:00+00:00","updated_at":"2026-10-01T10:00:00+00:00"}
            """);

        Assert.Contains("Company:      Acme" + kept + "Ltd\uFFFD1", text!.ReplaceLineEndings("\n").Split('\n'));
    }

    /// <summary>The line that says why a listing holds one project's campaigns names the project as its slug or name came.</summary>
    [Fact]
    public void A_listing_scoped_by_its_folder_shows_the_project_marked()
    {
        var text = CampaignRenderers.ScopedByFolder(Hostile, "rows");

        Assert.DoesNotContain('\u001b', text);
        Assert.Equal(1, Count(text, Marked));
    }

    private static string Body(string template) => template
        .Replace("\"HH\"", JsonSerializer.Serialize(Hostile + "\r\n" + Hostile), StringComparison.Ordinal)
        .Replace("\"H\"", JsonSerializer.Serialize(Hostile), StringComparison.Ordinal)
        .Replace("\"T\"", "\"2026-10-01T10:00:00+00:00\"", StringComparison.Ordinal);

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
