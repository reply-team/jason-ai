using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>campaign.create</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. Reply has no command that creates a sequence, so the
/// package makes two raw calls — the list of sequences, read to its end, and the create — and one command on a repeat,
/// the read of the people a sequence it may have made holds. One test per row of the document's repeat table, and then
/// the refusals.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyCampaignCreateTests
{
    private const string Operation = "campaign.create";
    private const string Name = "Q4 Nordics";
    private const string FirstPage = "GET /v3/sequences?top=1000&skip=0";
    private const string Create = "POST /v3/sequences";

    /// <summary>The one step a Reply sequence needs to exist, exactly as the package sends it.</summary>
    private const string Placeholder =
        """{"type":"task","actionType":"toDo","delayInMinutes":0,"description":"Added because a sequence needs one step to exist; replace it before this sequence is ever started."}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // A first attempt
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_first_attempt_that_finds_no_campaign_of_the_name_makes_a_draft_and_pins_it()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3 LatAm founders", "active", false, 11);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input());

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(
            """{"items":[{"campaign_external_id":"2001","name":"Q4 Nordics","status":"created","state":"draft","enrolled":0,"steps_added":1}]}""",
            answer.ToJsonString());
        Assert.Equal("""{"campaign":"2001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());

        // The list first, read to its end, and then the create, whose body carries the name and the one step.
        Assert.Equal([FirstPage, Create], Lines(account));
        Assert.Equal($$"""{"name":"Q4 Nordics","steps":[{{Placeholder}}]}""", account.Calls[1].Body);
        var made = Assert.Single(account.Sequences, sequence => (int)sequence!["id"]! == 2001)!;
        Assert.Equal("new", (string?)made["status"]);
        Assert.False((bool)made["isArchived"]!);
    }

    /// <summary>
    /// The step a Reply sequence needs to exist is a task for its owner: it reaches nobody even once the sequence runs,
    /// and it says what it is and that it is replaced before the sequence is ever started.
    /// </summary>
    [Fact]
    public async Task The_one_step_added_is_a_task_for_the_owner_that_says_what_it_is()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        var body = JsonNode.Parse(account.Calls.Single(call => call.Line == Create).Body!)!.AsObject();
        Assert.Equal(["name", "steps"], body.Select(member => member.Key));
        var step = Assert.Single(body["steps"]!.AsArray())!.AsObject();
        Assert.Equal(Placeholder, step.ToJsonString());
        Assert.Equal("task", (string?)step["type"]);
        Assert.Equal("toDo", (string?)step["actionType"]);
    }

    [Fact]
    public async Task A_campaign_already_linked_is_refused_before_anything_is_called()
    {
        using var account = new ReplyAccount().WithSequence(7, Name, "new", false, 11);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "7")));

        Assert.Equal("campaign_already_linked", error.Code);
        Assert.Equal(FailureClass.Validation, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// A first attempt never makes a second campaign of a name, and never takes over the one that is there. A sequence that
    /// is archived or running still holds its name: Reply keeps it in the list, and it can be taken out of the archive.
    /// </summary>
    [Theory]
    [InlineData("Q4 Nordics", "new", false)]
    [InlineData("q4 NORDICS", "new", false)]
    [InlineData("Q4 Nordics", "new", true)]
    [InlineData("Q4 Nordics", "active", false)]
    public async Task A_first_attempt_that_finds_the_name_taken_makes_nothing(string held, string status, bool archived)
    {
        using var account = new ReplyAccount().WithSequence(7, held, status, archived, 11);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("campaign_name_taken", error.Code);
        Assert.Equal(FailureClass.Validation, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
        Assert.Single(account.Sequences);
    }

    /// <summary>Reply's own name filter matches part of a name; the package compares whole names, so a longer one is not this one.</summary>
    [Fact]
    public async Task A_name_that_only_holds_the_name_is_not_taken()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q4 Nordics (copy)", "new", false, 11);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        Assert.Equal([FirstPage, Create], Lines(account));
    }

    /// <summary>A name taken on the second page is taken: the list is read to its end before anything is made.</summary>
    [Fact]
    public async Task Every_page_of_the_list_is_read_before_a_name_counts_as_free()
    {
        using var account = new ReplyAccount();
        var others = new JsonArray([.. Enumerable.Range(1, 1000).Select(at => (JsonNode)ListItem(10_000 + at, "Other " + at.ToString(CultureInfo.InvariantCulture)))]);
        account.Answers("GET", "/v3/sequences?top=1000&skip=0", 200, new JsonObject { ["items"] = others, ["hasMore"] = true }.ToJsonString());
        account.Answers("GET", "/v3/sequences?top=1000&skip=1000", 200, new JsonObject { ["items"] = new JsonArray(ListItem(9, Name)), ["hasMore"] = false }.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("campaign_name_taken", error.Code);
        Assert.Equal([FirstPage, "GET /v3/sequences?top=1000&skip=1000"], Lines(account));
    }

    /// <summary>
    /// The name is an operation's argument, and an argument never reaches a command line: the host writes every argument
    /// to its <c>exec</c> line. So the list is asked for every sequence and the name is compared here, and the name
    /// travels in the create's body, on stdin.
    /// </summary>
    [Fact]
    public async Task The_name_never_reaches_a_command_line()
    {
        const string Marked = "Name-marker-7d3e";
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(name: Marked)));

        Assert.Equal([FirstPage, Create], Lines(account));
        Assert.All(account.Calls, call => Assert.DoesNotContain(Marked, string.Join(' ', call.Args), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Marked, account.Calls[1].Body!, StringComparison.Ordinal);
        Assert.Equal(["--json", "-q", "api", "/v3/sequences", "--method", "POST", "--body", "-"], account.Calls[1].Args);
    }

    // -------------------------------------------------------------------------------------------------------
    // An attempt after one that may have made the campaign
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_repeat_that_finds_no_campaign_of_the_name_makes_it()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3 LatAm founders", "new", false, 11);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(), attempt: 2));

        Assert.Equal("2001", (string?)answer["items"]![0]!["campaign_external_id"]);
        Assert.Equal([FirstPage, Create], Lines(account));
    }

    /// <summary>
    /// The create was sent and its answer lost; the sequence is there. The next attempt lists, finds exactly that one —
    /// a draft that holds nobody, made after the work item — and answers from it, making nothing.
    /// </summary>
    [Fact]
    public async Task A_create_whose_answer_was_lost_is_found_by_the_next_attempt_and_never_made_twice()
    {
        using var account = new ReplyAccount();
        account.LosesTheOutcomeOf("POST", "/v3/sequences");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var input = Input();

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);
        Assert.Null(lost.ExternalIds);
        var mark = account.Mark();

        var result = await InvokeAsync(api, input, attempt: 2);

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(
            """{"items":[{"campaign_external_id":"2001","name":"Q4 Nordics","status":"created","state":"draft","enrolled":0,"steps_added":1}]}""",
            answer.ToJsonString());
        Assert.Equal("""{"campaign":"2001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal([FirstPage, "sequence contacts 2001"], account.CallsSince(mark).Select(call => call.Line));
        Assert.Equal("1", account.CallsSince(mark)[1].Option("--limit"));
        Assert.Single(account.Sequences);
    }

    /// <summary>The two clocks may disagree: a campaign made up to ten minutes before the moment the work item carries is still its own.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-9)]
    public async Task A_repeat_adopts_the_one_empty_draft_made_no_earlier_than_the_work_item_less_ten_minutes(int minutes)
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(minutes));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("2001", (string?)answer["items"]![0]!["campaign_external_id"]);
        Assert.Equal([FirstPage, "sequence contacts 2001"], Lines(account));
    }

    /// <summary>Reply writes the moment with an offset and any number of fractional digits; the package reads it as the moment it is.</summary>
    [Theory]
    [InlineData("+03:00", 3 * 60 - 5, true)]
    [InlineData("+03:00", 3 * 60 - 15, false)]
    [InlineData("-02:00", -2 * 60 + 5, true)]
    public async Task The_moment_a_campaign_was_made_is_read_with_its_offset(string offset, int shiftMinutes, bool adopted)
    {
        var item = new DateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var local = item.AddMinutes(shiftMinutes);
        var written = local.ToString("yyyy-MM-dd'T'HH:mm:ss'.08'", CultureInfo.InvariantCulture) + offset;
        using var account = new ReplyAccount().WithSequence(2001, Name, "new", false, 9001).CreatedAt(2001, written);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(at: item), attempt: 2);

        if (adopted)
        {
            Assert.Equal("2001", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["campaign_external_id"]);
        }
        else
        {
            Assert.Equal("campaign_name_ambiguous", ReplyOperations.Failed(Operation, result).Code);
        }
    }

    [Fact]
    public async Task A_repeat_that_finds_one_campaign_of_the_name_made_before_the_work_item_makes_nothing()
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(-11));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
        Assert.Single(account.Sequences);
    }

    /// <summary>One that has been started, archived, or holds somebody is not the empty draft this item would have made.</summary>
    [Theory]
    [InlineData("active", false, false)]
    [InlineData("paused", false, false)]
    [InlineData("new", true, false)]
    [InlineData("new", false, true)]
    public async Task A_repeat_that_finds_one_campaign_of_the_name_that_is_not_an_empty_draft_makes_nothing(string status, bool archived, bool holdsSomebody)
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, status, archived, 9001)
            .CreatedAt(2001, item.AddMinutes(1));
        if (holdsSomebody)
        {
            account.WithContact(1001, "marta@example.com", "Marta").WithEnrollment(2001, 1001);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Null(error.ExternalIds);
        Assert.DoesNotContain(account.Calls, call => call.Line == Create);
        Assert.Single(account.Sequences);
    }

    /// <summary>Two of the name, however new and however empty: which one this item made cannot be told, so a person decides.</summary>
    [Fact]
    public async Task A_repeat_that_finds_two_campaigns_of_the_name_makes_nothing()
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .WithSequence(2002, Name.ToUpperInvariant(), "new", false, 9101)
            .CreatedAt(2001, item.AddMinutes(1))
            .CreatedAt(2002, item.AddMinutes(2));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
        Assert.Equal(2, account.Sequences.Count);
    }

    /// <summary>
    /// "Every page" holds on a repeat as well: a second of the name on a later page makes the one on the first page
    /// someone else's as much as this item's, so nothing is adopted and nobody's people are read.
    /// </summary>
    [Fact]
    public async Task A_repeat_reads_every_page_and_a_second_of_the_name_on_a_later_page_makes_nothing()
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(1));
        account.Answers(
            "GET",
            "/v3/sequences?top=1000&skip=0",
            200,
            new JsonObject { ["items"] = new JsonArray(ListItem(2001, Name, Written(item.AddMinutes(1)))), ["hasMore"] = true }.ToJsonString());
        account.Answers(
            "GET",
            "/v3/sequences?top=1000&skip=1000",
            200,
            new JsonObject { ["items"] = new JsonArray(ListItem(2002, Name.ToUpperInvariant(), Written(item.AddMinutes(2)))), ["hasMore"] = false }.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage, "GET /v3/sequences?top=1000&skip=1000"], Lines(account));
    }

    /// <summary>
    /// A candidate whose creation moment cannot be read — written without an offset, or not a moment at all — cannot be
    /// shown to be no earlier than the work item, so it is not adopted, whenever it was made.
    /// </summary>
    [Theory]
    [InlineData("2026-10-16T22:45:46.08")]
    [InlineData("not a moment")]
    public async Task A_repeat_refuses_a_candidate_whose_creation_moment_cannot_be_read(string written)
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, written);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
        Assert.Single(account.Sequences);
    }

    /// <summary>
    /// The one candidate is worked by only when its identifier is a Reply sequence number, as Reply writes one: anything
    /// else names nothing this package can read or pin, so nothing is adopted.
    /// </summary>
    [Theory]
    [InlineData("\"2001\"")]
    [InlineData("null")]
    [InlineData("0")]
    public async Task A_repeat_refuses_a_candidate_whose_identifier_is_not_a_sequence_number(string id)
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(1));
        var candidate = ListItem(2001, Name, Written(item.AddMinutes(1)));
        candidate["id"] = JsonNode.Parse(id);
        account.Answers("GET", "/v3/sequences?top=1000&skip=0", 200, new JsonObject { ["items"] = new JsonArray(candidate), ["hasMore"] = false }.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
    }

    /// <summary>
    /// A candidate is a draft only where the list says it is not archived. One whose <c>isArchived</c> is missing or null
    /// is not shown to be an empty draft, so nothing is adopted and nobody's people are read.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task A_repeat_refuses_a_candidate_the_list_does_not_say_is_unarchived(string? archived)
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(1));
        var candidate = ListItem(2001, Name, Written(item.AddMinutes(1)));
        Archived(candidate, archived);
        account.Answers("GET", "/v3/sequences?top=1000&skip=0", 200, new JsonObject { ["items"] = new JsonArray(candidate), ["hasMore"] = false }.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
    }

    /// <summary>
    /// A page of people that holds nobody yet says more follow contradicts itself, and "more follow" is the half that
    /// can cost a person: it is read as holding somebody, so nothing is adopted.
    /// </summary>
    [Fact]
    public async Task A_repeat_reads_a_people_page_that_says_more_follow_as_holding_somebody()
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(1));
        account.Prints("sequence contacts", 0, """{"items":[],"has_more":true,"total":null}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal("campaign_name_ambiguous", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage, "sequence contacts 2001"], Lines(account));
        Assert.Single(account.Sequences);
    }

    /// <summary>
    /// The read of a candidate's people names a sequence that is not this item's own yet, so nothing it fails with may
    /// link it: a refusal carrying the sequence's identifier pins nothing, and the attempt ends as that read's own row
    /// says — never as made, and with nothing made. An answer that is an object but not a page of people says nothing
    /// about who the sequence holds, so it is a read to ask again, never "holds nobody".
    /// </summary>
    [Theory]
    [InlineData("rate_limited", null, "rate_limited", FailureClass.Transient)]
    [InlineData("sequence.not_found", null, "provider_call_failed", FailureClass.Permanent)]
    [InlineData(null, "not a result", "provider_unavailable", FailureClass.Transient)]
    [InlineData(null, "{\"total\":3}", "provider_unavailable", FailureClass.Transient)]
    public async Task A_failed_read_of_a_candidate_s_people_pins_nothing_and_ends_as_that_read_s_row(
        string? cliCode,
        string? printed,
        string code,
        FailureClass failure)
    {
        var item = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount()
            .WithSequence(2001, Name, "new", false, 9001)
            .CreatedAt(2001, item.AddMinutes(1));
        if (cliCode is null)
        {
            account.Prints("sequence contacts", 0, printed!);
        }
        else
        {
            var limited = cliCode == "rate_limited";
            account.Refuses("sequence contacts", cliCode, status: limited ? 429 : 404, ids: new JsonObject { ["sequence_id"] = 2001 }, retryAfter: limited ? 30 : null);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(at: item), attempt: 2));

        Assert.Equal(code, error.Code);
        Assert.Equal(failure, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage, "sequence contacts 2001"], Lines(account));
        Assert.Single(account.Sequences);
    }

    /// <summary>
    /// The list is read to a cap of twenty pages of a thousand. An account that holds more is one the package declines
    /// to decide for: whether the name is taken cannot be known, so nothing is made, and the message says how far the
    /// list was read.
    /// </summary>
    [Fact]
    public async Task A_list_still_going_at_its_cap_of_twenty_thousand_makes_nothing_and_says_how_far_it_read()
    {
        using var account = new ReplyAccount();
        var pages = Enumerable.Range(0, 20).Select(page => "/v3/sequences?top=1000&skip=" + (page * 1000).ToString(CultureInfo.InvariantCulture)).ToList();
        foreach (var page in pages)
        {
            account.Answers("GET", page, 200, new JsonObject { ["items"] = new JsonArray(ListItem(30_000 + pages.IndexOf(page), "Other")), ["hasMore"] = true }.ToJsonString());
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("read to twenty thousand", error.Message, StringComparison.Ordinal);
        Assert.Equal("Read 20000 sequences, 20 pages of 1000, and Reply said there were more.", (string?)error.Details!["reason"]);
        Assert.Equal(pages.Select(page => "GET " + page), Lines(account));
        Assert.Null(error.ExternalIds);
    }

    // -------------------------------------------------------------------------------------------------------
    // What Reply answers
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Never expected: a create answered with a sequence started or archived, or one the answer does not say is
    /// unarchived. It is pinned, so a person can find it.
    /// </summary>
    [Theory]
    [InlineData("active", "false")]
    [InlineData("new", "true")]
    [InlineData("new", null)]
    [InlineData("new", "null")]
    public async Task A_create_answered_with_a_campaign_that_is_not_a_draft_fails_and_pins_it(string status, string? archived)
    {
        using var account = new ReplyAccount();
        var made = ListItem(2001, Name);
        made["status"] = status;
        Archived(made, archived);
        account.Answers("POST", "/v3/sequences", 201, made.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("campaign_not_draft", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal("""{"campaign":"2001"}""", error.ExternalIds!.ToJsonString());
    }

    /// <summary>A create answered without an identifier happened, and nothing says which sequence it made: that is a lost answer.</summary>
    [Fact]
    public async Task A_create_answered_without_an_identifier_is_a_lost_answer()
    {
        using var account = new ReplyAccount();
        account.Answers("POST", "/v3/sequences", 201, """{"name":"Q4 Nordics","status":"new","isArchived":false}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Null(error.ExternalIds);
    }

    [Theory]
    [InlineData("GET", "/v3/sequences?top=1000&skip=0", 401, "unauthorized", FailureClass.Permanent)]
    [InlineData("GET", "/v3/sequences?top=1000&skip=0", 429, "rate_limited", FailureClass.Transient)]
    [InlineData("GET", "/v3/sequences?top=1000&skip=0", 503, "provider_unavailable", FailureClass.Transient)]
    [InlineData("GET", "/v3/sequences?top=1000&skip=0", 400, "provider_call_failed", FailureClass.Permanent)]
    [InlineData("POST", "/v3/sequences", 400, "provider_call_failed", FailureClass.Permanent)]
    [InlineData("POST", "/v3/sequences", 403, "unauthorized", FailureClass.Permanent)]
    [InlineData("POST", "/v3/sequences", 429, "rate_limited", FailureClass.Transient)]
    [InlineData("POST", "/v3/sequences", 502, "provider_answer_lost", FailureClass.Ambiguous)]
    public async Task What_reply_answers_is_the_shared_ending_it_means(string method, string path, int status, string code, FailureClass failure)
    {
        using var account = new ReplyAccount();
        account.Answers(method, path, status, status == 401 ? string.Empty : """{"title":"Refused","status":0}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(code, error.Code);
        Assert.Equal(failure, error.Class);
        Assert.Null(error.ExternalIds);
    }

    /// <summary>A page that is not a list of sequences says nothing about the name, and a read costs nothing to ask again.</summary>
    [Fact]
    public async Task A_list_answer_that_is_not_a_page_is_asked_again()
    {
        using var account = new ReplyAccount();
        account.Answers("GET", "/v3/sequences?top=1000&skip=0", 200, """{"items":"none"}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal([FirstPage], Lines(account));
    }

    /// <summary>
    /// A page that does not say whether more follow is not the end of the list: the name may be taken on a page nobody
    /// read, and reading that silence as "free" is how a second campaign of a name gets made. It is asked again.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_list_page_that_does_not_say_whether_more_follow_is_asked_again_and_makes_nothing(bool saysNull)
    {
        using var account = new ReplyAccount();
        var page = new JsonObject { ["items"] = new JsonArray(ListItem(7, "Other")) };
        if (saysNull)
        {
            page["hasMore"] = null;
        }

        account.Answers("GET", "/v3/sequences?top=1000&skip=0", 200, page.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal([FirstPage], Lines(account));
        Assert.Empty(account.Sequences);
    }

    // -------------------------------------------------------------------------------------------------------

    private static JsonObject ListItem(int id, string name, string created = "2026-01-05T09:15:42.17+00:00") => new()
    {
        ["id"] = id,
        ["ownerUserId"] = 42,
        ["name"] = name,
        ["status"] = "new",
        ["created"] = created,
        ["isArchived"] = false,
        ["health"] = "healthy",
    };

    /// <summary>A list item's or an answer's <c>isArchived</c>, as JSON; absent where <paramref name="json"/> is null.</summary>
    private static void Archived(JsonObject sequence, string? json)
    {
        if (json is null)
        {
            sequence.Remove("isArchived");
        }
        else
        {
            sequence["isArchived"] = JsonNode.Parse(json);
        }
    }

    /// <summary>A moment as Reply writes one: two fractional digits and an offset.</summary>
    private static string Written(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ff'+00:00'", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    /// <summary>A work item's identifier carrying the moment it was made: a ULID's first 48 bits are that moment.</summary>
    private static string Key(DateTimeOffset at) => "wi_" + Ulid.NewUlid(at);

    private static JsonObject Input(string name = Name, string? pinned = null, DateTimeOffset? at = null) => new()
    {
        ["args"] = new JsonObject { ["name"] = name },
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q4 Nordics",
            ["status"] = "draft",
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
        },
        ["idempotency_key"] = Key(at ?? DateTimeOffset.UtcNow.AddMinutes(-1)),
    };

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
