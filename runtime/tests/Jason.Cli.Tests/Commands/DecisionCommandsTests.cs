using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The verbs a question travels through, from the side they are typed on: what a role sends when it asks,
/// what a person sends when they answer, and what is printed back when somebody asked to read rather than to
/// parse.
/// </summary>
public class DecisionCommandsTests
{
    private const string OneDecision = """
        {
          "id": "dec_01K52JR0000000000000000001",
          "campaign_id": "cmp_01K52JR0000000000000000003",
          "work_item_id": "wi_01K52JR0000000000000000002",
          "attempt_id": "att_01K52JR0000000000000000004",
          "status": "answered",
          "question": "Pause the LatAm sequence until the domain is warm?",
          "options": [
            {"label": "pause", "detail": "Stop sending until the warm-up finishes."},
            {"label": "continue", "detail": null}
          ],
          "references": [
            {"kind": "work_item", "id": "wi_01K52JR0000000000000000002"},
            {"kind": "journal_entry", "id": "jrn_01K52JR0000000000000000009"}
          ],
          "raised_at": "2026-09-19T09:00:00+00:00",
          "answer": "pause it",
          "chosen_option": "pause",
          "answered_at": "2026-09-19T10:00:00+00:00",
          "answered_by": {"type": "human", "id": "ada"}
        }
        """;
    [Fact]
    public async Task List_asks_for_what_is_waiting_and_passes_the_filters_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "list", "--status", "answered", "--campaign", "cmp_7", "--work-item", "wi_7", "--limit", "5", "--cursor", "ZGVjXzc");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.DecisionList, "{\"status\":\"answered\",\"campaign_id\":\"cmp_7\",\"work_item_id\":\"wi_7\",\"limit\":5,\"cursor\":\"ZGVjXzc\"}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body_and_lets_the_runtime_say_what_pending_means()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.DecisionList, "{}");
    }

    [Fact]
    public async Task Get_carries_the_positional_id_in_the_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "get", "dec_1");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.DecisionGet, "{\"decision_id\":\"dec_1\"}");
    }

    /// <summary>
    /// What a role types when it asks: the item and the attempt that fence the question, the question, and the
    /// two repeatable options — one typed answer per <c>--option</c>, one <c>kind:id</c> per <c>--reference</c>.
    /// </summary>
    [Fact]
    public async Task Raise_sends_the_question_fenced_by_the_attempt_with_every_option_and_reference()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "decision",
            "raise",
            "wi_1",
            "--attempt",
            "att_1",
            "--question",
            "Pause the sequence until the domain is warm?",
            "--option",
            "pause",
            "--option",
            "continue",
            "--reference",
            "work_item:wi_1",
            "--reference",
            "journal_entry:jrn_9",
            "--reason",
            "three bounces in a row");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.DecisionRaise,
            "{\"work_item_id\":\"wi_1\",\"attempt_id\":\"att_1\",\"question\":\"Pause the sequence until the domain is warm?\","
            + "\"options\":[{\"label\":\"pause\"},{\"label\":\"continue\"}],"
            + "\"references\":[{\"kind\":\"work_item\",\"id\":\"wi_1\"},{\"kind\":\"journal_entry\",\"id\":\"jrn_9\"}],"
            + "\"reason\":\"three bounces in a row\"}");
    }

    [Fact]
    public async Task Raise_without_options_or_references_sends_neither()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.DecisionRaise, "{\"work_item_id\":\"wi_1\",\"attempt_id\":\"att_1\",\"question\":\"q\"}");
    }

    /// <summary>
    /// The kind is everything before the first colon and the id is everything after it, so an id that carries
    /// a colon of its own arrives whole rather than cut where the CLI happened to look.
    /// </summary>
    [Fact]
    public async Task A_reference_is_split_at_its_first_colon_only()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q", "--reference", "report:rpt_1:extra");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.DecisionRaise,
            "{\"work_item_id\":\"wi_1\",\"attempt_id\":\"att_1\",\"question\":\"q\",\"references\":[{\"kind\":\"report\",\"id\":\"rpt_1:extra\"}]}");
    }

    /// <summary>
    /// A reference the CLI can tell is not one — no colon, nothing before it, nothing after it — is a usage
    /// error here, and no request is sent. What the kind means is the runtime's to judge; whether the text has
    /// the shape of a reference at all is not.
    /// </summary>
    [Theory]
    [InlineData("nonsense")]
    [InlineData(":wi_1")]
    [InlineData("work_item:")]
    public async Task A_reference_that_is_not_kind_and_id_is_a_usage_error(string reference)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q", "--reference", reference);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
        Assert.Contains("--reference", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("kind:id", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains(reference, cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The attempt is the only claim a question needs — it is the fencing token — so an actor is checked for
    /// its spelling and then left out of the body, exactly as it is on a heartbeat.
    /// </summary>
    [Fact]
    public async Task Raise_parses_the_actor_and_does_not_send_it()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "role:manager", "decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.DecisionRaise, "{\"work_item_id\":\"wi_1\",\"attempt_id\":\"att_1\",\"question\":\"q\"}");

        using var reserved = new CliRun();

        var refused = await reserved.RunAsync("--actor", "system", "decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q");

        Assert.Equal(ExitCodes.Usage, refused);
        Assert.Null(reserved.Sent);
    }

    [Theory]
    [InlineData("--attempt")]
    [InlineData("--question")]
    public async Task Raise_without_the_attempt_or_the_question_is_a_usage_error(string missing)
    {
        using var cli = new CliRun();
        var arguments = new List<string> { "decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q" };
        var at = arguments.IndexOf(missing);
        arguments.RemoveRange(at, 2);

        var exit = await cli.RunAsync([.. arguments]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(missing, cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The one verb of the four that is a person's: the answer travels with the actor the whole CLI carries,
    /// exactly as an approval's decision does, because the runtime refuses an answer that names nobody.
    /// </summary>
    [Fact]
    public async Task Answer_sends_the_decision_with_the_person_who_made_it()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "--actor", "human:ada", "decision", "answer", "dec_1", "--answer", "stop", "--option", "pause", "--reason", "the domain is not warm yet");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.DecisionAnswer,
            "{\"decision_id\":\"dec_1\",\"answer\":\"stop\",\"option\":\"pause\",\"reason\":\"the domain is not warm yet\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Answer_in_words_alone_names_no_option()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "answer", "dec_1", "--answer", "stop", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.DecisionAnswer, "{\"decision_id\":\"dec_1\",\"answer\":\"stop\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Answer_without_an_answer_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "answer", "dec_1", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
        Assert.Contains("--answer", cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What a person reads before answering, in the order the answer is arrived at: the question, then the
    /// answers the asker named, then what to read first, then the state of the question — and the identifiers
    /// last, as everywhere else.
    /// </summary>
    [Fact]
    public async Task Get_renders_the_question_and_its_choices_before_anything_about_identifiers()
    {
        using var cli = new CliRun(OneDecision);

        var exit = await cli.RunAsync("decision", "get", "dec_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text;
        Assert.Contains("Question:      Pause the LatAm sequence until the domain is warm?", text, StringComparison.Ordinal);
        Assert.Contains("OPTIONS", text, StringComparison.Ordinal);
        Assert.Contains("Stop sending until the warm-up finishes.", text, StringComparison.Ordinal);
        Assert.Contains("continue", text, StringComparison.Ordinal);
        Assert.Contains("REFERENCES", text, StringComparison.Ordinal);
        Assert.Contains("journal_entry  jrn_01K52JR0000000000000000009", text, StringComparison.Ordinal);
        Assert.Contains("Status:        answered", text, StringComparison.Ordinal);
        Assert.Contains("Answer:        pause it", text, StringComparison.Ordinal);
        Assert.Contains("Chosen:        pause", text, StringComparison.Ordinal);
        Assert.Contains("Answered by:   human:ada", text, StringComparison.Ordinal);
        Assert.Contains("Answered:      2026-09-19 10:00:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("Asked by:      att_01K52JR0000000000000000004", text, StringComparison.Ordinal);
        Assert.Contains("Raised:        2026-09-19 09:00:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("Id:            dec_01K52JR0000000000000000001", text, StringComparison.Ordinal);

        var question = text.IndexOf("Question:", StringComparison.Ordinal);
        var options = text.IndexOf("OPTIONS", StringComparison.Ordinal);
        var references = text.IndexOf("REFERENCES", StringComparison.Ordinal);
        var status = text.IndexOf("Status:", StringComparison.Ordinal);
        var id = text.IndexOf("Id:", StringComparison.Ordinal);
        Assert.True(question < options && options < references && references < status && status < id, text);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    /// <summary>A question nobody has answered, with no choices and nothing to read: no empty tables, and a dash where an answer would be.</summary>
    [Fact]
    public async Task Get_renders_a_pending_question_without_empty_tables()
    {
        const string pending = """
            {
              "id": "dec_1",
              "campaign_id": "cmp_1",
              "work_item_id": "wi_1",
              "attempt_id": "att_1",
              "status": "pending",
              "question": "Which of the two accounts do we mean?",
              "options": null,
              "references": null,
              "raised_at": "2026-09-19T09:00:00+00:00",
              "answer": null,
              "chosen_option": null,
              "answered_at": null,
              "answered_by": null
            }
            """;
        using var cli = new CliRun(pending);

        var exit = await cli.RunAsync("decision", "get", "dec_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Status:        pending", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Answer:        -", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("OPTIONS", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("REFERENCES", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// One row per question, the question last and cut to a width a table can hold: the whole of it is in the
    /// JSON and in <c>decision get</c>, and a row is for telling questions apart, not for answering one.
    /// </summary>
    [Fact]
    public async Task List_renders_a_table_of_what_is_waiting_with_long_questions_cut_to_fit()
    {
        var longQuestion = string.Join(' ', Enumerable.Repeat("word", 40));
        var page = $$"""
            {
              "items": [
                {
                  "id": "dec_1", "campaign_id": "cmp_1", "work_item_id": "wi_1", "status": "pending",
                  "question": "Which of the two accounts do we mean?",
                  "raised_at": "2026-09-19T09:00:00+00:00", "answered_at": null, "answered_by": null
                },
                {
                  "id": "dec_2", "campaign_id": "cmp_1", "work_item_id": "wi_2", "status": "pending",
                  "question": "{{longQuestion}}",
                  "raised_at": "2026-09-19T09:05:00+00:00", "answered_at": null, "answered_by": null
                }
              ],
              "next_cursor": "ZGVjXzI"
            }
            """;
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("decision", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("RAISED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("QUESTION", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Which of the two accounts do we mean?", cli.Text, StringComparison.Ordinal);
        Assert.Contains("pending", cli.Text, StringComparison.Ordinal);
        Assert.Contains("dec_2", cli.Text, StringComparison.Ordinal);
        Assert.Contains("word word …", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(longQuestion, cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: ZGVjXzI", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A question cut to fit is cut between characters. Cutting at a UTF-16 index puts half of an astral
    /// character at the end of the row — a lone surrogate, which is not text at all and which a terminal draws
    /// as a replacement box.
    /// </summary>
    /// <remarks>
    /// The astral character is placed at three offsets around the cut, so the assertion cannot pass by accident
    /// of where the width happens to fall: one of the three keeps it whole and two drop it, and none of them may
    /// leave half of it behind.
    /// </remarks>
    [Theory]
    [InlineData(58)]
    [InlineData(59)]
    [InlineData(60)]
    public async Task A_question_cut_to_fit_is_never_cut_through_a_character(int before)
    {
        var question = new string('a', before) + "\U0001F600" + new string('b', 40);
        var page = """
            {
              "items": [
                {
                  "id": "dec_1", "campaign_id": "cmp_1", "work_item_id": "wi_1", "status": "pending",
                  "question": "QUESTION",
                  "raised_at": "2026-09-19T09:00:00+00:00", "answered_at": null, "answered_by": null
                }
              ],
              "next_cursor": null
            }
            """.Replace("QUESTION", question, StringComparison.Ordinal);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("decision", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(LoneSurrogates(cli.Text));
    }

    /// <summary>
    /// Every surrogate that is not one half of a well-formed pair. Not "no surrogates": an emoji kept whole
    /// inside the excerpt is a pair, and keeping it is correct.
    /// </summary>
    private static IEnumerable<int> LoneSurrogates(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index]))
            {
                continue;
            }

            var isHalfOfAPair = char.IsHighSurrogate(text[index])
                ? index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                : index > 0 && char.IsHighSurrogate(text[index - 1]);

            if (!isHalfOfAPair)
            {
                yield return index;
            }
        }
    }
}
