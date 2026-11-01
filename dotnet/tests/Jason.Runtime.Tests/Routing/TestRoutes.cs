using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// The same merge into a <b>running</b> runtime's settings file, and then a wait until the options monitor
    /// has seen it. A hand edit reaches a running runtime through a file watcher, so a test that wrote the file
    /// and reloaded immediately would be racing it; this waits for the runtime's own view of the section to
    /// change, which is the only thing that makes the next reload deterministic. A section the validator refuses
    /// counts as a change too — that is exactly what a test about a mistyped route is waiting for.
    /// </summary>
    public static async Task WriteGlobalAsync(RuntimeApiFixture api, string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var monitor = api.Resolve<IOptionsMonitor<RoutesOptions>>();
        var before = Seen(monitor);
        WriteGlobal(api.Paths, json);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Seen(monitor) == before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, cancellationToken);
        }

        Assert.NotEqual(before, Seen(monitor));
    }

    /// <summary>What the running runtime currently makes of the section, including "nothing it could work with".</summary>
    private static string Seen(IOptionsMonitor<RoutesOptions> monitor)
    {
        RoutesOptions current;
        try
        {
            current = monitor.CurrentValue;
        }
        catch (OptionsValidationException failure)
        {
            return "refused: " + string.Join("; ", failure.Failures);
        }

        var described = new JsonObject
        {
            ["Default"] = Describe(current.Default),
            ["Operations"] = new JsonObject(current.Operations.Select(entry =>
                KeyValuePair.Create(entry.Key, Describe(entry.Value)))),
        };

        return described.ToJsonString(JasonJson.Options);
    }

    private static JsonNode? Describe(RouteEntry? entry) =>
        entry is null ? null : new JsonObject { ["Plugin"] = entry.Plugin, ["Binding"] = entry.Binding?.DeepClone() };

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
            Operations.RouteSet,
            new { campaign_id = campaignId, operation, plugin = pluginId, binding },
            cancellationToken);

        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }
}
