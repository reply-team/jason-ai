namespace Jason.Runtime.Tests;

public class TestRuntimeEnvironmentTests
{
    /// <summary>
    /// The two variables, spelled the way a shell would have to spell them. The names are literal here on
    /// purpose: what a child process reads is the string, and a helper that built it from a renamed constant
    /// would still be handing the child a name it no longer answers to.
    /// </summary>
    [Fact]
    public void A_spawned_runtime_is_given_its_directory_and_told_not_to_ask_the_feed()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-tests", "somewhere");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        TestRuntimeEnvironment.Offline(environment, root);

        Assert.Equal(root, environment["JASON_DATA_DIR"]);
        Assert.Equal("false", environment["JASON_Update__CheckEnabled"]);
        Assert.Equal(2, environment.Count);
    }
}
