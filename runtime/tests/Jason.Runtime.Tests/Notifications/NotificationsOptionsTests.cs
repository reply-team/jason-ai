using System.Text;
using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The <c>Notifications</c> section: whether anything is said when work starts waiting for a person, when, and on
/// which channels. Read the way every other section is, and refused on start where a value is outside its range.
/// </summary>
public sealed class NotificationsOptionsTests
{
    [Fact]
    public void The_defaults_are_the_scope_s_numbers()
    {
        var options = new NotificationsOptions();
        Assert.True(options.Enabled);
        Assert.True(options.Toast);
        Assert.Equal(60, options.QuietSeconds);
        Assert.Equal(600, options.MaxWaitSeconds);
        Assert.Equal(30, options.MinIntervalMinutes);
        Assert.Empty(options.Plugins);
        Assert.True(options.AnyChannel);
    }

    /// <summary>The defaults survive a settings file that does not mention the section at all.</summary>
    [Fact]
    public void A_settings_file_without_the_section_gets_the_defaults() =>
        Assert.Equivalent(new NotificationsOptions(), Read("{}"));

    /// <summary>A plugin is named by its entry; the binding is JSON, read the way a route's binding is.</summary>
    [Fact]
    public void A_named_plugin_carries_its_binding_as_json()
    {
        var options = Read("""{"Notifications":{"Plugins":{"ntfy":{"Binding":{"server":"https://ntfy.sh","topic":"jason-walk"}}}}}""");
        var entry = Assert.Single(options.Plugins);
        Assert.Equal("ntfy", entry.Key);
        Assert.True(entry.Value.Enabled);
        Assert.Equal("jason-walk", entry.Value.Binding!["topic"]!.GetValue<string>());
    }

    /// <summary>A plugin that needs no binding is named by saying it is enabled: an empty object is no entry at all.</summary>
    [Fact]
    public void A_plugin_named_with_enabled_alone_has_no_binding()
    {
        var entry = Assert.Single(Read("""{"Notifications":{"Plugins":{"ntfy":{"Enabled":true}}}}""").Plugins);
        Assert.True(entry.Value.Enabled);
        Assert.Null(entry.Value.Binding);
    }

    [Theory]
    [InlineData("""{"Notifications":{"Enabled":false}}""", false)]
    [InlineData("""{"Notifications":{"Toast":false}}""", false)]
    [InlineData("""{"Notifications":{"Toast":false,"Plugins":{"ntfy":{"Binding":{"topic":"t"}}}}}""", true)]
    [InlineData("""{"Notifications":{"Toast":false,"Plugins":{"ntfy":{"Enabled":false,"Binding":{"topic":"t"}}}}}""", false)]
    [InlineData("""{"Notifications":{"Enabled":false,"Plugins":{"ntfy":{"Binding":{"topic":"t"}}}}}""", false)]
    public void Every_channel_off_is_one_answer(string settings, bool any) => Assert.Equal(any, Read(settings).AnyChannel);

    [Theory]
    [InlineData("""{"Notifications":{"QuietSeconds":0}}""", "Notifications:QuietSeconds must be")]
    [InlineData("""{"Notifications":{"QuietSeconds":120,"MaxWaitSeconds":60}}""", "Notifications:MaxWaitSeconds must be")]
    [InlineData("""{"Notifications":{"MinIntervalMinutes":-1}}""", "Notifications:MinIntervalMinutes must be")]
    [InlineData("""{"Notifications":{"Plugins":{"Not An Id":{"Enabled":true}}}}""", "Notifications:Plugins")]
    [InlineData("""{"Notifications":{"Plugins":{"toast":{"Enabled":true}}}}""", "Notifications:Plugins cannot name a plugin 'toast'")]
    [InlineData("""{"Notifications":{"Plugins":{"ntfy":{"Binding":"a string"}}}}""", "Notifications:Plugins:ntfy:Binding must be an object")]
    public void A_setting_out_of_its_range_is_refused_with_its_name(string settings, string said) =>
        Assert.Contains(said, Assert.Single(Validate(settings)), StringComparison.Ordinal);

    private static NotificationsOptions Read(string settings) =>
        Provider(settings).GetRequiredService<IOptionsMonitor<NotificationsOptions>>().CurrentValue;

    private static IReadOnlyList<string> Validate(string settings)
    {
        try
        {
            _ = Read(settings);
            return [];
        }
        catch (OptionsValidationException ex)
        {
            return [.. ex.Failures];
        }
    }

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build());
        return services.BuildServiceProvider();
    }
}
