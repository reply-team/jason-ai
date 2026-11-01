using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>One call answers "what was decided": several statuses at once, or one, or pending when none is named.</summary>
public class ListStatusesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Several_statuses_are_one_listing_and_one_status_still_works()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var pending = await SummaryNamesTests.SeedApprovalAsync(api, "Ada", "Lovelace");
        var approved = await SummaryNamesTests.SeedApprovalAsync(api, "Grace", "Hopper");
        var rejected = await SummaryNamesTests.SeedApprovalAsync(api, "Edsger", "Dijkstra");
        await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = approved, actor = new { type = "human", id = "ada" } }, Ct);
        await api.PostOkAsync<ApprovalDto>(Operations.ApprovalReject, new { approval_id = rejected, actor = new { type = "human", id = "ada" } }, Ct);

        var decided = await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { statuses = new[] { "approved", "rejected" } }, Ct);
        Assert.Equal(new[] { approved, rejected }.Order(StringComparer.Ordinal), decided.Items.Select(a => a.Id).Order(StringComparer.Ordinal));

        var one = await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { status = "rejected" }, Ct);
        Assert.Equal([rejected], one.Items.Select(a => a.Id));

        var defaulted = await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, null, Ct);
        Assert.Equal([pending], defaulted.Items.Select(a => a.Id));

        var error = await api.PostErrorAsync(Operations.ApprovalList, new { statuses = new[] { "approved", "nonsense" } }, System.Net.HttpStatusCode.BadRequest, Ct);
        Assert.Equal("invalid_request", error.Code);
    }

    [Fact]
    public async Task Decisions_list_the_same_way()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var empty = await api.PostOkAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, new { statuses = new[] { "answered", "cancelled" } }, Ct);
        Assert.Empty(empty.Items);
    }
}
