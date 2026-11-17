using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// The refusal of a data directory whose database a newer Jason migrated: one code, and one sentence that names this build's
/// version and the data directory and says what to do, in the words the runtime, the CLI and the desktop all show.
/// </summary>
public sealed class DataNewerTests
{
    [Fact]
    public void The_code_is_data_newer()
    {
        Assert.Equal("data_newer", DataNewer.Code);
    }

    [Fact]
    public void The_sentence_names_this_version_and_the_data_directory_and_says_to_update()
    {
        var data = Path.Combine(Path.GetTempPath(), "jason-data-newer", ".jason");

        Assert.Equal(
            $"This Jason is 0.49.0, and the data in {data} was changed by a newer Jason, so the runtime was not started and nothing was changed. "
            + "Update Jason with `jason update apply`, or Check for Updates in Jason Desktop.",
            DataNewer.Sentence("0.49.0", data));
    }

    /// <summary>The build after a plus names a commit, not a version, and a person is told the version alone.</summary>
    [Theory]
    [InlineData("0.49.0+1ec19a3", "0.49.0")]
    [InlineData("0.49.0-dev+1ec19a3", "0.49.0-dev")]
    [InlineData("0.49.0-rc.1", "0.49.0-rc.1")]
    public void The_version_is_said_without_its_build_metadata(string current, string said)
    {
        Assert.StartsWith($"This Jason is {said}, and the data in ", DataNewer.Sentence(current, "/data"), StringComparison.Ordinal);
    }
}
