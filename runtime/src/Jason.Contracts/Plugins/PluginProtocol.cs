namespace Jason.Contracts.Plugins;

/// <summary>
/// The fixed numbers of the plugin protocol: the version the runtime and the plugin host speak, the version of
/// the manifest they read, and the caps every side of the protocol enforces. One place, so the runtime, the host
/// and the documentation cannot drift apart.
/// </summary>
public static class PluginProtocol
{
    /// <summary>The plugin-host protocol: the <c>--protocol</c> argument, the envelope and the outcome.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The <c>manifest_version</c> a <c>plugin.yaml</c> must declare.</summary>
    public const int ManifestVersion = 1;

    /// <summary>The only canonical-operation contract version there is; per-operation schemas arrive later.</summary>
    public const int OperationContractVersion = 1;

    public const string InvocationIdPrefix = "pin";
    public const string SnapshotIdPrefix = "snp";

    public const int MaxInputBytes = 1_048_576;
    public const int MaxBindingBytes = 65_536;
    public const int MaxResultBytes = 1_048_576;
    public const int MaxDetailsBytes = 65_536;
    public const int MaxErrorCodeLength = 64;
    public const int MaxErrorMessageLength = 2000;
    public const int MaxExternalIds = 64;
    public const int MaxExternalIdLength = 256;
    public const int MaxCorrelationIdLength = 128;

    /// <summary>
    /// The only commands a manifest may declare to ask an executable for its version, each a one-element argv.
    /// A reload runs a program the user granted; letting the manifest name the arguments would make that a way
    /// to run anything.
    /// </summary>
    public static readonly string[] AllowedVersionCommands = ["--version", "-v", "-V", "version"];
}
