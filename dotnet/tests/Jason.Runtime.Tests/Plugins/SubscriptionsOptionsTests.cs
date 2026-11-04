using System.Text;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The section <c>Subscriptions</c>. Its order, <c>Subscriptions:Order:&lt;kind&gt;</c>, is read like the routes: by
/// hand, from the file as the user's file is parsed, validated on start for its shape alone, and frozen until the plugin
/// load reads it again; whether a listed plugin is installed and granted the kind is the load's question, not this
/// one's. Its five numbers are bound, held to their ranges, and read live through the section's seam.
/// </summary>
public sealed class SubscriptionsOptionsTests
{
    /// <summary>Two kinds a plugin may subscribe to and one it may not, so no row here depends on the runtime's own list.</summary>
    private static readonly SubscribableKinds Kinds = new(kind => kind switch
    {
        "workitem_failed" or "campaign_created" => KindStanding.Subscribable,
        "machinery_ran" => KindStanding.NotSubscribable,
        _ => KindStanding.Unknown,
    });

    [Fact]
    public void Nothing_written_is_no_order_at_all()
    {
        var options = Read("""{"Dispatcher":{"Enabled":false}}""");

        Assert.Empty(options.Order);
        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Each_kind_keeps_its_ids_in_the_order_written_and_an_empty_list_is_kept_empty()
    {
        var options = Read("""{"Subscriptions":{"Order":{"workitem_failed":["second","first"],"campaign_created":[]}}}""");

        Assert.Equal(["second", "first"], options.Order["workitem_failed"]);
        Assert.Empty(options.Order["campaign_created"]);
        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void A_key_beside_the_order_neither_breaks_it_nor_enters_it()
    {
        var options = Read("""{"Subscriptions":{"MaxParallel":4,"Order":{"workitem_failed":["first"]},"Other":{"workitem_failed":["x"]}}}""");

        Assert.Equal(["workitem_failed"], options.Order.Keys);
        Assert.Equal(["first"], options.Order["workitem_failed"]);
        Assert.Equal(4, options.MaxParallel);
        Assert.True(Validate(options).Succeeded);
        Assert.False(SafeBinding.TryGetFailures(options, out _));
    }

    [Fact]
    public void The_numbers_default_to_a_thousand_runs_a_64_KiB_envelope_three_repeats_after_30_seconds_and_two_handlers_at_once()
    {
        var options = Read("""{"Dispatcher":{"Enabled":false}}""");

        Assert.Equal(1_000, options.RunRecordsPerKind);
        Assert.Equal(65_536, options.EnvelopeBytes);
        Assert.Equal(3, options.TransientRetries);
        Assert.Equal(30, options.RetryBackoffSeconds);
        Assert.Equal(2, options.MaxParallel);
        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Each_number_written_is_bound_beside_the_order()
    {
        var options = Read("""{"Subscriptions":{"RunRecordsPerKind":5,"EnvelopeBytes":1024,"TransientRetries":0,"RetryBackoffSeconds":1,"MaxParallel":16,"Order":{"workitem_failed":["first"]}}}""");

        Assert.Equal(5, options.RunRecordsPerKind);
        Assert.Equal(1_024, options.EnvelopeBytes);
        Assert.Equal(0, options.TransientRetries);
        Assert.Equal(1, options.RetryBackoffSeconds);
        Assert.Equal(16, options.MaxParallel);
        Assert.Equal(["first"], options.Order["workitem_failed"]);
    }

    [Fact]
    public void A_number_written_as_text_is_refused_by_its_path_beside_a_mistake_in_the_order()
    {
        var options = Read("""{"Subscriptions":{"EnvelopeBytes":"SENTINEL","Order":"SENTINEL"}}""");

        Assert.True(SafeBinding.TryGetFailures(options, out var failures));
        Assert.Equal(
            [
                "Subscriptions:Order could not be read as a map of journal kinds to lists of plugin ids.",
                "Subscriptions:EnvelopeBytes could not be read as System.Int32.",
            ],
            failures);
    }

    [Theory]
    [InlineData("Subscriptions:RunRecordsPerKind", 0, "Subscriptions:RunRecordsPerKind must be between 1 and 100000; got 0.")]
    [InlineData("Subscriptions:RunRecordsPerKind", 100_001, "Subscriptions:RunRecordsPerKind must be between 1 and 100000; got 100001.")]
    [InlineData("Subscriptions:EnvelopeBytes", 1, "Subscriptions:EnvelopeBytes must be between 2 and 524288; got 1.")]
    [InlineData("Subscriptions:EnvelopeBytes", 524_289, "Subscriptions:EnvelopeBytes must be between 2 and 524288; got 524289.")]
    [InlineData("Subscriptions:TransientRetries", -1, "Subscriptions:TransientRetries must be between 0 and 10; got -1.")]
    [InlineData("Subscriptions:TransientRetries", 11, "Subscriptions:TransientRetries must be between 0 and 10; got 11.")]
    [InlineData("Subscriptions:RetryBackoffSeconds", 0, "Subscriptions:RetryBackoffSeconds must be between 1 and 3600; got 0.")]
    [InlineData("Subscriptions:RetryBackoffSeconds", 3_601, "Subscriptions:RetryBackoffSeconds must be between 1 and 3600; got 3601.")]
    [InlineData("Subscriptions:MaxParallel", 0, "Subscriptions:MaxParallel must be between 1 and 16; got 0.")]
    [InlineData("Subscriptions:MaxParallel", 17, "Subscriptions:MaxParallel must be between 1 and 16; got 17.")]
    public void A_number_outside_its_range_is_refused_naming_the_setting(string setting, int value, string expected)
    {
        Assert.Equal([expected], Validate(With(setting, value)).Failures!);
    }

    [Theory]
    [InlineData("Subscriptions:RunRecordsPerKind", 1)]
    [InlineData("Subscriptions:RunRecordsPerKind", 100_000)]
    [InlineData("Subscriptions:EnvelopeBytes", 2)]
    [InlineData("Subscriptions:EnvelopeBytes", 524_288)]
    [InlineData("Subscriptions:TransientRetries", 0)]
    [InlineData("Subscriptions:TransientRetries", 10)]
    [InlineData("Subscriptions:RetryBackoffSeconds", 1)]
    [InlineData("Subscriptions:RetryBackoffSeconds", 3_600)]
    [InlineData("Subscriptions:MaxParallel", 1)]
    [InlineData("Subscriptions:MaxParallel", 16)]
    public void A_number_at_either_end_of_its_range_is_accepted(string setting, int value)
    {
        Assert.True(Validate(With(setting, value)).Succeeded);
    }

    /// <summary>
    /// The runner reads the numbers through the section's seam, so an edit applies live; an edit the validator refuses
    /// leaves the last numbers that validated in force.
    /// </summary>
    [Fact]
    public async Task An_edit_to_a_number_applies_live_and_a_refused_one_keeps_the_last_good_numbers()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await RuntimeApiFixture.StartAsync(
            ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Subscriptions":{"MaxParallel":3}}"""));
        await using (fixture)
        {
            var subscriptions = fixture.Resolve<LiveSettings<SubscriptionsOptions>>();
            Assert.Equal(3, subscriptions.Current.MaxParallel);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Subscriptions":{"MaxParallel":3,"EnvelopeBytes":4096}}""");
            Assert.True(await Dispatch.DispatchHarness.EventuallyAsync(() => subscriptions.Current.EnvelopeBytes == 4_096, ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Subscriptions":{"MaxParallel":17,"EnvelopeBytes":4096}}""");
            Assert.True(await TestOptions.RefusedAsync(subscriptions, ct));

            Assert.Equal(3, subscriptions.Current.MaxParallel);
            Assert.Equal(4_096, subscriptions.Current.EnvelopeBytes);
        }
    }

    /// <summary>
    /// The names a refused setting may print as typed. The doctor's allowlist is held to the option classes by
    /// <c>SettingsAllowlistAgreementTests</c>, which walks this section with the others.
    /// </summary>
    [Fact]
    public void The_vocabulary_names_the_section_and_its_numbers()
    {
        foreach (var name in new[] { "Subscriptions", "Order", "RunRecordsPerKind", "EnvelopeBytes", "TransientRetries", "RetryBackoffSeconds", "MaxParallel" })
        {
            Assert.Contains(name, SettingsVocabulary.Names);
        }
    }

    [Theory]
    [InlineData("""{"Subscriptions":"SENTINEL"}""", "Subscriptions could not be read as an object.")]
    [InlineData("""{"Subscriptions":{"Order":"SENTINEL"}}""", "Subscriptions:Order could not be read as a map of journal kinds to lists of plugin ids.")]
    [InlineData("""{"Subscriptions":{"Order":{"SENTINEL KIND":"SENTINEL"}}}""", "Subscriptions:Order:<key> could not be read as a list of plugin ids.")]
    [InlineData("""{"Subscriptions":{"Order":{"SENTINEL KIND":{"a":"SENTINEL"}}}}""", "Subscriptions:Order:<key> could not be read as a list of plugin ids.")]
    [InlineData("""{"Subscriptions":{"Order":{"SENTINEL KIND":["first",["SENTINEL"]]}}}""", "Subscriptions:Order:<key> could not be read as a list of plugin ids.")]
    public void A_shape_no_order_could_be_read_from_is_refused_by_its_path_and_never_by_what_was_written(string settings, string failure)
    {
        var options = Read(settings);

        Assert.True(SafeBinding.TryGetFailures(options, out var failures));
        Assert.Equal([failure], failures);
    }

    [Fact]
    public void A_key_that_is_not_a_kind_the_runtime_writes_is_refused_without_being_repeated()
    {
        var failure = Assert.Single(Validate(Read("""{"Subscriptions":{"Order":{"SENTINEL_kind":["first"]}}}""")).Failures!);

        Assert.Equal("Subscriptions:Order:<key> must name a journal kind the runtime writes; the key written is 13 characters long and is not one.", failure);
    }

    [Fact]
    public void A_kind_no_plugin_may_subscribe_to_is_refused()
    {
        var failure = Assert.Single(Validate(Read("""{"Subscriptions":{"Order":{"machinery_ran":["first"]}}}""")).Failures!);

        Assert.EndsWith("names a journal kind no plugin may subscribe to.", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_entry_is_a_plugin_id_written_once()
    {
        var failures = Validate(Read("""{"Subscriptions":{"Order":{"workitem_failed":["first","SENTINEL ID","","first"]}}}""")).Failures!.ToList();

        Assert.Equal(
            [
                "Subscriptions:Order:workitem_failed[1] must be a plugin id; the value written is 11 characters long and is not one.",
                "Subscriptions:Order:workitem_failed[2] must be a plugin id; nothing is written there.",
                "Subscriptions:Order:workitem_failed[3] repeats the id at [0]; a plugin is listed once.",
            ],
            failures);
    }

    [Fact]
    public void The_order_is_read_and_validated_where_the_other_options_are_and_a_bad_one_stops_the_start_by_its_path()
    {
        using (var accepted = Provider("""{"Subscriptions":{"Order":{"workitem_failed":["first"]}}}"""))
        {
            Assert.Equal(["first"], accepted.GetRequiredService<IOptionsMonitor<SubscriptionsOptions>>().CurrentValue.Order["workitem_failed"]);
        }

        using var refused = Provider("""{"Subscriptions":{"Order":{"workitem_failed":"SENTINEL"}}}""");
        var error = Assert.Throws<OptionsValidationException>(() => refused.GetRequiredService<IOptionsMonitor<SubscriptionsOptions>>().CurrentValue);

        Assert.Contains("Subscriptions:Order:workitem_failed could not be read as a list of plugin ids.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", error.Message, StringComparison.Ordinal);
    }

    private static SubscriptionsOptions With(string setting, int value) => setting switch
    {
        "Subscriptions:RunRecordsPerKind" => new SubscriptionsOptions { RunRecordsPerKind = value },
        "Subscriptions:EnvelopeBytes" => new SubscriptionsOptions { EnvelopeBytes = value },
        "Subscriptions:TransientRetries" => new SubscriptionsOptions { TransientRetries = value },
        "Subscriptions:RetryBackoffSeconds" => new SubscriptionsOptions { RetryBackoffSeconds = value },
        _ => new SubscriptionsOptions { MaxParallel = value },
    };

    private static ValidateOptionsResult Validate(SubscriptionsOptions options) =>
        new SubscriptionsOptionsValidator(SafeBinding.NoInstalledPlugins, Kinds).Validate(null, options);

    private static SubscriptionsOptions Read(string settings)
    {
        var options = new SubscriptionsOptions();
        SubscriptionsOptions.Fill(Configuration(settings).GetSection(SubscriptionsOptions.Section), options);
        return options;
    }

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(Configuration(settings), TestOptions.NothingInstalled());
        return services.BuildServiceProvider();
    }

    private static IConfigurationRoot Configuration(string settings) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
}
