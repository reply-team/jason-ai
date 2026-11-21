using System.Globalization;
using System.Text.Json.Nodes;
using Jint;
using Jint.Runtime;

namespace Jason.PluginHost.Tests.Packages;

/// <summary>
/// The moment the Reply package says a mailbox's daily limit clears, on a clock that does not move. The plugin host reads the
/// machine's clock and offers no other, so the package's own error table is run here, beside the engine it runs in, in an
/// engine of its own whose clock is pinned, with a stand-in for the one host function it calls, <c>host.fail</c>, that hands
/// back what it is given. The instants are in the middle of a month that changes nothing at its end, but for one row put at
/// the turn of a year on purpose, and UTC keeps no daylight saving time.
/// </summary>
public class ReplyEmailLimitClockTests
{
    /// <summary>The official Reply package as MSBuild links it next to these tests, the same files the release publishes.</summary>
    private static string PackageSource => Path.Combine(AppContext.BaseDirectory, "Fixtures", "packages", "reply");

    /// <summary>
    /// Reply counts a mailbox's day in UTC, so the daily limit's block clears at the next midnight UTC: the same moment from
    /// any instant of the day, the midnight that starts it included, and no later than the day's last millisecond allows.
    /// </summary>
    [Theory]
    [InlineData("2026-11-18T13:45:10.250Z", "2026-11-19T00:00:00.000Z")]
    [InlineData("2026-11-18T00:00:00.000Z", "2026-11-19T00:00:00.000Z")]
    [InlineData("2026-11-18T23:59:59.999Z", "2026-11-19T00:00:00.000Z")]
    [InlineData("2026-12-31T23:59:59.000Z", "2027-01-01T00:00:00.000Z")]
    public void The_daily_limit_clears_at_the_next_midnight_utc(string now, string clears)
    {
        var block = BlockAt(now, "rate_limited", 429, "contact.dailyLimitReached", wait: null);

        Assert.Equal(
            $$"""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"{{clears}}"}""",
            block);
    }

    /// <summary>A wait reply-cli names comes first: the block clears that long after the answer, midnight or not.</summary>
    [Theory]
    [InlineData("contact.dailyLimitReached")]
    [InlineData("contact.activeContactsLimitReached")]
    public void A_wait_reply_names_comes_before_the_midnight(string replyCode)
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", "rate_limited", 429, replyCode, wait: "600");

        Assert.Equal(
            """{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"2026-11-18T13:55:10.250Z"}""",
            block);
    }

    /// <summary>
    /// A wait that is not a number of seconds between none and a year is no wait the block can take: the daily limit's block
    /// clears at the next midnight UTC, as it does when no wait is named at all.
    /// </summary>
    [Theory]
    [InlineData("-5")]
    [InlineData("'600'")]
    [InlineData("31536001")]
    [InlineData("NaN")]
    public void A_wait_the_block_cannot_take_leaves_the_daily_limit_to_the_midnight(string wait)
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", "rate_limited", 429, "contact.dailyLimitReached", wait);

        Assert.Equal(
            """{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"2026-11-19T00:00:00.000Z"}""",
            block);
    }

    /// <summary>
    /// The daily limit's block clears no later than the next midnight UTC, whatever wait reply-cli names: the mailbox is free
    /// to send again tomorrow. Today used up names a wait to the end of today, which is that midnight; an email held for a
    /// later day that is already full names a wait to the end of that day, weeks out, and the block still clears at the next
    /// midnight, when the mailbox is tried again.
    /// </summary>
    [Theory]
    // Today used up: the wait Reply names runs to the end of today.
    [InlineData("2026-11-18T13:45:10.250Z", "36889.75")]
    // A later due day already full: the wait runs to the end of that day, three weeks out.
    [InlineData("2026-11-18T13:45:10.250Z", "1851289.75")]
    // A wait just past the midnight, and one at the year's turn.
    [InlineData("2026-11-18T13:45:10.250Z", "36890")]
    [InlineData("2026-12-31T23:59:59.000Z", "86400")]
    public void The_daily_limit_s_block_clears_no_later_than_the_next_midnight_utc(string now, string wait)
    {
        var block = BlockAt(now, "email.limit_reached", 429, "contact.dailyLimitReached", wait);
        var midnight = new DateTimeOffset(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime.Date, TimeSpan.Zero).AddDays(1);

        Assert.Equal(
            $$"""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"{{midnight.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)}}"}""",
            block);
    }

    /// <summary>
    /// A daily-limit wait that ends before the next midnight UTC is kept as it is: the cap moves no moment earlier than
    /// midnight, only one past it.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("36889.5")]
    public void A_daily_limit_wait_ending_before_the_midnight_is_kept(string wait)
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", "email.limit_reached", 429, "contact.dailyLimitReached", wait);
        var clears = DateTimeOffset.Parse("2026-11-18T13:45:10.250Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .AddMilliseconds(double.Parse(wait, CultureInfo.InvariantCulture) * 1000);

        Assert.True(clears < DateTimeOffset.Parse("2026-11-19T00:00:00.000Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        Assert.Equal(
            $$"""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"{{clears.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)}}"}""",
            block);
    }

    /// <summary>
    /// The cap is the daily limit's alone: the active contacts renew on a day Reply knows and the package does not, so a
    /// wait reply-cli names past the next midnight is kept.
    /// </summary>
    [Fact]
    public void A_long_wait_on_the_active_contacts_is_kept()
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", "email.limit_reached", 429, "contact.activeContactsLimitReached", wait: "1851289.75");

        Assert.Equal(
            """{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"2026-12-10T00:00:00.000Z"}""",
            block);
    }

    /// <summary>
    /// A mailbox that cannot send direct emails at all is the mailbox's block for its limit, read by Reply's code or by
    /// reply-cli's own: Reply names no moment it lifts, so the block names none, whatever the clock says.
    /// </summary>
    [Theory]
    [InlineData("email.sending_disabled", "contact.emailAccountSendingDisabled")]
    [InlineData("email.sending_disabled", null)]
    [InlineData("reply.refused", "contact.emailAccountSendingDisabled")]
    public void A_mailbox_that_cannot_send_is_its_block_with_no_moment(string cliCode, string? replyCode)
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", cliCode, 403, replyCode, wait: null);

        Assert.Equal("""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit"}""", block);
    }

    /// <summary>The active contacts renew on a day Reply does not say: with no wait named, the block names no moment.</summary>
    [Fact]
    public void The_active_contacts_block_names_no_moment_without_a_wait()
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", "email.limit_reached", 403, "contact.activeContactsLimitReached", wait: null);

        Assert.Equal("""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit"}""", block);
    }

    /// <summary>
    /// The block the package's own table makes of an email send refused under <paramref name="cliCode"/> at
    /// <paramref name="status"/> with Reply's <paramref name="replyCode"/>, from mailbox 12, with the clock at
    /// <paramref name="now"/> and the CLI's <c>retry_after</c> the JavaScript value <paramref name="wait"/> spells, or none:
    /// the row <c>rowOf</c> reads, ended by <c>fail</c>.
    /// </summary>
    private static string BlockAt(string now, string cliCode, int status, string? replyCode, string? wait)
    {
        var at = DateTimeOffset.Parse(now, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        using var engine = new Engine(options =>
        {
            options.Strict(true);
            options.EnableModules(PackageSource);
            options.TimeSystem = new PinnedClock(at);
        });
        engine.Execute("var host = { fail: function (failure) { return failure; } };");
        engine.SetValue("errors", engine.Modules.Import("./modules/errors.js"));

        var said = new JsonObject { ["code"] = cliCode, ["status"] = status };
        if (replyCode is not null) said["reply_code"] = replyCode;
        var written = engine.Evaluate(
            $$"""
            (function () {
              var said = {{said.ToJsonString()}};
              var observed = { row: errors.rowOf("message.send", said), sender: "12", channel: "email", retry_after: {{wait ?? "undefined"}} };
              try {
                errors.fail("message.send", observed, undefined);
              } catch (failure) {
                return JSON.stringify(failure.block);
              }
              return "no failure";
            })()
            """).AsString();
        return written;
    }

    /// <summary>A clock that says one moment and nothing else.</summary>
    private sealed class PinnedClock(DateTimeOffset at) : DefaultTimeSystem(TimeZoneInfo.Utc, CultureInfo.InvariantCulture)
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
