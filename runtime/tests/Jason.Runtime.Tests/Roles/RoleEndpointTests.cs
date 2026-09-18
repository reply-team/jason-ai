using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Roles;

public class RoleEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_registry_answers_the_builtin_roster_in_snake_case()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostAsync(Operations.RoleList, null, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"name\":\"manager\"", body, StringComparison.Ordinal);
        Assert.Contains("\"entry_command\":[]", body, StringComparison.Ordinal);
        Assert.Contains("\"profile_defaults\":{}", body, StringComparison.Ordinal);
        Assert.Contains("\"deliverability-specialist\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_role_added_over_http_is_listed_afterwards()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostAsync(
            Operations.RoleAdd,
            new { Name = "fake", EntryCommand = new[] { "node", "host.js" }, ProfileDefaults = new { Model = "sonnet" } },
            Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"builtin\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"entry_command\":[\"node\",\"host.js\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"profile_defaults\":{\"model\":\"sonnet\"}", body, StringComparison.Ordinal);

        var page = await api.PostOkAsync<Page<RoleDto>>(Operations.RoleList, null, Ct);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal("fake", page.Items[^1].Name);
    }

    [Fact]
    public async Task A_name_already_in_the_roster_is_a_conflict()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.RoleAdd, new { Name = "planner" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("role_exists", error.Code);
        Assert.False(error.Retryable);
    }

    /// <summary>
    /// The one verb that changes a role, reachable under its own name. Roles have no general update verb, and
    /// this wave is not the one that gives them one.
    /// </summary>
    [Fact]
    public async Task Set_profile_over_http_moves_the_policy_and_refuses_a_profile_nothing_answers()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.RoleSetProfile,
            new { Name = "researcher", ExecutionProfile = "nothing-answers-this" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("unknown", detail.Code);

        // Clearing needs no profile to exist, so it is the half of the verb a runtime with no profiles can prove.
        var (status, body) = await api.PostAsync(Operations.RoleSetProfile, new { Name = "researcher", ExecutionProfile = (string?)null }, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"execution_profile\":null", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_profile_on_a_role_nobody_registered_is_a_404()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.RoleSetProfile,
            new { Name = "nobody-registered-this", ExecutionProfile = (string?)null },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("role_not_found", error.Code);
    }

    [Fact]
    public async Task A_role_without_a_name_names_the_field()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.RoleAdd, new { }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("name", detail.Field);
    }
}
