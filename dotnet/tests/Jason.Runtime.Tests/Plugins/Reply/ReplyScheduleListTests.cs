using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>schedule.list</c> as the official package performs it: <c>reply schedule list</c> gives every schedule the team can
/// use in one answer, and the package pages it by Reply's identifier, the lowest first. Each time zone is Reply's Windows
/// identifier, carried as written under the <c>windows</c> scheme; the windows are Reply's as authored.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyScheduleListTests
{
    private const string Operation = "schedule.list";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Three schedules, planted out of Reply's identifier order.</summary>
    private static ReplyAccount Planted() => new ReplyAccount()
        .WithSchedule(57, "Madrid mornings", [ReplyAccount.Day("Friday", true, (8, 30, 12, 0), (13, 0, 14, 5))], timeZone: "Romance Standard Time", status: null,
            recipientTimeZone: true, followUp: [ReplyAccount.Day("Saturday", false)], useFollowUp: true)
        .WithSchedule(41, "Business hours", [ReplyAccount.Day("Monday", true, (9, 0, 17, 0)), ReplyAccount.Day("Saturday", false)], isDefault: true, status: "isDefaultForCurrentUser", skipsHolidays: true)
        .WithSchedule(9, " ", [], followUp: [ReplyAccount.Day("Monday", true, (10, 0, 11, 0))], useFollowUp: false);

    [Fact]
    public async Task Lists_every_schedule_by_its_identifier_with_its_windows_as_authored()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(
            JsonNode.Parse("""
                {"items":[
                  {"external_id":"9","name":null,"time_zone":{"id":"Eastern Standard Time","scheme":"windows"},"in_recipient_time_zone":false,
                   "skips_holidays":false,"default":false,"windows":[],"follow_up_windows":null,"vendor":{"status":"inCurrentUserSequence"}},
                  {"external_id":"41","name":"Business hours","time_zone":{"id":"Eastern Standard Time","scheme":"windows"},"in_recipient_time_zone":false,
                   "skips_holidays":true,"default":true,
                   "windows":[{"weekday":"monday","active":true,"ranges":[{"from":"09:00","to":"17:00"}]},{"weekday":"saturday","active":false,"ranges":[]}],
                   "follow_up_windows":null,"vendor":{"status":"isDefaultForCurrentUser"}},
                  {"external_id":"57","name":"Madrid mornings","time_zone":{"id":"Romance Standard Time","scheme":"windows"},"in_recipient_time_zone":true,
                   "skips_holidays":false,"default":false,
                   "windows":[{"weekday":"friday","active":true,"ranges":[{"from":"08:30","to":"12:00"},{"from":"13:00","to":"14:05"}]}],
                   "follow_up_windows":[{"weekday":"saturday","active":false,"ranges":[]}],"vendor":{"status":null}}],
                 "has_more":false,"vendor":{"ordering":"id_asc"}}
                """)!.ToJsonString(),
            result.ToJsonString());
        Assert.Equal(["schedule list"], account.Calls.Select(call => call.Line));
        Assert.Equal(["--json", "-q", "schedule", "list"], account.Calls[^1].Args);
    }

    [Theory]
    [InlineData(0, 2, new[] { "9", "41" }, true)]
    [InlineData(2, 2, new[] { "57" }, false)]
    [InlineData(1, 1, new[] { "41" }, true)]
    [InlineData(3, 5, new string[0], false)]
    public async Task A_page_starts_at_its_offset_by_the_identifier_lowest_first(int offset, int limit, string[] expected, bool more)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(offset, limit), Ct));

        Assert.Equal(expected, result["items"]!.AsArray().Select(item => (string)item!["external_id"]!));
        Assert.Equal(more, (bool)result["has_more"]!);
    }

    /// <summary>
    /// A weekday Reply's answer does not name, a time that is no time of day and a schedule with no time zone are left out
    /// rather than guessed at: what is listed is what Reply said.
    /// </summary>
    [Fact]
    public async Task What_reply_does_not_say_plainly_is_left_out_rather_than_guessed_at()
    {
        using var account = Planted();
        account.Returns("schedule list", new JsonObject
        {
            ["items"] = new JsonArray(
                Listed(3, "No zone", timeZone: null),
                Listed(5, "Kept", new JsonArray(
                    Timing("someday", true, ("09:00", "10:00")),
                    Timing("tuesday", true, ("9:00", "10:00"), ("10:00", "24:30"), ("10:00", "11:30")),
                    Timing("tuesday", false)))),
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        var items = result["items"]!.AsArray();
        Assert.Equal(["5"], items.Select(item => (string)item!["external_id"]!));
        Assert.Equal(
            """[{"weekday":"tuesday","active":true,"ranges":[{"from":"10:00","to":"11:30"}]}]""",
            items[0]!["windows"]!.ToJsonString());
    }

    /// <summary>A window may run to the end of the day, which Reply writes as 24:00; no window starts there.</summary>
    [Fact]
    public async Task A_window_may_end_at_midnight_and_never_start_there()
    {
        using var account = Planted();
        account.Returns("schedule list", new JsonObject
        {
            ["items"] = new JsonArray(
                Listed(5, "Evenings", new JsonArray(Timing("tuesday", true, ("18:00", "24:00"), ("24:00", "24:00"), ("00:00", "06:00"))))),
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(
            """[{"weekday":"tuesday","active":true,"ranges":[{"from":"18:00","to":"24:00"},{"from":"00:00","to":"06:00"}]}]""",
            result["items"]!.AsArray()[0]!["windows"]!.ToJsonString());
    }

    /// <summary>
    /// The short page. Schedules that each carry a full week of the most windows a day may hold are cut at the plugin's own
    /// budget, which counts coarser than the runtime does (a quote at six bytes) and so stops short of the cap: the plugin
    /// says there is more, and the page that follows, started at the offset plus what the first returned, carries on with
    /// no schedule lost and none repeated.
    /// </summary>
    [Fact]
    public async Task A_page_of_schedules_too_large_for_one_result_comes_back_short_and_the_next_one_carries_on()
    {
        const int Schedules = 80;
        var week = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var ranges = Enumerable.Range(0, 24).Select(hour => (hour, 0, hour, 30)).ToArray();
        using var account = new ReplyAccount();
        for (var id = 1; id <= Schedules; id++)
        {
            account.WithSchedule(id, new string('Ж', 200), [.. week.Select(day => ReplyAccount.Day(day, true, ranges))]);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var pages = new List<int>();
        var offset = 0;
        var more = true;
        while (more)
        {
            var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(offset, 100), Ct));
            var items = result["items"]!.AsArray();
            seen.AddRange(items.Select(item => (string)item!["external_id"]!));
            pages.Add(items.Count);
            more = (bool)result["has_more"]!;
            offset += items.Count;
            Assert.True(pages.Count <= 3, $"the pages never ended: {string.Join(",", pages)} so far");
        }

        Assert.True(pages[0] < Schedules, $"the first page held all {Schedules} schedules, so nothing was cut");
        Assert.True(pages.Count > 1, $"the pages were {string.Join(",", pages)}");
        Assert.Equal(Enumerable.Range(1, Schedules).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), seen);
    }

    /// <summary>
    /// What reply-cli prints of the CLI's own reading of a day: a weekday it could not read and a time that is no time of
    /// day come as null, and are left out as well.
    /// </summary>
    [Fact]
    public async Task What_the_cli_prints_null_is_left_out()
    {
        using var account = new ReplyAccount().WithSchedule(5, "Kept",
            [ReplyAccount.Day("", true, (9, 0, 10, 0)), ReplyAccount.Day("Tuesday", true, (25, 0, 26, 0), (9, 0, 24, 0))]);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(
            """[{"weekday":"tuesday","active":true,"ranges":[{"from":"09:00","to":"24:00"}]}]""",
            result["items"]!.AsArray()[0]!["windows"]!.ToJsonString());
    }

    /// <summary>
    /// The follow-up windows are listed only when the schedule uses them and Reply gave them as a list; a schedule that
    /// uses them with none given has none.
    /// </summary>
    [Fact]
    public async Task Follow_up_windows_used_but_not_given_are_none()
    {
        using var account = new ReplyAccount().WithSchedule(5, "Follow-ups", [], followUp: null, useFollowUp: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        var item = result["items"]!.AsArray()[0]!;
        Assert.True(item["follow_up_windows"] is null, $"follow_up_windows was {item["follow_up_windows"]?.ToJsonString()}");
    }

    [Fact]
    public async Task A_key_without_the_scope_fails_unauthorized()
    {
        using var account = Planted().Without("schedules");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("unauthorized", FailureClass.Permanent), (error.Code, error.Class));
    }

    /// <summary>
    /// reply-cli refuses a whole list holding a schedule without a usable identifier; that is no answer, and a read changes
    /// nothing, so it is asked again.
    /// </summary>
    [Fact]
    public async Task The_cli_refuses_a_list_with_a_schedule_without_an_id_and_it_is_asked_again()
    {
        using var account = Planted().WithSchedule(-4, "No identifier", []);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
    }

    /// <summary>An answer that is not a list of schedules is no answer; a read changes nothing, so it is asked again.</summary>
    [Fact]
    public async Task An_answer_that_is_not_a_list_is_asked_again()
    {
        using var account = Planted();
        account.Returns("schedule list", new JsonObject { ["schedules"] = new JsonArray() });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
    }

    [Fact]
    public async Task A_lost_answer_is_asked_again_and_pins_nothing()
    {
        using var account = Planted().LosesTheOutcome("schedule list");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
    }

    private static JsonObject Listed(long id, string name, JsonArray? timings = null, string? timeZone = "W. Europe Standard Time") => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["time_zone_id"] = timeZone,
        ["use_recipient_time_zone"] = false,
        ["exclude_holidays"] = false,
        ["is_default"] = false,
        ["main_timings"] = timings ?? [],
        ["use_follow_up_schedule"] = false,
        ["follow_up_timings"] = null,
        ["reply_status"] = "inCurrentUserSequence",
    };

    private static JsonObject Timing(string weekday, bool active, params (string From, string To)[] ranges) => new()
    {
        ["week_day"] = weekday,
        ["active"] = active,
        ["ranges"] = new JsonArray([.. ranges.Select(range => (JsonNode)new JsonObject { ["from"] = range.From, ["to"] = range.To })]),
    };

    private static JsonObject Input(int offset = 0, int limit = 20) => new()
    {
        ["args"] = new JsonObject { ["limit"] = limit, ["offset"] = offset },
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject(),
        },
    };
}
