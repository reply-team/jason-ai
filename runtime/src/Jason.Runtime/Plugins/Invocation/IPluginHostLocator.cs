namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// How to start the plugin host: the program and the arguments that come before the mode's own. In production
/// that is this very executable; a test that runs from a build output starts the same code through the host it
/// has, which is the only reason this is a seam at all.
/// </summary>
public interface IPluginHostLocator
{
    IReadOnlyList<string> Command { get; }
}
