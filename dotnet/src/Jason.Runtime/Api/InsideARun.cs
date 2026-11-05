using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>
/// A request that says it comes from inside a run. The CLI sends <see cref="ExecutionEnvironment.AttemptHeader"/> on every
/// request when its environment names the attempt it was launched for; the five operations that are a person's own word
/// refuse such a request with <c>decided_inside_a_run</c> before anything reads its body, so before its actor, whatever
/// actor that is. Every other operation reads past the header, and the fenced ones keep their attempt in the body.
/// </summary>
/// <remarks>
/// A higher bar, not a boundary: the runtime cannot tell a person from a program that holds the token, and a run that
/// stripped its own environment and claimed a person would not be told apart. What it removes is the easy way — a role's
/// CLI that writes <c>--actor human:&lt;Name&gt;</c> on a person's verb from inside the run that asked.
/// </remarks>
public static class InsideARun
{
    /// <summary>The operations that are a person's own word, and refuse a request from inside a run.</summary>
    public static IReadOnlySet<string> Refusing { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Operations.ApprovalApprove,
        Operations.ApprovalReject,
        Operations.ApprovalSetStanding,
        Operations.ApprovalUnsetStanding,
        Operations.DecisionAnswer,
    };

    /// <summary>The attempt a request says it comes from, as it said it; null where it names none.</summary>
    public static string? AttemptOf(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request.Headers[ExecutionEnvironment.AttemptHeader].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
