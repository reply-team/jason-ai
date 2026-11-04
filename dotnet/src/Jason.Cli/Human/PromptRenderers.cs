using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--prompt</c> for <c>approval get</c> and <c>decision get</c>: the answer is read only for the identifiers
/// it carries, the folder is resolved, and <see cref="CopyPrompt"/> composes the words. Each returns null when the
/// body is not what it expects, and the runner refuses it in <see cref="NoItem"/>'s words rather than print the body.
/// </summary>
public static class PromptRenderers
{
    /// <summary>
    /// Why no prompt was made from an answer: said by <c>--prompt</c> and by the toast's Copy prompt button alike, each
    /// followed by what it did not do.
    /// </summary>
    public const string NoItem = "the runtime's answer names no item to compose a prompt for";

    public static Func<string, CancellationToken, Task<string?>> Approval(CliEnvironment env) => async (json, cancellationToken) =>
    {
        var approval = RenderText.Read<ApprovalDto>(json);
        if (approval?.Id is not { Length: > 0 } id || approval.CampaignId is not { Length: > 0 } campaign || approval.WorkItemId is not { Length: > 0 } workItem)
        {
            return null;
        }

        var folder = await PromptFolderResolver.ResolveAsync(env, campaign, cancellationToken).ConfigureAwait(false);
        return CopyPrompt.Compose(PromptKind.Approval, id, campaign, workItem, folder, Decided(approval.Status, ApprovalStatus.Pending));
    };

    public static Func<string, CancellationToken, Task<string?>> Question(CliEnvironment env) => async (json, cancellationToken) =>
    {
        var decision = RenderText.Read<DecisionDto>(json);
        if (decision?.Id is not { Length: > 0 } id || decision.CampaignId is not { Length: > 0 } campaign || decision.WorkItemId is not { Length: > 0 } workItem)
        {
            return null;
        }

        var folder = await PromptFolderResolver.ResolveAsync(env, campaign, cancellationToken).ConfigureAwait(false);
        return CopyPrompt.Compose(PromptKind.Question, id, campaign, workItem, folder, Decided(decision.Status, DecisionStatus.Pending));
    };

    /// <summary>
    /// The word an item that no longer waits was decided with, in the form the runtime's JSON spells it, or null while it
    /// still waits: the brief and <c>session open</c>'s answer say it alike.
    /// </summary>
    public static string? Decided<T>(T status, T pending)
        where T : struct, Enum => EqualityComparer<T>.Default.Equals(status, pending) ? null : JsonSerializer.Serialize(status, JasonJson.Options).Trim('"');
}
