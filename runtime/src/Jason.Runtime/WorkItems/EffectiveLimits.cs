using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// What actually governs one work item: its own overrides where it has them, the kind's configured defaults
/// everywhere else. Resolved at every use rather than copied onto the item, so a settings change reaches the
/// work already queued.
/// </summary>
public readonly record struct EffectiveLimits(int TimeoutSeconds, int HeartbeatSeconds, int MaxAttempts)
{
    public static EffectiveLimits For(WorkItem item, DispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(options);
        var defaults = item.Kind == WorkItemKind.AiRole ? options.AiRole : options.ProviderOp;
        return new EffectiveLimits(
            item.TimeoutSeconds ?? defaults.TimeoutSeconds,
            item.HeartbeatSeconds ?? defaults.HeartbeatSeconds,
            item.MaxAttempts ?? defaults.MaxAttempts);
    }
}
