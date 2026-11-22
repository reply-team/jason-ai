using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>How a lift of the daily limits reads to a person.</summary>
public static class LaunchesRenderers
{
    private const int Label = 15;

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
