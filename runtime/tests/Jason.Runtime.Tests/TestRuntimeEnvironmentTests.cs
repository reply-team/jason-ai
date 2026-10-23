namespace Jason.Runtime.Tests;

public class TestRuntimeEnvironmentTests
{
    /// <summary>
    /// The five variables, spelled the way a shell would have to spell them. The names are literal here on
    /// purpose: what a child process reads is the string, and a helper that built it from a renamed constant
    /// would still be handing the child a name it no longer answers to. The harness root is a folder beside the data
    /// directory, so the folders a spawned runtime's creates make are never in the account's own home.
    /// </summary>
    [Fact]
    [Critical(Family.B9, "a test runtime never reaches the machine, the feed or a person")]
    public void A_spawned_runtime_is_given_its_directory_a_port_the_os_chooses_a_root_of_its_own_and_told_not_to_ask_the_feed_or_notify_anyone()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-tests", "somewhere");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        TestRuntimeEnvironment.Offline(environment, root);

        Assert.Equal(root, environment["JASON_DATA_DIR"]);
        Assert.Equal("false", environment["JASON_Update__CheckEnabled"]);
        Assert.Equal("false", environment["JASON_Notifications__Enabled"]);
        Assert.Equal("0", environment["JASON_Runtime__Port"]);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "jason-tests", "somewhere-home", "Jason"), environment["JASON_Harness__Root"]);
        Assert.Equal(5, environment.Count);
    }
}
