using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

public class OptionsValidationTests
{

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.C1, "the shipped defaults are valid")]
    public void The_defaults_of_every_option_class_validate_clean()
    {
        Assert.True(new RuntimeOptionsValidator().Validate(null, new RuntimeOptions()).Succeeded);
        Assert.True(new LoggingOptionsValidator().Validate(null, new LoggingOptions()).Succeeded);
        Assert.True(new DispatcherOptionsValidator().Validate(null, new DispatcherOptions()).Succeeded);
        Assert.True(new RolesOptionsValidator().Validate(null, new RolesOptions()).Succeeded);
        Assert.True(new PluginsOptionsValidator().Validate(null, new PluginsOptions()).Succeeded);
        Assert.True(new RoutesOptionsValidator().Validate(null, new RoutesOptions()).Succeeded);
        Assert.True(new UpdateOptionsValidator().Validate(null, new UpdateOptions()).Succeeded);
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
        var result = new PluginsOptionsValidator(Installed("fake")).Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = new PluginGrant { Exec = ["*"], Http = ["api.example.test", " "], Env = ["EXAMPLE_TOKEN"] },
            },
        });

        Assert.True(result.Failed);
        Assert.Equal(
            "Plugins:Grants:fake:Http[1] must be \"*\" or the name of an executable, a host, a variable or an api family the manifest requests; "
                + "the value written is 1 character of white space.",
            Assert.Single(result.Failures!));
    }

    /// <summary>
    /// A directory is no name a manifest requests, so the grammar of the other lists does not read it. Where it leads is
    /// the load's to refuse, as a package problem, and an edit of the file never stops a runtime for it.
    /// </summary>
    [Fact]
    public void A_file_grant_is_not_read_by_the_grammar_of_the_other_lists()
    {
        var result = new PluginsOptionsValidator(Installed("fake")).Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = new PluginGrant { Files = ["/var/lines with a space", "relative", "*"] },
            },
        });

        Assert.False(result.Failed, string.Join('\n', result.Failures ?? []));
    }

    [Fact]
    public void A_grant_written_as_nothing_is_told_every_list_it_may_hold()
    {
        var result = new PluginsOptionsValidator(Installed("fake")).Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase) { ["fake"] = null! },
        });

        Assert.Equal("Plugins:Grants:fake must be an object with Exec, Http, Env, Api, Subscriptions and Files lists.", Assert.Single(result.Failures!));
    }

    /// <summary>
    /// A grant entry that names an executable and then a newline names no executable at all: nothing a manifest
    /// requests can match it, so it would sit in the file looking like a permission and granting nothing. The
    /// settings file is a person's to edit, and what they need back is the line, not silence.
    /// </summary>
    [Fact]
    public void A_grant_entry_that_ends_in_a_newline_is_refused_rather_than_granting_nothing()
    {
        var result = new PluginsOptionsValidator(Installed("fake-provider")).Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake-provider"] = new PluginGrant { Exec = ["provider-cli\n"] },
            },
        });

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures!);
        Assert.StartsWith("Plugins:Grants:fake-provider:Exec[0] must ", failure, StringComparison.Ordinal);
        Assert.EndsWith("; the value written is 13 characters long and is not one.", failure, StringComparison.Ordinal);
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
        Assert.Equal(600, options.EventsCadenceSeconds);
        Assert.Equal(3600, options.AiRole.TimeoutSeconds);
        Assert.Equal(120, options.AiRole.HeartbeatSeconds);
        Assert.Equal(3, options.AiRole.MaxAttempts);
        Assert.Equal(1_209_600, options.AiRole.MaxWaitSeconds);
        Assert.Equal(20, options.AiRole.MaxWaits);
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

    [Theory]
    [InlineData(59)]
    [InlineData(86_401)]
    public void The_events_cadence_is_a_minute_to_a_day(int seconds)
    {
        var result = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { EventsCadenceSeconds = seconds });

        Assert.True(result.Failed, $"An events cadence of {seconds} s was accepted.");
        Assert.Contains($"Dispatcher:EventsCadenceSeconds must be between 60 and 86400; got {seconds}.", result.Failures!);
        Assert.True(new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { EventsCadenceSeconds = 60 }).Succeeded);
        Assert.True(new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { EventsCadenceSeconds = 86_400 }).Succeeded);
    }

    /// <summary>A block is tried again every fifteen minutes unless set, and a cadence is a minute to a day; nothing outside is clamped.</summary>
    [Theory]
    [InlineData(59)]
    [InlineData(86_401)]
    public void A_block_is_tried_again_every_minute_to_every_day(int seconds)
    {
        Assert.Equal(900, new DispatcherOptions().Blocks.TrialSeconds);

        var result = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { Blocks = new BlockDefaults { TrialSeconds = seconds } });

        Assert.True(result.Failed, $"A trial cadence of {seconds} s was accepted.");
        Assert.Contains($"Dispatcher:Blocks:TrialSeconds must be between 60 and 86400; got {seconds}.", result.Failures!);
        foreach (var edge in new[] { 60, 86_400 })
        {
            var accepted = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { Blocks = new BlockDefaults { TrialSeconds = edge } });
            Assert.True(accepted.Succeeded, $"A trial cadence of {edge} s was refused: {accepted.FailureMessage}");
        }
    }

    /// <summary>A wait's ceiling is a minute to thirty days, and an item waits once to a hundred times; nothing outside is clamped.</summary>
    [Theory]
    [InlineData(59, 20, "Dispatcher:AiRole:MaxWaitSeconds must be between 60 and 2592000; got 59.")]
    [InlineData(2_592_001, 20, "Dispatcher:AiRole:MaxWaitSeconds must be between 60 and 2592000; got 2592001.")]
    [InlineData(1_209_600, 0, "Dispatcher:AiRole:MaxWaits must be between 1 and 100; got 0.")]
    [InlineData(1_209_600, 101, "Dispatcher:AiRole:MaxWaits must be between 1 and 100; got 101.")]
    public void A_role_waits_at_most_thirty_days_at_a_time_and_a_hundred_times(int maxWaitSeconds, int maxWaits, string failure)
    {
        var result = new DispatcherOptionsValidator().Validate(
            null,
            new DispatcherOptions { AiRole = new AiRoleDefaults { TimeoutSeconds = 3600, HeartbeatSeconds = 120, MaxAttempts = 3, MaxWaitSeconds = maxWaitSeconds, MaxWaits = maxWaits } });

        Assert.True(result.Failed, $"A ceiling of {maxWaitSeconds} s and {maxWaits} waits was accepted.");
        Assert.Contains(failure, result.Failures!);
        foreach (var (seconds, waits) in new[] { (60, 1), (2_592_000, 100) })
        {
            var edge = new DispatcherOptionsValidator().Validate(
                null,
                new DispatcherOptions { AiRole = new AiRoleDefaults { TimeoutSeconds = 3600, HeartbeatSeconds = 120, MaxAttempts = 3, MaxWaitSeconds = seconds, MaxWaits = waits } });
            Assert.True(edge.Succeeded, $"A ceiling of {seconds} s and {waits} waits was refused: {edge.FailureMessage}");
        }
    }

    [Fact]
    public void A_heartbeat_interval_is_either_off_or_at_least_ten_seconds()
    {
        var tooShort = new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { AiRole = new AiRoleDefaults { TimeoutSeconds = 3600, HeartbeatSeconds = 5, MaxAttempts = 3 } });

        Assert.True(tooShort.Failed);
        Assert.Contains("Dispatcher:AiRole:HeartbeatSeconds must be 0 or between 10 and 3600; got 5.", tooShort.Failures!);
        Assert.True(new DispatcherOptionsValidator().Validate(null, new DispatcherOptions { AiRole = new AiRoleDefaults { TimeoutSeconds = 3600, HeartbeatSeconds = 0, MaxAttempts = 3 } }).Succeeded);
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

    /// <summary>
    /// The global default is a profile name, so a value that could never be one is the operator's mistake and is
    /// reported the way every other malformed setting is: before the runtime is listening.
    /// </summary>
    [Fact]
    public async Task A_default_execution_profile_that_is_not_a_role_name_refuses_to_start()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Roles":{"DefaultExecutionProfile":"Not A Name"}}""");

        var failure = await Assert.ThrowsAsync<InvalidSettingsException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Contains("Roles:DefaultExecutionProfile", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A well-formed name nothing answers is not a refusal to start. Settings are read before the database is
    /// open, so whether a profile of that name exists is a question only the claim can ask — and it answers it
    /// on the work item, where the person who has to fix it will see it.
    /// </summary>
    [Fact]
    public void A_default_execution_profile_no_profile_answers_is_not_a_settings_problem()
    {
        var result = new RolesOptionsValidator().Validate(null, new RolesOptions { DefaultExecutionProfile = "nothing-answers-this" });

        Assert.True(result.Succeeded, string.Join(" ", result.Failures ?? []));
    }

    /// <summary>
    /// The setting is read as written, with nothing trimmed first, so a name followed by a line break is a different
    /// string from the name and must not pass for it.
    /// </summary>
    [Fact]
    public void A_default_execution_profile_followed_by_a_line_break_is_not_a_profile_name()
    {
        Assert.True(new RolesOptionsValidator().Validate(null, new RolesOptions { DefaultExecutionProfile = "house" }).Succeeded);

        var result = new RolesOptionsValidator().Validate(null, new RolesOptions { DefaultExecutionProfile = "house\n" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.StartsWith("Roles:DefaultExecutionProfile must be", StringComparison.Ordinal));
    }

    [Fact]
    public void The_two_bounds_the_launcher_holds_a_child_to_are_reported_with_their_rule()
    {
        var result = new RolesOptionsValidator().Validate(null, new RolesOptions { MaxStdoutBytes = 65_535, MaxSkillBytes = 16_777_217 });

        Assert.True(result.Failed);
        Assert.Contains("Roles:MaxStdoutBytes must be between 65536 and 67108864; got 65535.", result.Failures!);
        Assert.Contains("Roles:MaxSkillBytes must be between 4096 and 16777216; got 16777217.", result.Failures!);
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

        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        var error = Assert.Throws<InvalidSettingsException>(() => JasonConfiguration.Load(configuration));

        Assert.Contains("Runtime:Port must be between 0 and 65535; got 70000.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_update_defaults_are_the_ones_the_wave_decided()
    {
        var options = new UpdateOptions();

        Assert.True(options.CheckEnabled);
        Assert.Equal(UpdateFeed.Default.ToString(), options.FeedUrl);
        Assert.Equal(5, options.InitialDelayMinutes);
        Assert.Equal(24, options.IntervalHours);
        Assert.True(new UpdateOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0, 24, "Update:InitialDelayMinutes must be between 1 and 1440; got 0.")]
    [InlineData(1441, 24, "Update:InitialDelayMinutes must be between 1 and 1440; got 1441.")]
    [InlineData(5, 0, "Update:IntervalHours must be between 1 and 168; got 0.")]
    [InlineData(5, 169, "Update:IntervalHours must be between 1 and 168; got 169.")]
    public void An_update_schedule_outside_its_bounds_is_reported_with_its_rule(int delay, int interval, string failure)
    {
        var result = new UpdateOptionsValidator().Validate(null, new UpdateOptions { InitialDelayMinutes = delay, IntervalHours = interval });

        Assert.True(result.Failed);
        Assert.Contains(failure, result.Failures!);
    }

    /// <summary>
    /// The feed is held to the reader's own rule — https, or http on loopback — by the validator as well, so a
    /// settings file naming a feed the reader would refuse is refused before the runtime listens, while the
    /// person who wrote it is still at the keyboard, rather than at the first check in a log line nobody reads.
    /// </summary>
    /// <remarks>
    /// The refusal names the line and says how long the value is, never what it says: an address can carry a token
    /// in its query, and this sentence goes to the log.
    /// </remarks>
    [Theory]
    [InlineData("manifest.json", "the value written is 13 characters long and is not one.")]
    [InlineData("http://example.com/manifest.json", "the value written is 32 characters long and is not one.")]
    [InlineData("file:///c:/manifest.json", "the value written is 24 characters long and is not one.")]
    [InlineData("", "nothing is written there.")]
    public void A_feed_the_reader_would_refuse_is_refused_by_the_validator(string feed, string written)
    {
        var result = new UpdateOptionsValidator().Validate(null, new UpdateOptions { FeedUrl = feed });

        Assert.True(result.Failed);
        Assert.Contains($"Update:FeedUrl must be an absolute https address, or http on loopback; {written}", result.Failures!);
    }

    [Theory]
    [InlineData("https://example.com/manifest.json")]
    [InlineData("http://127.0.0.1:5000/manifest.json")]
    [InlineData("http://localhost:5000/manifest.json")]
    public void A_feed_the_reader_would_read_is_accepted_by_the_validator(string feed) =>
        Assert.True(new UpdateOptionsValidator().Validate(null, new UpdateOptions { FeedUrl = feed }).Succeeded);

    /// <summary>The validator is only a rule until the host runs it: this is the registration, seen from the outside.</summary>
    [Fact]
    [Critical(Family.I2, "no plain-http feed")]
    public async Task A_feed_that_is_plain_http_on_a_network_refuses_to_start()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Update":{"FeedUrl":"http://example.com/manifest.json"}}""");

        var failure = await Assert.ThrowsAsync<InvalidSettingsException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Contains("Update:FeedUrl", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// No refusal repeats what a person wrote. Text typed onto the wrong line is often a secret — a token pasted
    /// where a name belonged — and every sentence a validator says goes to the log, which a diagnostic bundle
    /// carries off the machine. So every validator the runtime registers is handed its section with each piece of
    /// text set to a sentinel of its own and each map keyed by sentinels, and nothing any of them says may carry
    /// one back. A number the binder has already read is not text anybody typed in secret, and is still quoted.
    /// </summary>
    [Fact]
    [Critical(Family.B3, "no refusal echoes a setting's text")]
    public void No_refusal_quotes_back_the_text_a_person_wrote()
    {
        var services = Registered(TestOptions.NothingInstalled());
        using var provider = services.BuildServiceProvider();
        var sentinels = new Sentinels();

        var said = new List<string>();
        var silent = new List<string>();
        foreach (var section in ValidatedSections(services))
        {
            var options = sentinels.Fill(Activator.CreateInstance(section)!);
            var contract = typeof(IValidateOptions<>).MakeGenericType(section);
            var validate = contract.GetMethod(nameof(IValidateOptions<object>.Validate))!;
            var before = said.Count;
            foreach (var validator in provider.GetServices(contract))
            {
                var result = (ValidateOptionsResult)validate.Invoke(validator, [null, options])!;
                said.AddRange(result.Failures ?? []);
            }

            if (said.Count == before)
            {
                silent.Add(section.Name);
            }
        }

        // Each section refused something, or the walk proved nothing about it.
        Assert.Empty(silent);

        // Every sentinel carries the one word, in one case or the other, so a sentinel said back after being trimmed,
        // cut short or lowercased is caught as surely as one said back whole.
        var quoted = said
            .Where(sentence => sentence.Contains(Sentinels.Word, StringComparison.OrdinalIgnoreCase)
                || sentinels.Written.Any(text => sentence.Contains(text, StringComparison.Ordinal)))
            .ToList();
        Assert.True(quoted.Count == 0, "Refusals that quote what was written:" + Environment.NewLine + string.Join(Environment.NewLine, quoted));
        Assert.Contains(said, sentence => sentence.EndsWith(FormattableString.Invariant($"; got {Sentinels.Number}."), StringComparison.Ordinal));
    }

    /// <summary>The walk above finds the validators through the registration; this says it does not find too few.</summary>
    [Fact]
    public void Every_validator_the_runtime_declares_is_one_the_walk_reaches()
    {
        var services = Registered(TestOptions.NothingInstalled());

        static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var registered = services
            .Where(descriptor => IsValidator(descriptor.ServiceType))
            .Select(descriptor => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType())
            .OfType<Type>()
            .Select(Definition)
            .ToHashSet();
        var declared = typeof(RuntimeOptions).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(IsValidator))
            .ToHashSet();

        Assert.Superset(declared, registered);
        Assert.Superset(
            new HashSet<Type>
            {
                typeof(RuntimeOptions),
                typeof(LoggingOptions),
                typeof(DispatcherOptions),
                typeof(ManagerOptions),
                typeof(RolesOptions),
                typeof(PluginsOptions),
                typeof(RoutesOptions),
                typeof(UpdateOptions),
                typeof(NotificationsOptions),
                typeof(InboundOptions),
                typeof(SubscriptionsOptions),
            },
            ValidatedSections(services).ToHashSet());
    }

    /// <summary>
    /// What lets a key through is the plugins installed in the data directory the runtime was given, read when
    /// there is something to say: a grant is named by its plugin's id when that plugin is installed, and the same
    /// grant for one that is not is named by a placeholder.
    /// </summary>
    [Fact]
    public void A_grant_is_named_by_its_plugin_only_when_that_plugin_is_installed()
    {
        using var dir = new TempDataDir();
        Package(dir.Paths, "fake-provider");
        var configuration = Json("""{"Plugins":{"Grants":{"fake-provider":{"Exec":["provider cli"]},"not-installed":{"Exec":["provider cli"]}}}}""");
        using var provider = Registered(dir.Paths, configuration).BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>().CurrentValue);

        Assert.Contains(refused.Failures, failure => failure.StartsWith("Plugins:Grants:fake-provider:Exec[0] must ", StringComparison.Ordinal));
        Assert.Contains(refused.Failures, failure => failure.StartsWith("Plugins:Grants:<key>:Exec[0] must ", StringComparison.Ordinal));
        Assert.DoesNotContain(refused.Failures, failure => failure.Contains("not-installed", StringComparison.Ordinal));
    }

    /// <summary>
    /// The routes and the notification channels are told what is installed the same way, through the registration:
    /// a key that names an installed plugin is shown as it was typed, and one that names nothing installed is not.
    /// </summary>
    [Fact]
    public void A_route_or_a_channel_keyed_by_a_plugin_is_named_by_it_only_when_that_plugin_is_installed()
    {
        using var dir = new TempDataDir();
        Package(dir.Paths, "fake-provider");
        var configuration = Json("""
            {"Routes":{"Operations":{"fake-provider":{"Plugin":"fake-provider"},"not-installed":{"Plugin":"fake-provider"}}},
             "Notifications":{"Plugins":{"fake-provider":{"Binding":"text"},"not-installed":{"Binding":"text"}}}}
            """);
        using var provider = Registered(dir.Paths, configuration).BuildServiceProvider();

        var routes = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<RoutesOptions>>().CurrentValue);
        var channels = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<NotificationsOptions>>().CurrentValue);

        Assert.Contains(routes.Failures, failure => failure.StartsWith("Routes:Operations:fake-provider must name a published operation;", StringComparison.Ordinal));
        Assert.Contains(routes.Failures, failure => failure.StartsWith("Routes:Operations:<key> must name a published operation;", StringComparison.Ordinal));
        Assert.Contains("Notifications:Plugins:fake-provider:Binding must be an object; got string.", channels.Failures);
        Assert.Contains("Notifications:Plugins:<key>:Binding must be an object; got string.", channels.Failures);
        Assert.DoesNotContain(routes.Failures.Concat(channels.Failures), failure => failure.Contains("not-installed", StringComparison.Ordinal));
    }

    /// <summary>
    /// "Installed" is a directory with a manifest and the name of a plugin's id. A folder without a manifest, and one with
    /// a manifest under a name no plugin can have, are not plugins: what they are called is whatever somebody typed, so a
    /// key that matches either is written as a placeholder, as any key a person chose is.
    /// </summary>
    [Fact]
    public void A_key_that_names_a_folder_that_is_not_a_plugin_is_written_as_a_placeholder()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, "no-manifest"));
        Package(dir.Paths, "under_score");
        var configuration = Json("""{"Plugins":{"Grants":{"no-manifest":{"Exec":["provider cli"]},"under_score":{"Exec":["provider cli"]}}}}""");
        using var provider = Registered(dir.Paths, configuration).BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>().CurrentValue);

        Assert.Equal(2, refused.Failures.Count(failure => failure.StartsWith("Plugins:Grants:<key>:Exec[0] must ", StringComparison.Ordinal)));
        Assert.DoesNotContain(refused.Failures, failure => failure.Contains("no-manifest", StringComparison.Ordinal));
        Assert.DoesNotContain(refused.Failures, failure => failure.Contains("under_score", StringComparison.Ordinal));
    }

    /// <summary>A package directory as the plugin loader finds one: the directory, and the manifest that makes it one.</summary>
    private static void Package(JasonPaths paths, string name)
    {
        Directory.CreateDirectory(Path.Combine(paths.PluginsDirectory, name));
        File.WriteAllText(Path.Combine(paths.PluginsDirectory, name, "plugin.yaml"), $"id: {name}");
    }

    /// <summary>A settings file holding this JSON, read by the parser the user's file goes through.</summary>
    private static IConfigurationRoot Json(string settings) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();

    /// <summary>The runtime's own registration of every section and its validators, over the settings given.</summary>
    private static ServiceCollection Registered(JasonPaths paths, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(configuration ?? new ConfigurationBuilder().Build(), paths);

        // The dispatcher's section has a second validator that reads the plugin section through its seam.
        services.AddSingleton(provider => new LiveSettings<PluginsOptions>(
            provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>(),
            NullLogger<LiveSettings<PluginsOptions>>.Instance,
            PluginsOptions.Section));
        return services;
    }

    /// <summary>The listing a validator is handed, naming these plugins as installed.</summary>
    private static Func<IReadOnlySet<string>> Installed(params string[] ids) => () => new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

    private static bool IsValidator(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IValidateOptions<>);

    /// <summary>Every section some validator is registered for.</summary>
    private static IEnumerable<Type> ValidatedSections(IServiceCollection services) =>
        services.Select(descriptor => descriptor.ServiceType)
            .Where(IsValidator)
            .Select(contract => contract.GetGenericArguments()[0])
            .Distinct();

    /// <summary>
    /// Fills an option class the way a careless edit could: every piece of text a sentinel of its own, every number
    /// one outside every range, and every map keyed by two sentinels — one shaped like an id, so it reaches the
    /// checks an id passes, and one that could never be one. A setting of a type not listed here fails the test
    /// rather than going unfilled, so a new kind of setting cannot slip past it.
    /// </summary>
    private sealed class Sentinels
    {
        /// <summary>Below the lower bound of every range a setting is held to.</summary>
        public const int Number = -12_345;

        /// <summary>The word every sentinel carries, in one case or the other.</summary>
        public const string Word = "SENTINEL";

        /// <summary>
        /// The maps whose validators have a sentence for an entry written as <c>null</c>. Each is given a third
        /// sentinel key with no entry at all, so that sentence is held to the rule as well; a map whose validator has
        /// no such sentence is not given one.
        /// </summary>
        private static readonly HashSet<(Type Owner, string Property)> NullEntries =
        [
            (typeof(PluginsOptions), nameof(PluginsOptions.Grants)),
            (typeof(RoutesOptions), nameof(RoutesOptions.Operations)),
            (typeof(NotificationsOptions), nameof(NotificationsOptions.Plugins)),
        ];

        private readonly List<string> _written = [];

        /// <summary>Every sentinel handed out, text and keys alike.</summary>
        public IReadOnlyList<string> Written => _written;

        public object Fill(object options)
        {
            foreach (var property in options.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // A computed answer has no setter, and nothing in the file can name it.
                if (property.SetMethod is null)
                {
                    continue;
                }

                var type = property.PropertyType;
                object? value =
                    type == typeof(string) ? Next("ghp_SENTINEL{0:D3} token")
                    : type == typeof(int) ? Number
                    : type == typeof(long) ? (long)Number
                    : type == typeof(bool) ? property.GetValue(options)
                    : type == typeof(JsonNode) ? JsonValue.Create(Next("ghp_SENTINEL{0:D3} binding"))
                    : type == typeof(List<string>) ? new List<string> { Next("ghp_SENTINEL{0:D3} entry") }
                    : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                        ? Map((IDictionary)property.GetValue(options)!, type, NullEntries.Contains((options.GetType(), property.Name)))
                    : IsOptionClass(type) ? Fill(Activator.CreateInstance(type)!)
                    : throw new InvalidOperationException($"{options.GetType().Name}.{property.Name} is a {type.Name}, which this test does not fill.");
                property.SetValue(options, value);
            }

            return options;
        }

        private IDictionary Map(IDictionary map, Type type, bool withNullEntry)
        {
            var entry = type.GetGenericArguments()[1];
            if (entry == typeof(List<string>))
            {
                // A map of lists, the order of a kind's subscribers: each key a sentinel, each list one sentinel long.
                map.Clear();
                map[Next("sentinel{0:D3}_key")] = new List<string> { Next("ghp_SENTINEL{0:D3} entry") };
                map[Next("SENTINEL{0:D3} KEY")] = new List<string> { Next("ghp_SENTINEL{0:D3} entry") };
                return map;
            }

            if (entry == typeof(Dictionary<string, string>))
            {
                // A plugin's configuration: a key and its text, and the key of digits a list leaves behind, which has a sentence.
                map.Clear();
                map[Next("sentinel{0:D3}-key")] = new Dictionary<string, string>
                {
                    [Next("sentinel{0:D3}_name")] = Next("ghp_SENTINEL{0:D3} value"),
                    [Next("9{0:D8}")] = Next("ghp_SENTINEL{0:D3} value"),
                };
                return map;
            }

            if (entry == typeof(int))
            {
                // A map of numbers, the daily limits: one key shaped like a role's name, one that could never be one.
                map.Clear();
                map[Next("sentinel{0:D3}-key")] = Number;
                map[Next("SENTINEL{0:D3} KEY")] = Number;
                return map;
            }

            if (!IsOptionClass(entry))
            {
                throw new InvalidOperationException($"A map of {entry.Name} is not one this test fills.");
            }

            map.Clear();
            map[Next("sentinel{0:D3}-key")] = Fill(Activator.CreateInstance(entry)!);
            map[Next("SENTINEL{0:D3} KEY")] = Fill(Activator.CreateInstance(entry)!);
            if (withNullEntry)
            {
                map[Next("sentinel{0:D3}-null")] = null;
            }

            return map;
        }

        /// <summary>A sentinel numbered past every other, so no one of them is the beginning of another.</summary>
        private string Next(string shape)
        {
            var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, _written.Count);
            _written.Add(text);
            return text;
        }

        private static bool IsOptionClass(Type type) => type.IsClass && type.Assembly == typeof(RuntimeOptions).Assembly;
    }
}
