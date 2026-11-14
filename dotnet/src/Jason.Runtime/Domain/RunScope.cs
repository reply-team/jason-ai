namespace Jason.Runtime.Domain;

/// <summary>
/// The run a request says it comes from, for the flow of that one request: the attempt the <c>Jason-Attempt</c> header
/// names, as the CLI sends it from inside a run whatever actor a body claims. The API opens it for every request that
/// carries the header; it is what the request said, and whoever reads it checks it before trusting it.
/// </summary>
public static class RunScope
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The attempt this request names as the run it comes from, or null where it names none.</summary>
    public static string? Attempt => Current.Value;

    /// <summary>Opens the scope for <paramref name="attempt"/>; disposing it puts back what was there.</summary>
    public static IDisposable Open(string attempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attempt);
        var previous = Current.Value;
        Current.Value = attempt;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
