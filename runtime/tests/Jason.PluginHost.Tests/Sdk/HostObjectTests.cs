using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

public sealed class HostObjectTests : IDisposable
{
    private readonly TempPackage _package = new();

    public void Dispose() => _package.Dispose();

    private SdkHarness Harness(Action<InvocationBuilder>? configure = null) => new(_package.Root, configure);

    [Fact]
    public void The_host_object_is_exactly_five_functions()
    {
        using var harness = Harness();

        Assert.Equal("exec,http,env,log,fail", harness.Evaluate("Object.keys(host).join(',')").AsString());
        Assert.Equal("function", harness.Evaluate("typeof host.exec").AsString());
        Assert.Equal("function", harness.Evaluate("typeof host.http").AsString());
        Assert.Equal("function", harness.Evaluate("typeof host.env").AsString());
        Assert.Equal("function", harness.Evaluate("typeof host.log").AsString());
        Assert.Equal("function", harness.Evaluate("typeof host.fail").AsString());
    }

    [Fact]
    public void A_member_cannot_be_replaced()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught("host.exec = 1"));
    }

    [Fact]
    public void Nothing_can_be_added_to_it()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught("host.extra = 1"));
        Assert.Equal("false", harness.Evaluate("String(Object.isExtensible(host))").AsString());
    }

    [Fact]
    public void There_is_no_surface_beyond_the_five()
    {
        using var harness = Harness();

        Assert.Equal("true", harness.Evaluate("String(Object.getPrototypeOf(host) === Object.prototype)").AsString());
        Assert.Equal("exec,http,env,log,fail", harness.Evaluate("Object.getOwnPropertyNames(host).join(',')").AsString());
        Assert.Equal("undefined", harness.Evaluate("typeof host.GetType").AsString());
    }

    [Fact]
    public void A_failure_is_an_error_object_carrying_its_class_and_code()
    {
        using var harness = Harness();

        var name = harness.Evaluate("host.fail({ class: \"transient\", code: \"rate_limited\", message: \"slow down\" }).name").AsString();
        var code = harness.Evaluate("host.fail({ class: \"transient\", code: \"rate_limited\", message: \"slow down\" }).code").AsString();

        Assert.Equal("JasonFailure", name);
        Assert.Equal("rate_limited", code);
    }

    [Theory]
    [InlineData("{ class: \"bogus\", code: \"c\", message: \"m\" }")]
    [InlineData("{ class: \"transient\", code: \"Not Snake\", message: \"m\" }")]
    [InlineData("{ class: \"transient\", code: \"c\" }")]
    [InlineData("{ class: \"transient\", code: \"c\", message: \"m\", unknown: 1 }")]
    [InlineData("42")]
    public void A_failure_that_does_not_follow_the_contract_is_a_type_error(string specification)
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught("host.fail(" + specification + ")"));
    }

    /// <summary>
    /// A code that is lower snake_case until the newline the plugin built onto its end. The code leaves here for
    /// the outcome document and ends up on an attempt a person reads, so the rule has to hold to the end of the
    /// string: a plugin is a third party, and this is the door its words come through.
    /// </summary>
    [Fact]
    public void A_code_that_ends_in_a_newline_is_not_lower_snake_case()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught("host.fail({ class: \"transient\", code: \"rate_limited\\n\", message: \"m\" })"));
    }

    [Fact]
    public void External_identifiers_on_a_failure_are_bounded()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught("host.fail({ class: \"transient\", code: \"c\", message: \"m\", external_ids: { a: 1 } })"));
        Assert.Equal(
            "no-throw",
            harness.Caught("host.fail({ class: \"transient\", code: \"c\", message: \"m\", external_ids: { contact: \"r_1\" } })"));
    }
}
