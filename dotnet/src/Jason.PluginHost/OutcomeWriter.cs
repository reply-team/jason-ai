using System.Text.Json;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost;

/// <summary>
/// The only thing that ever writes to stdout, and it writes exactly once: one compact JSON document, at the very
/// end. There is no <c>console</c> in the engine, so nothing a plugin does can get between the runtime and this.
/// </summary>
public static class OutcomeWriter
{
    public static async Task WriteAsync(TextWriter stdout, PluginOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(outcome);

        await stdout.WriteAsync(JsonSerializer.Serialize(outcome, JasonJson.Options)).ConfigureAwait(false);
        await stdout.FlushAsync().ConfigureAwait(false);
    }
}
