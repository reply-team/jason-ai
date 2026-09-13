using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Configuration;

public class JasonConfigurationTests
{
    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        using var dir = new TempDataDir();
        var options = JasonConfiguration.Load(JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null));
        Assert.Equal(0, options.Runtime.Port);
        Assert.Equal("Information", options.Logging.MinimumLevel);
        Assert.Equal(14, options.Logging.RetainedFileCountLimit);
        Assert.Equal(200L * 1024 * 1024, options.Logging.FileSizeLimitBytes);
    }

    [Fact]
    public void User_settings_override_shipped_settings()
    {
        using var dir = new TempDataDir();
        var shipped = Path.Combine(dir.Paths.Root, "install");
        Directory.CreateDirectory(shipped);
        File.WriteAllText(Path.Combine(shipped, "appsettings.json"), """{"Runtime":{"Port":1111},"Logging":{"MinimumLevel":"Debug"}}""");
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Runtime":{"Port":2222}}""");

        var options = JasonConfiguration.Load(JasonConfiguration.Build(dir.Paths, shipped));

        Assert.Equal(2222, options.Runtime.Port);
        Assert.Equal("Debug", options.Logging.MinimumLevel);
    }

    [Fact]
    public void Environment_variables_with_the_jason_prefix_win()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Runtime":{"Port":2222}}""");
        const string variable = "JASON_Runtime__Port";
        Environment.SetEnvironmentVariable(variable, "3333");
        try
        {
            var options = JasonConfiguration.Load(JasonConfiguration.Build(dir.Paths, null));
            Assert.Equal(3333, options.Runtime.Port);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Invalid_values_are_rejected_with_every_problem_named()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Runtime":{"Port":70000},"Logging":{"MinimumLevel":"Loud","RetainedFileCountLimit":0}}""");

        var error = Assert.Throws<InvalidOperationException>(() => JasonConfiguration.Load(JasonConfiguration.Build(dir.Paths, null)));

        Assert.Contains("Runtime:Port", error.Message, StringComparison.Ordinal);
        Assert.Contains("Logging:MinimumLevel", error.Message, StringComparison.Ordinal);
        Assert.Contains("Logging:RetainedFileCountLimit", error.Message, StringComparison.Ordinal);
    }
}
