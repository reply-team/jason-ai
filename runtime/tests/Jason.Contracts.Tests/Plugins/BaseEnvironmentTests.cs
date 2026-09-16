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

    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("ld_preload")]
    [InlineData("LD_ANYTHING_AT_ALL")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    [InlineData("COMPlus_ETWEnabled")]
    [InlineData("CORECLR_PROFILER")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_ROOT")]
    [InlineData("NODE_OPTIONS")]
    [InlineData("PYTHONPATH")]
    [InlineData("PERL5OPT")]
    [InlineData("JAVA_TOOL_OPTIONS")]
    [InlineData("CLASSPATH")]
    [InlineData("PATH")]
    [InlineData("SHELL")]
    [InlineData("JASON_DATA_DIR")]
    public void A_name_that_decides_what_a_program_loads_is_not_a_plugin_s_to_set(string name) =>
        Assert.False(BaseEnvironment.MayAPluginSet(name));

    [Theory]
    [InlineData("LANG")]
    [InlineData("NODE_ENV")]
    [InlineData("DOTNET_NOLOGO")]
    [InlineData("PYTHONUNBUFFERED")]
    [InlineData("EXAMPLE_TOKEN")]
    [InlineData("HTTPS_PROXY")]
    public void An_ordinary_variable_is_a_plugin_s_to_set(string name) =>
        Assert.True(BaseEnvironment.MayAPluginSet(name));

    [Fact]
    public void The_hook_families_are_refused_whole_rather_than_name_by_name()
    {
        // A family is listed as a prefix exactly when every name under it exists to load somebody's code; the
        // runtimes with ordinary variables of their own are named one by one instead.
        Assert.Equal<IEnumerable<string>>(["LD_", "DYLD_", "COMPLUS_", "CORECLR_"], BaseEnvironment.InjectionPrefixes);
        Assert.Contains("DOTNET_STARTUP_HOOKS", BaseEnvironment.InjectionNames);
        Assert.DoesNotContain("DOTNET_", BaseEnvironment.InjectionPrefixes);
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

    [Fact]
    public void A_childs_own_configuration_directory_is_machine_configuration_on_every_platform()
    {
        // A vendor CLI keeps its credential under the user's configuration directory, and on Unix that location
        // is this variable when it is set. Windows already carries its exact counterpart, APPDATA; a child that
        // saw one and not the other would look in the right place on one platform and the wrong one on two.
        var source = new Dictionary<string, string>
        {
            ["HOME"] = "/home/someone",
            ["XDG_CONFIG_HOME"] = "/home/someone/.config-elsewhere",
        };

        var built = BaseEnvironment.Build(source, []);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("APPDATA", BaseEnvironment.Windows);
            return;
        }

        Assert.Equal("/home/someone/.config-elsewhere", built["XDG_CONFIG_HOME"]);
    }

    [Fact]
    public void No_variable_the_runtime_copies_is_named_after_a_vendor()
    {
        // The base environment is machine configuration, not an integration point. A vendor's own variables
        // reach a plugin the way every other secret does — declared, granted, and read by name.
        var copied = BaseEnvironment.Common.Concat(BaseEnvironment.Windows).Concat(BaseEnvironment.Unix);

        Assert.DoesNotContain(copied, name => name.Contains("REPLY", StringComparison.OrdinalIgnoreCase));
    }
}
