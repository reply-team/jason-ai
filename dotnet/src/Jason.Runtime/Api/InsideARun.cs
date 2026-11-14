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
    /// anybody remembers to.
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
    };

    /// <summary>The operations that change Jason itself: what a plugin may use. A role Jason launches is no person granting it.</summary>
    public static IReadOnlySet<string> ChangesJason { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Operations.PluginGrant,
        Operations.PluginRevoke,
    };

    /// <summary>Every operation that refuses a request from inside a run.</summary>
    public static IReadOnlySet<string> Refusing { get; } = new HashSet<string>(PersonsWord.Concat(ChangesJason), StringComparer.Ordinal);

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
