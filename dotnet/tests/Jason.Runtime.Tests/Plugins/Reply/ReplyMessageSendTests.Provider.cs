using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The provider's own object beside the arguments, <c>args.provider</c>, which the runtime never reads and hands to the
/// package as written. On <c>message.send</c> the package reads one member of it, <c>schedule_id</c>, the Reply sending
/// schedule an email is timed by, written as a string of its digits; anything else is refused before any call with
/// <c>provider_input_invalid</c>, class <c>validation</c>, a message naming the shape it reads and quoting nothing of
/// what it was given. A schedule goes on the send itself with nothing read before it for it; with a moment beside it,
/// Reply times the send at the schedule's first opening at or after the moment, and only estimates when that is.
/// </summary>
public partial class ReplyMessageSendTests
{
    private const string Schedule = "4021";

    private const string ProviderShape =
        "`args.provider` is read on message.send as `{\"schedule_id\": \"<id>\"}` and nothing else, the id of a Reply sending schedule written as a string of its digits; the object given is not that shape.";

    [Fact]
    public async Task A_message_naming_a_schedule_goes_with_it()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithSchedule(Input("email", pinned: "1001"))))["items"]![0]!;

        Assert.Equal(("accepted", "email-1001", true), ((string?)item["status"], (string?)item["reference"], (bool)item["cancelable"]!));
        // While the send waits for the schedule Reply names no moment, and the result names none.
        Assert.Null(Assert.Single(account.DirectSends)!["sendAfter"]);
        Assert.Null(item["accepted_for"]);
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal((Schedule, Key), (account.Calls[^1].Option("--schedule-id"), account.Calls[^1].Option("--idempotency-key")));
        Assert.Null(account.Calls[^1].Option("--send-after"));
        Assert.All(account.Calls.SkipLast(1), call => Assert.Null(call.Option("--schedule-id")));
        Assert.Equal(4021, (int)Assert.Single(account.DirectSends)!["scheduleId"]!);
    }

    /// <summary>
    /// A schedule and a moment go together: Reply sends at the schedule's first opening at or after the moment, and the
    /// moment it answers is its estimate of that, not one it holds the message for, so the result names none.
    /// </summary>
    [Fact]
    public async Task A_schedule_and_a_moment_go_together_and_the_result_names_no_moment()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithSchedule(WithMoment(Input("email", pinned: "1001"), "2099-11-03T09:00:00-05:00"))))["items"]![0]!;

        Assert.Equal(("accepted", true), ((string?)item["status"], (bool)item["cancelable"]!));
        Assert.Null(Assert.Single(account.DirectSends)!["sendAfter"]);
        Assert.Null(item["accepted_for"]);
        Assert.Equal(("2099-11-03T09:00:00-05:00", Schedule), (account.Calls[^1].Option("--send-after"), account.Calls[^1].Option("--schedule-id")));
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
    }

    /// <summary>
    /// Reply answers an email timed by a schedule in one of two ways: where the schedule is open now, or opens within ten
    /// minutes, the send is <c>queued</c> with its moment under <c>send_after</c>; where it opens later, the send is
    /// <c>waitingForSchedule</c> and Reply only estimates the moment. Both are accepted, both can still be cancelled, and
    /// neither names a moment: a message timed by a schedule goes when the schedule lets it.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1001","state":"queued","send_after":"2099-11-03T14:05:00Z"}""")]
    [InlineData("""{"reference":"email-1001","state":"waitingForSchedule","estimated_send_at":"2099-11-03T14:30:00Z"}""")]
    public async Task A_message_timed_by_a_schedule_is_accepted_without_a_moment_however_reply_holds_it(string given)
    {
        using var account = ByEmail();
        account.Returns("contact send-email", Answer(given));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithSchedule(Input("email", pinned: "1001"))))["items"]![0]!.AsObject();

        Assert.Equal(("accepted", "email-1001", true), ((string?)item["status"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.False(item.ContainsKey("accepted_for"), item.ToJsonString());
        Assert.Equal(Schedule, account.Calls[^1].Option("--schedule-id"));
    }

    /// <summary>
    /// Reply answers a LinkedIn message timed by a schedule as it answers an
    /// email: <c>queued</c> with the opening under <c>send_after</c> where the schedule is open now or opens within ten
    /// minutes, and <c>waitingForSchedule</c> with no moment where it opens later, both under the scheduled reference.
    /// Both are accepted, both can still be cancelled, and neither names a moment.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", """{"reference":"linkedin-scheduled-1001","state":"waitingForSchedule","send_after":null}""")]
    [InlineData("linkedin_voice", """{"reference":"linkedin-scheduled-1001","state":"waitingForSchedule","send_after":null}""")]
    [InlineData("linkedin_inmail", """{"reference":"linkedin-scheduled-1001","state":"queued","send_after":"2099-11-03T14:05:00Z"}""")]
    public async Task A_linkedin_message_timed_by_a_schedule_is_accepted_without_a_moment_however_reply_holds_it(string format, string given)
    {
        using var account = OnLinkedIn();
        account.Returns(FormatCommand(format), Answer(given));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithSchedule(Input(format, pinned: "1001"))))["items"]![0]!.AsObject();

        Assert.Equal(("accepted", "linkedin-scheduled-1001", true), ((string?)item["status"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.False(item.ContainsKey("accepted_for"), item.ToJsonString());
        Assert.Equal(Schedule, account.Calls[^1].Option("--schedule-id"));
    }

    /// <summary>
    /// A message waiting for a schedule that opens later is answered by reply-cli 0.18.0 with the moment Reply expects to
    /// send it, <c>estimated_send_at</c>, or null where the schedule does not open within 30 days. An estimate is not a
    /// moment Reply holds the message for, so it is never <c>accepted_for</c>: the message is accepted, can still be
    /// cancelled, and names no moment, on an email and on a LinkedIn message alike.
    /// </summary>
    [Theory]
    [InlineData("email", "2099-11-04T14:00:00Z")]
    [InlineData("email", null)]
    [InlineData("linkedin_message", "2099-11-04T14:00:00Z")]
    [InlineData("linkedin_inmail", null)]
    public async Task A_message_waiting_for_its_schedule_is_accepted_without_the_moment_reply_estimates(string format, string? estimate)
    {
        using var account = (format == "email" ? ByEmail() : OnLinkedIn())
            .WithSchedule(4021, "Next week", [ReplyAccount.Day("Monday", true, (9, 0, 17, 0))], opensLater: true, estimate: estimate);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithSchedule(Input(format, pinned: "1001"))))["items"]![0]!.AsObject();

        Assert.Equal(("accepted", true), ((string?)item["status"], (bool)item["cancelable"]!));
        Assert.False(item.ContainsKey("accepted_for"), item.ToJsonString());
        Assert.Equal("waitingForSchedule", (string?)Assert.Single(account.DirectSends)!["state"]);
    }

    /// <summary>
    /// A schedule is not dropped: where Reply answers the send timed by it with a 404 that carries no code of Reply's
    /// own, the message goes by the schedule or not at all, so it fails <c>provider_call_failed</c>, final, with
    /// <c>reply.refused</c> and the 404 in its details, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_schedule_whose_send_meets_a_404_without_reply_s_code_is_a_provider_call_failed_and_nothing_is_sent()
    {
        using var account = ByEmail().NotOffering("send_direct_email");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithSchedule(Input("email", pinned: "1001"))));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.DoesNotContain(Schedule, error.Message, StringComparison.Ordinal);
        Assert.Equal(("reply.refused", 404), ((string?)error.Details!["cli_code"], (int?)error.Details["status"]));
        Assert.Null(error.Details["provider_code"]);
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal(Schedule, account.Calls[^1].Option("--schedule-id"));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// An object that names another member, or none, is refused before anything is called: the pointer is the object's,
    /// and the message the shape this package reads, the same sentence whatever was given.
    /// </summary>
    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"schedule":"4021"}""")]
    [InlineData("""{"schedule_id":"4021","send_window":"weekdays"}""")]
    [InlineData("""{"send_window":{"days":["mon"],"label":"café de mañana"}}""")]
    public async Task An_object_of_another_shape_is_refused_before_any_call_and_nothing_of_it_is_quoted(string provider)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithProvider(Input("email", pinned: "1001"), JsonNode.Parse(provider)!)));

        Assert.Equal((UnjudgedOutcomeCodes.ProviderInputInvalid, FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(ProviderShape, error.Message);
        Assert.Equal(("/args/provider", "object whose one member is schedule_id"), ((string?)error.Details!["pointer"], (string?)error.Details["expected"]));
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A schedule that is not a string of a Reply id's digits — a number, a zero, a leading zero, one past the largest id,
    /// a space, nothing, or another kind — is refused before anything is called, its pointer the member's.
    /// </summary>
    [Theory]
    [InlineData("4021")]
    [InlineData("\"0\"")]
    [InlineData("\"04021\"")]
    [InlineData("\"2147483648\"")]
    [InlineData("\"99999999999\"")]
    [InlineData("\" 4021\"")]
    [InlineData("\"4021 \"")]
    [InlineData("\"+4021\"")]
    [InlineData("\"4021.0\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task A_schedule_that_is_not_a_reply_id_s_digits_is_refused_before_any_call(string value)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var provider = new JsonObject { ["schedule_id"] = JsonNode.Parse(value) };
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithProvider(Input("email", pinned: "1001"), provider)));

        Assert.Equal((UnjudgedOutcomeCodes.ProviderInputInvalid, FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(ProviderShape, error.Message);
        Assert.Equal("/args/provider/schedule_id", (string?)error.Details!["pointer"]);
        Assert.Empty(account.Calls);
    }

    /// <summary>The largest id Reply has, 2147483647, is one this package reads.</summary>
    [Fact]
    public async Task The_largest_reply_id_is_a_schedule_this_package_reads()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithProvider(Input("email", pinned: "1001"), new JsonObject { ["schedule_id"] = "2147483647" })));

        Assert.Equal("2147483647", account.Calls[^1].Option("--schedule-id"));
    }

    /// <summary>
    /// A LinkedIn message names a schedule as an email does: the schedule goes as <c>--schedule-id</c> on the format's own
    /// command, beside the key and on no other command, with nothing read before it for it.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message")]
    [InlineData("linkedin_inmail")]
    [InlineData("linkedin_voice")]
    public async Task A_linkedin_message_naming_a_schedule_goes_with_it(string format)
    {
        using var account = OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithSchedule(Input(format, pinned: "1001"))))["items"]![0]!.AsObject();

        Assert.Equal(("accepted", true), ((string?)item["status"], (bool)item["cancelable"]!));
        Assert.False(item.ContainsKey("accepted_for"), item.ToJsonString());
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001", FormatCommand(format)], Lines(account));
        Assert.Equal((Schedule, Key), (account.Calls[^1].Option("--schedule-id"), account.Calls[^1].Option("--idempotency-key")));
        Assert.All(account.Calls.SkipLast(1), call => Assert.Null(call.Option("--schedule-id")));
        Assert.Equal(4021, (int)Assert.Single(account.DirectSends)!["scheduleId"]!);
    }

    /// <summary>
    /// A LinkedIn message naming a schedule or a moment ahead goes on its own command with nothing read before it; where
    /// Reply answers that command with a 404 that carries no code of Reply's own, it ends <c>provider_call_failed</c>,
    /// final, with <c>reply.refused</c> and the 404 in its details, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", "schedule")]
    [InlineData("linkedin_inmail", "schedule")]
    [InlineData("linkedin_voice", "schedule")]
    [InlineData("linkedin_message", "moment")]
    [InlineData("linkedin_inmail", "moment")]
    [InlineData("linkedin_voice", "moment")]
    public async Task A_linkedin_message_whose_command_meets_a_404_without_reply_s_code_is_a_provider_call_failed(string format, string held)
    {
        using var account = OnLinkedIn();
        account.Refuses(FormatCommand(format), "reply.refused", status: 404);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = held == "schedule"
            ? WithSchedule(Input(format, pinned: "1001"))
            : WithMoment(Input(format, pinned: "1001"), "2099-11-03T14:00:00Z");

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(("reply.refused", 404), ((string?)error.Details!["cli_code"], (int?)error.Details["status"]));
        Assert.Null(error.Details["provider_code"]);
        Assert.Equal(["account list", "contact get 1001", FormatCommand(format)], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A send timed by a schedule Reply cannot time it by: one the account holds none under, unknown and another
    /// account's alike (<c>schedule.not_found</c> at 404); one with no day that has hours to send in
    /// (<c>schedule.no_slot</c> at 409); and one that keeps the contact's own time zone for a contact Reply holds none for
    /// (<c>contact.time_zone_missing</c> at 409). Reply names each in the route's own family, <c>contact.</c> on an email
    /// and <c>directLinkedIn.</c> on a LinkedIn message, and reply-cli 0.18.0 gives both the one code
    /// (<c>src/operations/direct-send.ts</c>:130-138 and :192-197), so an email and a LinkedIn message end on the same
    /// row: final, naming its own remedy, with the code it was read by, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("email", "unknown", "schedule.not_found", "is not one Reply holds for this account")]
    [InlineData("email", "closed", "schedule.no_slot", "has no day with hours to send in")]
    [InlineData("email", "zoneless", "contact.time_zone_missing", "keeps the contact's own time zone")]
    [InlineData("linkedin_message", "unknown", "schedule.not_found", "is not one Reply holds for this account")]
    [InlineData("linkedin_inmail", "closed", "schedule.no_slot", "has no day with hours to send in")]
    [InlineData("linkedin_voice", "zoneless", "contact.time_zone_missing", "keeps the contact's own time zone")]
    [InlineData("linkedin_message", "zoneless", "contact.time_zone_missing", "keeps the contact's own time zone")]
    public async Task A_schedule_reply_cannot_time_a_send_by_ends_an_email_as_it_ends_a_linkedin_message(string format, string kind, string code, string said)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        _ = kind switch
        {
            "unknown" => account.WithSchedule(4022, "Weekdays", [ReplyAccount.Day("Monday", true, (9, 0, 17, 0))]),
            "closed" => account.WithSchedule(4021, "Never open", [ReplyAccount.Day("Monday", false, (9, 0, 17, 0)), ReplyAccount.Day("Tuesday", true)]),
            _ => account.WithSchedule(4021, "Their mornings", [ReplyAccount.Day("Monday", true, (9, 0, 12, 0))], recipientTimeZone: true),
        };
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithSchedule(Input(format, pinned: "1001"))));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal(code, (string?)error.Details!["cli_code"]);
        Assert.Equal(["account list", "contact get 1001", FormatCommand(format)], Lines(account));
        Assert.Empty(account.Sent);
        Assert.Empty(account.DirectSends);
    }

    /// <summary>
    /// A LinkedIn route that does not take a schedule answers one with a 400 whose <c>errors[]</c> names the field (scheduleId is not supported for LinkedIn sends in this version.). reply-cli
    /// keeps only the field's name, as <c>reply.invalid_request</c> with <c>details.fields</c> (<c>invalid_fields</c> and
    /// <c>refusal</c> in <c>src/operations/call.ts</c>). The package gives Reply only a schedule it can read, so the field
    /// refused is the route not taking one: <c>capability_not_offered</c> naming the member, never a request built wrong.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message")]
    [InlineData("linkedin_inmail")]
    [InlineData("linkedin_voice")]
    public async Task A_linkedin_route_that_does_not_take_a_schedule_is_a_capability_not_offered(string format)
    {
        using var account = OnLinkedIn();
        account.Refuses(FormatCommand(format), "reply.invalid_request", status: 400, details: new JsonObject { ["fields"] = new JsonArray("scheduleId") });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithSchedule(Input(format, pinned: "1001"))));

        Assert.Equal((UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("`provider.schedule_id`", error.Message, StringComparison.Ordinal);
        Assert.Contains("message.send", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A 400 whose <c>errors[]</c> names another field of a send timed by a schedule is a request Reply refused in a way this
    /// version has no word for, as any field the operation has no row for: only <c>scheduleId</c> says the route does not
    /// take a schedule.
    /// </summary>
    [Fact]
    public async Task A_linkedin_schedule_refused_for_another_field_is_a_refusal_this_version_has_no_word_for()
    {
        using var account = OnLinkedIn();
        account.Refuses("contact linkedin-message", "reply.invalid_request", status: 400, details: new JsonObject { ["fields"] = new JsonArray("sendAfter") });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithSchedule(Input("linkedin_message", pinned: "1001"))));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("a word this version of the package does not know", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>A message the work item carries no provider object for reads none, and goes as it always went.</summary>
    [Fact]
    public async Task A_message_without_the_object_carries_no_schedule()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.All(account.Calls, call => Assert.Null(call.Option("--schedule-id")));
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
    }

    private static JsonObject WithSchedule(JsonObject input) => WithProvider(input, new JsonObject { ["schedule_id"] = Schedule });

    private static JsonObject WithProvider(JsonObject input, JsonNode provider)
    {
        input["args"]!["provider"] = provider;
        return input;
    }
}
