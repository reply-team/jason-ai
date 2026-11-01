using System.Text.Json;

namespace Jason.Cli.Installation;

/// <summary>
/// One uninstall entry this account's registry carries for a package winget installed: the entry's own name, the
/// package identifier winget wrote into it, and the directory it says the package was installed into.
/// </summary>
public sealed record WingetRegistration(string Key, string? PackageIdentifier, string? InstallLocation);

/// <summary>
/// What the package managers themselves recorded about what they installed — the one question about ownership that a
/// path cannot answer.
/// </summary>
/// <remarks>
/// <para>
/// A seam, and null on <see cref="CliEnvironment"/> everywhere but <see cref="CliEnvironment.Default"/>. winget's record
/// lives in this account's registry, and a test that read it would be reading the registry of whoever ran the suite.
/// Null is "no manager recorded anything", which leaves every installation Jason's own: the answer every verb gave
/// before this seam existed, and a harmless one to get by accident.
/// </para>
/// <para>
/// It answers two questions and decides nothing. <see cref="PackageOwner"/> decides, over the path and these answers.
/// </para>
/// </remarks>
public interface IManagerRecords
{
    /// <summary>
    /// Homebrew: whether <c>&lt;keg&gt;/INSTALL_RECEIPT.json</c> is there and is a JSON object naming
    /// <c>homebrew_version</c>, as Homebrew writes it into every keg it installs.
    /// </summary>
    bool HomebrewReceipt(string keg);

    /// <summary>
    /// winget: this account's uninstall entries whose names begin with <paramref name="packageIdentifier"/>, with the
    /// two values the rule reads from each.
    /// </summary>
    IReadOnlyList<WingetRegistration> WingetRegistrations(string packageIdentifier);
}

/// <summary>The records this build knows how to read.</summary>
public static class ManagerRecords
{
    /// <summary>The key under <c>HKEY_CURRENT_USER</c> where winget writes a portable package's uninstall entry.</summary>
    /// <remarks>
    /// A user-scoped portable install, which is what <c>Reply.Jason</c> is. A real <c>winget install</c> of the release
    /// from a local manifest recorded the entry there, named <c>Reply.Jason__DefaultSource</c> and carrying
    /// <c>WinGetPackageIdentifier</c> and <c>InstallLocation</c>, the package directory, and kept both across a
    /// <c>winget upgrade</c>. A community-source install names it for its source,
    /// <c>Reply.Jason_Microsoft.Winget.Source_8wekyb3d8bbwe</c> by winget's rule, which was not recorded; both begin with
    /// the identifier and an underscore.
    /// </remarks>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>This machine's: the keg's own file, and this account's registry on Windows.</summary>
    public static IManagerRecords ForThisMachine() => new MachineRecords();

    /// <summary>
    /// Whether <c>&lt;keg&gt;/INSTALL_RECEIPT.json</c> is a JSON object naming <c>homebrew_version</c>. Anything else — no
    /// file, a file that cannot be read, a file that is not JSON, JSON that is not an object or does not name the key —
    /// is no receipt.
    /// </summary>
    public static bool ReadHomebrewReceipt(string keg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keg);

        var receipt = Path.Combine(keg, "INSTALL_RECEIPT.json");
        try
        {
            if (!File.Exists(receipt))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(receipt));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("homebrew_version", out _);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class MachineRecords : IManagerRecords
    {
        public bool HomebrewReceipt(string keg) => ReadHomebrewReceipt(keg);

        public IReadOnlyList<WingetRegistration> WingetRegistrations(string packageIdentifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageIdentifier);

            if (!OperatingSystem.IsWindows())
            {
                return [];
            }

            try
            {
                using var uninstall = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKey, writable: false);
                if (uninstall is null)
                {
                    return [];
                }

                var found = new List<WingetRegistration>();
                foreach (var name in uninstall.GetSubKeyNames().Where(name => name.StartsWith(packageIdentifier, StringComparison.OrdinalIgnoreCase)))
                {
                    using var entry = uninstall.OpenSubKey(name, writable: false);
                    if (entry is not null)
                    {
                        found.Add(new WingetRegistration(
                            name,
                            entry.GetValue("WinGetPackageIdentifier") as string,
                            entry.GetValue("InstallLocation") as string));
                    }
                }

                return found;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A registry that cannot be read records nothing this build can show, so the installation is Jason's own:
                // the answer every verb gave before it asked.
                return [];
            }
        }
    }
}
