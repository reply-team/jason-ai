using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Configuration;

[Collection(ProcessEnvironmentCollection.Name)]
public class JasonConfigurationTests
{
    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        using var dir = new TempDataDir();
        using var configuration = JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null);
        var options = JasonConfiguration.Load(configuration);
        Assert.Equal(52766, options.Runtime.Port);
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

        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        var options = JasonConfiguration.Load(configuration);

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

        using var configuration = JasonConfiguration.Build(dir.Paths, shipped);
        var options = JasonConfiguration.Load(configuration);

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
            using var configuration = JasonConfiguration.Build(dir.Paths, null);

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

        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        var error = Assert.Throws<InvalidSettingsException>(() => JasonConfiguration.Load(configuration));

        Assert.Contains("Runtime:Port", error.Message, StringComparison.Ordinal);
        Assert.Contains("Logging:MinimumLevel", error.Message, StringComparison.Ordinal);
        Assert.Contains("Logging:RetainedFileCountLimit", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three sections read before the host exists are read before there is a log to write to, so a value of
    /// the wrong type used to end the process with the binder's own exception — and its message, which repeats
    /// what was typed. The start still fails, in the same sentence a range would get: the setting's path and the
    /// type it has to be, with nothing underneath that could carry the text.
    /// </summary>
    [Fact]
    public void A_value_of_the_wrong_type_stops_the_start_naming_the_setting_and_never_the_value()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":"SENTINEL_E"}}""");

        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        var error = Assert.Throws<InvalidSettingsException>(() => JasonConfiguration.Load(configuration));

        Assert.Equal("Invalid Jason configuration: Dispatcher:Enabled could not be read as System.Boolean.", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("SENTINEL_E", error.ToString(), StringComparison.Ordinal);
    }
}
