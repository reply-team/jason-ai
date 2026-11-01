using System.Globalization;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The account's sending schedules, answered as reply-cli 0.16.0 documents <c>schedule list</c>: every schedule the
/// team can use, in one answer, each with its time zone as Reply spells it — a Windows time zone identifier — and its
/// windows by weekday, the times written <c>HH:MM</c>.
/// </summary>
internal static partial class Account
{
    private const string SchedulesFile = "schedules.json";

    /// <summary>
    /// <c>schedule list</c>: every schedule, in the order the account holds them. A key without Reply's sequences read
    /// scope fails the whole command, as Reply refuses the whole list; its refusal carries no code, so there is no
    /// <c>reply_code</c>. A schedule without a usable identifier fails the whole command too, as the CLI refuses a list
    /// it cannot key (<c>reply.unavailable</c>).
    /// </summary>
    private static Ending ScheduleList(string root)
    {
        if (!Enabled(root, "schedules"))
        {
            return Refused("access.feature_unavailable", "The account's plan or role does not include this.", status: 403);
        }

        var items = new JsonArray();
        foreach (var schedule in Records(root, SchedulesFile))
        {
            if (!UsableId(schedule))
            {
                return Refused("reply.unavailable", "Reply listed a schedule without a usable id.", status: 200);
            }

            items.Add(ScheduleListed(schedule));
        }

        return Done(new JsonObject { ["items"] = items });
    }

    /// <summary>One schedule as the list answers it: its name null when blank, Reply's flags under the CLI's names, and its state word as Reply gives it.</summary>
    private static JsonObject ScheduleListed(JsonObject schedule)
    {
        var word = Text(schedule, "status");
        return new JsonObject
        {
            ["id"] = schedule["id"]!.GetValue<long>(),
            ["name"] = TextOrNull(schedule, "name"),
            ["time_zone_id"] = TextOrNull(schedule, "timezoneId"),
            ["use_recipient_time_zone"] = Flag(schedule, "useProspectTimezone"),
            ["exclude_holidays"] = Flag(schedule, "excludeHolidays"),
            ["is_default"] = Flag(schedule, "isDefault"),
            ["main_timings"] = Timings(schedule["mainTimings"]),
            ["use_follow_up_schedule"] = Flag(schedule, "useFollowUpSchedule"),
            ["follow_up_timings"] = schedule["followUpTimings"] is JsonArray ? Timings(schedule["followUpTimings"]) : null,
            ["reply_status"] = word.Length == 0 ? null : word,
        };
    }

    /// <summary>A whole number above zero that JavaScript holds exactly, the only identifier the CLI keys a schedule by.</summary>
    private static bool UsableId(JsonObject schedule) =>
        schedule["id"] is JsonValue value && value.TryGetValue<long>(out var id) && id is > 0 and <= 9007199254740991;

    /// <summary>
    /// Reply's timings, a weekday each, with the weekday in lower case and each time of day as <c>HH:MM</c>; a weekday that
    /// is not named, or a time that is no time of day, is null, as the CLI prints it.
    /// </summary>
    private static JsonArray Timings(JsonNode? planted)
    {
        var days = new JsonArray();
        foreach (var day in (planted as JsonArray ?? []).OfType<JsonObject>())
        {
            var ranges = new JsonArray();
            foreach (var range in (day["timeRanges"] as JsonArray ?? []).OfType<JsonObject>())
            {
                ranges.Add(new JsonObject { ["from"] = Clock(range["fromTime"]), ["to"] = Clock(range["toTime"]) });
            }

            days.Add(new JsonObject
            {
                ["week_day"] = Text(day, "weekDay") is { Length: > 0 } weekDay ? weekDay.ToLowerInvariant() : null,
                ["active"] = Flag(day, "isActive"),
                ["ranges"] = ranges,
            });
        }

        return days;
    }

    /// <summary>A time of day as <c>HH:MM</c>; a range may end at 24:00, the end of the day, and anything else that is no time of day is null.</summary>
    private static string? Clock(JsonNode? time)
    {
        if (time is not JsonObject at
            || at["hour"] is not JsonValue hourValue || !hourValue.TryGetValue<int>(out var hour)
            || at["minute"] is not JsonValue minuteValue || !minuteValue.TryGetValue<int>(out var minute))
        {
            return null;
        }

        var valid = minute is >= 0 and < 60 && hour >= 0 && (hour < 24 || (hour == 24 && minute == 0));
        return valid ? hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture) : null;
    }
}
