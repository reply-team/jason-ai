using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>enrollment.list</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted, which answers in the shapes a live account answered —
/// the address present, an empty last name, a date added with an offset. One test per line of the document's
/// conformance list, and then the decisions this provider forces.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyEnrollmentListTests
{
    private const string Operation = "enrollment.list";
    private const int Sequence = ReplyOperations.Sequence;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The campaign, named one way or the other
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_first_page_links_the_campaign_and_lists_the_people_it_holds_with_the_provider_s_count()
    {
        using var account = Planted(people: 3);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(limit: 100, offset: 0, supplied: "7"));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(3, answer["items"]!.AsArray().Count);
        Assert.Equal(3, (int)answer["total"]!);
        Assert.False((bool)answer["has_more"]!);
        Assert.Equal("added_desc", (string?)answer["vendor"]!["ordering"]);

        // The first link is made the way campaign.get makes it: the identifier travels out as the campaign's pin.
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("""{"campaign":"7"}""", outcome.ExternalIds!.ToJsonString());

        // One command, the page with its count, and nothing on stdin.
        Assert.Equal(["sequence contacts 7"], account.Calls.Select(call => call.Line));
        Assert.Equal(["--json", "-q", "sequence", "contacts", "7", "--limit", "100", "--offset", "0"], account.Calls[0].Args);
        Assert.All(account.Calls, call => Assert.Null(call.Body));
    }

    [Fact]
    public async Task A_linked_campaign_is_listed_by_its_pin()
    {
        using var account = Planted(people: 1);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.Single(answer["items"]!.AsArray());
        Assert.Equal("sequence contacts 7", account.Calls[0].Line);
        Assert.Equal("10", account.Calls[0].Option("--limit"));
        Assert.Equal("0", account.Calls[0].Option("--offset"));
    }

    [Fact]
    public async Task A_campaign_the_provider_does_not_hold_fails_permanently_rather_than_answering_an_empty_page()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, supplied: "7")));

        // The CLI tells a sequence the account does not hold from an empty one, and says so in its own word.
        Assert.Equal("campaign_not_found", error.Code);
        Assert.Equal("sequence.not_found", (string?)error.Details!["cli_code"]);

        // The provider has just denied the link, so it is not recorded.
        Assert.Null(error.ExternalIds);
        Assert.Equal(["sequence contacts 7"], account.Calls.Select(call => call.Line));
    }

    /// <summary>A sequence the account holds with nobody in it is an empty page with a total of nought, not a missing one.</summary>
    [Fact]
    public async Task A_campaign_the_provider_holds_with_nobody_in_it_is_an_empty_page_counted_nought()
    {
        using var account = Planted(people: 0);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.Equal(0, answer["total"]!.GetValue<int>());
        Assert.Empty(answer["items"]!.AsArray());
    }

    [Fact]
    public async Task An_identifier_that_is_not_a_sequence_number_is_refused_before_anything_is_asked()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, supplied: "seq-7")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    // -------------------------------------------------------------------------------------------------------
    // What a person in the page is, and what of them never travels
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task No_person_s_address_company_or_title_appears_anywhere_in_the_result()
    {
        using var account = Planted(people: 0);
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName, lastName: ReplyOperations.LastName,
            company: ReplyOperations.Company, title: ReplyOperations.Title);
        account.WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7"));

        var answer = ReplyOperations.Succeeded(Operation, result).ToJsonString();
        Assert.Contains(ReplyOperations.FirstName, answer, StringComparison.Ordinal);

        // The stand-in answers all three, as a live account does; the plugin carried none of them on.
        var (_, said, _) = await account.RunAsync(["--json", "-q", "sequence", "contacts", "7", "--limit", "10"], Ct);
        Assert.Contains(ReplyOperations.Address, said, StringComparison.Ordinal);
        Assert.Contains(ReplyOperations.Company, said, StringComparison.Ordinal);
        Assert.DoesNotContain(ReplyOperations.Address, answer, StringComparison.Ordinal);
        Assert.DoesNotContain(ReplyOperations.Company, answer, StringComparison.Ordinal);
        Assert.DoesNotContain(ReplyOperations.Title, answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_the_provider_holds_as_empty_is_null_and_the_date_added_is_in_utc()
    {
        using var account = Planted(people: 0);
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName, lastName: string.Empty);
        account.WithEnrollment(Sequence, 1001, addedAt: "2026-10-02T09:15:42.17+03:00", stepId: 5421, stepName: "2A",
            disposition: (Replied: true, Bounced: false));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        var person = answer["items"]![0]!;
        Assert.Equal("1001", (string?)person["external_id"]);
        Assert.Equal(ReplyOperations.FirstName, (string?)person["first_name"]);
        Assert.Null(person["last_name"]);
        Assert.Equal("2026-10-02T06:15:42.170Z", (string?)person["added_at"]);
        Assert.Equal("2A", (string?)person["current_step"]);
        Assert.Equal("5421", (string?)person["vendor"]!["current_step_id"]);
        Assert.Equal("""{"is_replied":true,"is_bounced":false}""", person["vendor"]!["email_disposition"]!.ToJsonString());
    }

    [Fact]
    public async Task Each_state_reads_in_the_neutral_vocabulary_with_the_provider_s_own_word_beside_it()
    {
        using var account = Planted(people: 0);
        var states = new[] { "active", "paused", "outOfOffice", "finished", "inactive" };
        for (var at = 0; at < states.Length; at++)
        {
            account.WithContact(1001 + at, $"p{at}@example.com", "Person" + at);
            account.WithEnrollment(Sequence, 1001 + at, states[at], addedAt: $"2026-10-0{at + 1}T10:00:00+00:00");
        }

        account.WithContact(1010, "opted@example.com", "Opted", optedOut: true);
        account.WithEnrollment(Sequence, 1010, "paused", addedAt: "2026-09-01T10:00:00+00:00");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        // Most recently added first, as the provider orders them.
        var people = answer["items"]!.AsArray().Select(person => (
            Id: (string?)person!["external_id"],
            Status: (string?)person["status"],
            Word: (string?)person["vendor"]!["status_in_sequence"],
            OptedOut: (bool)person["opted_out"]!)).ToList();
        Assert.Equal(
            [
                ("1005", "other", "inactive", false),
                ("1004", "finished", "finished", false),
                ("1003", "paused", "outOfOffice", false),
                ("1002", "paused", "paused", false),
                ("1001", "active", "active", false),
                ("1010", "paused", "paused", true),
            ],
            people);
    }

    /// <summary>
    /// A provider word is looked up as a word. One that happens to name a property every JavaScript object has —
    /// <c>constructor</c>, <c>toString</c> — is a state this vocabulary has no word for, like any other.
    /// </summary>
    [Fact]
    public async Task A_provider_word_that_names_an_object_property_is_still_a_word_this_vocabulary_lacks()
    {
        using var account = Planted(people: 0);
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.WithEnrollment(Sequence, 1001, "constructor");
        account.WithContact(1002, "p2@example.com", "Second");
        account.WithEnrollment(Sequence, 1002, "toString", addedAt: "2026-10-03T10:00:00+00:00");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.All(answer["items"]!.AsArray(), person => Assert.Equal("other", (string?)person!["status"]));
    }

    // -------------------------------------------------------------------------------------------------------
    // The page, and the count beside it
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pages_follow_on_by_the_number_of_people_returned_and_the_total_is_never_the_page()
    {
        using var account = Planted(people: 5);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var offset = 0;
        for (var page = 0; page < 3; page++)
        {
            var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 2, offset: offset, pinned: "7")));
            var items = answer["items"]!.AsArray();
            seen.AddRange(items.Select(person => (string)person!["external_id"]!));
            Assert.Equal(5, (int)answer["total"]!);
            Assert.Equal(page < 2, (bool)answer["has_more"]!);
            offset += items.Count;
        }

        Assert.Equal(["1005", "1004", "1003", "1002", "1001"], seen);
        Assert.Contains(account.Calls, call => call.Option("--limit") == "2" && call.Option("--offset") == "4");
    }

    [Fact]
    public async Task A_count_the_provider_would_not_give_leaves_the_total_null_and_the_page_answered()
    {
        using var account = Planted(people: 1);
        account.Returns("sequence contacts", new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["contactId"] = 1001, ["sequenceId"] = 7, ["email"] = "a@example.com", ["firstName"] = "A", ["lastName"] = "",
                ["statusInSequence"] = "active", ["currentStep"] = new JsonObject { ["stepId"] = 9901, ["displayName"] = "1" },
                ["addingDate"] = "2026-10-02T09:15:42.17+03:00", ["isOptedOut"] = false, ["callStatus"] = "none",
                ["meetingStatus"] = "none", ["emailDisposition"] = null,
            }),
            ["has_more"] = false,
            ["total"] = null,
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, supplied: "7")));

        Assert.Null(answer["total"]);
        Assert.Equal("1001", (string?)answer["items"]![0]!["external_id"]);
    }

    /// <summary>
    /// The short page. People whose names are as long as the document allows, in a script the runtime counts at
    /// six bytes a character, make a page of a thousand far larger than a result may be — so the plugin stops before
    /// the budget, says there is more, and the pages that follow, each started at the offset plus what the one
    /// before returned, list every person exactly once.
    /// </summary>
    [Fact]
    public async Task A_page_of_people_too_large_for_one_result_comes_back_short_and_the_next_one_carries_on()
    {
        const int People = 400;
        using var account = Planted(people: 0);
        var name = new string('Ж', 200);
        for (var at = 0; at < People; at++)
        {
            account.WithContact(2000 + at, $"p{at}@example.com", name, lastName: name);
            account.WithEnrollment(Sequence, 2000 + at, addedAt: DateTimeOffset.UnixEpoch.AddMinutes(at).ToString("O", CultureInfo.InvariantCulture));
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var pages = new List<int>();
        var offset = 0;
        var more = true;
        while (more)
        {
            var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 1000, offset: offset, pinned: "7")));
            var items = answer["items"]!.AsArray();
            seen.AddRange(items.Select(person => (string)person!["external_id"]!));
            pages.Add(items.Count);
            more = (bool)answer["has_more"]!;
            offset += items.Count;
            Assert.True(pages.Count <= 5, "the pages never ended");
        }

        Assert.True(pages[0] < People, $"the first page held all {People} people, so nothing was cut");
        Assert.True(pages.Count > 1);
        Assert.Equal(Enumerable.Range(0, People).Reverse().Select(at => (2000 + at).ToString(CultureInfo.InvariantCulture)), seen);
    }

    // -------------------------------------------------------------------------------------------------------
    // Refusals, and a read that writes nothing
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_role_that_may_not_read_a_sequence_s_people_is_a_feature_the_account_lacks()
    {
        using var account = Planted(people: 1);
        account.Refuses("sequence contacts", "access.feature_unavailable", status: 403, replyCode: "sequenceContact.forbidden");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.Equal("feature_unavailable", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public async Task Any_other_403_is_the_credential_and_says_so()
    {
        using var account = Planted(people: 1);
        account.Refuses("sequence contacts", "access.denied", status: 403, replyCode: "TEAM_NOT_ACCESSIBLE");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task A_read_creates_nothing_and_pins_no_person()
    {
        using var account = Planted(people: 2);
        var before = (account.Contacts.ToJsonString(), account.Enrollments.ToJsonString(), account.Sequences.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7"));

        ReplyOperations.Succeeded(Operation, result);
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal(["campaign"], outcome.ExternalIds!.AsObject().Select(pin => pin.Key));
        Assert.All(account.Calls, call => Assert.Equal("sequence contacts", call.Command));
        Assert.Equal(before, (account.Contacts.ToJsonString(), account.Enrollments.ToJsonString(), account.Sequences.ToJsonString()));
    }

    [Fact]
    public async Task A_page_that_is_not_a_page_is_no_answer_and_is_asked_again()
    {
        using var account = Planted(people: 1);
        account.Prints("sequence contacts", 0, """{"items":"nobody","has_more":false,"total":0}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    /// <summary>A cut answer names the command it was cut on, and a read cut at the capture is too large rather than lost.</summary>
    [Fact]
    public async Task A_page_cut_off_at_the_capture_is_too_large_and_names_the_command()
    {
        using var account = Planted(people: 1);
        account.Prints(
            "sequence contacts",
            0,
            new JsonObject { ["items"] = new JsonArray(), ["has_more"] = false, ["total"] = 1, ["note"] = new string('a', 200_000) }.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, paths => Capping(paths, 65_536));

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 10, offset: 0, pinned: "7")));

        Assert.Equal("provider_answer_too_large", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal("sequence contacts", (string?)error.Details!["call"]);
    }

    // -------------------------------------------------------------------------------------------------------
    // Planting, composing and invoking
    // -------------------------------------------------------------------------------------------------------

    /// <summary>A live sequence holding <paramref name="people"/> people, added a minute apart in order.</summary>
    private static ReplyAccount Planted(int people)
    {
        var account = ReplyOperations.Plant(new ReplyAccount());
        for (var at = 0; at < people; at++)
        {
            account.WithContact(1001 + at, $"person{at}@example.com", "Person" + at, lastName: "Surname" + at);
            account.WithEnrollment(Sequence, 1001 + at, addedAt: $"2026-10-02T10:{at:00}:00+00:00");
        }

        return account;
    }

    private static JsonObject Input(int limit, int offset, string? supplied = null, string? pinned = null)
    {
        var args = new JsonObject { ["limit"] = limit, ["offset"] = offset };
        if (supplied is not null)
        {
            args["campaign"] = new JsonObject { ["external_id"] = supplied };
        }

        return new JsonObject
        {
            ["args"] = args,
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
            },
        };
    }

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct);

    private static void Capping(JasonPaths paths, int outputBytes)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!.AsObject()["Exec"] = new JsonObject { ["OutputBytes"] = outputBytes };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
    }
}
