using Jason.Contracts.Harness;

namespace Jason.Contracts.Tests.Harness;

/// <summary>
/// The rule the harness root is held to, one place for the runtime, which refuses a setting by it, and for the CLI's
/// <c>export</c> and <c>import</c>, which read and place a root while no runtime runs: one row for each thing a root may
/// not be, the default held to the data directory's rule alone, and a root that leads elsewhere judged where it leads.
/// </summary>
public sealed class HarnessRootRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-contracts-tests", Guid.NewGuid().ToString("N"));

    public HarnessRootRuleTests()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Data);
    }

    private string Home => Path.Combine(_root, "home");

    private string Data => Path.Combine(_root, "box", "data");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("relative/Jason")]
    [InlineData("~/Jason")]
    public void A_value_that_is_no_absolute_path_is_refused_and_not_said_back(string written)
    {
        var refusal = HarnessRootRule.Refusal(written, Data, Home);

        Assert.NotNull(refusal);
        Assert.StartsWith("Harness:Root must be an absolute path", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(written, refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("home", "must not be the home directory itself")]
    [InlineData("above-home", "must not hold the home directory")]
    [InlineData("top", "must not be the top of a drive")]
    [InlineData("data", "must not be the data directory")]
    [InlineData("inside-data", "must not be inside the data directory")]
    [InlineData("above-data", "must not hold the data directory")]
    public void A_root_the_runtime_may_not_have_is_refused_by_its_own_sentence(string which, string said)
    {
        var written = which switch
        {
            "home" => Home,
            "above-home" => _root,
            "top" => Path.GetPathRoot(_root)!,
            "data" => Data,
            "inside-data" => Path.Combine(Data, "harness"),
            _ => Path.GetDirectoryName(Data)!,
        };

        Assert.Contains(said, HarnessRootRule.Refusal(written, Data, Home), StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_apart_from_the_home_and_the_data_directory_is_taken()
    {
        Assert.Null(HarnessRootRule.Refusal(Path.Combine(_root, "elsewhere", "Jason"), Data, Home));
        Assert.Null(HarnessRootRule.Refusal(Path.Combine(Home, "Jason"), Data, Home));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_value_is_the_default_which_is_Jason_in_the_home_and_is_held_to_the_data_directory_alone(string? written)
    {
        Assert.Equal(Path.Combine(Home, HarnessRootRule.DefaultFolder), HarnessRootRule.Of(written, Home));
        Assert.Null(HarnessRootRule.Refusal(written, Data, Home));

        var refusal = HarnessRootRule.Refusal(written, Path.Combine(Home, "Jason", ".data"), Home);
        Assert.StartsWith("Harness:Root is not set, and its default", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_is_judged_where_it_leads_so_a_link_into_the_data_directory_is_refused()
    {
        var link = Path.Combine(_root, "linked-root");
        Link(link, Data);
        try
        {
            Assert.Contains("must not be the data directory", HarnessRootRule.Refusal(link, Data, Home), StringComparison.Ordinal);
            Assert.Equal(HarnessPlace.Data, HarnessRootRule.Where(link, Data, Home));
        }
        finally
        {
            // The link alone, never what it leads to.
            Directory.Delete(link);
        }
    }

    /// <summary>A link to a directory of the kind this account may make: a symbolic link, or on Windows a junction, which needs no privilege.</summary>
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), $"the junction at '{link}' was not made.");
    }
}
