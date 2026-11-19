using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The default <c>Manager:Triggers</c> less <c>campaign_started</c>, for a runtime whose test is about something else and
/// counts claims, work or lines that the review a start summons would add to: a campaign's one claim a scan, its chronicle,
/// the work its items wait behind. A test of what a start does keeps the defaults (<see cref="StartSummonsManagerTests"/>).
/// </summary>
internal static class StartlessTriggers
{
    /// <summary>The kinds, in the order the defaults hold them.</summary>
    public static JsonArray Kinds() =>
        new([.. ManagerOptions.Default.Where(kind => kind != JournalKinds.CampaignStarted).Select(kind => (JsonNode?)JsonValue.Create(kind))]);

    /// <summary>Writes the list into a settings document, beside whatever its <c>Manager</c> section already holds.</summary>
    public static JsonObject Narrow(JsonObject settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings["Manager"] is not JsonObject manager)
        {
            manager = new JsonObject();
            settings["Manager"] = manager;
        }

        manager["Triggers"] = Kinds();
        return settings;
    }

    /// <summary>The same, for settings held as text.</summary>
    public static string Into(string settings) => Narrow(JsonNode.Parse(settings)!.AsObject()).ToJsonString();
}
