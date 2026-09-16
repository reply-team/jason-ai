using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Declaration is not permission. What a manifest asks for and what the user granted are two lists, and the
/// resolution is the intersection — never the union, and never something the manifest did not ask for.
/// </summary>
public class GrantResolverTests
{
    private static readonly CapabilityRequests Requested = new(
        new ExecRequestSpec([new ExecutableRequest("provider-cli", null, null), new ExecutableRequest("dotnet", null, null)]),
        new HttpRequestSpec(["api.example.test", "localhost:5555"]),
        new EnvRequestSpec(["EXAMPLE_API_KEY", "EXAMPLE_OTHER"]));

    [Fact]
    public void A_plugin_nobody_decided_about_is_granted_nothing()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), null);

        Assert.Empty(grants.Exec);
        Assert.Empty(grants.Http);
        Assert.Empty(grants.Env);
        Assert.Empty(grants.Warnings);
        Assert.Equal(ResolvedGrants.None, grants);
    }

    [Fact]
    public void An_empty_list_grants_nothing_either()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant());

        Assert.Empty(grants.Exec);
        Assert.Empty(grants.Http);
        Assert.Empty(grants.Env);
    }

    [Fact]
    public void A_star_grants_everything_that_capability_asked_for()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant { Exec = ["*"], Http = ["*"], Env = ["*"] });

        Assert.Equal(["provider-cli", "dotnet"], grants.Exec);
        Assert.Equal(["api.example.test", "localhost:5555"], grants.Http);
        Assert.Equal(["EXAMPLE_API_KEY", "EXAMPLE_OTHER"], grants.Env);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_subset_grants_exactly_what_it_names()
    {
        var grants = GrantResolver.Resolve(
            Manifest(Requested),
            new PluginGrant { Exec = ["dotnet"], Http = ["localhost:5555"], Env = ["EXAMPLE_API_KEY"] });

        Assert.Equal(["dotnet"], grants.Exec);
        Assert.Equal(["localhost:5555"], grants.Http);
        Assert.Equal(["EXAMPLE_API_KEY"], grants.Env);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_grant_the_manifest_never_asked_for_grants_nothing_and_says_so()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant { Http = ["evil.test", "api.example.test"] });

        Assert.Equal(["api.example.test"], grants.Http);
        var warning = Assert.Single(grants.Warnings);
        Assert.Equal(ProblemCodes.GrantUnrequested, warning.Code);
        Assert.Equal("grants.http[0]", warning.Path);
    }

    [Fact]
    public void A_capability_the_manifest_never_requested_cannot_be_granted_at_all()
    {
        var grants = GrantResolver.Resolve(
            Manifest(new CapabilityRequests(null, null, null)),
            new PluginGrant { Exec = ["*"], Http = ["api.example.test"], Env = ["*"] });

        Assert.Empty(grants.Exec);
        Assert.Empty(grants.Http);
        Assert.Empty(grants.Env);

        // A star over an empty request is not a mistake worth a warning; naming something is.
        var warning = Assert.Single(grants.Warnings);
        Assert.Equal("grants.http[0]", warning.Path);
    }

    [Fact]
    public void A_host_is_matched_the_way_a_host_is_compared()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant { Http = ["API.Example.Test"] });

        // The grant is recorded as the manifest spells it, so everything downstream compares one form.
        Assert.Equal(["api.example.test"], grants.Http);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_program_and_a_variable_are_matched_exactly()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant { Exec = ["Provider-Cli"], Env = ["example_api_key"] });

        Assert.Empty(grants.Exec);
        Assert.Empty(grants.Env);
        Assert.Equal(["grants.exec[0]", "grants.env[0]"], grants.Warnings.Select(w => w.Path));
    }

    [Fact]
    public void The_same_grant_written_twice_grants_it_once()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant { Exec = ["provider-cli", "provider-cli"] });

        Assert.Equal(["provider-cli"], grants.Exec);
    }

    private static PluginManifest Manifest(CapabilityRequests capabilities) => new(
        "fake",
        "1.0.0",
        PluginKind.Provider,
        null,
        null,
        null,
        new ManifestContracts([1], [1]),
        ["echo.run"],
        new PluginEntry("main.js", "invoke"),
        capabilities,
        new ManifestLimits(null, null),
        Binding: null);
}
