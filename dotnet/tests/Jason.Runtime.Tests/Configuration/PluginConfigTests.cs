using System.Text;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// <c>Plugins:Config:&lt;id&gt;</c>, read by the real configuration parser and the real binder: a flat map from a key to
/// text. A value is text as the configuration system hands it, so a token of digits arrives as it was written; what is
/// refused is structure, by its path, and never by anything a person wrote there.
/// </summary>
public sealed class PluginConfigTests
{
    private const string Plugin = "fake-provider";

    /// <summary>A digit-only token is the case the routes' reader gets wrong: it reads it back as a number.</summary>
    [Fact]
    public void A_value_arrives_as_the_text_the_settings_hand_over_and_is_never_read_again_as_something_else()
    {
        var config = Read("""{"Plugins":{"Config":{"fake-provider":{"token":"0012345","chat":12345,"rate":1.50,"secure":true,"url":"https://receiver.example.test/in"}}}}""")
            .Config[Plugin];

        Assert.Equal("0012345", config["token"]);
        Assert.Equal("12345", config["chat"]);
        Assert.Equal("1.50", config["rate"]);
        Assert.Equal("True", config["secure"]);
        Assert.Equal("https://receiver.example.test/in", config["url"]);
    }

    /// <summary>Plugin ids are matched as a grant's are, and as every configuration key is.</summary>
    [Fact]
    public void A_plugin_is_found_whatever_case_its_id_was_typed_in()
    {
        var options = Read("""{"Plugins":{"Config":{"FAKE-Provider":{"url":"https://receiver.example.test/in"}}}}""");

        Assert.Equal("https://receiver.example.test/in", options.Config[Plugin]["url"]);
    }

    /// <summary>A section nobody wrote is an empty map, and the settings that have never heard of it read as they did.</summary>
    [Fact]
    public void The_section_is_empty_until_a_person_writes_it()
    {
        Assert.Empty(new PluginsOptions().Config);
    }

    /// <summary>
    /// Structure, wherever it stands in the section: each is refused by the path and what has to be there, the sentinel
    /// nowhere in the sentence. A list directly under a plugin is flattened into keys that are numbers, which is how it
    /// is told from the map it would otherwise pass for.
    /// </summary>
    [Theory]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{"url":{"host":"SENTINEL"}}}}}""", "Plugins:Config:fake-provider:<key> could not be read as System.String.")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{"url":["SENTINEL"]}}}}""", "Plugins:Config:fake-provider:<key> could not be read as System.String.")]
    [InlineData("""{"Plugins":{"Config":{"not-installed":{"url":{"host":"SENTINEL"}}}}}""", "Plugins:Config:<key>:<key> could not be read as System.String.")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":"SENTINEL"}}}""", "Plugins:Config:fake-provider could not be read as a map of System.String.")]
    [InlineData("""{"Plugins":{"Config":"SENTINEL"}}""", "Plugins:Config could not be read as a map of a map of System.String.")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":["SENTINEL"]}}}""", "Plugins:Config:fake-provider:<key> must be a name: a plugin's configuration is a flat map from a name to text, and a list is not one.")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{"0":"SENTINEL"}}}}""", "Plugins:Config:fake-provider:<key> must be a name: a plugin's configuration is a flat map from a name to text, and a list is not one.")]
    [InlineData("""{"Plugins":{"Config":{"not-installed":["SENTINEL"]}}}""", "Plugins:Config:<key>:<key> must be a name: a plugin's configuration is a flat map from a name to text, and a list is not one.")]
    [InlineData("""{"Plugins":{"Config":[{"url":"SENTINEL"}]}}""", "Plugins:Config:<key> must be a plugin's id: this section is a map from a plugin's id to its configuration, and a list is not one.")]
    [InlineData("""{"Plugins":{"Config":{"0":{"url":"SENTINEL"}}}}""", "Plugins:Config:<key> must be a plugin's id: this section is a map from a plugin's id to its configuration, and a list is not one.")]
    public void Structure_is_refused_by_its_path_and_never_by_what_was_written(string settings, string expected)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, Plugin));
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, Plugin, "plugin.yaml"), $"id: {Plugin}");
        using var provider = Provider(settings, dir.Paths);

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>().CurrentValue);

        Assert.Null(refused.InnerException);
        Assert.Equal([expected], refused.Failures);
    }

    /// <summary>
    /// What the flattened settings cannot tell from a value is no structure to refuse: a key written as nothing has no
    /// children, and reads as a key that was never set. The loader's own row says so of what a plugin is handed.
    /// </summary>
    [Theory]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{"url":null}}}}""")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{"url":{}}}}}""")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{}}}}""")]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":null}}}""")]
    public void A_key_written_as_nothing_is_not_refused(string settings)
    {
        Assert.Single(Read(settings).Config);
    }

    private static PluginsOptions Read(string settings)
    {
        using var provider = Provider(settings, TestOptions.NothingInstalled());
        return provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>().CurrentValue;
    }

    private static ServiceProvider Provider(string settings, JasonPaths paths)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build(),
            paths);
        services.AddSingleton(provider => new LiveSettings<PluginsOptions>(
            provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>(),
            NullLogger<LiveSettings<PluginsOptions>>.Instance,
            PluginsOptions.Section));
        return services.BuildServiceProvider();
    }
}
