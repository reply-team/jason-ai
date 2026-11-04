namespace Jason.Runtime.Domain;

/// <summary>
/// The plugin whose door a request came through, for the flow of that one request. The API sets it only after it has
/// recognised a capability the runtime minted for that plugin's invocation, so a claim of <c>plugin</c> made anywhere
/// else finds no scope and is refused.
/// </summary>
public static class ActorScope
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The plugin this request's capability was minted for, or null where the request came through no door.</summary>
    public static string? Plugin => Current.Value;

    /// <summary>Opens the scope for <paramref name="pluginId"/>; disposing it puts back what was there.</summary>
    public static IDisposable ForPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var previous = Current.Value;
        Current.Value = pluginId;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
