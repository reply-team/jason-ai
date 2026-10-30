using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The provider's own object beside the arguments, <c>args.provider</c>, which the runtime never reads and hands to the
/// package as written. On <c>message.send</c> the package reads one member of it, <c>schedule_id</c>, the Reply sending
/// schedule an email is timed by, written as a string of its digits; anything else is refused before any call with
/// <c>provider_input_invalid</c>, class <c>validation</c>, a message naming the shape it reads and quoting nothing of
/// what it was given. A schedule is a direct send Reply keeps, so it is read first as a moment is; with a moment beside
/// it, Reply times the send at the schedule's first opening at or after the moment, and only estimates when that is.
/// </summary>
public partial class ReplyMessageSendTests
{
    private const string Schedule = "4021";

    private const string ProviderShape =
        "`args.provider` is read on message.send as `{\"schedule_id\": \"<id>\"}` and nothing else, the id of a Reply sending schedule written as a string of its digits; the object given is not that shape.";

    /// <summary>Why a schedule on a LinkedIn message is refused, as the message says it.</summary>
    private const string LinkedInRefusesASchedule =
        "Reply refuses a schedule on every LinkedIn direct send in this version (scheduleId is not supported for LinkedIn sends in this version, a 400 on each LinkedIn route), so a schedule on a LinkedIn message is refused before any call.";

    [Fact]
    public async Task A_message_naming_a_schedule_goes_with_it_once_reply_is_read_to_keep_direct_sends()
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
        Assert.Equal(["account list", "direct-send get email-1", "contact get 1001", "contact send-email"], Lines(account));
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
        Assert.Equal(["account list", "direct-send get email-1", "contact get 1001", "contact send-email"], Lines(account));
    }

    /// <summary>
    /// A schedule the account's Reply cannot time a direct send by is not dropped: the message goes by the schedule or not
    /// at all, so it fails <c>capability_not_offered</c>, its message naming the operation and the member, and nothing is
    /// sent.
    /// </summary>
    [Fact]
    public async Task A_schedule_reply_does_not_keep_is_a_capability_not_offered_and_nothing_is_sent()
    {
        using var account = ByEmail().NotOffering("direct_send");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithSchedule(Input("email", pinned: "1001"))));

        Assert.Equal((UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent), (error.Code, error.Class));
        Assert.StartsWith("message.send: ", error.Message, StringComparison.Ordinal);
        Assert.Contains("`provider.schedule_id`", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Schedule, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Calls, call => call.Line == "contact send-email");
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

    /// <summary>Reply refuses a schedule on every LinkedIn direct send, so a LinkedIn message naming one is refused before any call.</summary>
    [Theory]
    [InlineData("linkedin_message")]
    [InlineData("linkedin_inmail")]
    [InlineData("linkedin_voice")]
    public async Task A_linkedin_message_naming_a_schedule_is_refused_before_any_call(string format)
    {
        using var account = OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithSchedule(Input(format, pinned: "1001"))));

        Assert.Equal((UnjudgedOutcomeCodes.ProviderInputInvalid, FailureClass.Validation), (error.Code, error.Class));
        Assert.StartsWith(LinkedInRefusesASchedule, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Schedule, error.Message, StringComparison.Ordinal);
        Assert.Equal("/args/provider/schedule_id", (string?)error.Details!["pointer"]);
        Assert.Empty(account.Calls);
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
