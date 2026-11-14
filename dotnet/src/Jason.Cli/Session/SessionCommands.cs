using System.CommandLine;
using Jason.Cli.Commands;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;

namespace Jason.Cli.Session;

/// <summary>
/// The <c>session</c> noun: one verb, <c>open</c>, and no operation behind either. It starts the person's own agent host
/// in a new terminal window, on one approval or question with its brief as the first input, at a folder with none (or
/// the system's plain shell there), or with <c>--setup</c> on Jason itself with the setup brief, which is not the
/// runtime's to start or to know about; <c>CliApp</c> states it with the other declared exceptions.
/// </summary>
public static class SessionCommands
{
    public const string OpenDescription =
        "Start your own agent host in a new terminal window, in one of three forms. On one approval or question (--approval or "
        + "--question): at the campaign's folder (else its project's, else the harness root, else your home folder), running the "
        + "host the preference agent.interactive_host names (default claude_code), with the brief `approval get --prompt` or, for "
        + "a question, `decision get --prompt` prints as its first message. Opening it decides nothing. Jason's brief asks the "
        + "agent to propose only and not to approve, reject or answer; what it may run is your host's own permission settings' to "
        + "decide. Of the runtime this asks only the item, its campaign's folder and the preference. "
        + "At a folder (no item): at the campaign's folder with --campaign, the project's with --project (its id or slug), each "
        + "else the harness root, the folder this command runs in with --here, or the harness root with none of them; running the "
        + "host --host names, else the one the preference names, or with --shell the system's plain shell; with no first input. "
        + "With --resume <id> the host continues its own conversation of that id instead of starting a new one (claude_code by "
        + "--resume <id>, codex by resume <id>; another host is refused). Of the runtime this asks only the folder's reads and, "
        + "when no program is named, the preference. "
        + "With --setup: at this installation's role skills folder, with its plugins folder beside it where the host can take "
        + "one, running the host --host names, else the one the preference names, with the setup brief as its first message. "
        + "Opening it changes nothing. Not with an item, --campaign, --project, --here, --shell or --resume. "
        + "Every form starts no runtime, writes no file of Jason's, and on macOS writes one script to the temporary folder that "
        + "removes itself when Terminal runs it.";

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var session = new Command("session", "Your own agent host, started on one item that waits for you or at a folder. No operation stands behind it: the runtime never starts a terminal.");
        session.Subcommands.Add(Open(env, actor));
        return session;
    }

    private static Command Open(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("open", OpenDescription);
        var approval = new Option<string?>("--approval") { Description = "Start it on one approval, as approval list shows its id." };
        var question = new Option<string?>("--question") { Description = "Start it on one question, as decision list shows its id." };
        var campaign = new Option<string?>("--campaign") { Description = "With no item: open at this campaign's folder, as cmp_.... Not with --project." };
        var project = new Option<string?>("--project") { Description = "With no item: open at this project's folder, by its id (prj_...) or its slug. Not with --campaign." };
        var host = new Option<string?>("--host") { Description = $"With no item: the agent host to start, one of {AgentHosts.Vocabulary}; absent, the preference agent.interactive_host names it. Not with --shell." };
        var here = new Option<bool>("--here") { Description = "With no item: open at the folder this command runs in, as it is; no marker is read there. Not with --campaign or --project." };
        var shell = new Option<bool>("--shell") { Description = "With no item: open the system's plain shell instead of an agent host. Not with --host or --resume." };
        var resume = new Option<string?>("--resume")
        {
            Description = "With no item: the host continues its own conversation of this id, as the host itself named it, instead of starting a new one. "
                + $"Taken by {AgentHosts.ResumeVocabulary}. Not with --shell.",
        };
        var setup = new Option<bool>("--setup")
        {
            Description = "Open your own agent host on Jason itself, with the setup brief as its first message. Not with an item, --campaign, --project, --here, --shell or --resume.",
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Start nothing and write nothing: print what would start, with the whole command, the brief included where there is one, and on macOS the script.",
        };
        var human = VerbOptions.Human();
        command.Options.Add(approval);
        command.Options.Add(question);
        command.Options.Add(campaign);
        command.Options.Add(project);
        command.Options.Add(here);
        command.Options.Add(host);
        command.Options.Add(shell);
        command.Options.Add(resume);
        command.Options.Add(setup);
        command.Options.Add(dryRun);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var approvalId = parseResult.GetValue(approval);
            var questionId = parseResult.GetValue(question);
            var campaignId = parseResult.GetValue(campaign);
            var projectName = parseResult.GetValue(project);
            var hostWord = parseResult.GetValue(host);
            var plainShell = parseResult.GetValue(shell);
            var atHere = parseResult.GetValue(here);
            var conversation = parseResult.GetValue(resume);
            if (parseResult.GetValue(setup))
            {
                if (approvalId is not null || questionId is not null)
                {
                    throw new UsageException("--setup opens a session on Jason itself, with no item; it is not taken with --approval or --question.");
                }

                if (campaignId is not null || projectName is not null || atHere)
                {
                    throw new UsageException("--setup opens at this installation's role skills folder; it is not taken with --campaign, --project or --here.");
                }

                if (plainShell)
                {
                    throw new UsageException("--setup starts an agent host with the setup brief as its first message; it is not taken with --shell.");
                }

                if (conversation is not null)
                {
                    throw new UsageException("--setup starts a new conversation with the setup brief as its first message; it is not taken with --resume.");
                }

                return SetupSessionOpener.OpenAsync(env, ParseHost(hostWord), parseResult.GetValue(dryRun), parseResult.GetValue(human), cancellationToken);
            }

            if (approvalId is not null && questionId is not null)
            {
                throw new UsageException("Say which: --approval <id> or --question <id>, one of them.");
            }

            if (approvalId is not null || questionId is not null)
            {
                if (campaignId is not null || projectName is not null || atHere || hostWord is not null || plainShell || conversation is not null)
                {
                    throw new UsageException("--campaign, --project, --here, --host, --shell and --resume open a session at a folder, with no item; they are not taken with --approval or --question.");
                }

                if ((approvalId ?? questionId) is { Length: 0 })
                {
                    throw new UsageException($"{(approvalId is not null ? "--approval" : "--question")} was given an empty id; it takes the id {(approvalId is not null ? "approval list" : "decision list")} shows.");
                }

                return approvalId is not null
                    ? SessionOpener.OpenAsync(env, PromptKind.Approval, approvalId, parseResult.GetValue(dryRun), parseResult.GetValue(human), cancellationToken)
                    : SessionOpener.OpenAsync(env, PromptKind.Question, questionId!, parseResult.GetValue(dryRun), parseResult.GetValue(human), cancellationToken);
            }

            if (campaignId is not null && projectName is not null)
            {
                throw new UsageException("Say which folder: --campaign <id> or --project <id or slug>, one of them, or neither for the harness root.");
            }

            if (atHere && (campaignId is not null || projectName is not null))
            {
                throw new UsageException("--here opens at the folder this command runs in; it is not taken with --campaign or --project.");
            }

            if (hostWord is not null && plainShell)
            {
                throw new UsageException("Say which program: --host <host> or --shell, one of them, or neither for the host the preference agent.interactive_host names.");
            }

            if (conversation is not null && plainShell)
            {
                throw new UsageException("--resume continues an agent host's own conversation; it is not taken with --shell.");
            }

            if (conversation is not null && !AgentHosts.IsConversationId(conversation))
            {
                throw new UsageException("--resume takes the id the host named its conversation by: a letter or digit, then at most 199 letters, digits, dots, underscores, colons or hyphens.");
            }

            if (campaignId is { Length: 0 } || projectName is { Length: 0 })
            {
                throw new UsageException($"{(campaignId is { Length: 0 } ? "--campaign" : "--project")} was given an empty value.");
            }

            var named = ParseHost(hostWord);
            var place = new FolderSessionOpener.Request(campaignId, projectName, atHere, named, plainShell, conversation);
            return FolderSessionOpener.OpenAsync(env, place, parseResult.GetValue(dryRun), parseResult.GetValue(human), cancellationToken);
        });
        return command;
    }

    /// <summary>The host <c>--host</c> names, or null where it names none; an empty or unknown word is a usage error.</summary>
    private static AgentHostKind? ParseHost(string? hostWord)
    {
        if (hostWord is null)
        {
            return null;
        }

        if (hostWord.Length == 0)
        {
            throw new UsageException("--host was given an empty value.");
        }

        return AgentHosts.Parse(hostWord)
            ?? throw new UsageException($"--host names '{hostWord}', which this jason does not know as a host; it knows {AgentHosts.Vocabulary}.");
    }
}
