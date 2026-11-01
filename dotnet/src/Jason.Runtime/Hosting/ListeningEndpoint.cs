namespace Jason.Runtime.Hosting;

/// <summary>The port the settings asked for and the one Kestrel took; the second is known only once the host has started.</summary>
public sealed class ListeningEndpoint(int configuredPort)
{
    public int ConfiguredPort { get; } = configuredPort;

    public int Port { get; set; }
}
