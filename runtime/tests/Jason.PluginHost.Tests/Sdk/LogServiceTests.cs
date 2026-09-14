using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

public sealed class LogServiceTests : IDisposable
{
    private readonly TempPackage _package = new();
    private readonly string _name = "FAKE_TOKEN_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, null);
        _package.Dispose();
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("error")]
    public void Every_level_writes_one_line_the_plugin_owns(string level)
    {
        using var harness = new SdkHarness(_package.Root);

        harness.Evaluate($"host.log(\"{level}\", \"something happened\")");

        var line = Assert.Single(harness.Lines);
        Assert.Equal(level, line["level"]!.GetValue<string>());
        Assert.Equal(HostDiagnostics.PluginSource, line["source"]!.GetValue<string>());
        Assert.Equal("something happened", line["message"]!.GetValue<string>());
        Assert.Equal(harness.Invocation.InvocationId, line["invocation_id"]!.GetValue<string>());
        Assert.Equal(1, harness.Diagnostics.LogLines);
    }

    [Fact]
    public void Data_travels_as_JSON_beside_the_message()
    {
        using var harness = new SdkHarness(_package.Root);

        harness.Evaluate("host.log(\"info\", \"counted\", { items: 3, names: [\"a\"] })");

        var line = Assert.Single(harness.Lines);
        Assert.Equal(3, line["data"]!["items"]!.GetValue<int>());
        Assert.Equal("a", line["data"]!["names"]![0]!.GetValue<string>());
    }

    [Fact]
    public void A_level_that_is_not_one_of_the_four_is_a_type_error()
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal("TypeError", harness.Caught("host.log(\"fatal\", \"m\")"));
        Assert.Equal("TypeError", harness.Caught("host.log(\"info\", 42)"));
        Assert.Equal("TypeError", harness.Caught("host.log()"));
        Assert.Empty(harness.Lines);
    }

    [Fact]
    public void The_value_of_a_granted_variable_never_reaches_the_log()
    {
        Environment.SetEnvironmentVariable(_name, "super-secret-token-value");
        using var harness = new SdkHarness(_package.Root, builder => builder.Grants = new InvocationGrants(null, null, new EnvGrants([_name])));

        harness.Evaluate($"host.log(\"info\", \"sending \" + host.env(\"{_name}\"), {{ token: host.env(\"{_name}\") }})");

        var line = Assert.Single(harness.Lines);
        Assert.Equal("sending " + Redactor.Mask, line["message"]!.GetValue<string>());
        Assert.Equal(Redactor.Mask, line["data"]!["token"]!.GetValue<string>());
        Assert.DoesNotContain("super-secret-token-value", harness.Stderr, StringComparison.Ordinal);
    }
}
