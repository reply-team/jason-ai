using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's <c>schedule list</c>, answered as the reply-cli release <see cref="Jason.ReplyCliStub.ReplyCliRelease"/>
/// names documents it: every schedule the team can use in one answer, in the order the account holds them, the time
/// zone as Reply spells it, the weekday in lower case and each time of day as <c>HH:MM</c>.
/// </summary>
public class ReplyStubScheduleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Schedule_list_answers_every_schedule_with_its_windows_as_reply_holds_them()
    {
        using var account = new ReplyAccount()
            .WithSchedule(57, "Madrid mornings", [ReplyAccount.Day("Friday", true, (8, 30, 12, 0), (13, 0, 14, 5))], timeZone: "Romance Standard Time", status: null,
                recipientTimeZone: true, followUp: [ReplyAccount.Day("Saturday", false)], useFollowUp: true)
            .WithSchedule(41, " ", [ReplyAccount.Day("Monday", true, (9, 0, 17, 0))], isDefault: true, status: "isDefaultForCurrentUser", skipsHolidays: true);

        var (exit, stdout, _) = await account.RunAsync(Line("schedule", "list"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal(
            """[{"id":57,"name":"Madrid mornings","time_zone_id":"Romance Standard Time","use_recipient_time_zone":true,"exclude_holidays":false,"is_default":false,"main_timings":"""
            + """[{"week_day":"friday","active":true,"ranges":[{"from":"08:30","to":"12:00"},{"from":"13:00","to":"14:05"}]}],"use_follow_up_schedule":true,"follow_up_timings":"""
            + """[{"week_day":"saturday","active":false,"ranges":[]}],"reply_status":null},"""
            + """{"id":41,"name":null,"time_zone_id":"Eastern Standard Time","use_recipient_time_zone":false,"exclude_holidays":true,"is_default":true,"main_timings":"""
            + """[{"week_day":"monday","active":true,"ranges":[{"from":"09:00","to":"17:00"}]}],"use_follow_up_schedule":false,"follow_up_timings":null,"reply_status":"isDefaultForCurrentUser"}]""",
            JsonNode.Parse(stdout)!["items"]!.ToJsonString());
    }

    [Fact]
    public async Task Schedule_list_takes_no_option_and_no_id()
    {
        using var account = new ReplyAccount();

        var (option, _, _) = await account.RunAsync(Line("schedule", "list", "--name", "Business hours"), Ct);
        var (id, _, _) = await account.RunAsync(Line("schedule", "list", "41"), Ct);

        Assert.Equal((2, 2), (option, id));
    }

    /// <summary>
    /// A key without Reply's sequences read scope is refused the whole list. Reply's refusal carries no code of its own,
    /// so reply-cli's envelope has no <c>reply_code</c>.
    /// </summary>
    [Fact]
    public async Task A_key_without_the_scope_is_refused_the_whole_list_with_no_reply_code()
    {
        using var account = new ReplyAccount().WithSchedule(41, "Business hours", []).Without("schedules");

        var (exit, _, said) = await account.RunAsync(Line("schedule", "list"), Ct);

        Assert.Equal(1, exit);
        var error = Error(said);
        Assert.Equal(["code", "status", "title"], error.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            ("access.feature_unavailable", 403, "The account's plan or role does not include this."),
            ((string?)error["code"], (int)error["status"]!, (string?)error["title"]));
    }

    /// <summary>A schedule without a usable identifier fails the whole list, as the CLI refuses a list it cannot key.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task A_schedule_without_a_usable_id_fails_the_whole_list(long id)
    {
        using var account = new ReplyAccount().WithSchedule(41, "Business hours", []).WithSchedule(id, "No identifier", []);

        var (exit, stdout, said) = await account.RunAsync(Line("schedule", "list"), Ct);

        Assert.Equal((1, ""), (exit, stdout.Trim()));
        var error = Error(said);
        Assert.Equal(("reply.unavailable", 200), ((string?)error["code"], (int)error["status"]!));
    }

    /// <summary>A weekday Reply does not name and a time that is no time of day are printed null; a range may end at 24:00.</summary>
    [Fact]
    public async Task An_unreadable_weekday_or_time_is_printed_null()
    {
        using var account = new ReplyAccount().WithSchedule(41, "Business hours",
            [ReplyAccount.Day("", true, (9, 0, 24, 0)), ReplyAccount.Day("Monday", true, (25, 0, 9, 60), (24, 30, 10, 0))]);

        var (exit, stdout, _) = await account.RunAsync(Line("schedule", "list"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal(
            """[{"week_day":null,"active":true,"ranges":[{"from":"09:00","to":"24:00"}]},"""
            + """{"week_day":"monday","active":true,"ranges":[{"from":null,"to":null},{"from":null,"to":"10:00"}]}]""",
            JsonNode.Parse(stdout)!["items"]![0]!["main_timings"]!.ToJsonString());
    }

    private static string[] Line(params string[] command) => ["--json", "-q", .. command];

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();
}
