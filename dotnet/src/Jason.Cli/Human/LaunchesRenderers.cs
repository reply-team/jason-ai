using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>How a lift of the daily limits reads to a person.</summary>
public static class LaunchesRenderers
{
    private const int Label = 15;

    /// <summary>
    /// The limits as they stand: a line for the installation's day, read on its own zone's clock, a line for a lift where one
    /// stands, a table of each role's limit and where it comes from — with the runs started today where a campaign was
    /// named — and what is held.
    /// </summary>
    public static string? List(string json)
    {
        var list = RenderText.Read<LaunchesListDto>(json);
        if (list?.Day?.TimeZone is null || list.Roles is null)
        {
            return null;
        }

        var lines = new List<string> { $"Today runs until {Clock(list.Day.End, list.Day.TimeZone)}, {list.Day.TimeZone}." };
        if (list.Lift is { } lift)
        {
            lines.Add($"Today's limits are lifted until {Clock(lift.Until, list.Day.TimeZone)}, by {Who(lift.Actor)} at {RenderText.Moment(lift.LiftedAt)}.");
        }

        var campaign = list.Roles.Any(role => role.StartedToday is not null);
        var table = campaign ? new HumanTable("ROLE", "LIMIT A DAY", "FROM", "STARTED TODAY") : new HumanTable("ROLE", "LIMIT A DAY", "FROM");
        foreach (var role in list.Roles)
        {
            var limit = role.MaxPerDay is { } number ? number.ToString(CultureInfo.InvariantCulture) : "none";
            var from = RenderText.Snake(role.Source);
            if (campaign)
            {
                table.Row(role.Role, limit, from, role.StartedToday?.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                table.Row(role.Role, limit, from);
            }
        }

        lines.Add(table.Render());
        var items = list.Held == 1 ? "1 item" : string.Create(CultureInfo.InvariantCulture, $"{list.Held} items");
        lines.Add(campaign ? $"Held at a daily limit now in this campaign: {items}." : $"Held at a daily limit now: {items}.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>
    /// A moment on the clock of the zone the day is counted in, as a person there reads it; where this machine does not know
    /// the zone, the moment in UTC.
    /// </summary>
    private static string Clock(DateTimeOffset moment, string zone)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(moment, TimeZoneInfo.FindSystemTimeZoneById(zone)).ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return RenderText.Moment(moment);
        }
    }

    private static string Who(ActorRef? actor) =>
        actor is null ? "-" : actor.Id is null ? RenderText.Snake(actor.Type) : $"{RenderText.Snake(actor.Type)}:{RenderText.Shown(actor.Id)}";

    /// <summary>Until when the limits are lifted and in which zone, what was let go, and whether today was lifted already.</summary>
    public static string? Lift(string json)
    {
        var lift = RenderText.Read<LaunchesLiftDto>(json);
        if (lift?.TimeZone is null)
        {
            return null;
        }

        var released = lift.Released == 1 ? "1 held item" : string.Create(CultureInfo.InvariantCulture, $"{lift.Released} held items");
        return string.Join(
            Environment.NewLine,
            lift.AlreadyLifted ? "Today's limits were lifted already." : "Today's limits are lifted.",
            "Until:".PadRight(Label) + RenderText.Moment(lift.Until),
            "Time zone:".PadRight(Label) + lift.TimeZone,
            "Let go:".PadRight(Label) + released) + Environment.NewLine;
    }
}
