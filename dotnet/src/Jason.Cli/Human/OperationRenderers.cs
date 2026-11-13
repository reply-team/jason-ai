using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the operation verbs. A renderer returns null when the body is not what it expects, and the
/// runner falls back to printing the response as it came.
/// </summary>
public static class OperationRenderers
{
    private const int Label = 12;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The operation and the plugin it is routed to, then each schema, written out whole.</summary>
    public static string? Description(string json)
    {
        var description = RenderText.Read<OperationDescriptionDto>(json);
        if (description?.Operation is null || description.PluginId is null || description.Arguments is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Operation:", string.Create(CultureInfo.InvariantCulture, $"{RouteRenderers.OnChannel(description.Operation, description.Channel)} v{description.Version}")),
            Line("Plugin:", $"{description.PluginId} {description.PluginVersion}"),
            string.Empty,
            "arguments:",
        };
        lines.AddRange(RenderText.ShownLines(description.Arguments.ToJsonString(Indented)));

        if (description.Definition is { } definition)
        {
            lines.Add(string.Empty);
            lines.Add("definition:");
            lines.AddRange(RenderText.ShownLines(definition.ToJsonString(Indented)));
        }

        return RenderText.Lines(lines);
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));
}
