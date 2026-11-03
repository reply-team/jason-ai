using System.CommandLine;
using Jason.Cli.Commands;
using Jason.Cli.Human;

namespace Jason.Cli.Session;

/// <summary>
/// The <c>session</c> noun: one verb, <c>open</c>, and no operation behind either. It starts the person's own agent host
/// on one approval or question in a new terminal window, which is not the runtime's to start or to know about;
/// <c>CliApp</c> states it with the other declared exceptions.
/// </summary>
public static class SessionCommands
{
    public const string OpenDescription =
        "Start your own agent host on one approval or question: a new terminal window at the campaign's folder (else its "
        + "project's, else the harness root, else your home folder), running the host the preference agent.interactive_host "
        + "names (default claude_code), with the brief `approval get --prompt` prints as its first message. The agent reads "
        + "and proposes; it never approves, rejects or answers, and nothing is decided by opening it. Of the runtime this asks "
        + "only the item, its campaign's folder and the preference. It starts no runtime, writes no file of Jason's, and on "
        + "macOS writes one script to the temporary folder that removes itself when Terminal runs it.";

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var session = new Command("session", "Your own agent host, started on one item that waits for you. No operation stands behind it: the runtime never starts a terminal.");
        session.Subcommands.Add(Open(env, actor));
        return session;
    }

    private static Command Open(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("open", OpenDescription);
        var approval = new Option<string?>("--approval") { Description = "Start it on one approval, as approval list shows its id." };
        var question = new Option<string?>("--question") { Description = "Start it on one question, as decision list shows its id." };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Start nothing and write nothing: print what would start, with the whole command, the brief included, and on macOS the script.",
        };
        var human = VerbOptions.Human();
        command.Options.Add(approval);
        command.Options.Add(question);
        command.Options.Add(dryRun);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var approvalId = parseResult.GetValue(approval);
            var questionId = parseResult.GetValue(question);
            if ((approvalId is null) == (questionId is null))
            {
                throw new UsageException("Say which: --approval <id> or --question <id>, one of them.");
            }

            return approvalId is not null
                ? SessionOpener.OpenAsync(env, PromptKind.Approval, approvalId, parseResult.GetValue(dryRun), parseResult.GetValue(human), cancellationToken)
                : SessionOpener.OpenAsync(env, PromptKind.Question, questionId!, parseResult.GetValue(dryRun), parseResult.GetValue(human), cancellationToken);
        });
        return command;
    }
}
