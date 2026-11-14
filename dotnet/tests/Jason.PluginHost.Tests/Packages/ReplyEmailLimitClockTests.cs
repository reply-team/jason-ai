using System.Globalization;
using System.Text.Json.Nodes;
using Jint;
using Jint.Runtime;

namespace Jason.PluginHost.Tests.Packages;

/// <summary>
/// The moment the Reply package says a mailbox's daily limit clears, on a clock that does not move. The plugin host reads the
/// machine's clock and offers no other, so the package's own error table is run here, beside the engine it runs in, in an
/// engine of its own whose clock is pinned, with a stand-in for the one host function it calls, <c>host.fail</c>, that hands
/// back what it is given. The instants are in the middle of a month that changes nothing at its end, and UTC keeps no
/// daylight saving time.
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
    public void The_daily_limit_clears_at_the_next_midnight_utc(string now, string clears)
    {
        var block = BlockAt(now, "rate_limited", 429, "contact.dailyLimitReached", retryAfter: null);

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
        var block = BlockAt("2026-11-18T13:45:10.250Z", "rate_limited", 429, replyCode, retryAfter: 600);

        Assert.Equal(
            """{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit","clears_at":"2026-11-18T13:55:10.250Z"}""",
            block);
    }

    /// <summary>The active contacts renew on a day Reply does not say: with no wait named, the block names no moment.</summary>
    [Fact]
    public void The_active_contacts_block_names_no_moment_without_a_wait()
    {
        var block = BlockAt("2026-11-18T13:45:10.250Z", "email.limit_reached", 403, "contact.activeContactsLimitReached", retryAfter: null);

        Assert.Equal("""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit"}""", block);
    }

    /// <summary>
    /// The block the package's own table makes of an email send refused under <paramref name="cliCode"/> at
    /// <paramref name="status"/> with Reply's <paramref name="replyCode"/>, from mailbox 12, with the clock at
    /// <paramref name="now"/>: the row <c>rowOf</c> reads, ended by <c>fail</c>.
    /// </summary>
    private static string BlockAt(string now, string cliCode, int status, string replyCode, int? retryAfter)
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

        var said = new JsonObject { ["code"] = cliCode, ["status"] = status, ["reply_code"] = replyCode };
        var wait = retryAfter is { } seconds ? seconds.ToString(CultureInfo.InvariantCulture) : "undefined";
        var written = engine.Evaluate(
            $$"""
            (function () {
              var said = {{said.ToJsonString()}};
              var observed = { row: errors.rowOf("message.send", said), sender: "12", channel: "email", retry_after: {{wait}} };
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
