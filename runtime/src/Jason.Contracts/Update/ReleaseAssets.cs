using System.Runtime.InteropServices;

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
