using Jason.Cli.Commands;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Link;

/// <summary>
/// What a click on a <c>jason:</c> link does: the shell starts this executable with the link as its one argument, and
/// it is read against <see cref="AttentionLinks"/>, the table the toast's links are written from. A mode of the
/// executable, like <c>toast-host</c>, and never a verb: nothing is typed, and the runtime is asked only what
/// <c>jason ui open</c> asks.
/// </summary>
/// <remarks>
/// <para>
/// Any web page can navigate to a <c>jason:</c> link after the browser's own confirmation. So every act is local and
/// leaves nothing decided: it opens the person's own UI on an id they could have typed. The link's text reaches no log
/// and no child: what is sent is re-made from the parsed, checked id, and a link the table does not know is refused
/// without being repeated.
/// </para>
/// <para>
/// Exit codes are the CLI's: 0 done, 1 refused by the runtime, 2 a link the table does not know, 3 no runtime to ask.
/// Started by the shell, nobody reads what it prints; the person sees the browser open, or nothing.
/// </para>
/// </remarks>
public static class LinkHandler
{
    public static Task<int> RunAsync(string[] args, CliEnvironment env, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        var target = args is [var link] ? AttentionLinks.Parse(link) : null;
        if (target is null)
        {
            env.Error.WriteLine("usage: a jason: link names the attention list, or one approval or question by its id.");
            return Task.FromResult(ExitCodes.Usage);
        }

        var body = RequestBody.Empty();
        if (target.Id is { } id)
        {
            body = body.Set(target.Kind == AttentionKind.Approval ? "approval_id" : "decision_id", id);
        }

        return UiCommands.RunOpenAsync(env, body, noOpen: false, human: false, cancellationToken);
    }
}
