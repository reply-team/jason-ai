using System.Text;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// <c>Subscriptions:Order:&lt;kind&gt;</c>, read like the routes: by hand, from the file as the user's file is parsed,
/// validated on start for its shape alone, and frozen until the plugin load reads it again. Whether a listed plugin is
/// installed and granted the kind is the load's question, not this one's.
/// </summary>
public sealed class SubscriptionOrderOptionsTests
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
    public void A_setting_beside_the_order_neither_breaks_it_nor_enters_it()
    {
        var options = Read("""{"Subscriptions":{"RunRecordsPerKind":"anything","Order":{"workitem_failed":["first"]},"Other":{"workitem_failed":["x"]}}}""");

        Assert.Equal(["workitem_failed"], options.Order.Keys);
        Assert.Equal(["first"], options.Order["workitem_failed"]);
        Assert.True(Validate(options).Succeeded);
        Assert.False(SafeBinding.TryGetFailures(options, out _));
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
                "Subscriptions:Order:<key>[1] must be a plugin id; the value written is 11 characters long and is not one.",
                "Subscriptions:Order:<key>[2] must be a plugin id; nothing is written there.",
                "Subscriptions:Order:<key>[3] repeats the id at [0]; a plugin is listed once.",
            ],
            failures);
    }

    [Fact]
    public void The_order_is_read_and_validated_where_the_other_options_are_and_a_bad_one_stops_the_start_by_its_path()
    {
        using (var accepted = Provider("""{"Subscriptions":{"Order":{"workitem_failed":["first"]}}}"""))
        {
            Assert.Equal(["first"], accepted.GetRequiredService<IOptionsMonitor<SubscriptionOrderOptions>>().CurrentValue.Order["workitem_failed"]);
        }

        using var refused = Provider("""{"Subscriptions":{"Order":{"workitem_failed":"SENTINEL"}}}""");
        var error = Assert.Throws<OptionsValidationException>(() => refused.GetRequiredService<IOptionsMonitor<SubscriptionOrderOptions>>().CurrentValue);

        Assert.Contains("Subscriptions:Order:<key> could not be read as a list of plugin ids.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", error.Message, StringComparison.Ordinal);
    }

    private static ValidateOptionsResult Validate(SubscriptionOrderOptions options) =>
        new SubscriptionOrderOptionsValidator(SafeBinding.NoInstalledPlugins, Kinds).Validate(null, options);

    private static SubscriptionOrderOptions Read(string settings)
    {
        var options = new SubscriptionOrderOptions();
        SubscriptionOrderOptions.Fill(Configuration(settings).GetSection(SubscriptionOrderOptions.Section), options);
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
