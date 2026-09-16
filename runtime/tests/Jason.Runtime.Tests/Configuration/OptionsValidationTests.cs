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
        Assert.True(new PluginsOptionsValidator().Validate(null, new PluginsOptions()).Succeeded);
        Assert.True(new RoutesOptionsValidator().Validate(null, new RoutesOptions()).Succeeded);
    }

    [Fact]
    public void The_plugin_defaults_are_the_ones_the_wave_decided()
    {
        var options = new PluginsOptions();

        Assert.Empty(options.Grants);
        Assert.Equal(300_000, options.Limits.TimeoutMs);
        Assert.Equal(3_600_000, options.Limits.MaxTimeoutMs);
        Assert.Equal(64, options.Limits.MemoryMb);
        Assert.Equal(512, options.Limits.MaxMemoryMb);
        Assert.Equal(10_000_000, options.Limits.MaxStatements);
        Assert.Equal(64, options.Limits.MaxRecursion);
        Assert.Equal(4_194_304, options.Exec.OutputBytes);
        Assert.Equal(64, options.Exec.MaxCalls);
        Assert.Equal(4_194_304, options.Http.ResponseBytes);
        Assert.Equal(1_048_576, options.Http.RequestBytes);
        Assert.Equal(64, options.Http.MaxCalls);
        Assert.Equal(30_000, options.Http.TimeoutMs);
        Assert.Equal(2_097_152, options.Invoker.OutcomeBytes);
        Assert.Equal(4_194_304, options.Invoker.StderrBytes);
        Assert.Equal(5_000, options.Invoker.KillGraceMs);
        Assert.Equal(5_000, options.Invoker.VersionCheckTimeoutMs);
        Assert.Equal(16_384, options.Invoker.LogLineBytes);
    }

    [Fact]
    public void Every_out_of_range_plugin_limit_is_reported_with_its_rule()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Limits = new PluginLimitsOptions { TimeoutMs = 999, MemoryMb = 8 },
            Exec = new PluginExecOptions { MaxCalls = 0 },
            Http = new PluginHttpOptions { TimeoutMs = 999 },
            Invoker = new PluginInvokerOptions { KillGraceMs = 60_001 },
        });

        Assert.True(result.Failed);
        Assert.Contains("Plugins:Limits:TimeoutMs must be between 1000 and 3600000; got 999.", result.Failures!);
        Assert.Contains("Plugins:Limits:MemoryMb must be between 16 and 1024; got 8.", result.Failures!);
        Assert.Contains("Plugins:Exec:MaxCalls must be between 1 and 1000; got 0.", result.Failures!);
        Assert.Contains("Plugins:Http:TimeoutMs must be between 1000 and 300000; got 999.", result.Failures!);
        Assert.Contains("Plugins:Invoker:KillGraceMs must be between 0 and 60000; got 60001.", result.Failures!);
    }

    [Fact]
    public void A_ceiling_below_the_default_it_caps_is_refused()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Limits = new PluginLimitsOptions { TimeoutMs = 300_000, MaxTimeoutMs = 30_000, MemoryMb = 64, MaxMemoryMb = 32 },
        });

        Assert.True(result.Failed);
        Assert.Contains("Plugins:Limits:MaxTimeoutMs must be between 300000 and 86400000; got 30000.", result.Failures!);
        Assert.Contains("Plugins:Limits:MaxMemoryMb must be between 64 and 4096; got 32.", result.Failures!);
    }

    [Fact]
    public void A_grant_names_something_or_everything_and_nothing_else()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = new PluginGrant { Exec = ["*"], Http = ["api.example.test", " "], Env = ["EXAMPLE_TOKEN"] },
            },
        });

        Assert.True(result.Failed);
        Assert.Single(result.Failures!);
        Assert.Contains(result.Failures!, failure => failure.StartsWith("Plugins:Grants:fake:Http[1] must ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A grant entry that names an executable and then a newline names no executable at all: nothing a manifest
    /// requests can match it, so it would sit in the file looking like a permission and granting nothing. The
    /// settings file is a person's to edit, and what they need back is the line, not silence.
    /// </summary>
    [Fact]
    public void A_grant_entry_that_ends_in_a_newline_is_refused_rather_than_granting_nothing()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake-provider"] = new PluginGrant { Exec = ["provider-cli\n"] },
            },
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.StartsWith("Plugins:Grants:fake-provider:Exec[0] must ", StringComparison.Ordinal));
    }

    [Fact]
    public void Everything_a_manifest_can_request_is_a_valid_grant_entry()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake-provider"] = new PluginGrant { Exec = ["provider-cli", "Jason.FakeProviderCli"], Http = ["api.example.test", "localhost:5555"], Env = ["EXAMPLE_TOKEN", "*"] },
            },
        });

        Assert.True(result.Succeeded);
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
        Assert.Equal(600, options.ProviderOp.TimeoutSeconds);
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
