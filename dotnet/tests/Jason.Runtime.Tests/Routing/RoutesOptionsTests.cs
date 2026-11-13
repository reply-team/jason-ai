using System.Text;
using System.Text.Json;
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
    [Critical(Family.W11, "nothing routed by default")]
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

    /// <summary>
    /// A channel route stands under its operation, a key of its own: the colon a compound key would need is the separator
    /// of a settings path. Where an operation names channel routes its own route may be left out, and then it has none.
    /// </summary>
    [Fact]
    public void A_channel_route_is_read_under_its_operation_and_its_channel()
    {
        var options = Read("""
            {"Routes":{"Operations":{
              "message.send":{"Channels":{"email":{"Plugin":"fake-provider","Binding":{"workspace":"west"}},"linkedin":{"Plugin":"other-provider"}}},
              "conversation.reply":{"Plugin":"fake-provider","Channels":{"linkedin":{"Plugin":"other-provider"}}}}}}
            """);

        var send = Assert.Contains("message.send", options.Operations);
        Assert.Null(send.Plugin);
        Assert.Null(send.Binding);
        Assert.Equal(["email", "linkedin"], send.Channels.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("fake-provider", send.Channels["email"].Plugin);
        Assert.Equal("west", (string?)Assert.IsType<JsonObject>(send.Channels["email"].Binding)["workspace"]);
        Assert.Equal("other-provider", send.Channels["linkedin"].Plugin);
        Assert.Null(send.Channels["linkedin"].Binding);

        var reply = Assert.Contains("conversation.reply", options.Operations);
        Assert.Equal("fake-provider", reply.Plugin);
        Assert.Equal("other-provider", Assert.Single(reply.Channels).Value.Plugin);
        Assert.True(new RoutesOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>
    /// Every settings file written before channel routes existed stays valid unchanged and is read as it was: no operation
    /// gains a channel route, and the section that reaches a running runtime is the one it always was.
    /// </summary>
    [Fact]
    public void A_file_written_before_channel_routes_is_read_and_valid_unchanged()
    {
        const string settings = """
            {"Routes":{"Default":{"Plugin":"fake-provider","Binding":{"workspace":"west"}},
            "Operations":{"campaign.get":{"Plugin":"other-provider"},"message.send":{"Plugin":"fake-provider","Binding":{"workspace":"east"}}}}}
            """;
        using var provider = Provider(settings);

        var options = provider.GetRequiredService<IOptionsMonitor<RoutesOptions>>().CurrentValue;

        Assert.Equal("fake-provider", options.Default!.Plugin);
        Assert.Equal(["campaign.get", "message.send"], options.Operations.Keys.Order(StringComparer.Ordinal));
        Assert.All(options.Operations.Values, entry => Assert.Empty(entry.Channels));
        Assert.Equal("east", (string?)Assert.IsType<JsonObject>(options.Operations["message.send"].Binding)["workspace"]);
        Assert.True(new RoutesOptionsValidator().Validate(null, Read(settings)).Succeeded);
    }

    /// <summary>
    /// A channel route is held to the shape an operation's is, named by its whole path; a key that is no channel is written
    /// as a placeholder, as an operation that is none is. Whether the operation takes channel routes, and that channel, is
    /// the reload's to answer, with the codes a campaign's channel route is answered with.
    /// </summary>
    [Theory]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Channels":{"email":{"Plugin":"Fake Provider"}}}}}}""", "Routes:Operations:message.send:Channels:email:Plugin must be a plugin id; the value written is 13 characters long and is not one.")]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Channels":{"email":{"Binding":{"workspace":"west"}}}}}}}""", "Routes:Operations:message.send:Channels:email:Plugin must be a plugin id; nothing is written there.")]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Channels":{"linkedin":{"Plugin":"fake-provider","Binding":"west"}}}}}}""", "Routes:Operations:message.send:Channels:linkedin:Binding must be an object; got string.")]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Channels":{"fax":{"Plugin":"fake-provider"}}}}}}""", "Routes:Operations:message.send:Channels:<key> must name a channel; the key written is 3 characters long and is not one.")]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Channels":{"Email":{"Plugin":"fake-provider"}}}}}}""", "Routes:Operations:message.send:Channels:<key> must name a channel; the key written is 5 characters long and is not one.")]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Channels":"email"}}}}""", "Routes:Operations:message.send:Channels must be a map of channels, each an object with Plugin and an optional Binding.")]
    [InlineData("""{"Routes":{"Operations":{"message.send":{"Binding":{"workspace":"west"},"Channels":{"email":{"Plugin":"fake-provider"}}}}}}""", "Routes:Operations:message.send:Binding must be left out where Plugin is left out: an operation routed by its channels alone names no account of its own.")]
    public void A_channel_route_that_could_never_be_right_is_named_with_its_line(string settings, string expected)
    {
        var result = new RoutesOptionsValidator().Validate(null, Read(settings));

        Assert.True(result.Failed);
        Assert.Contains(expected, result.Failures!);
        Assert.DoesNotContain(result.Failures!, failure => failure.Contains("fax", StringComparison.Ordinal) || failure.Contains("west", StringComparison.Ordinal));
    }

    /// <summary>An operation that names neither its own plugin nor a channel route is still an operation routed nowhere.</summary>
    [Fact]
    public void An_operation_with_no_plugin_and_an_empty_map_of_channels_is_refused_as_before()
    {
        var result = new RoutesOptionsValidator().Validate(null, Read("""{"Routes":{"Operations":{"message.send":{"Channels":{}}}}}"""));

        Assert.True(result.Failed);
        Assert.Contains("Routes:Operations:message.send:Plugin must be a plugin id; nothing is written there.", result.Failures!);
    }

    /// <summary>
    /// A settings key folds case, as every key of the configuration system does, so <c>channels</c> written in another
    /// case is the same key and its routes are read. A channel is a value of the contract's, not a key of the file's: it
    /// keeps its case, and the validator holds it to the contract's word.
    /// </summary>
    [Fact]
    public void Channels_written_in_another_case_under_an_operation_is_read_as_Channels()
    {
        var options = Read("""{"Routes":{"Operations":{"message.send":{"channels":{"email":{"Plugin":"fake-provider"}}}}}}""");

        var send = Assert.Contains("message.send", options.Operations);
        Assert.Null(send.Plugin);
        Assert.Equal("fake-provider", Assert.Contains("email", send.Channels).Plugin);
        Assert.True(new RoutesOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>
    /// The singular <c>Channel</c> is not a key of an operation's entry. It is ignored, as an unknown key is under every
    /// part of <c>Routes</c> but a <c>Channels</c> where no operation stands: the operation keeps its own route, gains
    /// no channel route, and the file is valid. This row records that, so a change to it is a decision.
    /// </summary>
    [Fact]
    public void The_singular_Channel_under_an_operation_is_an_unknown_key_and_is_ignored()
    {
        var options = Read("""{"Routes":{"Operations":{"message.send":{"Plugin":"fake-provider","Channel":{"email":{"Plugin":"other-provider"}}}}}}""");

        var send = Assert.Contains("message.send", options.Operations);
        Assert.Equal("fake-provider", send.Plugin);
        Assert.Null(send.Binding);
        Assert.Empty(send.Channels);
        Assert.Empty(options.MisplacedChannels);
        Assert.True(new RoutesOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>
    /// The configuration system hands every scalar over as text, so what a binding value is — a number or a
    /// string — is decided here by pattern. A quoted value ending in a newline was read as a number: the binding
    /// is hashed into the identity an attempt records and measured against the plugin's declared schema, and
    /// neither the identity nor the type would be the one the person editing the file wrote. The value is built
    /// rather than written into the JSON, so what is tested is the runtime's reading of it.
    /// </summary>
    [Fact]
    public void A_binding_value_that_ends_in_a_newline_stays_the_string_it_was_written_as()
    {
        var settings = new JsonObject
        {
            ["Routes"] = new JsonObject
            {
                ["Default"] = new JsonObject
                {
                    ["Plugin"] = "fake-provider",
                    ["Binding"] = new JsonObject { ["seats"] = "3\n", ["desks"] = "3 " },
                },
            },
        };

        var binding = Assert.IsType<JsonObject>(Read(settings.ToJsonString()).Default!.Binding);

        // A trailing space was already a string; only a trailing newline was not, which is the anchor's doing.
        Assert.Equal(JsonValueKind.String, binding["seats"]!.GetValueKind());
        Assert.Equal("3\n", (string?)binding["seats"]);
        Assert.Equal("3 ", (string?)binding["desks"]);
    }

    [Fact]
    public void A_default_with_no_binding_has_none_rather_than_an_empty_one()
    {
        var options = Read("""{"Routes":{"Default":{"Plugin":"fake-provider","Binding":null}}}""");

        Assert.Null(options.Default!.Binding);
    }

    /// <summary>Named with its line and described by its length: what was written is not said back into the log.</summary>
    [Theory]
    [InlineData("""{"Routes":{"Default":{"Plugin":""}}}""", "Routes:Default:Plugin must be a plugin id; nothing is written there.")]
    [InlineData("""{"Routes":{"Default":{"Binding":{"workspace":"west"}}}}""", "Routes:Default:Plugin must be a plugin id; nothing is written there.")]
    [InlineData("""{"Routes":{"Default":{"Plugin":"Fake Provider"}}}""", "Routes:Default:Plugin must be a plugin id; the value written is 13 characters long and is not one.")]
    [InlineData("""{"Routes":{"Operations":{"campaign.get":{"Plugin":"-nope"}}}}""", "Routes:Operations:campaign.get:Plugin must be a plugin id; the value written is 5 characters long and is not one.")]
    public void A_plugin_that_could_not_be_a_plugin_id_is_named_with_its_line(string settings, string expected)
    {
        var result = new RoutesOptionsValidator().Validate(null, Read(settings));

        Assert.True(result.Failed);
        Assert.Contains(expected, result.Failures!);
    }

    /// <summary>
    /// A key that names no published operation is a key the code does not know, and could be anything a person
    /// pasted, so its line is written with a placeholder where the key was, and the key is described by its length.
    /// </summary>
    [Theory]
    [InlineData("not.an.operation", 16)]
    [InlineData("notification.send", 17)]
    public void An_operation_this_build_does_not_publish_is_named_with_its_line(string operation, int length)
    {
        var result = new RoutesOptionsValidator().Validate(null, Read("""{"Routes":{"Operations":{"OPERATION":{"Plugin":"fake-provider"}}}}""".Replace("OPERATION", operation, StringComparison.Ordinal)));

        Assert.True(result.Failed);
        Assert.Contains(
            FormattableString.Invariant($"Routes:Operations:<key> must name a published operation; the key written is {length} characters long and is not one."),
            result.Failures!);
        Assert.DoesNotContain(result.Failures!, failure => failure.Contains(operation, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"Routes":{"Default":{"Plugin":"fake-provider","Binding":"west"}}}""", "Routes:Default:Binding must be an object; got string.")]
    [InlineData("""{"Routes":{"Operations":{"campaign.get":{"Plugin":"fake-provider","Binding":7}}}}""", "Routes:Operations:campaign.get:Binding must be an object; got number.")]
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

        Assert.Contains("Routes:Operations:<key> must name a published operation", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not.an.operation", error.Message, StringComparison.Ordinal);
    }

    private static RoutesOptions Read(string settings) =>
        RoutesOptions.Read(Configuration(settings).GetSection(RoutesOptions.Section));

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(Configuration(settings), TestOptions.NothingInstalled());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The settings text read by the very parser the user's file goes through, so what these tests see is what a
    /// hand-edited <c>settings.json</c> becomes — including everything the configuration system flattens away.
    /// </summary>
    private static IConfigurationRoot Configuration(string settings) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
}
