using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Configuration;

public class OptionsValidationTests
{
    [Fact]
    public void The_defaults_of_every_option_class_validate_clean()
    {
        Assert.True(new RuntimeOptionsValidator().Validate(null, new RuntimeOptions()).Succeeded);
        Assert.True(new LoggingOptionsValidator().Validate(null, new LoggingOptions()).Succeeded);
        Assert.True(new DispatcherOptionsValidator().Validate(null, new DispatcherOptions()).Succeeded);
        Assert.True(new RolesOptionsValidator().Validate(null, new RolesOptions()).Succeeded);
    }

    [Fact]
    public void The_dispatcher_defaults_are_the_ones_the_wave_decided()
    {
        var options = new DispatcherOptions();

        Assert.True(options.Enabled);
        Assert.Equal(10, options.TickSeconds);
        Assert.Equal(4, options.MaxParallel);
        Assert.Equal(10, options.DrainSeconds);
        Assert.Equal(60, options.RetryDelaySeconds);
        Assert.Equal(30, options.ExitGraceSeconds);
        Assert.Equal(3600, options.AiRole.TimeoutSeconds);
        Assert.Equal(120, options.AiRole.HeartbeatSeconds);
        Assert.Equal(3, options.AiRole.MaxAttempts);
        Assert.Equal(300, options.ProviderOp.TimeoutSeconds);
        Assert.Equal(0, options.ProviderOp.HeartbeatSeconds);
        Assert.Equal(3, options.ProviderOp.MaxAttempts);
    }

    [Fact]
    public void Every_out_of_range_dispatcher_setting_is_reported_with_its_rule()
    {
        var result = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { TickSeconds = 0, DrainSeconds = 21, ExitGraceSeconds = 601, MaxParallel = 65 });

        Assert.True(result.Failed);
        Assert.Contains("Dispatcher:TickSeconds must be between 1 and 3600; got 0.", result.Failures!);
        Assert.Contains("Dispatcher:DrainSeconds must be between 0 and 20; got 21.", result.Failures!);
        Assert.Contains("Dispatcher:ExitGraceSeconds must be between 0 and 600; got 601.", result.Failures!);
        Assert.Contains("Dispatcher:MaxParallel must be between 1 and 64; got 65.", result.Failures!);
    }

    [Fact]
    public void A_heartbeat_interval_is_either_off_or_at_least_ten_seconds()
    {
        var tooShort = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { AiRole = new KindDefaults { TimeoutSeconds = 3600, HeartbeatSeconds = 5, MaxAttempts = 3 } });

        Assert.True(tooShort.Failed);
        Assert.Contains("Dispatcher:AiRole:HeartbeatSeconds must be 0 or between 10 and 3600; got 5.", tooShort.Failures!);
        Assert.True(new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { AiRole = new KindDefaults { TimeoutSeconds = 3600, HeartbeatSeconds = 0, MaxAttempts = 3 } }).Succeeded);
    }

    [Fact]
    public void The_defaults_of_each_kind_are_validated_under_their_own_name()
    {
        var result = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { ProviderOp = new KindDefaults { TimeoutSeconds = 10, HeartbeatSeconds = 0, MaxAttempts = 0 } });

        Assert.True(result.Failed);
        Assert.Contains("Dispatcher:ProviderOp:MaxAttempts must be between 1 and 10; got 0.", result.Failures!);
        Assert.Contains("Dispatcher:ProviderOp:TimeoutSeconds must be between 30 and 86400; got 10.", result.Failures!);
    }

    [Fact]
    public void A_blank_argument_in_the_default_entry_command_is_refused()
    {
        var result = new RolesOptionsValidator().Validate(null, new RolesOptions { DefaultEntryCommand = ["dotnet", "  "] });

        Assert.True(result.Failed);
        Assert.Contains("Roles:DefaultEntryCommand[1] must not be blank.", result.Failures!);
    }

    [Fact]
    public void The_logging_rules_survive_the_move_into_their_own_validator()
    {
        var result = new LoggingOptionsValidator().Validate(null, new LoggingOptions { MinimumLevel = "Loud", RetainedFileCountLimit = 0, FileSizeLimitBytes = 1024 });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("Logging:MinimumLevel", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.StartsWith("Logging:RetainedFileCountLimit", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.StartsWith("Logging:FileSizeLimitBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pre_host_load_refuses_an_impossible_port_through_the_same_validator()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Runtime":{"Port":70000}}""");

        var error = Assert.Throws<InvalidOperationException>(() => JasonConfiguration.Load(JasonConfiguration.Build(dir.Paths, null)));

        Assert.Contains("Runtime:Port must be between 0 and 65535; got 70000.", error.Message, StringComparison.Ordinal);
    }
}
