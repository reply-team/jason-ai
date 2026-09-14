using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

public sealed class EnvServiceTests : IDisposable
{
    private readonly TempPackage _package = new();

    /// <summary>Tests run in parallel and share one process environment, so every name here is this test's own.</summary>
    private readonly string _name = "FAKE_TOKEN_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, null);
        _package.Dispose();
    }

    [Fact]
    public void A_granted_variable_that_is_set_is_readable()
    {
        Environment.SetEnvironmentVariable(_name, "a-secret-value");
        using var harness = new SdkHarness(_package.Root, builder => builder.Grants = new InvocationGrants(null, null, new EnvGrants([_name])));

        Assert.Equal("a-secret-value", harness.Evaluate($"host.env(\"{_name}\")").AsString());
    }

    [Fact]
    public void A_granted_variable_that_is_not_set_reads_as_undefined()
    {
        using var harness = new SdkHarness(_package.Root, builder => builder.Grants = new InvocationGrants(null, null, new EnvGrants([_name])));

        Assert.Equal("undefined", harness.Evaluate($"typeof host.env(\"{_name}\")").AsString());
    }

    [Fact]
    public void A_variable_that_was_not_granted_is_invisible_and_the_probe_is_recorded()
    {
        Environment.SetEnvironmentVariable(_name, "a-secret-value");
        using var harness = new SdkHarness(_package.Root, builder => builder.Grants = new InvocationGrants(null, null, new EnvGrants(["OTHER_VARIABLE"])));

        Assert.Equal("undefined", harness.Evaluate($"typeof host.env(\"{_name}\")").AsString());
        var probe = Assert.Single(harness.Lines);
        Assert.Equal("env_probe", probe["message"]!.GetValue<string>());
        Assert.Equal(HostDiagnostics.HostSource, probe["source"]!.GetValue<string>());
        Assert.Equal(_name, probe["data"]!["variable"]!.GetValue<string>());
        Assert.DoesNotContain("a-secret-value", harness.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_capability_nothing_is_readable()
    {
        Environment.SetEnvironmentVariable(_name, "a-secret-value");
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal("undefined", harness.Evaluate($"typeof host.env(\"{_name}\")").AsString());
        Assert.Equal("undefined", harness.Evaluate("typeof host.env(\"PATH\")").AsString());
    }

    [Fact]
    public void A_name_that_is_not_a_string_is_a_type_error()
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal("TypeError", harness.Caught("host.env(42)"));
        Assert.Equal("TypeError", harness.Caught("host.env()"));
    }
}
