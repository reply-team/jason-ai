using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Plugins.Door;

/// <summary>
/// The request feature that says a request came through a handler's door, set by the API's authentication once it
/// has recognised a live capability. Only a request carrying it may speak as a plugin.
/// </summary>
public sealed record HandlerCaller(DoorGrant Grant)
{
    /// <summary>The reason every request through a door carries: the journal line the handler is answering.</summary>
    public string Reason => "journal " + Grant.LineId;

    /// <summary>
    /// Whether a body speaks exactly as this door says it must: the actor is <c>{"type":"plugin","id":&lt;its
    /// plugin&gt;}</c> and nothing more, and the reason names its line. The plugin-host sets both and the plugin sets
    /// neither, so anything else did not come from the host and is refused. The refusal names the field, never a value.
    /// </summary>
    public void Check(JsonElement body)
    {
        var errors = new ValidationErrors();
        if (body.ValueKind != JsonValueKind.Object)
        {
            errors.Add("actor", "required", "a request through a handler's door carries the plugin's actor, set by the plugin-host.");
            errors.ThrowIfAny();
            return;
        }

        if (!body.TryGetProperty("actor", out var actor))
        {
            errors.Add("actor", "required", "a request through a handler's door carries the plugin's actor, set by the plugin-host.");
        }
        else if (!IsThisPlugin(actor))
        {
            errors.Add("actor", "not_allowed", "a request through a handler's door speaks only as the plugin the door was opened for.");
        }

        if (!body.TryGetProperty("reason", out var reason))
        {
            errors.Add("reason", "required", "a request through a handler's door carries the journal line it answers, set by the plugin-host.");
        }
        else if (reason.ValueKind != JsonValueKind.String || !string.Equals(reason.GetString(), Reason, StringComparison.Ordinal))
        {
            errors.Add("reason", "not_allowed", "a request through a handler's door names only the journal line the door was opened for.");
        }

        errors.ThrowIfAny();
    }

    private bool IsThisPlugin(JsonElement actor)
    {
        if (actor.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var count = 0;
        foreach (var _ in actor.EnumerateObject())
        {
            count++;
        }

        return count == 2
            && actor.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ActorType.Plugin)), StringComparison.Ordinal)
            && actor.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            && string.Equals(id.GetString(), Grant.PluginId, StringComparison.Ordinal);
    }
}
