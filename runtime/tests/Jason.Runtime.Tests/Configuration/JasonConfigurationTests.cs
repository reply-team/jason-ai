using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;

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
    [Critical(Family.C1, "settings.json precedence")]
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
    [Critical(Family.C1, "JASON_* precedence")]
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

    /// <summary>
    /// The file's watch says an edit only after a delay of its own; a read asked for says it at once. Whatever reads a
    /// section is told by the same token the watch fires, so the options system binds the edit at its next read.
    /// </summary>
    [Fact]
    public void A_read_of_the_settings_file_holds_an_edit_before_its_watch_says_it()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        var told = configuration.GetReloadToken();

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"after"}}""");
        Assert.Null(JasonConfiguration.ReadSettingsFile(configuration));

        Assert.Equal("after", configuration["Sample:Value"]);
        Assert.True(told.HasChanged, "a read of the settings file left the configuration's change token unfired");
    }

    /// <summary>
    /// A file another handle holds for itself alone — an editor in the middle of saving it — cannot be opened. The read
    /// is refused as an edit the watch cannot read is: said once, by the failure's type, and the configuration keeps what
    /// it held; nothing is thrown at the caller, which is handed the failure to refuse what it was asked to do.
    /// </summary>
    [Fact]
    public void A_settings_file_held_by_another_handle_at_a_read_is_said_by_type_and_keeps_what_was_read()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"kept"}}""");
        var said = new List<SettingsFileFailure>();
        using var configuration = JasonConfiguration.Build(dir.Paths, null, failure => { lock (said) { said.Add(failure); } });

        SettingsFileFailure? refused;
        using (new FileStream(dir.Paths.UserSettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            refused = JasonConfiguration.ReadSettingsFile(configuration);
        }

        Assert.Equal(new SettingsFileFailure("IOException", null, null), refused);
        lock (said)
        {
            Assert.Equal([new SettingsFileFailure("IOException", null, null)], said);
        }

        Assert.Equal("kept", configuration["Sample:Value"]);
    }

    /// <summary>
    /// A file that does not parse at a read leaves the keys the file last held where they are, and tells no section of a
    /// change: the whole file is parsed before anything it held is replaced. Said once, by position. The watch is stopped,
    /// so that its own read of the same file, which drops those keys, cannot come in between.
    /// </summary>
    [Fact]
    public void A_settings_file_that_is_not_valid_JSON_at_a_read_keeps_the_keys_it_last_held_and_is_said_by_position()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"kept"}}""");
        var said = new List<SettingsFileFailure>();
        using var configuration = JasonConfiguration.Build(dir.Paths, null, failure => { lock (said) { said.Add(failure); } });
        StopTheWatch(configuration);
        var told = configuration.GetReloadToken();

        File.WriteAllText(dir.Paths.UserSettingsFile, "{\"Sample\":{\"Value\":\n  unparsed");
        Assert.Equal(new SettingsFileFailure("JsonException", 2, 3), JasonConfiguration.ReadSettingsFile(configuration));

        Assert.Equal("kept", configuration["Sample:Value"]);
        Assert.False(told.HasChanged, "a read of a file that does not parse told the configuration's sections of a change");
        lock (said)
        {
            Assert.Equal([new SettingsFileFailure("JsonException", 2, 3)], said);
        }
    }

    /// <summary>
    /// A fresh installation has no settings file, and not even the folder it goes in. A read finds nothing of the file's
    /// to keep: it says nothing and tells no section of a change, so every setting stays on its default.
    /// </summary>
    [Fact]
    public void A_read_where_no_settings_file_was_ever_written_says_nothing_and_tells_no_section()
    {
        using var dir = new TempDataDir();
        var said = new List<SettingsFileFailure>();
        using var configuration = JasonConfiguration.Build(dir.Paths, null, failure => { lock (said) { said.Add(failure); } });
        var told = configuration.GetReloadToken();
        Assert.False(Directory.Exists(dir.Paths.ConfigDirectory), $"the folder of the settings file exists at '{dir.Paths.ConfigDirectory}'");

        Assert.Null(JasonConfiguration.ReadSettingsFile(configuration));

        lock (said)
        {
            Assert.Empty(said);
        }

        Assert.False(told.HasChanged, "a read that found no settings file told the configuration's sections of a change");
        Assert.Equal(10, JasonConfiguration.Load(configuration).Dispatcher.TickSeconds);
    }

    /// <summary>
    /// A file that is not there at a read, where the configuration holds keys the file last held: a save that removes the
    /// file and then renames a new one into place, met in between. It is said by its type and the keys are kept, no
    /// section is told of a change, and nothing is handed back to refuse: a file that is not there refuses nothing.
    /// </summary>
    [Fact]
    public void A_settings_file_gone_at_a_read_is_said_by_type_keeps_the_keys_and_refuses_nothing()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"kept"}}""");
        var said = new List<SettingsFileFailure>();
        using var configuration = JasonConfiguration.Build(dir.Paths, null, failure => { lock (said) { said.Add(failure); } });
        StopTheWatch(configuration);
        var told = configuration.GetReloadToken();

        File.Delete(dir.Paths.UserSettingsFile);
        Assert.Null(JasonConfiguration.ReadSettingsFile(configuration));

        Assert.Equal("kept", configuration["Sample:Value"]);
        Assert.False(told.HasChanged, "a read that found the settings file gone told the configuration's sections of a change");
        lock (said)
        {
            Assert.Equal([new SettingsFileFailure(nameof(FileNotFoundException), null, null)], said);
        }
    }

    /// <summary>Stops the settings file's watch, so that only the reads the test asks for meet the file.</summary>
    private static void StopTheWatch(ConfigurationRoot configuration)
    {
        foreach (var file in configuration.Providers.OfType<FileConfigurationProvider>())
        {
            (file.Source.FileProvider as IDisposable)?.Dispose();
        }
    }
}
