using Jason.Contracts.Api;
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
    [Critical(Family.R5, "default deny")]
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

    /// <summary>The word lets a person name a host the manifest could not know. It names no host itself, and the star stays the declared ones.</summary>
    private static readonly CapabilityRequests AnyHost = new(null, new HttpRequestSpec(["api.example.test"], AnyGrantedHost: true), null);

    [Fact]
    [Critical(Family.R5, "a host beyond the declared ones takes the manifest's word and a person's grant")]
    public void With_the_word_a_host_a_person_names_is_honoured_as_a_host_is_compared()
    {
        var grants = GrantResolver.Resolve(Manifest(AnyHost), new PluginGrant { Http = ["receiver.example.test", "127.0.0.1:9123", "API.Example.Test", "Other.Example.Test"] });

        Assert.Equal(["receiver.example.test", "127.0.0.1:9123", "api.example.test", "other.example.test"], grants.Http);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void With_the_word_the_star_still_means_the_declared_hosts_alone()
    {
        var grants = GrantResolver.Resolve(Manifest(AnyHost), new PluginGrant { Http = ["*"] });

        Assert.Equal(["api.example.test"], grants.Http);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void With_the_word_and_no_grant_nothing_is_granted()
    {
        Assert.Empty(GrantResolver.Resolve(Manifest(AnyHost), new PluginGrant()).Http);
        Assert.Equal(ResolvedGrants.None, GrantResolver.Resolve(Manifest(AnyHost), null));
    }

    /// <summary>The word opens hosts, not patterns: what no manifest could declare, no grant can name.</summary>
    [Theory]
    [InlineData("*.example.test")]
    [InlineData("https://receiver.example.test")]
    [InlineData("receiver.example.test:99999")]
    [InlineData("receiver.example.test/in")]
    public void With_the_word_what_is_no_host_grants_nothing_and_says_so(string entry)
    {
        var grants = GrantResolver.Resolve(Manifest(AnyHost), new PluginGrant { Http = [entry] });

        Assert.Empty(grants.Http);
        Assert.Equal(ProblemCodes.GrantUnrequested, Assert.Single(grants.Warnings).Code);
    }

    [Fact]
    public void The_word_opens_hosts_alone()
    {
        var capabilities = new CapabilityRequests(Requested.Exec, new HttpRequestSpec(["api.example.test"], AnyGrantedHost: true), Requested.Env);

        var grants = GrantResolver.Resolve(Manifest(capabilities), new PluginGrant { Exec = ["curl"], Env = ["OTHER_TOKEN"] });

        Assert.Empty(grants.Exec);
        Assert.Empty(grants.Env);
        Assert.Equal(2, grants.Warnings.Count);
    }

    [Fact]
    [Critical(Family.R5, "declaration then grant")]
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

    [Fact]
    [Critical(Family.R2, "a handler's door opens only granted families")]
    public void The_api_families_granted_resolve_once_to_the_operations_a_door_opens()
    {
        var requested = new CapabilityRequests(null, null, null, new ApiRequestSpec([OperationFamilies.Read, OperationFamilies.WorkItem]));

        var none = GrantResolver.Resolve(Manifest(requested), null);
        Assert.Empty(none.Api);
        Assert.Empty(none.ApiOperations);

        var all = GrantResolver.Resolve(Manifest(requested), new PluginGrant { Api = ["*"] });
        Assert.Equal([OperationFamilies.Read, OperationFamilies.WorkItem], all.Api);
        Assert.Contains(Operations.CampaignList, all.ApiOperations);
        Assert.Contains(Operations.WorkItemCreate, all.ApiOperations);
        Assert.DoesNotContain(Operations.CampaignCreate, all.ApiOperations);
        Assert.DoesNotContain(Operations.WorkItemResume, all.ApiOperations);
        Assert.DoesNotContain(Operations.SystemInfo, all.ApiOperations);
        Assert.Empty(all.Warnings);

        var some = GrantResolver.Resolve(Manifest(requested), new PluginGrant { Api = [OperationFamilies.Read, OperationFamilies.Campaign] });
        Assert.Equal([OperationFamilies.Read], some.Api);
        Assert.DoesNotContain(Operations.CampaignCreate, some.ApiOperations);
        Assert.DoesNotContain(Operations.WorkItemCreate, some.ApiOperations);
        Assert.Equal("grants.api[1]", Assert.Single(some.Warnings).Path);
    }

    [Fact]
    public void An_api_grant_over_a_manifest_that_requests_none_grants_nothing()
    {
        var grants = GrantResolver.Resolve(Manifest(Requested), new PluginGrant { Api = ["*", OperationFamilies.Read] });

        Assert.Empty(grants.Api);
        Assert.Empty(grants.ApiOperations);
        Assert.Equal("grants.api[1]", Assert.Single(grants.Warnings).Path);
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
