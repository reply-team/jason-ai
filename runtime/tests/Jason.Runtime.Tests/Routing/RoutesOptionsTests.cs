using System.Text;
using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The global half of routing is a settings section, read like every other one and validated on start for its
/// shape alone: whether a plugin exists is a reload question, because settings are read before any package is
/// loaded. What a shipped Jason routes by default is nothing at all.
/// </summary>
public class RoutesOptionsTests
{
    /// <summary>
    /// A7(c), as a test rather than a comment: a shipped default provider would make "vendor-neutral" a claim
    /// contradicted by the configuration the product ships with.
    /// </summary>
    [Fact]
    public void Out_of_the_box_nothing_is_routed_anywhere_and_no_plugin_is_named()
    {
        var options = new RoutesOptions();

        Assert.Null(options.Default);
        Assert.Empty(options.Operations);
        Assert.True(new RoutesOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void A_runtime_with_no_routes_section_at_all_binds_an_empty_one()
    {
        var options = Read("""{"Dispatcher":{"Enabled":false}}""");

        Assert.Null(options.Default);
        Assert.Empty(options.Operations);
    }

    [Fact]
    public void The_global_default_and_its_binding_survive_the_configuration_system()
    {
        var options = Read("""
            {"Routes":{"Default":{"Plugin":"fake-provider","Binding":{"workspace":"west","shared":true,"seats":3,
            "account":{"id":"A"},"boxes":["one","two"]}}}}
            """);

        Assert.Equal("fake-provider", options.Default!.Plugin);
        var binding = Assert.IsType<JsonObject>(options.Default.Binding);
        Assert.Equal("west", (string?)binding["workspace"]);
        Assert.True((bool?)binding["shared"]);
        Assert.Equal(3, (int?)binding["seats"]);
        Assert.Equal("A", (string?)binding["account"]!["id"]);
        Assert.Equal(["one", "two"], Assert.IsType<JsonArray>(binding["boxes"]).Select(item => (string?)item));
    }

    [Fact]
    public void An_operation_override_is_read_under_the_operation_it_names()
    {
        var options = Read("""{"Routes":{"Operations":{"campaign.get":{"Plugin":"other-provider"}}}}""");

        var entry = Assert.Contains("campaign.get", options.Operations);
        Assert.Equal("other-provider", entry.Plugin);
        Assert.Null(entry.Binding);
    }

    [Fact]
    public void A_default_with_no_binding_has_none_rather_than_an_empty_one()
    {
        var options = Read("""{"Routes":{"Default":{"Plugin":"fake-provider","Binding":null}}}""");

        Assert.Null(options.Default!.Binding);
    }

    [Theory]
    [InlineData("""{"Routes":{"Default":{"Plugin":""}}}""", "Routes:Default:Plugin must be a plugin id; got ''.")]
    [InlineData("""{"Routes":{"Default":{"Binding":{"workspace":"west"}}}}""", "Routes:Default:Plugin must be a plugin id; got ''.")]
    [InlineData("""{"Routes":{"Default":{"Plugin":"Fake Provider"}}}""", "Routes:Default:Plugin must be a plugin id; got 'Fake Provider'.")]
    [InlineData("""{"Routes":{"Operations":{"campaign.get":{"Plugin":"-nope"}}}}""", "Routes:Operations:campaign.get:Plugin must be a plugin id; got '-nope'.")]
    public void A_plugin_that_could_not_be_a_plugin_id_is_named_with_its_line(string settings, string expected)
    {
        var result = new RoutesOptionsValidator().Validate(null, Read(settings));

        Assert.True(result.Failed);
        Assert.Contains(expected, result.Failures!);
    }

    [Fact]
    public void An_operation_this_build_does_not_publish_is_named_with_its_line()
    {
        var result = new RoutesOptionsValidator().Validate(null, Read("""{"Routes":{"Operations":{"not.an.operation":{"Plugin":"fake-provider"}}}}"""));

        Assert.True(result.Failed);
        Assert.Contains("Routes:Operations:not.an.operation must name a published operation; got 'not.an.operation'.", result.Failures!);
    }

    [Theory]
    [InlineData("""{"Routes":{"Default":{"Plugin":"fake-provider","Binding":"west"}}}""", "Routes:Default:Binding must be an object; got 'west'.")]
    [InlineData("""{"Routes":{"Operations":{"campaign.get":{"Plugin":"fake-provider","Binding":7}}}}""", "Routes:Operations:campaign.get:Binding must be an object; got '7'.")]
    public void A_binding_that_is_not_an_object_is_named_the_same_way(string settings, string expected)
    {
        var result = new RoutesOptionsValidator().Validate(null, Read(settings));

        Assert.True(result.Failed);
        Assert.Contains(expected, result.Failures!);
    }

    /// <summary>
    /// Shape only. A plugin nobody has installed is a perfectly valid setting until a reload goes looking for
    /// it: the settings file is read before any package is, and the reload is where a route meets reality.
    /// </summary>
    [Fact]
    public void A_plugin_that_is_not_installed_is_still_a_valid_setting()
    {
        var result = new RoutesOptionsValidator().Validate(null, Read("""{"Routes":{"Default":{"Plugin":"nobody-has-this"}}}"""));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Every_problem_in_the_section_is_reported_at_once()
    {
        var result = new RoutesOptionsValidator().Validate(null, Read("""
            {"Routes":{"Default":{"Plugin":"NOPE"},"Operations":{"not.an.operation":{"Plugin":"fake-provider"},
            "campaign.get":{"Plugin":"fake-provider","Binding":[1,2]}}}}
            """));

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    /// <summary>The section reaches a running runtime through the options system, like every other one.</summary>
    [Fact]
    public void The_section_is_bound_and_validated_where_the_other_options_are()
    {
        using var provider = Provider("""{"Routes":{"Default":{"Plugin":"fake-provider","Binding":{"workspace":"west"}}}}""");

        var options = provider.GetRequiredService<IOptionsMonitor<RoutesOptions>>().CurrentValue;

        Assert.Equal("fake-provider", options.Default!.Plugin);
        Assert.Equal("west", (string?)Assert.IsType<JsonObject>(options.Default.Binding)["workspace"]);
    }

    [Fact]
    public void A_route_the_runtime_cannot_make_sense_of_stops_it_the_way_every_other_setting_does()
    {
        using var provider = Provider("""{"Routes":{"Operations":{"not.an.operation":{"Plugin":"fake-provider"}}}}""");

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<RoutesOptions>>().CurrentValue);

        Assert.Contains("Routes:Operations:not.an.operation", error.Message, StringComparison.Ordinal);
    }

    private static RoutesOptions Read(string settings) =>
        RoutesOptions.Read(Configuration(settings).GetSection(RoutesOptions.Section));

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(Configuration(settings));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The settings text read by the very parser the user's file goes through, so what these tests see is what a
    /// hand-edited <c>settings.json</c> becomes — including everything the configuration system flattens away.
    /// </summary>
    private static IConfigurationRoot Configuration(string settings) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
}
