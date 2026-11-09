using System.Text.Json;
using Jason.Contracts.Json;

namespace Jason.Cli.Moving;

/// <summary>One file an export carries, as <c>export.json</c> lists it.</summary>
/// <param name="Path">Its name in the archive: under <c>data/</c> or <c>harness/</c>, segments separated by <c>/</c>.</param>
/// <param name="Bytes">Its length.</param>
/// <param name="Sha256">The SHA-256 of its bytes, in lowercase hexadecimal.</param>
public sealed record ExportEntry(string Path, long Bytes, string Sha256);

/// <summary>The logon registration as the export found it.</summary>
/// <param name="Registered">Whether a registration at logon started the runtime on this data directory.</param>
/// <param name="Platform">How this machine registers one: <c>windows</c>, <c>macos</c>, <c>linux</c> or <c>unsupported</c>.</param>
public sealed record ExportAutostart(bool Registered, string Platform);

/// <summary>
/// <c>export.json</c>, at the archive's root: which Jason wrote it, when, on what system, from which places, whether it
/// started at logon, and every file and directory it carries, each file with its digest.
/// </summary>
/// <param name="Format">The layout of the archive; <see cref="CurrentFormat"/> is the only one.</param>
/// <param name="JasonVersion">The version of the Jason that wrote it.</param>
/// <param name="ExportedAt">The moment it was written, in UTC.</param>
/// <param name="OperatingSystem"><c>windows</c>, <c>macos</c> or <c>linux</c>.</param>
/// <param name="DataDirectory">The data directory it was read from, as that machine spelt it.</param>
/// <param name="HarnessRoot">The harness root it was read from, as that machine spelt it.</param>
/// <param name="Home">The home directory of the account it was read from, as that machine spelt it.</param>
/// <param name="Autostart">The logon registration as it found it.</param>
/// <param name="Entries">Every file carried.</param>
/// <param name="Directories">Every directory carried, with its trailing <c>/</c>, so an empty one travels too.</param>
public sealed record ExportManifest(
    int Format,
    string JasonVersion,
    DateTimeOffset ExportedAt,
    string OperatingSystem,
    string DataDirectory,
    string HarnessRoot,
    string Home,
    ExportAutostart Autostart,
    IReadOnlyList<ExportEntry> Entries,
    IReadOnlyList<string> Directories)
{
    /// <summary>The name of the file at the archive's root.</summary>
    public const string FileName = "export.json";

    /// <summary>The prefix of every name from the data directory.</summary>
    public const string DataPrefix = "data/";

    /// <summary>The prefix of every name from the harness root.</summary>
    public const string HarnessPrefix = "harness/";

    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Written = new(JasonJson.Options) { WriteIndented = true };

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, Written);

    /// <summary>The name of this machine's system, as <c>export.json</c> writes it.</summary>
    public static string ThisSystem() =>
        System.OperatingSystem.IsWindows() ? "windows" : System.OperatingSystem.IsMacOS() ? "macos" : "linux";
}
