namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// One process-wide variable, set for as long as a test needs it and put back afterwards. The stand-in vendor
/// CLI finds its account the way the real one finds its store, so this is how a test says which account it
/// planted — and it is carried to the child by the base environment, which is the runtime's own code doing the
/// work rather than a test arranging it.
/// </summary>
/// <remarks>
/// A test that uses this belongs to <see cref="ProcessEnvironmentCollection"/>: the variable is the whole
/// process's, so two classes setting it at once would each see the other's account.
/// </remarks>
internal sealed class ProcessVariable : IDisposable
{
    private readonly string _name;
    private readonly string? _was;

    public ProcessVariable(string name, string value)
    {
        _name = name;
        _was = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _was);
}
