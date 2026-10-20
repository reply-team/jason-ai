using Jason.PluginHost.Tests.Fixtures;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// <c>host.channelKey(channel, value)</c> answers with the runtime's own key for a value on a channel, or <c>null</c>: a pure
/// function, so it needs no grant, says nothing on stderr and spends nothing of the call budget.
/// </summary>
public sealed class ChannelKeyFunctionTests : IDisposable
{
    private readonly TempPackage _package = new();

    public void Dispose() => _package.Dispose();

    [Theory]
    [InlineData("host.channelKey('linkedin', 'https://uk.linkedin.com/in/Marta-Example//?trk=feed')", "https://www.linkedin.com/in/marta-example")]
    [InlineData("host.channelKey('LinkedIn', 'linkedin.com/in/J%C3%96RN-Example')", "https://www.linkedin.com/in/jörn-example")]
    [InlineData("host.channelKey('email', ' Marta@Example.com ')", "marta@example.com")]
    [InlineData("host.channelKey('phone', '+1 (604) 555-0100')", "+16045550100")]
    [InlineData("host.channelKey('telegram', ' @marta ')", "@marta")]
    public void A_value_the_runtime_keys_is_answered_with_its_key(string call, string key)
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal(key, harness.Evaluate(call).AsString());
        Assert.Empty(harness.Lines);
        Assert.Equal(0, harness.Budget.ExecCalls);
        Assert.Equal(0, harness.Budget.HttpCalls);
    }

    [Theory]
    [InlineData("host.channelKey('linkedin', 'https://www.linkedin.com/company/example')")]
    [InlineData("host.channelKey('linkedin', 'https://example.com/in/marta-example')")]
    [InlineData("host.channelKey('linkedin', '')")]
    [InlineData("host.channelKey('email', 'marta')")]
    [InlineData("host.channelKey('e mail', 'marta@example.com')")]
    [InlineData("host.channelKey('linkedin', 'https://www.linkedin.com/in/' + 'a'.repeat(500))")]
    [InlineData("host.channelKey('linkedin', 'https://www.linkedin.com/in/marta-example?q=' + 'x'.repeat(500))")]
    public void A_value_the_runtime_would_refuse_is_null(string call)
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal("null", harness.Evaluate("String(" + call + ")").AsString());
        Assert.Equal("true", harness.Evaluate("String(" + call + " === null)").AsString());
    }

    [Theory]
    [InlineData("host.channelKey()")]
    [InlineData("host.channelKey('linkedin')")]
    [InlineData("host.channelKey(1, 'https://www.linkedin.com/in/marta-example')")]
    [InlineData("host.channelKey('linkedin', null)")]
    [InlineData("host.channelKey('linkedin', undefined)")]
    [InlineData("host.channelKey('linkedin', { value: 'https://www.linkedin.com/in/marta-example' })")]
    public void Anything_but_two_strings_is_a_type_error(string call)
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal("TypeError", harness.Caught(call));
        Assert.StartsWith("host.channelKey: ", harness.Message(call), StringComparison.Ordinal);
    }
}
