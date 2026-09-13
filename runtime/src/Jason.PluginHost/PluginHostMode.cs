namespace Jason.PluginHost;

/// <summary>
/// Entry point of the <c>jason plugin-host</c> mode: a short-lived child process that will run one plugin
/// invocation received on stdin and answer on stdout. The protocol is not part of this build; the mode
/// exists so the executable's shape (one binary, three modes) is fixed from the start.
/// </summary>
public static class PluginHostMode
{
    public static int Run(IReadOnlyList<string> args, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        stderr.WriteLine("jason plugin-host: the plugin host protocol is not implemented in this build.");
        return 1;
    }
}
