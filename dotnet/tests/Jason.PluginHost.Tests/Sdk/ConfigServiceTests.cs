using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jason.Runtime.Tests;
using Jint;
using Jint.Runtime;

namespace Jason.PluginHost.Tests.Sdk;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly TempPackage _package = new();

    public void Dispose() => _package.Dispose();

    [Fact]
    public void A_key_the_settings_hold_reads_as_its_text()
    {
        using var harness = new SdkHarness(_package.Root, builder => builder.Config = Config(("chat", "0012345")));

        Assert.Equal("0012345", harness.Evaluate("host.config(\"chat\")").AsString());
    }

    /// <summary>The empty text is a value a person wrote, which is not the null of a key they did not.</summary>
    [Fact]
    public void An_empty_value_is_the_empty_text()
    {
        using var harness = new SdkHarness(_package.Root, builder => builder.Config = Config(("chat", string.Empty)));

        Assert.Equal("string", harness.Evaluate("typeof host.config(\"chat\")").AsString());
    }

    /// <summary>
    /// A key that is not there is the ordinary answer of an optional setting, so it is null and says nothing: a plugin
    /// looking for its configuration before its environment is no plugin reaching for what it was not given.
    /// </summary>
    [Fact]
    public void A_key_the_settings_do_not_hold_reads_as_null_and_nothing_is_recorded()
    {
        using var harness = new SdkHarness(_package.Root, builder => builder.Config = Config(("chat", "0012345")));

        Assert.True(harness.Evaluate("host.config(\"other\")").IsNull());
        Assert.Empty(harness.Lines);
    }

    [Fact]
    public void An_invocation_with_no_configuration_reads_null_for_every_key()
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.True(harness.Evaluate("host.config(\"chat\")").IsNull());
    }

    [Theory]
    [InlineData("host.config()")]
    [InlineData("host.config(12)")]
    [InlineData("host.config(null)")]
    public void A_key_that_is_not_a_string_is_the_plugin_s_own_error(string call)
    {
        using var harness = new SdkHarness(_package.Root);

        var thrown = Assert.Throws<JavaScriptException>(() => harness.Evaluate(call));

        Assert.Contains(ConfigService.Function, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_function_stands_beside_the_others_on_an_object_nothing_can_replace()
    {
        using var harness = new SdkHarness(_package.Root, builder => builder.Config = Config(("chat", "0012345")));

        Assert.Equal("function", harness.Evaluate("typeof host.config").AsString());
        harness.Evaluate("try { host.config = () => \"replaced\"; } catch { }");
        Assert.Equal("0012345", harness.Evaluate("host.config(\"chat\")").AsString());
    }

    private static Dictionary<string, string> Config(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
}
