using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Routes written the two ways a runtime reads them: the global half into the settings file a reload freezes,
/// the campaign half through the API that writes the row and activates a new snapshot. Every routing test says
/// what it wants here rather than depending on what a machine happens to have.
/// </summary>
public static class TestRoutes
{
    /// <summary>
    /// Merges a <c>Routes</c> section into the settings file rather than writing over it, exactly as
    /// <c>TestPlugins.Grant</c> does: a fixture that sets a route must not silently drop the grants another
    /// helper wrote into the same file, and the dispatcher must stay off.
    /// </summary>
    /// <param name="json">The <c>Routes</c> section itself, as JSON — what <see cref="GlobalDefault"/> produces.</param>
    public static void WriteGlobal(JasonPaths paths, string json)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Directory.CreateDirectory(paths.ConfigDirectory);

        var settings = File.Exists(paths.UserSettingsFile)
            ? JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject()
            : JsonNode.Parse(RuntimeApiFixture.DispatcherOff)!.AsObject();

        settings[RoutesOptions.Section] = JsonNode.Parse(json)!.AsObject();
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    /// <summary>The <c>Routes</c> section of a runtime that sends everything to one plugin.</summary>
    public static string GlobalDefault(string pluginId, JsonObject? binding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var entry = new JsonObject { ["Plugin"] = pluginId };
        if (binding is not null)
        {
            entry["Binding"] = binding.DeepClone();
        }

        return new JsonObject { ["Default"] = entry }.ToJsonString(JasonJson.Options);
    }

    /// <summary>
    /// The campaign half, through the operation that writes it: a row, validated against the active plugin
    /// snapshot, and a new route snapshot activated under the reload gate. A null <paramref name="operation"/>
    /// is the campaign's default route.
    /// </summary>
    public static async Task SetCampaignAsync(
        RuntimeApiFixture api,
        string campaignId,
        string? operation,
        string pluginId,
        JsonObject? binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);

        var (status, body) = await api.PostAsync(
            "route.set",
            new { campaign_id = campaignId, operation, plugin = pluginId, binding },
            cancellationToken);

        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }
}
