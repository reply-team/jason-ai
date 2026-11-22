using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>
/// A request that says it comes from inside a run. The CLI sends <see cref="ExecutionEnvironment.AttemptHeader"/> on every
/// request when its environment names the attempt it was launched for; the operations that are a person's own word,
/// and the two that change what a plugin may use, refuse such a request with <c>decided_inside_a_run</c> before anything
/// reads its body, so before its actor, whatever actor that is: inside a run, nothing changes Jason itself. Every other
/// operation reads past the header, and the fenced ones keep their attempt in the body. One reads it as a fact about where
/// a verdict came from: <c>reply.record</c> records a person named from inside a run as that run's verdict, checked to be
/// an attempt of work in the verdict's campaign, so the person's word it would otherwise carry — the one that lifts an
/// opt-out's lock — is not taken from the run (<see cref="Domain.RunScope"/>).
/// </summary>
/// <remarks>
/// A higher bar, not a boundary: the runtime cannot tell a person from a program that holds the token, and a run that
/// stripped its own environment and claimed a person would not be told apart. What it removes is the easy way — a role's
/// CLI that writes <c>--actor human:&lt;Name&gt;</c> on a person's verb from inside the run that asked.
/// </remarks>
public static class InsideARun
{
    /// <summary>
    /// The operations that are a person's own word. A preference is one for every key: what a named person states, where in
    /// a dispatched run no person is there. Refused here by operation, never by key, so a key added later is refused before
    /// anybody remembers to. A campaign's start, or its resume, is one: a role may pause a campaign, and only a person, or
    /// their own session on their word, starts one; a check-in that started its campaign under a role's or a person's name
    /// would write a start that is not its own line, and that start would summon its successor. A lift of the daily limits
    /// of launches is one: a run that lifted them would let every role in every campaign run past them for the rest of the
    /// day on no person's word.
    /// </summary>
    public static IReadOnlySet<string> PersonsWord { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Operations.ApprovalApprove,
        Operations.ApprovalReject,
        Operations.ApprovalSetStanding,
        Operations.ApprovalUnsetStanding,
        Operations.DecisionAnswer,
        Operations.PreferenceSet,
        Operations.PreferenceUnset,
        Operations.CampaignSetSendMode,
        Operations.CampaignStart,
        Operations.LaunchesLift,
    };

    /// <summary>
    /// The operations that change Jason itself: what a plugin may use, which profile agent work runs on by default, and which
    /// channels tell the person. A role Jason launches is no person granting it, nor the person who chose the host its kind
    /// of work runs on or how they are told.
    /// </summary>
    public static IReadOnlySet<string> ChangesJason { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Operations.PluginGrant,
        Operations.PluginRevoke,
        Operations.ProfileSetDefault,
        Operations.ProfileUnsetDefault,
        Operations.NotificationEnable,
        Operations.NotificationDisable,
    };

    /// <summary>Every operation that refuses a request from inside a run.</summary>
    public static IReadOnlySet<string> Refusing { get; } = new HashSet<string>(PersonsWord.Concat(ChangesJason), StringComparer.Ordinal);

    /// <summary>The refusal an operation of <see cref="Refusing"/> answers a request from inside a run with, in its own sentence.</summary>
    public static Domain.ForbiddenException Refusal(string operation, string attempt) => operation switch
    {
        Operations.ProfileSetDefault or Operations.ProfileUnsetDefault => Domain.DomainErrors.DefaultProfileInsideARun(attempt),
        Operations.NotificationEnable or Operations.NotificationDisable => Domain.DomainErrors.NotificationInsideARun(attempt),
        Operations.CampaignStart => Domain.DomainErrors.StartInsideARun(attempt),
        Operations.LaunchesLift => Domain.DomainErrors.LiftInsideARun(attempt),
        _ when ChangesJason.Contains(operation) => Domain.DomainErrors.ChangesJasonInsideARun(attempt),
        _ => Domain.DomainErrors.DecidedInsideARun(attempt),
    };

    /// <summary>The attempt a request says it comes from, as it said it; null where it names none.</summary>
    public static string? AttemptOf(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request.Headers[ExecutionEnvironment.AttemptHeader].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Whether a request says it comes from a confined call of the CLI, the desktop's chat: it carries the
    /// <see cref="Confinement.Header"/> header with any value at all, since a marker read too widely only parks a send for a
    /// person, never releases one.
    /// </summary>
    public static bool ConfinedOf(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return !string.IsNullOrWhiteSpace(request.Headers[Confinement.Header].ToString());
    }
}
