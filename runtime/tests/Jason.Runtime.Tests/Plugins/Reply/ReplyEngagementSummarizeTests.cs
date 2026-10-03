using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>engagement.summarize</c> as the official package performs it, against a stand-in answering in the shape a live
/// account answered: an overview per channel, every value a whole number — the percentages included — and counts no
/// document names. Each conformance line of the document is a test here, and so is each refusal Reply has a word for.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyEngagementSummarizeTests
{
    private const string Operation = "engagement.summarize";
    private const string StatsPath = "/v3/sequences/7/stats";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_channel_the_provider_reports_is_a_row_of_counts_with_the_envelope_that_says_what_they_count()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithStats(ReplyOperations.Sequence, LiveEmail(), LiveLinkedIn());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["preset"] = "last_month" }, pinned: "7")));
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var rows = answer["rows"]!.AsArray();
        Assert.Equal(["email", "linkedin"], rows.Select(row => (string)row!["group"]!["channel"]!));

        var email = rows[0]!;
        Assert.Equal(
            """{"contacted":120,"delivered":117,"opened":64,"replied":9,"interested":4,"bounced":3,"opted_out":1,"out_of_office":5,"auto_replied":2,"meetings_booked":1}""",
            email["measures"]!.ToJsonString());
        Assert.Equal(
            """{"connections_sent":40,"connections_accepted":11,"messages_sent":11,"replied":2,"inmails_sent":0,"inmails_replied":0,"profile_views":37}""",
            rows[1]!["measures"]!.ToJsonString());

        // Every row says what its numbers count: as of the plugin's own clock, over the window as it was asked.
        var envelope = email["envelope"]!;
        Assert.Equal("""{"preset":"last_month"}""", envelope["window"]!.ToJsonString());
        Assert.Equal("provider_reported", (string?)envelope["basis"]);
        Assert.Equal("unknown", (string?)envelope["denominator"]);
        Assert.Equal("provider", (string?)envelope["detection"]);
        var asOf = DateTimeOffset.Parse((string)envelope["as_of"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(asOf, before, after);
        Assert.EndsWith("Z", (string)envelope["as_of"]!, StringComparison.Ordinal);

        // A rate is never copied, and the counts no document names are not returned.
        var text = answer.ToJsonString();
        Assert.DoesNotContain("ercentage", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not_reached", text, StringComparison.Ordinal);
        Assert.DoesNotContain("likes", text, StringComparison.Ordinal);
        Assert.DoesNotContain("comments", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_window_travels_on_stdin_as_the_provider_s_own_filter()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var span = new JsonObject { ["from"] = "2026-09-01T00:00:00Z", ["to"] = "2026-10-01T00:00:00Z" };
        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(span, supplied: "7")));
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["preset"] = "all_time" }, pinned: "7")));

        Assert.Equal(span.ToJsonString(), answer["rows"]![0]!["envelope"]!["window"]!.ToJsonString());
        Assert.Equal(
            ["""{"filters":{"from":"2026-09-01T00:00:00Z","to":"2026-10-01T00:00:00Z"}}""", """{"filters":{"dateRangePreset":"allTime"}}"""],
            account.Calls.Select(call => call.Body));
        Assert.All(account.Calls, call => Assert.Equal(["--json", "-q", "api", StatsPath, "--method", "POST", "--body", "-"], call.Args));
    }

    [Fact]
    public async Task A_count_the_provider_does_not_report_is_absent_and_never_zero_and_a_channel_it_does_not_report_has_no_row()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithStats(
            ReplyOperations.Sequence,
            new JsonObject { ["contacted"] = 120, ["delivered"] = 117.5, ["opened"] = -3, ["replied"] = "9", ["bounced"] = 0 },
            linkedInOverview: null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["preset"] = "last_week" }, pinned: "7")));

        var row = Assert.Single(answer["rows"]!.AsArray())!;
        Assert.Equal("""{"contacted":120,"bounced":0}""", row["measures"]!.ToJsonString());
    }

    [Fact]
    public async Task A_campaign_the_provider_does_not_hold_fails_permanently_and_is_not_pinned()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["preset"] = "last_week" }, supplied: "7")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.Equal("sequenceStats.notFound", (string?)error.Details!["provider_code"]);
        Assert.Null(error.ExternalIds);
    }

    [Fact]
    public async Task A_window_the_provider_refuses_is_a_validation_failure()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var backwards = new JsonObject { ["from"] = "2026-10-01T00:00:00Z", ["to"] = "2026-09-01T00:00:00Z" };
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(backwards, pinned: "7")));

        Assert.Equal("window_invalid", error.Code);
        Assert.Equal(FailureClass.Validation, error.Class);
    }

    [Theory]
    [InlineData("sequenceStats.forbidden", "feature_unavailable")]
    [InlineData("auth.scopeMissing", "unauthorized")]
    public async Task A_403_is_a_feature_the_account_lacks_only_where_reply_says_so(string providerCode, string expected)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Answers("POST", StatsPath, 403, new JsonObject
        {
            ["type"] = "about:blank",
            ["title"] = "Forbidden",
            ["status"] = 403,
            ["code"] = providerCode,
        }.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["preset"] = "last_week" }, pinned: "7")));

        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public async Task A_read_writes_nothing_and_pins_nothing_but_the_campaign()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var before = (account.Contacts.ToJsonString(), account.Enrollments.ToJsonString(), account.Sequences.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(new JsonObject { ["preset"] = "last_week" }, supplied: "7"));

        ReplyOperations.Succeeded(Operation, result);
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("""{"campaign":"7"}""", outcome.ExternalIds!.ToJsonString());
        Assert.Single(account.Calls);
        Assert.Equal(before, (account.Contacts.ToJsonString(), account.Enrollments.ToJsonString(), account.Sequences.ToJsonString()));
    }

    /// <summary>The email overview a live account answered, every value — the percentages too — a whole number.</summary>
    private static JsonObject LiveEmail() => new()
    {
        ["contacted"] = 120, ["delivered"] = 117, ["opened"] = 64, ["replied"] = 9, ["interested"] = 4, ["notReached"] = 3,
        ["optedOut"] = 1, ["outOfOffice"] = 5, ["bounced"] = 3, ["autoReplied"] = 2, ["meetingsBooked"] = 1,
        ["deliveredPercentage"] = 98, ["openedPercentage"] = 55, ["repliedPercentage"] = 8, ["interestedPercentage"] = 3,
        ["notReachedPercentage"] = 2, ["optedOutPercentage"] = 1, ["outOfOfficePercentage"] = 4, ["bouncedPercentage"] = 3,
        ["autoRepliedPercentage"] = 2, ["meetingsBookedPercentage"] = 1,
    };

    private static JsonObject LiveLinkedIn() => new()
    {
        ["connectionsSent"] = 40, ["connectionsAccepted"] = 11, ["messagesSent"] = 11, ["replied"] = 2, ["inMailsSent"] = 0,
        ["inMailsReplied"] = 0, ["connectionNotesSent"] = 6, ["connectionNotesReplied"] = 1, ["profileViews"] = 37, ["likes"] = 4,
        ["follows"] = 2, ["endorses"] = 0, ["comments"] = 1, ["regularMessagesSent"] = 5, ["regularMessagesReplied"] = 1,
        ["connectionsAcceptedPercentage"] = 27, ["repliedPercentage"] = 18, ["inMailsRepliedPercentage"] = 0,
        ["connectionNotesRepliedPercentage"] = 16, ["regularMessagesRepliedPercentage"] = 20,
    };

    private static JsonObject Input(JsonObject window, string? supplied = null, string? pinned = null)
    {
        var args = new JsonObject { ["window"] = window, ["group_by"] = "channel" };
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
}
