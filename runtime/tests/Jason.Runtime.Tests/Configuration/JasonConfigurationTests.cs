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
        Assert.Equal(10, options.Dispatcher.TickSeconds);
        Assert.Equal(60, options.Dispatcher.RetryDelaySeconds);
    }

    [Fact]
    public void The_dispatcher_section_binds_from_the_user_file()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":3,"RetryDelaySeconds":0,"AiRole":{"MaxAttempts":2}}}""");

        var options = JasonConfiguration.Load(JasonConfiguration.Build(dir.Paths, null));

        Assert.Equal(3, options.Dispatcher.TickSeconds);
        Assert.Equal(0, options.Dispatcher.RetryDelaySeconds);
        Assert.Equal(2, options.Dispatcher.AiRole.MaxAttempts);
        Assert.Equal(3600, options.Dispatcher.AiRole.TimeoutSeconds);
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

    /// <summary>
    /// The environment is process-wide and the test classes run in parallel, so this test proves the layer with a
    /// key no option class binds: a real setting placed in the environment here — a port, a tick — would be
    /// inherited by every runtime another test starts in the same moment.
    /// </summary>
    [Fact]
    public void Environment_variables_with_the_jason_prefix_win()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Probe":{"Value":"from the user file","Only":"file"}}""");
        const string variable = "JASON_Probe__Value";
        Environment.SetEnvironmentVariable(variable, "from the environment");
        try
        {
            var configuration = JasonConfiguration.Build(dir.Paths, null);

            Assert.Equal("from the environment", configuration["Probe:Value"]);
            Assert.Equal("file", configuration["Probe:Only"]);
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
