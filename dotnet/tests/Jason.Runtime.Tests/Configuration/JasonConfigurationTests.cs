using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
    /// Two edits close together: the second is written while the watch is still telling the sections of the first. The
    /// watch takes its next notification before it reads the file, so the second makes it read once more, and every
    /// section ends bound to the second. A section of the test's own holds the watch in the middle of telling them, so the
    /// second edit lands there every time; nothing waits on the clock.
    /// </summary>
    [Fact]
    public async Task A_second_edit_written_while_the_watch_still_tells_the_sections_of_the_first_is_applied_by_the_watch()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        using var sections = Sections(configuration);
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("before", sample.CurrentValue.Value);
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listening = sample.OnChange(options =>
        {
            if (options.Value == "later")
            {
                later.TrySetResult();
            }
        });

        using (var held = new HeldNotification(configuration))
        {
            File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"earlier"}}""");
            await held.WaitUntilHeldAsync(Ct);
            File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"later"}}""");
        }

        await later.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal("later", sample.CurrentValue.Value);
    }

    /// <summary>
    /// The same two edits, and a read asked for straight after the second — the read a plugin reload makes — while the
    /// watch is still telling the sections of the first. The read waits for the watch to finish, and once it returns,
    /// every section binds the second edit: a reload resolves its grants and routes from those sections as soon as the
    /// read returns. The read runs on a thread of its own, and the watch is let go once that thread waits, so what the
    /// read returned to is seen there, before anything else could tell the sections.
    /// </summary>
    [Fact]
    public async Task A_read_while_the_watch_still_tells_the_sections_of_an_earlier_edit_leaves_them_bound_to_the_later_one()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        using var sections = Sections(configuration);
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("before", sample.CurrentValue.Value);

        using var held = new HeldNotification(configuration);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"earlier"}}""");
        await held.WaitUntilHeldAsync(Ct);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"later"}}""");

        var read = new OnItsOwnThread<(SettingsFileFailure? Refused, string? Keys, string? Section)>(
            () => (JasonConfiguration.ReadSettingsFile(configuration), configuration["Sample:Value"], sample.CurrentValue.Value));
        await read.WaitingOrDoneAsync(Ct);
        held.LetGo();
        var (refused, keys, section) = await read.DoneAsync(Ct);

        Assert.Null(refused);
        Assert.Equal("later", keys);
        Assert.Equal("later", section);
    }

    /// <summary>
    /// The other way round: a read is still telling the sections of the edit it read when the watch is told of a later
    /// one. The read tells them under the lock it read under, so the watch waits until every section has been told of the
    /// read before it reads the file, and every section ends bound to the later edit. Told after the read had let the lock
    /// go, the watch would read the later edit and tell the sections of it while the configuration's callback was still
    /// running on the read's thread, and return with no section told. The read is held in its telling, and the watch is
    /// fired on a thread of its own, so the test sees that thread wait.
    /// </summary>
    [Fact]
    public async Task An_edit_the_watch_is_told_of_while_a_read_still_tells_the_sections_waits_for_the_read_and_is_applied()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        using var configuration = WatchedByTheTest(dir, null, out var watch);
        using var sections = Sections(configuration);
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("before", sample.CurrentValue.Value);

        using var held = new HeldNotification(configuration);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"earlier"}}""");
        var read = new OnItsOwnThread<SettingsFileFailure?>(() => JasonConfiguration.ReadSettingsFile(configuration));
        await held.WaitUntilHeldAsync(Ct);

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"later"}}""");
        var fired = new OnItsOwnThread<bool>(() =>
        {
            watch.Fire();
            return true;
        });
        await fired.WaitingOrDoneAsync(Ct);
        Assert.False(fired.IsDone, "the watch applied an edit while a read was still telling the sections of the one before, without waiting for the read");
        held.LetGo();

        Assert.Null(await read.DoneAsync(Ct));
        await fired.DoneAsync(Ct);
        Assert.Equal("later", configuration["Sample:Value"]);
        Assert.Equal("later", sample.CurrentValue.Value);
    }

    /// <summary>
    /// The watch applies an edit as the loader's own reload does. A file that parses replaces every key the file held —
    /// one it no longer has is gone — and every section is told, so each binds the edit at its next read.
    /// </summary>
    [Fact]
    public void An_edit_the_watch_reads_replaces_the_keys_and_tells_every_section()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before","Extra":"dropped"}}""");
        using var configuration = WatchedByTheTest(dir, null, out var watch);
        using var sections = Sections(configuration);
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("before", sample.CurrentValue.Value);
        var told = configuration.GetReloadToken();

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"after"}}""");
        watch.Fire();

        Assert.Equal("after", configuration["Sample:Value"]);
        Assert.Null(configuration["Sample:Extra"]);
        Assert.True(told.HasChanged, "the watch read an edit and told the configuration's sections of no change");
        Assert.Equal("after", sample.CurrentValue.Value);
    }

    /// <summary>
    /// A file the watch finds gone is read as one with no keys, as the loader reads it: every section is told, so each goes
    /// back to its default, and nothing is said. A read asked for is stricter, and keeps the keys the file last held.
    /// </summary>
    [Fact]
    public void An_edit_that_removes_the_settings_file_leaves_no_keys_and_tells_every_section()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        var said = new List<SettingsFileFailure>();
        using var configuration = WatchedByTheTest(dir, failure => { lock (said) { said.Add(failure); } }, out var watch);
        using var sections = Sections(configuration);
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("before", sample.CurrentValue.Value);
        var told = configuration.GetReloadToken();

        File.Delete(dir.Paths.UserSettingsFile);
        watch.Fire();

        Assert.Null(configuration["Sample:Value"]);
        Assert.True(told.HasChanged, "the watch found the settings file gone and told the configuration's sections of no change");
        Assert.Null(sample.CurrentValue.Value);
        lock (said)
        {
            Assert.Empty(said);
        }
    }

    /// <summary>
    /// The watch drops the file's old keys when the file does not parse, as the loader's reload does, and tells no
    /// section: telling them would put every setting back on its default. It is said once, by position, though the watch
    /// is told of the one write twice, as the file watcher tells it; a section bound before keeps its value.
    /// </summary>
    [Fact]
    public void An_edit_that_does_not_parse_drops_the_keys_tells_no_section_and_is_said_once_by_position()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"kept"}}""");
        var said = new List<SettingsFileFailure>();
        using var configuration = WatchedByTheTest(dir, failure => { lock (said) { said.Add(failure); } }, out var watch);
        using var sections = Sections(configuration);
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("kept", sample.CurrentValue.Value);
        var told = configuration.GetReloadToken();

        File.WriteAllText(dir.Paths.UserSettingsFile, "{\"Sample\":{\"Value\":\n  unparsed");
        watch.Fire();
        watch.Fire();

        Assert.Null(configuration["Sample:Value"]);
        Assert.False(told.HasChanged, "the watch read a file that does not parse and told the configuration's sections of a change");
        Assert.Equal("kept", sample.CurrentValue.Value);
        lock (said)
        {
            Assert.Equal([new SettingsFileFailure("JsonException", 2, 3)], said);
        }
    }

    /// <summary>
    /// A file the watch cannot open — another handle holds it for itself alone, an editor in the middle of saving it — is
    /// left as the loader leaves it: the keys are kept, no section is told and nothing is said. The edit is read at the
    /// next notification, if one comes, or by the next read a reload makes.
    /// </summary>
    [Fact]
    public void An_edit_the_watch_cannot_open_keeps_the_keys_tells_no_section_and_says_nothing()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"kept"}}""");
        var said = new List<SettingsFileFailure>();
        using var configuration = WatchedByTheTest(dir, failure => { lock (said) { said.Add(failure); } }, out var watch);
        var told = configuration.GetReloadToken();

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"after"}}""");
        using (new FileStream(dir.Paths.UserSettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            watch.Fire();
        }

        Assert.Equal("kept", configuration["Sample:Value"]);
        Assert.False(told.HasChanged, "the watch could not open the settings file and told the configuration's sections of a change");
        lock (said)
        {
            Assert.Empty(said);
        }
    }

    /// <summary>
    /// An edit a section's validator refuses is applied as any other: the keys are replaced and every section is told,
    /// and the refusal is that section's to say when it is next read. Nothing of it reaches the watch, which goes on to
    /// apply the next edit.
    /// </summary>
    [Fact]
    public void An_edit_a_section_refuses_is_said_by_that_section_and_the_watch_goes_on_to_the_next()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        using var configuration = WatchedByTheTest(dir, null, out var watch);
        using var sections = Sections(configuration, refuse: "refused");
        var sample = sections.GetRequiredService<IOptionsMonitor<SampleOptions>>();
        Assert.Equal("before", sample.CurrentValue.Value);

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"refused"}}""");
        Assert.Null(Record.Exception(watch.Fire));

        Assert.Equal("refused", configuration["Sample:Value"]);
        Assert.Throws<OptionsValidationException>(() => sample.CurrentValue);

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"after"}}""");
        Assert.Null(Record.Exception(watch.Fire));

        Assert.Equal("after", sample.CurrentValue.Value);
    }

    /// <summary>
    /// A disposed configuration stops the settings file's watch before it disposes the file provider it watches through,
    /// so a notification that comes after the configuration is disposed applies nothing: no edit is read from a file
    /// provider that is gone.
    /// </summary>
    [Fact]
    public void A_notification_after_the_configuration_is_disposed_applies_nothing()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"before"}}""");
        var configuration = WatchedByTheTest(dir, null, out var watch);
        configuration.Dispose();

        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Sample":{"Value":"after"}}""");
        watch.Fire();

        Assert.Equal("before", configuration["Sample:Value"]);
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// One section bound as the runtime binds its own: told of a change by the configuration's token, and validated as it
    /// binds — refusing the one value given, when one is.
    /// </summary>
    private static ServiceProvider Sections(IConfiguration configuration, string? refuse = null)
    {
        var section = configuration.GetSection("Sample");
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsChangeTokenSource<SampleOptions>>(new ConfigurationChangeTokenSource<SampleOptions>(section));
        services.AddOptions<SampleOptions>()
            .Configure(options => options.Value = section["Value"])
            .Validate(options => refuse is null || options.Value != refuse, "Sample:Value is refused.");
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The configuration <see cref="JasonConfiguration.Build"/> makes for the directory, with the settings file's provider
    /// made again from the same source over a watch the test fires: the edit is applied inside <see cref="FiredWatch.Fire"/>,
    /// on the test's thread, and is over when it returns.
    /// </summary>
    private static ConfigurationRoot WatchedByTheTest(TempDataDir dir, Action<SettingsFileFailure>? onReloadFailure, out FiredWatch watch)
    {
        FileConfigurationSource source;
        using (var built = JasonConfiguration.Build(dir.Paths, null, onReloadFailure))
        {
            source = Assert.Single(built.Providers.OfType<FileConfigurationProvider>()).Source;
        }

        watch = new FiredWatch(dir.Paths.ConfigDirectory);
        source.FileProvider = watch;
        source.ReloadDelay = 0;
        return new ConfigurationRoot([source.Build(new ConfigurationBuilder())]);
    }

    /// <summary>
    /// The settings file's folder, read as the watch reads it, with a watch that is told of an edit only when the test
    /// fires it. A file watcher's notifications arrive on a thread of their own, when the system gets to them; these
    /// arrive inside the call. Its change tokens outlive its own disposal, so a test can still fire it once the
    /// configuration that disposes it is gone.
    /// </summary>
    private sealed class FiredWatch(string directory) : IFileProvider, IDisposable
    {
        private readonly PhysicalFileProvider _files = new(directory);
        private CancellationTokenSource _next = new();

        public IFileInfo GetFileInfo(string subpath) => _files.GetFileInfo(subpath);

        public IDirectoryContents GetDirectoryContents(string subpath) => _files.GetDirectoryContents(subpath);

        public IChangeToken Watch(string filter) => new CancellationChangeToken(_next.Token);

        /// <summary>Tells the watch of an edit, and returns once the watch has done with it.</summary>
        public void Fire()
        {
            var fired = _next;
            _next = new CancellationTokenSource();
            fired.Cancel();
        }

        public void Dispose() => _files.Dispose();
    }

    private sealed class SampleOptions
    {
        public string? Value { get; set; }
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
