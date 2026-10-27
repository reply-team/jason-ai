using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class ContactCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_sends_every_scalar_field()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "contact",
            "create",
            "--first-name",
            "Ada",
            "--last-name",
            "Lovelace",
            "--company",
            "Analytical Engines",
            "--title",
            "Mathematician",
            "--time-zone",
            "Europe/Kyiv");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactCreate,
            "{\"first_name\":\"Ada\",\"last_name\":\"Lovelace\",\"company\":\"Analytical Engines\",\"title\":\"Mathematician\",\"time_zone\":\"Europe/Kyiv\"}");
    }

    [Fact]
    public async Task Create_turns_emails_and_channels_into_entries()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "contact",
            "create",
            "--email",
            "ada@example.com",
            "--email",
            "ada@work.example.com",
            "--channel",
            "linkedin=https://www.linkedin.com/in/ada",
            "--custom",
            "{\"source\":\"a conference\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactCreate,
            "{\"channels\":["
                + "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"primary\":false},"
                + "{\"channel\":\"email\",\"value\":\"ada@work.example.com\",\"primary\":false},"
                + "{\"channel\":\"linkedin\",\"value\":\"https://www.linkedin.com/in/ada\",\"primary\":false}],"
                + "\"custom\":{\"source\":\"a conference\"}}");
    }

    [Fact]
    public async Task A_channel_without_a_value_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--channel", "linkedin");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--channel", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_channel_value_may_hold_further_equals_signs()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--channel", "portal=https://example.com/?id=7");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactCreate,
            "{\"channels\":[{\"channel\":\"portal\",\"value\":\"https://example.com/?id=7\",\"primary\":false}]}");
    }

    [Fact]
    public async Task A_custom_value_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--custom", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--custom", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"first_name\":\"from the file\",\"company\":\"Analytical Engines\"}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--file", file.Path, "--first-name", "Ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactCreate, "{\"first_name\":\"Ada\",\"company\":\"Analytical Engines\"}");
    }

    [Fact]
    public async Task Get_carries_the_positional_id_in_the_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "get", "cnt_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactGet, "{\"contact_id\":\"cnt_A\"}");
    }

    [Fact]
    public async Task Archive_carries_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "archive", "cnt_A", "--reason", "they asked to be forgotten", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactArchive,
            "{\"contact_id\":\"cnt_A\",\"reason\":\"they asked to be forgotten\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task List_passes_the_filters()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "list", "--channel", "email", "--value", "ada@example.com", "--query", "ada@", "--campaign", "cmp_A", "--order", "activity", "--include-archived", "--limit", "5", "--cursor", "Y250X0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactList,
            "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"query\":\"ada@\",\"campaign_id\":\"cmp_A\",\"order\":\"activity\",\"include_archived\":true,\"limit\":5,\"cursor\":\"Y250X0E\"}");
    }

    [Fact]
    public async Task List_leaves_out_the_archive_flag_when_it_was_not_asked_for()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "list", "--channel", "email");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactList, "{\"channel\":\"email\"}");
    }

    [Fact]
    public async Task Update_patches_the_named_fields_only()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--company", "Analytical Engines");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactUpdate, "{\"contact_id\":\"cnt_A\",\"company\":\"Analytical Engines\"}");
        Assert.DoesNotContain("first_name", cli.Sent!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_clears_the_fields_it_is_told_to_clear()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--clear", "title", "--clear", "time_zone");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactUpdate, "{\"contact_id\":\"cnt_A\",\"title\":null,\"time_zone\":null}");
    }

    [Fact]
    public async Task Clearing_a_field_that_is_not_patchable_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--clear", "channels");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--clear", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_replaces_the_whole_channel_list()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--email", "ada@example.com", "--custom", "{\"source\":\"a conference\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactUpdate,
            "{\"contact_id\":\"cnt_A\",\"channels\":[{\"channel\":\"email\",\"value\":\"ada@example.com\",\"primary\":false}],\"custom\":{\"source\":\"a conference\"}}");
    }

    [Fact]
    public async Task Human_mode_renders_one_contact_with_its_channels()
    {
        var contact = JsonSerializer.Serialize(
            new ContactDto(
                "cnt_A",
                "Ada",
                "Lovelace",
                "Analytical Engines",
                "Mathematician",
                "Europe/Kyiv",
                [new ChannelDto("email", "ada@example.com", "work", true, null)],
                new JsonObject { ["source"] = "a conference" },
                [],
                Moment,
                Moment,
                null,
                [new ContactCampaignDto("cmp_A", "LatAm pilot", MembershipState.Enrolled, Moment, Moment), new ContactCampaignDto("cmp_B", "Re-engage", MembershipState.Excluded, Moment, Moment)]),
            JasonJson.Options);
        using var cli = new CliRun(contact);

        var exit = await cli.RunAsync("contact", "get", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("cnt_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Analytical Engines", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Europe/Kyiv", cli.Text, StringComparison.Ordinal);
        Assert.Contains("CHANNEL", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("source", cli.Text, StringComparison.Ordinal);
        Assert.Contains("LatAm pilot", cli.Text, StringComparison.Ordinal);
        Assert.Contains("excluded", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_contact_listing()
    {
        var page = JsonSerializer.Serialize(
            new Page<ContactDto>(
                [
                    new ContactDto(
                        "cnt_A",
                        "Ada",
                        "Lovelace",
                        "Analytical Engines",
                        null,
                        "Europe/Kyiv",
                        [new ChannelDto("email", "ada@example.com", null, false, null)],
                        [],
                        [],
                        Moment,
                        Moment,
                        null,
                        LastActivity: new JournalEntryDto("jrn_A", Moment, new ActorRef(ActorType.System, "dispatcher"), "inbound_reply_observed", null, null, null, null, null, null, null)),
                ],
                "Y250X0E"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("contact", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("COMPANY", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("LAST", cli.Text, StringComparison.Ordinal);
        Assert.Contains("inbound_reply_observed", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: Y250X0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Threads_and_thread_send_the_person_and_print_the_conversation()
    {
        const string threads = """
            {"plugin_id":"reply","items":[{"external_id":"t_1","channel":"email","subject":"Re: Q3","preview":"Thursday?","last_activity_at":"2026-10-16T08:41:00.000Z","unread":true}],"has_more":false}
            """;
        using (var cli = new CliRun(threads))
        {
            var exit = await cli.RunAsync("contact", "threads", "cnt_A", "--plugin", "reply", "--limit", "5", "--offset", "10", "--human");

            Assert.Equal(ExitCodes.Success, exit);
            cli.AssertPosted(Operations.ContactThreads, "{\"contact_id\":\"cnt_A\",\"plugin_id\":\"reply\",\"limit\":5,\"offset\":10}");
            Assert.Contains("t_1", cli.Text, StringComparison.Ordinal);
            Assert.Contains("Re: Q3", cli.Text, StringComparison.Ordinal);
        }

        const string thread = """
            {"plugin_id":"reply","thread":{"external_id":"t_1","channel":"email","subject":"Re: Q3\u001b]0;owned\u0007"},"messages":[{"direction":"inbound","sent_at":"2026-10-16T08:41:00.000Z","from":"marta@example.com","to":["sdr@example.com"],"body":"<p>Thursday?</p>\n\u001b[2J\u009b31mred\tend"}],"has_more":false}
            """;
        using (var cli = new CliRun(thread))
        {
            var exit = await cli.RunAsync("contact", "thread", "cnt_A", "t_1", "--human");

            Assert.Equal(ExitCodes.Success, exit);
            cli.AssertPosted(Operations.ContactThread, "{\"contact_id\":\"cnt_A\",\"thread_id\":\"t_1\"}");
            Assert.Contains("marta@example.com", cli.Text, StringComparison.Ordinal);
            Assert.Contains("<p>Thursday?</p>", cli.Text, StringComparison.Ordinal);
            Assert.Contains("\uFFFD[2J\uFFFD31mred\tend", cli.Text, StringComparison.Ordinal);
            Assert.Contains("Re: Q3\uFFFD]0;owned\uFFFD", cli.Text, StringComparison.Ordinal);
            Assert.DoesNotContain('\u001b', cli.Text);
            Assert.DoesNotContain('\u0007', cli.Text);
            Assert.DoesNotContain('\u009b', cli.Text);
        }
    }

    /// <summary>
    /// A provider's text follows the rule the thread views hold for a person's text: a character that changes the
    /// line (a right-to-left override, a zero-width character, a line separator, a lone carriage return) is shown as
    /// U+FFFD and never runs, in the list's subject as in a thread's subject and body; a tab stays, and a body
    /// keeps its own line breaks.
    /// </summary>
    [Fact]
    public async Task Threads_and_thread_show_a_character_that_changes_the_line_as_a_replacement_mark()
    {
        const string threads = """
            {"plugin_id":"reply","items":[{"external_id":"t_1","channel":"email","subject":"Re:\u202eQ3\u200bnow\u2028next\tTabbed","last_activity_at":"2026-10-16T08:41:00.000Z","unread":false}],"has_more":false}
            """;
        using (var cli = new CliRun(threads))
        {
            await cli.RunAsync("contact", "threads", "cnt_A", "--human");

            Assert.Contains("Re:\uFFFDQ3\uFFFDnow\uFFFDnext\tTabbed", cli.Text, StringComparison.Ordinal);
            foreach (var character in new[] { '\u202e', '\u200b', '\u2028' })
            {
                Assert.DoesNotContain(character, cli.Text);
            }
        }

        const string thread = """
            {"plugin_id":"reply","thread":{"external_id":"t_1","channel":"email","subject":"Re:\u202eQ3"},"messages":[{"direction":"inbound","sent_at":"2026-10-16T08:41:00.000Z","from":"marta@example.com","to":["sdr@example.com"],"body":"Hi,\rStatus: approved\r\nBye\u2029done\ttab"}],"has_more":false}
            """;
        using (var cli = new CliRun(thread))
        {
            await cli.RunAsync("contact", "thread", "cnt_A", "t_1", "--human");

            foreach (var character in new[] { '\u202e', '\u2029' })
            {
                Assert.DoesNotContain(character, cli.Text);
            }

            var raw = cli.Text;
            for (var at = raw.IndexOf('\r', StringComparison.Ordinal); at >= 0; at = raw.IndexOf('\r', at + 1))
            {
                Assert.True(at + 1 < raw.Length && raw[at + 1] == '\n', "a CR that is not followed by an LF");
            }

            var lines = raw.ReplaceLineEndings("\n").Split('\n');
            Assert.Contains("Subject:      Re:\uFFFDQ3", lines);
            Assert.Contains("Hi,", lines);
            Assert.Contains("Status: approved", lines);
            Assert.Contains("Bye\uFFFDdone\ttab", lines);
        }
    }

    /// <summary>
    /// The joiners and the soft hyphen are ordinary in a message (an emoji sequence, a Persian word, text that came from
    /// HTML) and change no line, so a thread prints them as it always did; a zero-width space and a right-to-left override
    /// next to them are still marked.
    /// </summary>
    [Fact]
    public async Task Thread_keeps_the_joiners_and_the_soft_hyphen_and_marks_the_rest_of_the_format_characters()
    {
        const string thread = """
            {"plugin_id":"reply","thread":{"external_id":"t_1","channel":"email","subject":"x"},"messages":[{"direction":"inbound","sent_at":"2026-10-16T08:41:00.000Z","from":"marta@example.com","to":["sdr@example.com"],"body":"\ud83e\udd37\u200d\u2642\ufe0f mi\u200crav co\u00adop\u200b\u202ex"}],"has_more":false}
            """;
        using var cli = new CliRun(thread);

        await cli.RunAsync("contact", "thread", "cnt_A", "t_1", "--human");

        var lines = cli.Text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("\U0001F937\u200d\u2642\ufe0f mi\u200crav co\u00adop\uFFFD\uFFFDx", lines);
    }

    /// <summary>
    /// A message with no body, an empty body and a body that ends in a line break each keep the lines they always had: the
    /// first two print one empty line, and the third a trailing empty line, each between the message's header and the next.
    /// </summary>
    [Fact]
    public async Task Thread_prints_the_lines_of_a_missing_an_empty_and_a_trailing_break_body()
    {
        const string thread = """
            {"plugin_id":"reply","thread":{"external_id":"t_1","channel":"email","subject":"x"},"messages":[
              {"direction":"inbound","sent_at":"s1","from":"a1@example.com","to":["sdr@example.com"]},
              {"direction":"inbound","sent_at":"s2","from":"a2@example.com","to":["sdr@example.com"],"body":""},
              {"direction":"inbound","sent_at":"s3","from":"a3@example.com","to":["sdr@example.com"],"body":"last\n"}],"has_more":false}
            """;
        using var cli = new CliRun(thread);

        await cli.RunAsync("contact", "thread", "cnt_A", "t_1", "--human");

        var lines = cli.Text.ReplaceLineEndings("\n").Split('\n');
        var first = Array.FindIndex(lines, line => line.StartsWith("s1  a1@example.com", StringComparison.Ordinal));
        var second = Array.FindIndex(lines, line => line.StartsWith("s2  a2@example.com", StringComparison.Ordinal));
        var third = Array.FindIndex(lines, line => line.StartsWith("s3  a3@example.com", StringComparison.Ordinal));
        Assert.True(first > 0);
        Assert.Equal(first + 3, second);
        Assert.Equal(["", string.Empty], lines[(first + 1)..(first + 3)]);
        Assert.Equal(second + 3, third);
        Assert.Equal(["", string.Empty], lines[(second + 1)..(second + 3)]);
        Assert.Equal(["last", string.Empty], lines[(third + 1)..(third + 3)]);
    }

    /// <summary>
    /// A thread with no subject is listed by the start of its latest message, where line breaks are ordinary, so each run
    /// of them folds into one space in the one-line column, as a question's excerpt does; a character that changes the
    /// line is still marked, and a subject, which is one line, keeps the mark for a break.
    /// </summary>
    [Fact]
    public async Task Threads_fold_the_line_breaks_of_a_preview_into_one_space_and_mark_those_of_a_subject()
    {
        const string threads = """
            {"plugin_id":"reply","items":[
              {"external_id":"t_1","channel":"linkedin","preview":"Hi Ada,\r\n\r\nthanks\u2028\u2029again\nbye\u202e\tend","last_activity_at":"2026-10-16T08:41:00.000Z","unread":false},
              {"external_id":"t_2","channel":"email","subject":"one\ntwo","preview":"ignored","last_activity_at":"2026-10-16T08:41:00.000Z","unread":false}],"has_more":false}
            """;
        using var cli = new CliRun(threads);

        await cli.RunAsync("contact", "threads", "cnt_A", "--human");

        Assert.Contains("Hi Ada, thanks again bye\uFFFD\tend", cli.Text, StringComparison.Ordinal);
        Assert.Contains("one\uFFFDtwo", cli.Text, StringComparison.Ordinal);
        Assert.Equal(4, cli.Text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
