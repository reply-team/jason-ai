using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The directories <c>--purge-data</c> refuses before anything is removed, decided as text: no directory here is
/// read, created or removed, which is the only safe way to ask this question about a drive's root.
/// </summary>
public class DataDirectoryBeltTests
{
    private static readonly string Temporary = Path.GetTempPath();

    private static readonly string Profile = Path.Combine(Temporary, "belt-home", "someone");

    private static readonly string Install = Path.Combine(Temporary, "belt-apps", "jason");

    public static TheoryData<string, string> Refused() => new()
    {
        { "the root of a file system", Path.GetPathRoot(Temporary)! },
        { "the profile", Profile },
        { "the profile, spelled with a trailing separator", Profile + Path.DirectorySeparatorChar },
        { "an ancestor of the profile", Path.GetDirectoryName(Profile)! },
        { "the temporary directory", Temporary },
        { "the installation", Install },
        { "an ancestor of the installation", Path.GetDirectoryName(Install)! },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_directory_that_is_not_a_data_directory_is_refused(string what, string directory)
    {
        var refusal = DataDirectoryBelt.Refusal(directory, Profile, Temporary, Install);

        Assert.True(refusal is not null, $"{what} ('{directory}') was not refused.");
    }

    public static TheoryData<string, string> Allowed() => new()
    {
        { "the default, inside the profile", Path.Combine(Profile, ".jason") },
        { "a directory of its own under the temporary directory", Path.Combine(Temporary, "jason-data") },
        { "a sibling of the installation", Path.Combine(Temporary, "belt-apps", "jason-data") },
        { "a directory whose name only begins like the profile's", Profile + "-data" },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void A_data_directory_of_its_own_is_not(string what, string directory)
    {
        var refusal = DataDirectoryBelt.Refusal(directory, Profile, Temporary, Install);

        Assert.True(refusal is null, $"{what} ('{directory}') was refused: {refusal}");
    }

    /// <summary>Where nothing names an installation or a profile, those two simply have nothing to hold.</summary>
    [Fact]
    public void An_unnamed_installation_or_profile_holds_nothing()
    {
        Assert.Null(DataDirectoryBelt.Refusal(Path.Combine(Temporary, "jason-data"), null, Temporary, null));
    }
}
