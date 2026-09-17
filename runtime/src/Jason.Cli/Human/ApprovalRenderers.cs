using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the approval verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class ApprovalRenderers
{
    private const int Label = 15;

    /// <summary>What is waiting, as a table: the work, what it would do, and how long it has been asked.</summary>
    public static string? ApprovalList(string json)
    {
        var page = RenderText.Read<Page<ApprovalSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("REQUESTED", "OPERATION", "STATUS", "WORK ITEM", "ID");
        foreach (var approval in page.Items)
        {
            table.Row(
                RenderText.Moment(approval.RequestedAt),
                approval.Operation,
                RenderText.Snake(approval.Status),
                approval.WorkItemId,
                approval.Id);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// One decision, with what it would do first: a person reads the effect, the person it reaches and the
    /// account it acts through before anything about identifiers, because that is the order in which the
    /// decision is actually made.
    /// </summary>
    public static string? Approval(string json)
    {
        var approval = RenderText.Read<ApprovalDto>(json);
        if (approval?.Id is null || approval.Operation is null)
        {
            return null;
        }

        var preview = approval.Preview;
        var text = new StringBuilder();
        text.AppendLine(Line("Operation:", approval.Operation));
        text.AppendLine(Line("Intent:", (string?)preview?["intent"]));
        text.AppendLine(Line("Reach:", Property(preview?["reach"])));
        text.AppendLine(Line("Undo:", Property(preview?["reversibility"])));
        text.AppendLine(Line("Cost:", Property(preview?["cost"])));
        text.AppendLine(Line("Campaign:", Named(preview?["campaign"])));
        text.AppendLine(Line("Contact:", Person(preview?["contact"])));
        text.AppendLine(Line("Plugin:", approval.PluginId));
        text.AppendLine(Line("Account:", approval.BindingIdentity));
        text.AppendLine(Line("Status:", RenderText.Snake(approval.Status)));
        text.AppendLine(Line("Parked:", approval.Reason));
        text.AppendLine(Line("Requested:", RenderText.Moment(approval.RequestedAt)));
        text.AppendLine(Line("Decided:", RenderText.Moment(approval.DecidedAt)));
        text.AppendLine(Line("Decided by:", RenderText.Actor(approval.DecidedBy)));
        text.AppendLine(Line("Note:", approval.DecisionReason));
        text.AppendLine(Line("Work item:", approval.WorkItemId));
        text.AppendLine(Line("Subject:", approval.SubjectHash));
        text.Append(Line("Id:", approval.Id));
        return text.ToString();
    }

    /// <summary>A published property as a person reads it: the dangerous value, and the condition beside it.</summary>
    private static string? Property(JsonNode? property) =>
        property is null
            ? null
            : (string?)property["value"] + ((bool?)property["conditional"] == true ? " (conditional)" : string.Empty);

    private static string? Named(JsonNode? entity) =>
        entity is null ? null : $"{(string?)entity["name"]} ({(string?)entity["id"]})";

    private static string? Person(JsonNode? contact)
    {
        if (contact is null)
        {
            return null;
        }

        var where = (string?)contact["value"];
        var channel = (string?)contact["channel"];
        return $"{(string?)contact["name"]} ({(string?)contact["id"]})" + (where is null ? string.Empty : $" · {channel} {where}");
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
