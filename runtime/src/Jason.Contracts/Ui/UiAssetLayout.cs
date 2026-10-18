namespace Jason.Contracts.Ui;

/// <summary>
/// What a UI build is on disk and in the release asset: the names every side agrees on. The archive carries these at
/// its root; the directory the runtime serves holds the same files. The asset's name carries no version, like every
/// other release file; the version is in the manifest.
/// </summary>
public static class UiAssetLayout
{
    public const string AssetName = "jason-ui.zip";

    public const string ManifestFile = "manifest.json";

    public const string IndexFile = "index.html";

    public const string NoticesFile = "THIRD-PARTY-NOTICES";

    public const string AssetsDirectory = "assets";
}
