using Jason.Contracts;
using Jason.Contracts.Api;

namespace Jason.Contracts.Tests;

// The canonical operation contracts live in Jason.Contracts.Operations, whose name shadows the operation-name
// class from inside this namespace, so that one name is bound here explicitly.
using Operations = Jason.Contracts.Api.Operations;

public class ApiVersionTests
{
    [Fact]
    public void Api_version_is_v1() => Assert.Equal("v1", ApiVersion.Current);

    [Fact]
    public void Operation_routes_follow_the_rpc_convention() =>
        Assert.Equal("/v1/system.info", Operations.Route(Operations.SystemInfo));

    [Fact]
    public void Build_version_starts_with_the_wave_baseline() =>
        Assert.StartsWith("0.1.0", JasonVersion.Current, StringComparison.Ordinal);
}
