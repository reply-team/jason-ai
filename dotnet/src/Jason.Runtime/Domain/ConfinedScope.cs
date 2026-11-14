namespace Jason.Runtime.Domain;

/// <summary>
/// Whether a request says it comes from a confined call of the CLI — the desktop's chat — for the flow of that one request:
/// the <c>Jason-Confined</c> header the CLI sends whenever <c>JASON_CONFINED</c> is set. The API opens it for every request
/// that carries the header. It is what the request said: a higher bar, not a boundary, since the runtime cannot tell a
/// person from a program that holds the token, and a request made around the CLI carries no header.
/// </summary>
public static class ConfinedScope
{
    private static readonly AsyncLocal<bool> Current = new();

    /// <summary>Whether this request says it comes from a confined call.</summary>
    public static bool Confined => Current.Value;

    /// <summary>Opens the scope; disposing it puts back what was there.</summary>
    public static IDisposable Open()
    {
        var previous = Current.Value;
        Current.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
