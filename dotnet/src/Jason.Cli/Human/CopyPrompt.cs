using System.Globalization;
using System.Text;
using Jason.Contracts.Hosts;

namespace Jason.Cli.Human;

/// <summary>What the prompt is about: an approval or a question.</summary>
public enum PromptKind
{
    Approval,
    Question,
}

/// <summary>Which recorded folder the prompt names, from the most specific to the least.</summary>
public enum PromptFolderLevel
{
    Campaign,
    Project,
    HarnessRoot,
}

/// <summary>A folder a session can stand in, and the level it was recorded at.</summary>
public sealed record PromptFolder(PromptFolderLevel Level, string Path);

/// <summary>
/// The text that briefs an agent session on one item that waits for a person: what waits, where to stand, the skill
/// to use, what to read and what to bring back, and that the session decides nothing. It is composed from opaque
/// identifiers and a folder and holds nothing of the item itself — not what it is about, not a preview, not a
/// question's words or its options — because the session reads the item live. It does no input or output, so the
/// verbs' <c>--prompt</c> and the handler of a toast's button make the same words.
/// </summary>
public static class CopyPrompt
{
    /// <summary>
    /// <paramref name="decidedState"/> is null for an item that waits (pending). For one that no longer does it is the item's own
    /// status word (approved, rejected, superseded, cancelled, answered): the first line says it was decided and that
    /// nothing waits, and step 5 is only a summary if the person asks. The rest stays, the paragraph that the session
    /// decides nothing included.
    /// </summary>
    public static string Compose(PromptKind kind, string itemId, string campaignId, string workItemId, PromptFolder? folder, string? decidedState = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        ArgumentException.ThrowIfNullOrEmpty(campaignId);
        ArgumentException.ThrowIfNullOrEmpty(workItemId);

        var approval = kind == PromptKind.Approval;
        var text = new StringBuilder();
        var noun = approval ? "approval" : "question";
        if (decidedState is null)
        {
            text.Append(approval
                ? $"Jason is waiting for a person's decision on an approval: {itemId}, in campaign {campaignId}.\n"
                : $"Jason is waiting for a person's answer to a question: {itemId}, in campaign {campaignId}.\n");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"Nothing waits on this {noun}: {itemId}, in campaign {campaignId}; it was already {decidedState}.\n");
        }

        if (folder is not null)
        {
            var (label, whose) = folder.Level switch
            {
                PromptFolderLevel.Campaign => ("Campaign folder", "campaign"),
                PromptFolderLevel.Project => ("Project folder (the campaign has none recorded)", "project"),
                _ => ("Harness root (no folder is recorded for the campaign or its project)", "client"),
            };
            text.Append(CultureInfo.InvariantCulture, $"\n{label}: {folder.Path}\n");
            text.Append(CultureInfo.InvariantCulture, $"Read its memory file, AGENTS.md: it holds what the person keeps about this {whose}. If you can, work from that folder.\n");
        }

        var get = approval ? $"jason approval get {itemId}" : $"jason decision get {itemId}";
        text.Append(CultureInfo.InvariantCulture, $"\nUse the skill approvals-and-questions. If your agent host does not have it, install the skills for the host you are running in with jason skills install --host <host>, where <host> is one of: {AgentHosts.Vocabulary}. Then start the skill.\n");
        text.Append("\nWhat to do:\n");
        text.Append(CultureInfo.InvariantCulture, $"1. Read the {(approval ? "approval" : "question")}: {get}\n");
        text.Append(CultureInfo.InvariantCulture, $"2. Read the campaign and its timeline: jason campaign get {campaignId} and jason journal list --campaign {campaignId}\n");
        text.Append(CultureInfo.InvariantCulture, $"3. Read the work item that raised it: jason workitem get {workItemId}; it names its role. Then that role's note, jason rolenote get --role <role> --campaign {campaignId}, and the other roles' as you need them (jason rolenote list --campaign {campaignId}).\n");
        text.Append("4. Research whatever else you need to understand the request.\n");
        text.Append(decidedState is null
            ? string.Create(CultureInfo.InvariantCulture, $"5. Come back to the person with a short summary and a proposed {(approval ? "decision" : "answer")}, with your reasons.\n")
            : $"5. If the person asks, come back with a short summary of what was decided and why.\n");
        text.Append("\nYou decide nothing. Approving, rejecting and answering stay with the person, and so does a standing approval: setting one is the person's own act. Do not run jason approval approve, jason approval reject, jason approval set-standing or jason decision answer.");
        return text.ToString();
    }
}
