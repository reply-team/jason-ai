using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;

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

        // The one line the operation's own template renders, where it declares one: what would be done, to whom
        // and how, read before the properties it is drawn from. A preview without one prints as it always did.
        if ((string?)preview?["summary"] is { Length: > 0 } summary)
        {
            text.AppendLine(Line("Summary:", summary));
        }

        text.AppendLine(Line("Operation:", approval.Operation));
        text.AppendLine(Line("Intent:", (string?)preview?["intent"]));
        text.AppendLine(Line("Reach:", Property(preview?["reach"])));
        text.AppendLine(Line("Undo:", Property(preview?["reversibility"])));
        text.AppendLine(Line("Cost:", Property(preview?["cost"])));
        text.AppendLine(Line("Campaign:", Named(preview?["campaign"])));
        text.AppendLine(Line("Contact:", Person(preview?["contact"])));
        text.AppendLine(Line("Plugin:", approval.PluginId));
        text.AppendLine(Line("Account:", approval.BindingIdentity));
        AppendExample(text, preview?["example"]);
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

    /// <summary>
    /// The one reading of a template that the approval holds, printed as the runtime rendered it. It is somebody's
    /// message, so a character that changes how the line around it reads (see
    /// <see cref="PreviewTemplate.ChangesTheLine"/>) is shown as U+FFFD, a mark that can be seen, and never runs; a
    /// tab stays. The subject is one line and its line breaks are such characters; the body keeps its own, each
    /// of its lines indented under the label so that none reads as a labelled line of the approval. An empty subject
    /// says so. A preview without an example adds nothing.
    /// </summary>
    private static void AppendExample(StringBuilder text, JsonNode? example)
    {
        if (example is not JsonObject)
        {
            return;
        }

        var subject = (string?)example["subject"];
        text.AppendLine(Line("Example:", "one reading of the template, as the runtime renders it"));
        text.AppendLine(Indented("Subject:", subject is { Length: 0 } ? "(empty)" : Shown(subject)));
        text.AppendLine("  Body:");
        foreach (var line in ((string?)example["body"] ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            text.AppendLine(line.Length == 0 ? string.Empty : "    " + Shown(line));
        }

        if (example["unfilled"] is JsonArray { Count: > 0 } unfilled)
        {
            text.AppendLine(Indented("Unfilled:", string.Join(", ", unfilled.Select(name => (string?)name))));
        }

        text.AppendLine("  You approve the template as written; the example is the runtime's rendering, not the provider's, which may differ.");
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
        var name = (string?)contact["name"];
        var id = (string?)contact["id"];

        // A contact known only by an address has no name to set its id apart from: the id stands where the name would.
        var who = string.IsNullOrEmpty(name) ? id : $"{name} ({id})";
        return who + (where is null ? string.Empty : $" | {channel} {where}");
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);

    private static string? Shown(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var shown = new StringBuilder(text.Length);
        foreach (var character in text.EnumerateRunes())
        {
            // A tab is whitespace a person reads as such; the rule of the summary would turn it into a space.
            shown.Append(character.Value != '\t' && PreviewTemplate.ChangesTheLine(character) ? "\uFFFD" : character.ToString());
        }

        return shown.ToString();
    }

    private static string Indented(string label, string? value) => "  " + label.PadRight(Label - 2) + (string.IsNullOrEmpty(value) ? "-" : value);
}
