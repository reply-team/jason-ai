using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>
/// How a send that reaches a real person goes: each one to a person first, or released without asking. The installation's
/// mode for a contact who has not answered in a campaign is the named preference <c>send_mode.cold</c>, for one who has
/// <c>send_mode.warm</c>, and a campaign may hold its own for either. The first word is the one nothing set reads as.
/// </summary>
public enum SendMode
{
    /// <summary>Every send waits for a person's approval, one at a time.</summary>
    EveryMessage,

    /// <summary>A send is released without asking anybody, by the standing word of the person who chose the mode.</summary>
    Automatic,
}

/// <summary>Where the mode in force comes from: the campaign's own, the installation's preference, or neither.</summary>
public enum SendModeSource
{
    /// <summary>The campaign holds a mode of its own, which a named person set.</summary>
    Campaign,

    /// <summary>The installation's named preference, which a named person set.</summary>
    Preference,

    /// <summary>Nobody set one, so every send waits for a person.</summary>
    Default,
}

/// <summary>
/// A campaign's own send modes, one for its cold contacts and one for its warm ones, each null where the campaign sets
/// none and follows the installation. The words are the ones <c>campaign.set_send_mode</c> takes, and null is how it takes
/// one back.
/// </summary>
public sealed record SendModesDto(SendMode? Cold, SendMode? Warm);

/// <summary>One mode in force, and where it comes from.</summary>
public sealed record SendModeInForce(SendMode Mode, SendModeSource Source);

/// <summary>The mode in force for a cold contact and for a warm one, each with where it comes from.</summary>
public sealed record SendModesInForce(SendModeInForce Cold, SendModeInForce Warm);

/// <summary>
/// The body of <c>campaign.set_send_mode</c>: a campaign's own mode for its cold contacts, its warm ones, or both. An
/// absent member leaves that mode alone, null takes the campaign's own back so it follows the installation again, and a
/// word sets it. At least one is named. Only a named person sets it, and never from inside a run.
/// </summary>
public sealed record CampaignSetSendModeRequest(
    string? CampaignId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<SendMode?> Cold,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<SendMode?> Warm,
    ActorRef? Actor,
    string? Reason);
