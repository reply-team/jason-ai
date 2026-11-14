using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// Whether the contact a send reaches has answered in its campaign: <see cref="Cold"/> until they have, <see cref="Warm"/>
/// from then on. Which one a contact is, is the caller's to know; the resolution only takes it.
/// </summary>
public enum Temperature
{
    Cold,
    Warm,
}

/// <summary>
/// The send mode in force for one temperature, where it comes from, and who set it: the campaign's own setter, or the
/// person who last set the preference, read from the rows; null where nobody did, which is only ever
/// <see cref="SendMode.EveryMessage"/>.
/// </summary>
public sealed record ModeInForce(SendMode Mode, SendModeSource Source, ActorRef? SetBy);

/// <summary>The installation's two send-mode preferences as their rows stand, each null while it is unset.</summary>
public sealed record InstallationModes(Preference? Cold, Preference? Warm);

/// <summary>
/// The one resolution of a send mode: the campaign's own, else the installation's preference, else every message to a
/// person. Each temperature resolves on its own, so a person who set only one has that one in force. It reads rows and
/// never the journal, and it is read at the moment a send is decided, never frozen when the work is made.
/// </summary>
public static class SendModes
{
    public const string ColdKey = "send_mode.cold";

    public const string WarmKey = "send_mode.warm";

    /// <summary>Both preferences in one read, tracked by nobody.</summary>
    public static async Task<InstallationModes> ReadAsync(JasonDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.Preferences.AsNoTracking()
            .Where(p => p.Key == ColdKey || p.Key == WarmKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new InstallationModes(
            rows.FirstOrDefault(row => row.Key == ColdKey),
            rows.FirstOrDefault(row => row.Key == WarmKey));
    }

    /// <summary>
    /// The mode in force for a send to a contact of <paramref name="temperature"/> in <paramref name="campaign"/>. A
    /// campaign's own mode counts only with the name of the person who set it beside it, since a send it releases goes in
    /// that name. A preference whose value does not read as one of the two words, a word a later release wrote, is in force
    /// as every message: what nobody can read releases nothing.
    /// </summary>
    public static ModeInForce Resolve(Campaign? campaign, InstallationModes installation, Temperature temperature)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var (own, setter) = campaign is null ? (null, null)
            : temperature == Temperature.Cold ? (campaign.SendModeCold, campaign.SendModeColdSetBy)
            : (campaign.SendModeWarm, campaign.SendModeWarmSetBy);
        if (own is { } mode && !string.IsNullOrWhiteSpace(setter))
        {
            return new ModeInForce(mode, SendModeSource.Campaign, new ActorRef(ActorType.Human, setter));
        }

        var row = temperature == Temperature.Cold ? installation.Cold : installation.Warm;
        if (row is null)
        {
            return new ModeInForce(SendMode.EveryMessage, SendModeSource.Default, null);
        }

        return Word(row.Value) == SendMode.Automatic
            ? new ModeInForce(SendMode.Automatic, SendModeSource.Preference, new ActorRef(row.UpdatedByType, row.UpdatedById))
            : new ModeInForce(SendMode.EveryMessage, SendModeSource.Preference, new ActorRef(row.UpdatedByType, row.UpdatedById));
    }

    /// <summary>The installation's mode in force for <paramref name="temperature"/>, wherever no campaign holds its own.</summary>
    public static ModeInForce Installation(InstallationModes installation, Temperature temperature) => Resolve(null, installation, temperature);

    /// <summary>
    /// Both modes in force, as an answer says them: the mode and where it comes from, and never who set it. With no
    /// campaign, the installation's.
    /// </summary>
    public static SendModesInForce InForce(Campaign? campaign, InstallationModes installation)
    {
        var cold = Resolve(campaign, installation, Temperature.Cold);
        var warm = Resolve(campaign, installation, Temperature.Warm);
        return new SendModesInForce(new SendModeInForce(cold.Mode, cold.Source), new SendModeInForce(warm.Mode, warm.Source));
    }

    /// <summary>
    /// The keys of the two preferences nobody has set yet, in key order: the installation's question, open while either is
    /// in the list. Nothing is stored about having asked it; an unset key brings it back.
    /// </summary>
    public static IReadOnlyList<string> Unset(InstallationModes installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var unset = new List<string>(2);
        if (installation.Cold is null)
        {
            unset.Add(ColdKey);
        }

        if (installation.Warm is null)
        {
            unset.Add(WarmKey);
        }

        return unset;
    }

    /// <summary>The mode a preference's value names, or null where it names neither word.</summary>
    private static SendMode? Word(JsonObject value) =>
        value["mode"] is JsonValue mode && mode.TryGetValue(out string? word)
            ? word switch
            {
                "automatic" => SendMode.Automatic,
                "every_message" => SendMode.EveryMessage,
                _ => null,
            }
            : null;
}
