using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>How the subscription runner hands one line to one handler: a seam, so a test answers without a child.</summary>
public interface ISubscriptionInvoker
{
    Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken);
}

/// <summary>The runtime's invoker: one child of the plugin host per handler, made at the one door every child is made at.</summary>
public sealed class PluginSubscriptionInvoker(PluginInvoker invoker) : ISubscriptionInvoker
{
    public Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken) =>
        invoker.InvokeAsync(request, cancellationToken);
}
