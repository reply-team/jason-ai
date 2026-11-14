using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The verbs a person decides through, from the side they are typed on: what is sent, and what is printed back
/// when a person asked to read rather than to parse.
/// </summary>
public class ApprovalCommandsTests
{
    private const string OneApproval = """
        {
          "id": "apr_01K52JR0000000000000000001",
          "work_item_id": "wi_01K52JR0000000000000000002",
          "campaign_id": "cmp_01K52JR0000000000000000003",
          "operation": "campaign.enroll",
          "operation_version": 1,
          "status": "pending",
          "reason": "approval_required",
          "subject_hash": "sha256:abc",
          "subject": {"operation": "campaign.enroll"},
          "preview": {
            "intent": "Put one person into a campaign at the provider.",
            "reach": {"value": "act", "conditional": true, "detail": "bookkeeping until the campaign is live"},
            "reversibility": {"value": "irreversible", "conditional": true, "detail": null},
            "cost": {"value": "metered", "conditional": true, "detail": null},
            "campaign": {"id": "cmp_01K52JR0000000000000000003", "name": "Autumn outreach"},
            "contact": {"id": "cnt_1", "name": "Ada", "channel": "email", "value": "ada@example.test"}
          },
          "plugin_id": "reply",
          "binding_identity": "sha256:account",
          "route_scope": "global_default",
          "plugin_snapshot_id": "snp_1",
          "routing_snapshot_id": "rts_1",
          "requested_at": "2026-09-14T10:00:00+00:00",
          "decided_at": null,
          "decided_by": null,
          "decision_reason": null
        }
        """;

    [Fact]
    public async Task List_asks_for_what_is_waiting_and_passes_the_filters_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("approval", "list", "--status", "rejected", "--work-item", "wi_7", "--limit", "5");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ApprovalList, "{\"status\":\"rejected\",\"work_item_id\":\"wi_7\",\"limit\":5}");
    }

    [Fact]
    public async Task List_sends_one_status_as_status_and_several_as_statuses()
    {
        using var one = new CliRun();
        await one.RunAsync("approval", "list", "--status", "rejected");
        one.AssertPosted(Operations.ApprovalList, "{\"status\":\"rejected\"}");

        using var several = new CliRun();
        await several.RunAsync("approval", "list", "--status", "approved", "--status", "rejected");
        several.AssertPosted(Operations.ApprovalList, "{\"statuses\":[\"approved\",\"rejected\"]}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body_and_lets_the_runtime_say_what_pending_means()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("approval", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ApprovalList, "{}");
    }

    [Fact]
    [Critical(Family.S2, "an approval names the person")]
    public async Task Approve_sends_the_decision_with_the_person_who_made_it()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("--actor", "human:ada@example.test", "approval", "approve", "apr_7", "--reason", "go ahead");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalApprove,
            "{\"approval_id\":\"apr_7\",\"reason\":\"go ahead\",\"actor\":{\"type\":\"human\",\"id\":\"ada@example.test\"}}");
    }

    /// <summary>
    /// "Approve and allow up to N more people": the approval of a search carries the allowance for its audience, its
    /// ceiling, cap per search and expiry, in the one request the decision is. A cap or an expiry with no ceiling to go
    /// with is refused before anything is sent, so the approval never goes out without the allowance its writer meant.
    /// </summary>
    [Fact]
    public async Task Approve_with_allow_sends_the_allowance_with_the_decision()
    {
        using var cli = new CliRun(OneApproval);
        using var capOnly = new CliRun(OneApproval);

        var exit = await cli.RunAsync(
            "--actor", "human:ada", "approval", "approve", "apr_7", "--allow", "300", "--allow-per-search", "100",
            "--allow-expires-at", "2026-12-01T00:00:00Z");
        var refused = await capOnly.RunAsync("--actor", "human:ada", "approval", "approve", "apr_7", "--allow-per-search", "100");

        Assert.Equal((ExitCodes.Usage, (string?)null), (refused, capOnly.Sent));
        Assert.Contains("--allow", capOnly.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalApprove,
            "{\"approval_id\":\"apr_7\",\"allow\":{\"max_people\":300,\"max_per_search\":100,\"expires_at\":\"2026-12-01T00:00:00Z\"},"
            + "\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Reject_sends_the_decision_with_the_person_who_made_it()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("--actor", "human:ada@example.test", "approval", "reject", "apr_7", "--reason", "not this quarter");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalReject,
            "{\"approval_id\":\"apr_7\",\"reason\":\"not this quarter\",\"actor\":{\"type\":\"human\",\"id\":\"ada@example.test\"}}");
    }

    /// <summary>
    /// What a person reads before deciding, in the order the decision is made in: the effect first, then who it
    /// reaches and through which account, and the identifiers last.
    /// </summary>
    /// <summary>
    /// What the decision would do, before anything about identifiers. The values an operation highlights and the
    /// allowance belong to a search's card alone: an enrollment's prints neither, its highlights already in the lines above.
    /// </summary>
    [Fact]
    public async Task Get_renders_what_the_decision_would_do_before_anything_about_identifiers()
    {
        using var cli = new CliRun(OneApproval.Replace(
            "\"contact\": {\"id\": \"cnt_1\"",
            "\"highlights\": {\"/args/message/body\": \"Hello Ada,\\nsee you\"},\n    \"contact\": {\"id\": \"cnt_1\"",
            StringComparison.Ordinal));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text;
        Assert.Contains("Operation:     campaign.enroll", text, StringComparison.Ordinal);
        Assert.Contains("Intent:        Put one person into a campaign at the provider.", text, StringComparison.Ordinal);
        Assert.Contains("Reach:         act (conditional)", text, StringComparison.Ordinal);
        Assert.Contains("Undo:          irreversible (conditional)", text, StringComparison.Ordinal);
        Assert.Contains("Contact:       Ada (cnt_1) | email ada@example.test", text, StringComparison.Ordinal);
        Assert.Contains("Account:       sha256:account", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Intent:", StringComparison.Ordinal) < text.IndexOf("Id:", StringComparison.Ordinal));
        Assert.DoesNotContain("Body:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Allowance:", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A contact known only by an address has no name to set its id apart from, so the id stands alone where the name
    /// would be, rather than in parentheses behind a space — whether the preview leaves the name out or leaves it empty.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task Get_of_an_address_only_contact_names_it_by_its_id_without_a_leading_space(string name)
    {
        using var cli = new CliRun(OneApproval.Replace("\"name\": \"Ada\"", $"\"name\": {name}", StringComparison.Ordinal));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var contact = Assert.Single(cli.Text.ReplaceLineEndings("\n").Split('\n'), line => line.StartsWith("Contact:", StringComparison.Ordinal));
        Assert.Equal("Contact:       cnt_1 | email ada@example.test", contact);
    }

    /// <summary>
    /// Where the operation declares a summary, it is the first thing a person reads: one line saying what would be
    /// done, to whom and how, above the properties it is drawn from.
    /// </summary>
    [Fact]
    public async Task Get_leads_with_the_summary_when_the_operation_declares_one()
    {
        var summarised = OneApproval.Replace(
            "\"preview\": {",
            "\"preview\": {\"summary\": \"Enroll Ada — (ada@example.test) in campaign Autumn outreach at the provider.\", \"highlights\": {\"/campaign/name\": \"Autumn outreach\"},",
            StringComparison.Ordinal);
        using var cli = new CliRun(summarised);

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("Summary:       Enroll Ada — (ada@example.test) in campaign Autumn outreach at the provider.", lines[0]);
        Assert.StartsWith("Operation:", lines[1], StringComparison.Ordinal);
    }

    /// <summary>A preview stored without a summary — every operation that declares none — prints as it always did.</summary>
    [Fact]
    public async Task Get_of_a_preview_without_a_summary_prints_as_it_always_did()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.StartsWith("Operation:     campaign.enroll", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary:", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the message is a template, the approval holds one reading of it, and a person approving a template
    /// reads that reading: the subject, the body with its lines, what the runtime could not fill, and the one line
    /// that says what is being approved. It sits after what the decision would do and before the identifiers.
    /// </summary>
    [Fact]
    public async Task Get_prints_the_example_of_a_template_after_the_properties_and_before_the_identifiers()
    {
        using var cli = new CliRun(WithExample("""{"subject": "Autumn outreach, Ada", "body": "Hi Ada,\n\n  thanks <3 {{x}}\n[sender.first_name]", "unfilled": ["contact.custom.team", "sender.first_name"]}"""));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text.ReplaceLineEndings("\n");
        Assert.Contains(
            "Example:       one reading of the template, as the runtime renders it\n"
            + "  Subject:     Autumn outreach, Ada\n"
            + "  Body:\n"
            + "    Hi Ada,\n"
            + "\n"
            + "      thanks <3 {{x}}\n"
            + "    [sender.first_name]\n"
            + "  Unfilled:    contact.custom.team, sender.first_name\n"
            + "  You approve the template as written; the example is the runtime's rendering, not the provider's, which may differ.\n",
            text,
            StringComparison.Ordinal);
        Assert.True(text.IndexOf("Contact:", StringComparison.Ordinal) < text.IndexOf("Example:", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Example:", StringComparison.Ordinal) < text.IndexOf("Work item:", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Example:", StringComparison.Ordinal) < text.IndexOf("Id:", StringComparison.Ordinal));
    }

    /// <summary>
    /// The block sits between the account and the lifecycle lines: pinned from the account through the status, so
    /// moving it after the note or before the account turns this red.
    /// </summary>
    [Fact]
    public async Task Get_places_the_example_between_the_account_and_the_status()
    {
        using var cli = new CliRun(WithExample("""{"subject": "Autumn outreach, Ada", "body": "Hi Ada", "unfilled": []}"""));

        await cli.RunAsync("approval", "get", "apr_1", "--human");

        var text = cli.Text.ReplaceLineEndings("\n");
        Assert.Contains(
            "Account:       sha256:account\n"
            + "Example:       one reading of the template, as the runtime renders it\n"
            + "  Subject:     Autumn outreach, Ada\n"
            + "  Body:\n"
            + "    Hi Ada\n"
            + "  You approve the template as written; the example is the runtime's rendering, not the provider's, which may differ.\n"
            + "Status:        pending\n",
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The example is a message's text, written by whoever wrote the template and the contact: a character that
    /// changes how the line around it reads (an escape sequence, backspaces, a right-to-left override, a line
    /// separator) is shown as U+FFFD and never runs, and the subject, one line by contract, never breaks.
    /// </summary>
    [Fact]
    public async Task Get_shows_a_character_that_changes_the_line_as_a_replacement_mark()
    {
        var body = "Hi Ada,\\u001b[2J done\\n\\b\\b\\b\\b\\b\\b\\bStatus:        approved\\n\\u202eevil\\u2028next\\u2029more\\u0085end\\fform\\tTabbed";
        var subject = "Hello\\u001b[31m Ada\\nsecond\\rthird";
        using var cli = new CliRun(WithExample($$"""{"subject": "{{subject}}", "body": "{{body}}", "unfilled": []}"""));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text;
        foreach (var character in new[] { '\u001b', '\b', '\u202e', '\u2028', '\u2029', '\u0085', '\f' })
        {
            Assert.DoesNotContain(character, text);
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("  Subject:     Hello\uFFFD[31m Ada\uFFFDsecond\uFFFDthird", lines);
        var start = Array.IndexOf(lines, "  Body:") + 1;
        var end = Array.FindIndex(lines, start, line => line.StartsWith("  You approve", StringComparison.Ordinal));
        Assert.True(start > 0 && end > start);
        Assert.All(lines[start..end], line => Assert.True(line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal), line));
        Assert.Contains("    Hi Ada,\uFFFD[2J done", lines);
        Assert.Contains("    \uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFDStatus:        approved", lines);
        Assert.Contains("    \uFFFDevil\uFFFDnext\uFFFDmore\uFFFDend\uFFFDform\tTabbed", lines);
    }

    /// <summary>
    /// A carriage return alone is a line break in a message, and it must not reach the terminal as one: the parts
    /// after it are lines of their own, indented like the rest, and no CR is printed but as half of a CR LF.
    /// </summary>
    [Fact]
    public async Task Get_breaks_a_lone_carriage_return_in_the_body_into_indented_lines()
    {
        using var cli = new CliRun(WithExample("""{"subject": "Autumn outreach, Ada", "body": "Hi Ada,\rStatus:        approved\r\nBye", "unfilled": []}"""));

        await cli.RunAsync("approval", "get", "apr_1", "--human");

        var raw = cli.Text;
        for (var at = raw.IndexOf('\r', StringComparison.Ordinal); at >= 0; at = raw.IndexOf('\r', at + 1))
        {
            Assert.True(at + 1 < raw.Length && raw[at + 1] == '\n', "a CR that is not followed by an LF");
        }

        var lines = raw.ReplaceLineEndings("\n").Split('\n');
        var start = Array.IndexOf(lines, "  Body:");
        Assert.Equal(["    Hi Ada,", "    Status:        approved", "    Bye"], lines[(start + 1)..(start + 4)]);
    }

    [Fact]
    public async Task Get_says_so_when_the_rendered_subject_is_empty()
    {
        using var cli = new CliRun(WithExample("""{"subject": "", "body": "Hi Ada", "unfilled": []}"""));

        await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Contains("  Subject:     (empty)", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_of_an_example_with_nothing_unfilled_prints_no_unfilled_line()
    {
        using var cli = new CliRun(WithExample("""{"subject": "Autumn outreach, Ada", "body": "Hi Ada", "unfilled": []}"""));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Example:", cli.Text, StringComparison.Ordinal);
        Assert.Contains("    Hi Ada", cli.Text, StringComparison.Ordinal);
        Assert.Contains("You approve the template as written", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unfilled:", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_of_a_preview_without_an_example_prints_no_example()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Example:", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unfilled:", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("You approve the template", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>The default output is the response as it came: the example is in it, untouched, and nothing is rendered.</summary>
    [Fact]
    public async Task Get_without_human_prints_the_response_as_it_came_example_included()
    {
        var body = WithExample("""{"subject": "Autumn outreach, Ada", "body": "Hi Ada,\n<b>x</b>", "unfilled": ["sender.first_name"]}""");
        using var cli = new CliRun(body);

        var exit = await cli.RunAsync("approval", "get", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var printed = System.Text.Json.Nodes.JsonNode.Parse(cli.Text)!;
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(body), printed));
        Assert.DoesNotContain("Example:", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hash of what is approved is not a subject in the sense a message has one, and the example above it prints
    /// the message's own subject: the one unindented label that says what the hash is cannot read as the other, so
    /// the only <c>Subject:</c> in the view is the example's, and it is indented under the example.
    /// </summary>
    [Fact]
    public async Task Get_labels_the_hash_of_what_is_approved_as_a_content_hash_and_never_as_a_subject()
    {
        using var cli = new CliRun(WithExample("""{"subject": "Autumn outreach, Ada", "body": "Hi Ada", "unfilled": []}"""));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("Content hash:  sha256:abc", lines);
        Assert.Contains("  Subject:     Autumn outreach, Ada", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("Subject", StringComparison.Ordinal));
        Assert.True(Array.IndexOf(lines, "Content hash:  sha256:abc") < Array.FindIndex(lines, line => line.StartsWith("Id:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A campaign's name and a contact's name are refused a control character when they are written, but one written
    /// before that rule, or one from a provider, may still hold one, so the lines that print them follow the rule the
    /// example does: a character that changes how the line reads (an escape sequence, a backspace, a right-to-left
    /// override) is shown as U+FFFD and never runs, and a tab stays.
    /// </summary>
    [Fact]
    public async Task Get_shows_a_character_that_changes_the_line_in_a_campaign_name_and_a_contact_name_as_a_replacement_mark()
    {
        var hostile = System.Text.Json.Nodes.JsonNode.Parse(OneApproval)!;
        hostile["preview"]!["campaign"]!["name"] = "Autumn\u001b[2J\b\b\u202eoutreach\tTabbed";
        hostile["preview"]!["contact"]!["name"] = "Ada\u001b]0;owned\u0007\u202eLovelace\tTabbed";
        using var cli = new CliRun(hostile.ToJsonString());

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text;
        foreach (var character in new[] { '\u001b', '\b', '\u0007', '\u202e' })
        {
            Assert.DoesNotContain(character, text);
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("Campaign:      Autumn\uFFFD[2J\uFFFD\uFFFD\uFFFDoutreach\tTabbed (cmp_01K52JR0000000000000000003)", lines);
        Assert.Contains("Contact:       Ada\uFFFD]0;owned\uFFFD\uFFFDLovelace\tTabbed (cnt_1) | email ada@example.test", lines);
    }

    /// <summary>
    /// The reason a decision was given and the id of whoever decided are a person's words, held only to a length when
    /// they are written, so the lines that print them follow the same rule as the names above them: a character that
    /// changes how the line reads is shown as U+FFFD and never rewrites the lines before it, and a tab stays.
    /// </summary>
    [Fact]
    public async Task Get_shows_a_character_that_changes_the_line_in_a_decision_note_and_a_decider_as_a_replacement_mark()
    {
        var decided = System.Text.Json.Nodes.JsonNode.Parse(OneApproval)!;
        decided["status"] = "rejected";
        decided["decided_at"] = "2026-09-14T11:00:00+00:00";
        decided["decided_by"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "human", ["id"] = "ada\u001b]0;t\u0007\b\u202ex\ttab" };
        decided["decision_reason"] = "no\u001b[2J\b\b\u202ereason\tkept";
        using var cli = new CliRun(decided.ToJsonString());

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text;
        foreach (var character in new[] { '\u001b', '\b', '\u0007', '\u202e' })
        {
            Assert.DoesNotContain(character, text);
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("Decided by:    human:ada\uFFFD]0;t\uFFFD\uFFFD\uFFFDx\ttab", lines);
        Assert.Contains("Note:          no\uFFFD[2J\uFFFD\uFFFD\uFFFDreason\tkept", lines);
    }

    /// <summary>
    /// Three format characters are ordinary text: the zero-width joiner and non-joiner that emoji sequences and Persian and
    /// Indic spelling are written with, and the soft hyphen that HTML-made text carries. None of them moves a character or
    /// breaks a line, so a person's name prints them as it came, while a right-to-left override and a zero-width space,
    /// which do not belong to any spelling, stay marked.
    /// </summary>
    [Fact]
    public async Task Get_keeps_the_joiners_and_the_soft_hyphen_in_a_name_and_marks_the_rest_of_the_format_characters()
    {
        var named = System.Text.Json.Nodes.JsonNode.Parse(OneApproval)!;
        named["preview"]!["contact"]!["name"] = "Ada\u200c\u200d\u00adLovelace\u200b\u202ex";
        using var cli = new CliRun(named.ToJsonString());

        await cli.RunAsync("approval", "get", "apr_1", "--human");

        var lines = cli.Text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("Contact:       Ada\u200c\u200d\u00adLovelace\uFFFD\uFFFDx (cnt_1) | email ada@example.test", lines);
    }

    /// <summary>
    /// The installation's open question is said on the approval a person reads, while it is open, naming the keys; once
    /// both modes are chosen the line is gone.
    /// </summary>
    [Theory]
    [InlineData("[\"send_mode.cold\",\"send_mode.warm\"]", "Send modes:    not chosen yet: send_mode.cold, send_mode.warm; until a person sets them, every send waits for approval")]
    [InlineData("[\"send_mode.warm\"]", "Send modes:    not chosen yet: send_mode.warm; until a person sets them, every send waits for approval")]
    [InlineData("[]", null)]
    public async Task Get_says_which_send_modes_nobody_has_chosen_while_any_is_open(string unset, string? said)
    {
        using var cli = new CliRun(OneApproval.Replace("\"preview\": {", $"\"send_modes_unset\": {unset}, \"preview\": {{", StringComparison.Ordinal));

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.ReplaceLineEndings("\n").Split('\n');
        if (said is null)
        {
            Assert.DoesNotContain(lines, line => line.StartsWith("Send modes:", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(said, lines);
        }
    }

    private static string WithExample(string example) =>
        OneApproval.Replace("\"preview\": {", $"\"preview\": {{\"example\": {example},", StringComparison.Ordinal);

    [Fact]
    public async Task List_renders_a_table_of_what_is_waiting()
    {
        using var cli = new CliRun($$"""{"items": [{{OneApproval}}], "next_cursor": null}""");

        var exit = await cli.RunAsync("approval", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("REQUESTED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("campaign.enroll", cli.Text, StringComparison.Ordinal);
        Assert.Contains("pending", cli.Text, StringComparison.Ordinal);
    }

    private const string OneStanding = """
        {
          "campaign_id": "cmp_01K52JR0000000000000000003",
          "category": "interested",
          "operations": ["conversation.reply", "message.send"],
          "expires_at": "2026-12-01T00:00:00+00:00",
          "max_releases": 20,
          "released": 3,
          "in_force": true,
          "set_by": {"type": "human", "id": "ada"},
          "set_at": "2026-09-14T10:00:00+00:00",
          "line_id": "jrn_01K52JR0000000000000000009"
        }
        """;

    private const string OneAllowance = """
        {
          "campaign_id": "cmp_01K52JR0000000000000000003",
          "category": null,
          "operations": ["audience.search"],
          "audience": {"id": "aud_01K52JR0000000000000000004", "number": 7},
          "expires_at": null,
          "max_releases": null,
          "max_people": 500,
          "max_per_search": 100,
          "released": 2,
          "people": 60,
          "in_force": true,
          "set_by": {"type": "human", "id": "ada"},
          "set_at": "2026-09-14T10:00:00+00:00",
          "line_id": "jrn_01K52JR0000000000000000010"
        }
        """;

    /// <summary>A search waiting for a person, for the campaign's audience 7, over the allowance the campaign holds.</summary>
    private const string WaitingSearch = """
        {
          "id": "apr_01K52JR0000000000000000005",
          "work_item_id": "wi_01K52JR0000000000000000006",
          "campaign_id": "cmp_01K52JR0000000000000000003",
          "operation": "audience.search",
          "operation_version": 1,
          "status": "pending",
          "reason": "approval_required",
          "subject_hash": "sha256:abc",
          "subject": {"operation": "audience.search"},
          "preview": {
            "summary": "Search the provider for up to 100 people matching the definition below, for Jason's campaign Autumn outreach: a metered search, charged per person found.",
            "highlights": {
              "/args/count": 100,
              "/definition": {"industries": [{"value": "Logistics", "type": "include"}, {"value": "Software", "type": "exclude"}], "company_sizes": ["51-200"]},
              "/args/audience": 7,
              "/args/people_per_company": null,
              "/args/include_without_email": null,
              "/args/find_phones": null
            },
            "standing_approval": {"standing": "jrn_1", "audience": {"id": "aud_7", "number": 7}, "expires_at": null, "max_people": 500, "max_per_search": null, "people": 480, "count": 100, "why": "ceiling_reached"}
          },
          "plugin_id": "reply",
          "binding_identity": "sha256:account",
          "route_scope": "global_default",
          "plugin_snapshot_id": "snp_1",
          "routing_snapshot_id": "rts_1",
          "requested_at": "2026-09-14T10:00:00+00:00",
          "decided_at": null,
          "decided_by": null,
          "decision_reason": null
        }
        """;

    /// <summary>
    /// A person deciding on a search's spend from the terminal reads what an agent reads: the definition laid out by key,
    /// the count beside it, and why the campaign's allowance did not let the search go, with its numbers.
    /// </summary>
    [Fact]
    public async Task Get_of_a_waiting_search_lays_out_its_definition_and_why_the_allowance_did_not_cover_it()
    {
        using var cli = new CliRun(WaitingSearch);

        var exit = await cli.RunAsync("approval", "get", "apr_01K52JR0000000000000000005", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text.ReplaceLineEndings("\n");
        Assert.Contains("Count:", text, StringComparison.Ordinal);
        Assert.Contains("  industries: Logistics (include), Software (exclude)\n", text, StringComparison.Ordinal);
        Assert.Contains("  company_sizes: 51-200\n", text, StringComparison.Ordinal);
        Assert.Contains("Allowance:", text, StringComparison.Ordinal);
        Assert.Contains("ceiling_reached", text, StringComparison.Ordinal);
        Assert.Contains("480 of 500 people", text, StringComparison.Ordinal);
        Assert.Contains("this search asks 100", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Approving a search moves the campaign's allowance in the same act, so under <c>--human</c> the answer says where the
    /// allowance stands after it: the one <c>--allow</c> set, with its ceiling, or none in force where approving another
    /// audience's search took it back; and where the campaign's standing approvals could not be read, that it could not,
    /// never that none is in force.
    /// </summary>
    [Theory]
    [InlineData("allowed", "The campaign's allowance: audience 7, 0 of 500 people asked for")]
    [InlineData("taken back", "The campaign's allowance: none in force")]
    [InlineData("unreadable", "The campaign's allowance could not be read: jason approval list-standing cmp_01K52JR0000000000000000003")]
    public async Task Approving_a_search_says_where_the_campaign_s_allowance_stands_after_it(string after, string said)
    {
        var approved = WaitingSearch.Replace("\"status\": \"pending\"", "\"status\": \"approved\"", StringComparison.Ordinal);
        var standing = after == "allowed"
            ? "{\"items\":[" + OneAllowance.Replace("\"people\": 60", "\"people\": 0", StringComparison.Ordinal) + "],\"next_cursor\":null}"
            : "{\"items\":[" + OneAllowance.Replace("\"in_force\": true", "\"in_force\": false", StringComparison.Ordinal) + "],\"next_cursor\":null}";
        using var cli = new Folders.FolderRun(null, (operation, _) => operation != Operations.ApprovalListStanding
            ? (System.Net.HttpStatusCode.OK, approved)
            : after == "unreadable"
                ? (System.Net.HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"internal_error\",\"message\":\"failed\",\"retryable\":true}}")
                : (System.Net.HttpStatusCode.OK, standing));
        string[] allow = after == "allowed" ? ["--allow", "500"] : [];

        var exit = await cli.RunAsync(["--actor", "human:ada", "approval", "approve", "apr_01K52JR0000000000000000005", .. allow, "--human"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operations.ApprovalApprove, Operations.ApprovalListStanding], cli.Operations);
        Assert.Contains(said, cli.Text, StringComparison.Ordinal);
    }

    /// <summary>The allowance is set by its operation, the audience's number and the ceiling in people, under the person who gives it.</summary>
    [Fact]
    public async Task Set_standing_sends_an_allowance_by_its_audience_and_ceiling_in_people()
    {
        using var cli = new CliRun(OneAllowance);

        var exit = await cli.RunAsync(
            "--actor", "human:ada", "approval", "set-standing", "cmp_7", "--operation", "audience.search", "--audience", "2",
            "--max-people", "500", "--max-per-search", "100");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalSetStanding,
            "{\"campaign_id\":\"cmp_7\",\"operations\":[\"audience.search\"],\"audience\":2,\"max_people\":500,\"max_per_search\":100,"
            + "\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>A standing approval is set with what it covers and its limits, under the person who gives it.</summary>
    [Fact]
    public async Task Set_standing_sends_the_category_its_cover_and_limits_with_the_person()
    {
        using var cli = new CliRun(OneStanding);

        var exit = await cli.RunAsync(
            "--actor", "human:ada", "approval", "set-standing", "cmp_7", "--category", "interested",
            "--operation", "conversation.reply", "--operation", "message.send",
            "--expires-at", "2026-12-01T00:00:00Z", "--max-releases", "20", "--reason", "the interested may hear back at once");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalSetStanding,
            "{\"campaign_id\":\"cmp_7\",\"category\":\"interested\",\"operations\":[\"conversation.reply\",\"message.send\"],"
            + "\"expires_at\":\"2026-12-01T00:00:00Z\",\"max_releases\":20,\"reason\":\"the interested may hear back at once\","
            + "\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>What is left out is not sent: the runtime's own defaults apply, a reply alone and no limit.</summary>
    [Fact]
    public async Task Set_standing_leaves_out_what_was_not_named()
    {
        using var cli = new CliRun(OneStanding);

        var exit = await cli.RunAsync("--actor", "human:ada", "approval", "set-standing", "cmp_7", "--category", "interested");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalSetStanding,
            "{\"campaign_id\":\"cmp_7\",\"category\":\"interested\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>A standing approval is taken back by its category, and the allowance by its operation, under the person.</summary>
    [Fact]
    public async Task Unset_standing_sends_the_category_or_the_allowances_operation_with_the_person()
    {
        using var cli = new CliRun(OneStanding);
        using var allowance = new CliRun(OneAllowance);

        var exit = await cli.RunAsync("--actor", "human:ada", "approval", "unset-standing", "cmp_7", "--category", "interested");
        var retired = await allowance.RunAsync("--actor", "human:ada", "approval", "unset-standing", "cmp_7", "--operation", "audience.search");

        Assert.Equal((ExitCodes.Success, ExitCodes.Success), (exit, retired));
        cli.AssertPosted(
            Operations.ApprovalUnsetStanding,
            "{\"campaign_id\":\"cmp_7\",\"category\":\"interested\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
        allowance.AssertPosted(
            Operations.ApprovalUnsetStanding,
            "{\"campaign_id\":\"cmp_7\",\"operation\":\"audience.search\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>
    /// A standing approval names what it is for, a category of verdict or the allowance's operation, and never both: with
    /// neither, or a category beside <c>--operation audience.search</c>, setting or taking one back is a usage error, exit 2,
    /// refused before the runtime is asked, as a missing required option is.
    /// </summary>
    [Theory]
    [InlineData("set-standing")]
    [InlineData("unset-standing")]
    public async Task A_standing_approval_names_its_category_or_the_allowance_and_never_both(string verb)
    {
        using var neither = new CliRun(OneStanding);
        using var both = new CliRun(OneStanding);

        var withNeither = await neither.RunAsync("--actor", "human:ada", "approval", verb, "cmp_7");
        var withBoth = await both.RunAsync("--actor", "human:ada", "approval", verb, "cmp_7", "--category", "interested", "--operation", "audience.search");

        Assert.Equal((ExitCodes.Usage, ExitCodes.Usage), (withNeither, withBoth));
        Assert.Equal(((string?)null, (string?)null), (neither.Sent, both.Sent));
        Assert.Contains("--category", neither.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("--category", both.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The listing, for a person: each category, what it covers, whether it is in force and how much it let go; and the
    /// allowance, by its audience, with the people its searches asked for against its ceiling.
    /// </summary>
    [Fact]
    public async Task List_standing_renders_one_category_to_a_row()
    {
        using var cli = new CliRun("{\"items\":[" + OneAllowance + "," + OneStanding + "],\"next_cursor\":null}");

        var exit = await cli.RunAsync("approval", "list-standing", "cmp_7", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ApprovalListStanding, "{\"campaign_id\":\"cmp_7\"}");
        var row = Assert.Single(cli.Text.Split('\n'), line => line.StartsWith("interested", StringComparison.Ordinal));
        Assert.Contains("conversation.reply, message.send", row, StringComparison.Ordinal);
        Assert.Contains("3 of 20", row, StringComparison.Ordinal);
        Assert.Contains("human:ada", row, StringComparison.Ordinal);
        var allowance = Assert.Single(cli.Text.Split('\n'), line => line.Contains("audience.search", StringComparison.Ordinal));
        Assert.Contains(" 7 ", allowance, StringComparison.Ordinal);
        Assert.Contains("60 of 500", allowance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_standing_renders_the_record_for_a_person()
    {
        using var cli = new CliRun(OneStanding);

        var exit = await cli.RunAsync("--actor", "human:ada", "approval", "set-standing", "cmp_7", "--category", "interested", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Category:      interested", cli.Text, StringComparison.Ordinal);
        Assert.Contains("In force:      yes", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Let go:        3 of 20", cli.Text, StringComparison.Ordinal);

        using var allowance = new CliRun(OneAllowance);
        await allowance.RunAsync("--actor", "human:ada", "approval", "set-standing", "cmp_7", "--operation", "audience.search", "--human");
        Assert.Contains("Covers:        audience.search", allowance.Text, StringComparison.Ordinal);
        Assert.Contains("Audience:      7 (aud_01K52JR0000000000000000004)", allowance.Text, StringComparison.Ordinal);
        Assert.Contains("People:        60 of 500", allowance.Text, StringComparison.Ordinal);
        Assert.Contains("Per search:    100", allowance.Text, StringComparison.Ordinal);
        Assert.Contains("Let go:        2", allowance.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Category:", allowance.Text, StringComparison.Ordinal);
    }
}
