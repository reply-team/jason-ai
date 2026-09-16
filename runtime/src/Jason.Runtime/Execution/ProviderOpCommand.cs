using Jason.Contracts.Api;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Execution;

/// <summary>
/// Runs one attempt of a <c>provider_op</c> work item as one invocation of the package the claim pinned. There
/// is nothing to decide here: the route was resolved, the package chosen, the input composed and validated, and
/// the operation's contract read, all inside the claim transaction. This turns that plan into one call and
/// hands back what came of it.
/// </summary>
/// <remarks>
/// The child's budget is the operation's own, never what is left of the lease. A budget taken from the
/// remainder would make the same operation behave differently because the claim before it was slow, which is
/// not something anyone could reason about from a failure; the lease is kept longer than the contract instead,
/// and <see cref="Configuration.ProviderOpBudgetValidator"/> is what refuses a configuration where it is not.
/// </remarks>
public sealed class ProviderOpCommand(PluginInvoker invoker) : ICommand
{
    public WorkItemKind Kind => WorkItemKind.ProviderOp;

    public async Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Only the kill token reaches the child: stopping this runtime must not end a call a provider is
        // already acting on, which the invoker's own budget is what ends.
        var request = RequestFor(context);
        var result = await invoker.InvokeAsync(request, context.Kill).ConfigureAwait(false);

        return new CommandOutcome.Provider(result, Launch(result));
    }

    /// <summary>
    /// The claim's plan as one invocation. It is a function of the plan alone — nothing about when the attempt
    /// started, or how much of its lease is left, reaches the child.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The attempt reached the command without a plan. That is a mistake in the claim rather than a condition
    /// to carry on through: the claim is the only place a plan is built, and nothing here could invent one.
    /// </exception>
    public static PluginInvocationRequest RequestFor(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var plan = context.Plan ?? throw new InvalidOperationException(
            "A provider_op attempt reached the command without a plan; the claim is the only place that builds one.");

        return new PluginInvocationRequest(
            // The package that will run and the id the provenance records are one fact, taken from the plan:
            // the invoker refuses the two spellings disagreeing, and this is the caller that must not make it.
            plan.Plugin.Manifest.Id,

            // The operation is the contract the claim resolved, not a second copy of the item's own spelling.
            plan.Contract.Id,
            plan.Input,
            plan.Binding,
            CorrelationId: context.AttemptId,
            AttemptId: context.AttemptId,

            // Which try this is. What a plugin owes on a later one is its contract's business, not the runtime's.
            AttemptNumber: context.AttemptNumber,
            WorkItemId: context.WorkItemId,
            CampaignId: context.CampaignId,
            Timeout: TimeSpan.FromMilliseconds(plan.Contract.TimeoutMs),
            Pinned: new PinnedPlugin(plan.Plugin, plan.PluginSnapshotId));
    }

    /// <summary>
    /// How the attempt was actually run, in the same shape an agent attempt records: a provider attempt shows
    /// its command, its pid and its exit code exactly as the other kind does. Null where the invoker refused
    /// before any process existed, and the attempt then keeps the launch it was claimed with.
    /// </summary>
    private static AttemptLaunchDto? Launch(PluginInvocationResult result) =>
        result.Launch is { } launch ? new AttemptLaunchDto(launch.Command, launch.WorkDir, launch.Pid, launch.ExitCode) : null;
}
