namespace Jason.PluginHost;

/// <summary>
/// What the child's exit code means, and only that: whether the protocol completed. Everything that happens
/// inside or because of the plugin's code — an engine limit, a thrown error, a refused capability — is a failed
/// OUTCOME with exit 0, because the protocol did complete and the business answer is "it failed, here is why".
/// </summary>
public static class HostExitCodes
{
    /// <summary>An outcome was written: succeeded or failed, the protocol ran its course.</summary>
    public const int Completed = 0;

    /// <summary>The command line was not one this build accepts. One line on stderr, nothing on stdout.</summary>
    public const int Usage = 2;

    /// <summary>The invocation was refused before any JavaScript ran.</summary>
    public const int Rejected = 3;

    /// <summary>The host itself broke. Not the plugin's doing, and never a business answer.</summary>
    public const int HostFailure = 4;
}
