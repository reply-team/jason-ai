using Jason.Contracts;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests;

// The canonical operation contracts live in Jason.Contracts.Operations, whose name shadows the operation-name
// class from inside this namespace, so that one name is bound here explicitly.
using Operations = Jason.Contracts.Api.Operations;

public class ApiVersionTests
{
    [Fact]
    public void Api_version_is_v1() => Assert.Equal("v1", ApiVersion.Current);

    [Fact]
    [Critical(Family.R2, "every route is POST /v1/noun.verb")]
    public void Operation_routes_follow_the_rpc_convention() =>
        Assert.Equal("/v1/system.info", Operations.Route(Operations.SystemInfo));

    [Fact]
    public void Build_version_starts_with_the_version_this_tree_carries() =>
        Assert.StartsWith(TreeVersion.Prefix(), JasonVersion.Current, StringComparison.Ordinal);
}
