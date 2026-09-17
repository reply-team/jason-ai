using System.Globalization;
using Jason.Contracts.Operations;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The one rule that spans two sections: the lease a provider attempt is claimed under must outlast the child
/// that attempt will start. A plugin is given the operation's own budget and never the remainder of a lease —
/// the same operation must not behave differently because the claim before it took longer — so the two numbers
/// are kept apart here instead, where a settings file that cannot work is refused before the runtime listens.
/// </summary>
/// <remarks>
/// Registered beside <see cref="DispatcherOptionsValidator"/> rather than folded into it: the options machinery
/// runs every registered validator, so a relationship between two sections gets its own validator instead of
/// being smuggled into the one that happens to own the section being validated. The floor comes from the
/// published contracts, so an operation slower than the default moves it the day it is published.
/// </remarks>
public sealed class ProviderOpBudgetValidator(LiveSettings<PluginsOptions> plugins) : IValidateOptions<DispatcherOptions>
{
    public ValidateOptionsResult Validate(string? name, DispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ProviderOp is not { } defaults)
        {
            // The shape itself is wrong, which DispatcherOptionsValidator reports in its own words.
            return ValidateOptionsResult.Success;
        }

        if (OperationCatalog.Slowest is not { } slowest)
        {
            return ValidateOptionsResult.Success;
        }

        var killGraceMs = plugins.Current.Invoker.KillGraceMs;
        var floorSeconds = FloorSeconds(slowest.TimeoutMs, killGraceMs);
        if (defaults.TimeoutSeconds >= floorSeconds)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(string.Create(
            CultureInfo.InvariantCulture,
            $"Dispatcher:ProviderOp:TimeoutSeconds must be at least {floorSeconds} to hold '{slowest.Id}', which declares {slowest.TimeoutMs} ms, and the {killGraceMs} ms a child is given to stop; got {defaults.TimeoutSeconds}."));
    }

    /// <summary>
    /// The shortest lease that can cover one run of an operation: its own budget plus the grace the invoker
    /// gives a child to stop, rounded up — a lease that ends while the runtime is still waiting for a process
    /// to die leaves exactly the ambiguity the budget rule exists to remove.
    /// </summary>
    public static int FloorSeconds(int timeoutMs, int killGraceMs) =>
        (int)Math.Ceiling((timeoutMs + (double)killGraceMs) / 1000);
}
