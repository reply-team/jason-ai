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

    /// <summary>
    /// The directory for content the UI does not trust: the runtime serves everything under it unable to act, whatever
    /// build holds it and whatever the file is called.
    /// </summary>
    public const string SealedDirectory = "sealed";

    /// <summary>
    /// The one kind of file under <see cref="SealedDirectory"/>, matched exactly: a page. A sealed page loads no file, and a
    /// script or a style there could serve only the UI itself, which would run it with its own rights.
    /// </summary>
    public const string SealedPageExtension = ".html";

    /// <summary>Whether a relative path of the layout is under <see cref="SealedDirectory"/> and so must be a page.</summary>
    public static bool IsSealed(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        return relative.StartsWith(SealedDirectory + "/", StringComparison.Ordinal);
    }

    /// <summary>Whether a path of the layout may hold a file: anything outside <see cref="SealedDirectory"/>, and a page inside it.</summary>
    public static bool AdmitsKind(string relative) =>
        !IsSealed(relative) || relative.EndsWith(SealedPageExtension, StringComparison.Ordinal);
}
