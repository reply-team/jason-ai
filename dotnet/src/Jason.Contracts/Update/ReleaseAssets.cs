using System.Runtime.InteropServices;
using Jason.Contracts.Ui;

namespace Jason.Contracts.Update;

/// <summary>
/// What a release publishes, named once. The workflow that writes the files, the manifest that lists them and
/// the applier that downloads one all have to agree, and they cannot see each other — so the names live here
/// and the workflow's own guard test reads them from this type rather than from a string in a YAML file.
/// </summary>
/// <remarks>
/// The archive names carry no version. That is what makes
/// <c>https://github.com/…/releases/latest/download/jason-win-x64.zip</c> resolve for every release there will
/// ever be, so a README, an install script and a runtime's feed URL are written once and never edited.
/// </remarks>
public static class ReleaseAssets
{
    /// <summary>At most this long, which is longer than any name this project publishes and short enough to be a file name.</summary>
    private const int MaxAssetNameLength = 100;

    /// <summary>The platforms this build publishes for, in the order the release workflow builds them.</summary>
    public static IReadOnlyList<string> Rids { get; } = ["win-x64", "linux-x64", "osx-arm64"];

    /// <summary>The updater's feed, and the file a person checks a download against by hand.</summary>
    public const string Manifest = "manifest.json";

    public const string Checksums = "checksums.txt";

    /// <summary>
    /// The install scripts, published with every release beside its archives.
    /// </summary>
    /// <remarks>
    /// The one-liners fetch the script from a release rather than from the default branch: a script on the branch
    /// can be ahead of every release, and a pinned install has to run the script the release it installs was
    /// published with. As assets, the one-liner is a release's own file.
    /// </remarks>
    public static IReadOnlyList<string> InstallScripts { get; } = ["install.sh", "install.ps1"];

    /// <summary>
    /// The Reply plugin's package: the files of <c>plugins/reply</c> at the release's tag, at the archive's root, so that
    /// unpacked into the plugins directory it has the content digest the tag's package has.
    /// </summary>
    /// <remarks>
    /// Listed in <see cref="Checksums"/>, and named in the <see cref="Manifest"/> under its <c>plugins</c> rather than its
    /// artifacts, which are platforms. The name carries no version, as the archives' do not; the plugin's version is
    /// inside its <c>plugin.yaml</c>, and the manifest says it beside the package's content digest.
    /// </remarks>
    public const string ReplyPlugin = "jason-plugin-reply.zip";

    /// <summary>
    /// The UI's package: the build <c>npm run package</c> makes from <c>ui/</c> at the release's tag, with the release's
    /// version in its <c>manifest.json</c>. <c>jason ui install</c> places it as the default UI.
    /// </summary>
    /// <remarks>
    /// Listed in <see cref="Checksums"/> and never in the <see cref="Manifest"/>, which names platforms and plugins. Built
    /// at the tag rather than taken from CI, whose zip carries a development version and expires.
    /// </remarks>
    public const string Ui = UiAssetLayout.AssetName;

    /// <summary>Every file a release carries, in the order the release workflow attaches them.</summary>
    public static IReadOnlyList<string> Published { get; } =
        [.. Rids.Select(For), Manifest, Checksums, .. InstallScripts, ReplyPlugin, Ui];

    /// <summary>The archive a platform's release is published as.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The platform is not one this build publishes for.</exception>
    public static string For(string rid) => rid switch
    {
        "win-x64" => "jason-win-x64.zip",
        "linux-x64" => "jason-linux-x64.tar.gz",
        "osx-arm64" => "jason-osx-arm64.tar.gz",
        _ => throw new ArgumentOutOfRangeException(nameof(rid), rid, "No release is built for this platform."),
    };

    /// <summary>
    /// This machine's platform, or null where Jason publishes nothing for it. Null is the honest answer: a
    /// caller says "no release is built for this machine" rather than downloading something that cannot run.
    /// </summary>
    public static string? CurrentRid { get; } =
        (RuntimeInformation.OSArchitecture, OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS()) switch
        {
            (Architecture.X64, true, _, _) => "win-x64",
            (Architecture.X64, _, true, _) => "linux-x64",
            (Architecture.Arm64, _, _, true) => "osx-arm64",
            _ => null,
        };

    /// <summary>The executable inside the archive, which is the file an update swaps and a script installs.</summary>
    public static string ExecutableName { get; } = OperatingSystem.IsWindows() ? "jason.exe" : "jason";

    /// <summary>
    /// The notices an archive carries beside the executable, by the names the archive gives them: the licence and
    /// the third-party notices. Releases from 0.2.0 to 0.5.0 carry the first alone.
    /// </summary>
    public static IReadOnlyList<string> Notices { get; } = ["LICENSE", "THIRD-PARTY-NOTICES"];

    /// <summary>
    /// The name a notice is kept under beside the executable, wherever the executable is - installed, staged or kept for a
    /// rollback: prefixed, so that a directory other tools share holds no bare <c>LICENSE</c> of Jason's, and so that the
    /// uninstall can remove it by name.
    /// </summary>
    public static string NoticeFileName(string notice) => "jason-" + notice;

    /// <summary>A notice's path beside an executable.</summary>
    public static string NoticeBeside(string executable, string notice) =>
        Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, NoticeFileName(notice));

    /// <summary>
    /// Whether a name read from a feed is a file name and nothing else: ASCII letters, digits, dot, dash and
    /// underscore, starting with a letter or a digit.
    /// </summary>
    /// <remarks>
    /// An asset name arrives from a web page this runtime did not write and then becomes part of a URL and part
    /// of a path. A name that walks out of its directory, or that is a path at all, is refused here — before
    /// either is built — rather than sanitised afterwards, because the list of things to sanitise is never
    /// finished and the list of things a file name may contain is.
    /// </remarks>
    public static bool IsWellFormedAssetName(string? name)
    {
        // The first character must be a letter or a digit, which is also what refuses "." and ".." and every
        // other name that is only dots: a leading dot never gets past this line.
        if (string.IsNullOrEmpty(name) || name.Length > MaxAssetNameLength || !char.IsAsciiLetterOrDigit(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
