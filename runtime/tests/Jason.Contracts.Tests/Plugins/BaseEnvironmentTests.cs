using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

public class BaseEnvironmentTests
{
    private static Dictionary<string, string> Source() => new(StringComparer.Ordinal)
    {
        ["PATH"] = "/usr/bin",
        ["HTTPS_PROXY"] = "http://proxy.internal:3128",
        ["no_proxy"] = "localhost",
        ["EXAMPLE_TOKEN"] = "secret-value",
        ["SECRET"] = "not-listed",
        ["JASON_DATA_DIR"] = "/data",
        ["SYSTEMROOT"] = "C:\\Windows",
        ["Home"] = "/home/someone",
    };

    [Fact]
    public void The_machine_configuration_a_child_needs_is_copied_and_nothing_else()
    {
        var built = BaseEnvironment.Build(Source(), []);

        Assert.Equal("/usr/bin", built["PATH"]);
        Assert.Equal("http://proxy.internal:3128", built["HTTPS_PROXY"]);
        Assert.Equal("localhost", built["no_proxy"]);
        Assert.False(built.ContainsKey("SECRET"));
        Assert.False(built.ContainsKey("EXAMPLE_TOKEN"));
    }

    [Fact]
    public void A_granted_variable_is_copied_by_name()
    {
        var built = BaseEnvironment.Build(Source(), ["EXAMPLE_TOKEN"]);

        Assert.Equal("secret-value", built["EXAMPLE_TOKEN"]);
        Assert.False(built.ContainsKey("SECRET"));
    }

    [Fact]
    public void A_variable_that_is_not_in_the_source_stays_absent()
    {
        var built = BaseEnvironment.Build(Source(), ["ABSENT_TOKEN"]);

        Assert.False(built.ContainsKey("ABSENT_TOKEN"));
    }

    [Fact]
    public void The_data_directory_never_reaches_a_child_even_when_it_is_granted()
    {
        var built = BaseEnvironment.Build(Source(), ["JASON_DATA_DIR"]);

        Assert.False(built.ContainsKey("JASON_DATA_DIR"));
        Assert.DoesNotContain(built.Keys, key => key.StartsWith(BaseEnvironment.ReservedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Names_are_compared_the_way_the_operating_system_compares_them()
    {
        var built = BaseEnvironment.Build(Source(), []);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("C:\\Windows", built["SystemRoot"]);
            Assert.Contains("PATHEXT", BaseEnvironment.ForCurrentOs);
        }
        else
        {
            Assert.False(built.ContainsKey("HOME"));
            Assert.Contains("HOME", BaseEnvironment.ForCurrentOs);
        }
    }

    [Fact]
    public void The_list_is_the_common_set_plus_the_one_for_this_operating_system()
    {
        Assert.Contains("PATH", BaseEnvironment.Common);
        Assert.Contains("DOTNET_ROOT", BaseEnvironment.Common);
        Assert.Contains("HTTP_PROXY", BaseEnvironment.Common);
        Assert.Contains("https_proxy", BaseEnvironment.Common);
        Assert.Contains("NO_PROXY", BaseEnvironment.Common);
        Assert.Contains("ComSpec", BaseEnvironment.Windows);
        Assert.Contains("TMPDIR", BaseEnvironment.Unix);
        Assert.Equal(
            BaseEnvironment.Common.Concat(OperatingSystem.IsWindows() ? BaseEnvironment.Windows : BaseEnvironment.Unix),
            BaseEnvironment.ForCurrentOs);
        Assert.DoesNotContain(BaseEnvironment.ForCurrentOs, name => name.StartsWith(BaseEnvironment.ReservedPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
