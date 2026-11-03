using System.Text.Json.Nodes;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// The folder a prompt names: the campaign's recorded folder, else its project's, else the harness root the runtime
/// reports. Each step asks an operation that exists, one after the other and only as far as needed. Nothing here fails
/// a verb for want of a folder: a call that does not answer, or answers something else, means no folder.
/// </summary>
public static class PromptFolderResolver
{
    public static async Task<PromptFolder?> ResolveAsync(CliEnvironment env, string campaignId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var descriptor = new DescriptorReader(env.Paths).Read();
        if (descriptor is null)
        {
            return null;
        }

        using var client = new RuntimeClient(descriptor, env.HttpHandler);

        var campaign = RenderText.Read<CampaignDto>(await TryAsync(client, Operations.CampaignGet, new JsonObject { ["campaign_id"] = campaignId }, cancellationToken).ConfigureAwait(false));
        if (campaign?.FolderPath is { Length: > 0 } campaignFolder)
        {
            return new PromptFolder(PromptFolderLevel.Campaign, campaignFolder);
        }

        if (campaign?.ProjectId is { Length: > 0 } projectId)
        {
            var project = RenderText.Read<ProjectDto>(await TryAsync(client, Operations.ProjectGet, new JsonObject { ["project_id"] = projectId }, cancellationToken).ConfigureAwait(false));
            if (project?.FolderPath is { Length: > 0 } projectFolder)
            {
                return new PromptFolder(PromptFolderLevel.Project, projectFolder);
            }
        }

        var info = RenderText.Read<SystemInfoResponse>(await TryAsync(client, Operations.SystemInfo, null, cancellationToken).ConfigureAwait(false));
        return info?.Harness?.Root is { Length: > 0 } root ? new PromptFolder(PromptFolderLevel.HarnessRoot, root) : null;
    }

    private static async Task<string> TryAsync(RuntimeClient client, string operation, JsonObject? body, CancellationToken cancellationToken)
    {
        try
        {
            var response = body is null
                ? await client.PostAsync(operation, cancellationToken).ConfigureAwait(false)
                : await client.PostAsync(operation, RequestBody.Serialize(body), cancellationToken).ConfigureAwait(false);
            return response.IsSuccess ? response.Body : string.Empty;
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }
    }
}
