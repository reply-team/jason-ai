namespace Jason.Runtime.Plugins.Invocation;

/// <summary>One executable, several modes: the plugin host is this process, started again.</summary>
public sealed class ProcessPathLocator : IPluginHostLocator
{
    public IReadOnlyList<string> Command =>
        [Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unknown.")];
}
