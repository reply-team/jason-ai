using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Link;

/// <summary>
/// What a click on a <c>jason:</c> link does: the shell starts this executable with the link as its one argument, and
/// it is read against <see cref="AttentionLinks"/>, the table the toast's links are written from. A mode of the
/// executable, like <c>toast-host</c>, and never a verb: nothing is typed, and the runtime is asked only what
/// <c>jason ui open</c> asks, for a prompt link what <c>approval get --prompt</c> and <c>decision get --prompt</c> ask,
/// and for a session link what <c>jason session open</c> asks.
/// </summary>
/// <remarks>
/// <para>
/// Any web page can navigate to a <c>jason:</c> link after the browser's own confirmation. So every act is local and
/// leaves nothing decided: it opens the person's own UI on an id they could have typed, or puts on the clipboard the
/// prompt <c>--prompt</c> prints for that id, which replaces what the clipboard held, or starts the person's own agent
/// host on that id in a new terminal, in the host's own permission mode at the campaign's folder. Jason's brief asks the
/// agent to propose only and not to approve, reject or answer; what it may run is the host's own permission settings' to
/// decide. The link's text reaches no log and no child: what is sent is re-made from the parsed, checked id, and a link
/// the table does not know is refused without being repeated.
/// </para>
/// <para>
/// Exit codes are the CLI's: 0 done, 1 refused by the runtime or the clipboard, or nothing to start, 2 a link the table
/// does not know, 3 no runtime to ask. Started by the shell, nobody reads what it prints; the person sees the browser open, a prompt to
/// paste, a terminal window, or nothing. A prompt link prints nothing on stdout when it succeeds; a session link prints
/// what <c>jason session open</c> prints.
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

        return target.Act switch
        {
            AttentionAct.Prompt => CopyPromptAsync(env, target.Kind == AttentionKind.Approval, body, cancellationToken),
            AttentionAct.Session => Session.SessionOpener.OpenAsync(env, target.Kind == AttentionKind.Approval ? PromptKind.Approval : PromptKind.Question, target.Id!, dryRun: false, human: false, cancellationToken),
            _ => UiCommands.RunOpenAsync(env, body, noOpen: false, human: false, cancellationToken),
        };
    }

    /// <summary>
    /// The toast's Copy prompt button: the item read by its kind's own get, the prompt composed as <c>--prompt</c>
    /// composes it, and put on the clipboard. Where nothing can write the clipboard nothing is asked; a refusal, of the
    /// runtime or the clipboard, copies nothing.
    /// </summary>
    private static async Task<int> CopyPromptAsync(CliEnvironment env, bool approval, JsonObject body, CancellationToken cancellationToken)
    {
        if (env.Clipboard is not { } clipboard)
        {
            env.Error.WriteLine("Copy prompt: nothing on this machine can write the clipboard; jason approval get --prompt or jason decision get --prompt prints the same text.");
            return ExitCodes.ApiError;
        }

        var (exit, response) = await OperationRunner.SendAsync(env, approval ? Operations.ApprovalGet : Operations.DecisionGet, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exit;
        }

        if (!response.IsSuccess)
        {
            env.Error.WriteLine(OperationRunner.ErrorText(response));
            return ExitCodes.ApiError;
        }

        var render = approval ? PromptRenderers.Approval(env) : PromptRenderers.Question(env);
        if (await render(response.Body, cancellationToken).ConfigureAwait(false) is not { } prompt)
        {
            env.Error.WriteLine($"Copy prompt: {PromptRenderers.NoItem}; nothing was copied.");
            return ExitCodes.ApiError;
        }

        if (!clipboard.TrySetText(prompt, out var problem))
        {
            env.Error.WriteLine($"Copy prompt: the clipboard refused the text ({problem}); nothing was copied.");
            return ExitCodes.ApiError;
        }

        return ExitCodes.Success;
    }
}
