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

        // Only an answer that carries no folder falls through to the next level. A step that did not answer (a refused
        // call, a timeout, a body that is not what it should be) ends the resolution: a lower level named after it would
        // be labelled "no folder is recorded", which nobody has said.
        var campaign = await ReadAsync<CampaignDto>(client, Operations.CampaignGet, new JsonObject { ["campaign_id"] = campaignId }, cancellationToken).ConfigureAwait(false);
        if (campaign is null)
        {
            return null;
        }

        if (campaign.FolderPath is { Length: > 0 } campaignFolder)
        {
            return new PromptFolder(PromptFolderLevel.Campaign, campaignFolder);
        }

        if (campaign.ProjectId is { Length: > 0 } projectId)
        {
            var project = await ReadAsync<ProjectDto>(client, Operations.ProjectGet, new JsonObject { ["project_id"] = projectId }, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return null;
            }

            if (project.FolderPath is { Length: > 0 } projectFolder)
            {
                return new PromptFolder(PromptFolderLevel.Project, projectFolder);
            }
        }

        // The root is named only where it is a folder with the root's marker; any other state is not a place to stand.
        var info = await ReadAsync<SystemInfoResponse>(client, Operations.SystemInfo, null, cancellationToken).ConfigureAwait(false);
        return info?.Harness is { RootState: HarnessRootState.Marked, Root: { Length: > 0 } root } ? new PromptFolder(PromptFolderLevel.HarnessRoot, root) : null;
    }

    private static async Task<T?> ReadAsync<T>(RuntimeClient client, string operation, JsonObject? body, CancellationToken cancellationToken)
        where T : class
    {
        var answer = await TryAsync(client, operation, body, cancellationToken).ConfigureAwait(false);
        return answer is null ? null : RenderText.Read<T>(answer);
    }

    private static async Task<string?> TryAsync(RuntimeClient client, string operation, JsonObject? body, CancellationToken cancellationToken)
    {
        try
        {
            var response = body is null
                ? await client.PostAsync(operation, cancellationToken).ConfigureAwait(false)
                : await client.PostAsync(operation, RequestBody.Serialize(body), cancellationToken).ConfigureAwait(false);
            return response.IsSuccess ? response.Body : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
