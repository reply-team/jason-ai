using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--prompt</c> for <c>approval get</c> and <c>decision get</c>: the answer is read only for the identifiers
/// it carries, the folder is resolved, and <see cref="CopyPrompt"/> composes the words. Each returns null when the
/// body is not what it expects, and the runner prints the response as it came.
/// </summary>
public static class PromptRenderers
{
    public static Func<string, CancellationToken, Task<string?>> Approval(CliEnvironment env) => async (json, cancellationToken) =>
    {
        var approval = RenderText.Read<ApprovalDto>(json);
        if (approval?.Id is not { Length: > 0 } id || approval.CampaignId is not { Length: > 0 } campaign || approval.WorkItemId is not { Length: > 0 } workItem)
        {
            return null;
        }

        var folder = await PromptFolderResolver.ResolveAsync(env, campaign, cancellationToken).ConfigureAwait(false);
        return CopyPrompt.Compose(PromptKind.Approval, id, campaign, workItem, folder);
    };

    public static Func<string, CancellationToken, Task<string?>> Question(CliEnvironment env) => async (json, cancellationToken) =>
    {
        var decision = RenderText.Read<DecisionDto>(json);
        if (decision?.Id is not { Length: > 0 } id || decision.CampaignId is not { Length: > 0 } campaign || decision.WorkItemId is not { Length: > 0 } workItem)
        {
            return null;
        }

        var folder = await PromptFolderResolver.ResolveAsync(env, campaign, cancellationToken).ConfigureAwait(false);
        return CopyPrompt.Compose(PromptKind.Question, id, campaign, workItem, folder);
    };
}
