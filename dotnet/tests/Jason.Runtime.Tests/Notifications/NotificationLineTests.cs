using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The <c>notification_sent</c> line: what happened to one notification, in the shape the page publishes, read back
/// the way a start reads its watermark.
/// </summary>
public sealed class NotificationLineTests
{
    private static readonly DateTime At = new(2026, 10, 1, 9, 6, 0, DateTimeKind.Utc);

    /// <summary>One entry of each outcome, side by side: a real line has one entry per channel.</summary>
    private static NotificationLine Sample() => new(
        "ntf_one",
        3,
        1,
        "jrn_covered",
        [
            ChannelOutcome.Delivered("toast", ChannelKinds.Toast),
            ChannelOutcome.Failed("ntfy", ChannelKinds.Plugin, "notification_unreachable", FailureClass.Transient) with { Attempts = 2 },
            ChannelOutcome.NotAttempted("toast", ChannelKinds.Toast, NotAttemptedReasons.NoDesktop, "session 0"),
            ChannelOutcome.Silenced("toast", ChannelKinds.Toast, SilencedReasons.DisabledForApplication),
        ],
        At,
        2,
        "jrn_delivered",
        1,
        4,
        5);

    [Fact]
    public void The_line_is_the_published_shape()
    {
        var expected = JsonNode.Parse("""
            {
              "notification_id": "ntf_one",
              "counts": { "approvals": 3, "decisions": 1, "protective_failures": 2, "event_gaps": 1, "blocks": 4, "held": 5 },
              "covered_through": "jrn_covered",
              "channels": [
                { "channel": "toast", "kind": "toast", "outcome": "delivered", "attempts": 1 },
                { "channel": "ntfy", "kind": "plugin", "outcome": "failed", "code": "notification_unreachable", "class": "transient", "attempts": 2 },
                { "channel": "toast", "kind": "toast", "outcome": "not_attempted", "reason": "no_desktop", "detail": "session 0" },
                { "channel": "toast", "kind": "toast", "outcome": "silenced", "reason": "disabled_for_application", "attempts": 1 }
              ],
              "last_delivered_at": "2026-10-01T09:06:00.000Z",
              "delivered_through": "jrn_delivered"
            }
            """);

        var written = Sample().ToJson();
        Assert.True(JsonNode.DeepEquals(expected, written), written.ToJsonString());
    }

    [Fact]
    public void A_line_reads_back_as_it_was_written()
    {
        var read = NotificationLine.Read(Sample().ToJson());

        Assert.NotNull(read);
        Assert.Equal(Sample().ToJson().ToJsonString(), read.ToJson().ToJsonString());
        Assert.Equal(("jrn_covered", (DateTime?)At), NotificationLine.ReadWatermark(Sample().ToJson()));
    }

    /// <summary>A line an earlier release wrote names no hold, and reads as none.</summary>
    [Fact]
    public void A_line_written_before_holds_were_counted_reads_none()
    {
        var earlier = Sample().ToJson();
        earlier["counts"]!.AsObject().Remove("held");

        Assert.Equal(0, NotificationLine.Read(earlier)!.Held);
    }

    /// <summary>A line that names no delivery yet carries no time, rather than a made-up one.</summary>
    [Fact]
    public void A_line_with_no_delivery_ever_carries_none()
    {
        var line = Sample() with { LastDeliveredAt = null };
        Assert.Null(line.ToJson()["last_delivered_at"]);
        Assert.Null(NotificationLine.ReadWatermark(line.ToJson()).LastDeliveredAt);
    }

    [Fact]
    public void A_detail_is_cut_to_500_characters_and_a_receipt_to_256()
    {
        var line = Sample() with
        {
            Channels =
            [
                ChannelOutcome.Delivered("ntfy", ChannelKinds.Plugin, receipt: new string('r', 400)),
                ChannelOutcome.NotAttempted("toast", ChannelKinds.Toast, NotAttemptedReasons.Unavailable, new string('d', 900)),
            ],
        };

        var channels = line.ToJson()["channels"]!.AsArray();
        Assert.Equal(256, channels[0]!["receipt"]!.GetValue<string>().Length);
        Assert.Equal(500, channels[1]!["detail"]!.GetValue<string>().Length);
    }

    [Fact]
    public void A_cut_that_would_split_a_surrogate_pair_steps_back_for_a_detail_and_a_receipt()
    {
        var pair = "\U0001F600";
        var line = Sample() with
        {
            Channels =
            [
                ChannelOutcome.Delivered("ntfy", ChannelKinds.Plugin, receipt: new string('r', 255) + pair),
                ChannelOutcome.NotAttempted("toast", ChannelKinds.Toast, NotAttemptedReasons.Unavailable, new string('d', 499) + pair),
            ],
        };

        var channels = line.ToJson()["channels"]!.AsArray();
        Assert.Equal(255, channels[0]!["receipt"]!.GetValue<string>().Length);
        Assert.Equal(499, channels[1]!["detail"]!.GetValue<string>().Length);
    }

    /// <summary>The words a line can carry are the published ones; the page prints these lists.</summary>
    [Fact]
    public void The_outcome_words_and_the_reasons_are_the_published_ones()
    {
        Assert.Equal(["delivered", "silenced", "failed", "not_attempted"], ChannelResults.All);
        Assert.Equal(["off", "no_desktop", "not_declared", "unavailable", "binding_invalid"], NotAttemptedReasons.All);
        Assert.Equal(["disabled_for_application", "disabled_for_user", "disabled_by_group_policy", "disabled_by_manifest"], SilencedReasons.All);
    }
}
