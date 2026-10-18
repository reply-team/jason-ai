using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// A listing names the campaign and the person, so no reader has to resolve ids to know what it is looking at.
/// </summary>
public class SummaryNamesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_approval_carries_the_campaigns_name_and_the_persons()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var approvalId = await SeedApprovalAsync(api, "Ada", "Lovelace");

        var page = await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, null, Ct);
        var one = Assert.Single(page.Items);
        Assert.Equal("Autumn outreach", one.CampaignName);
        Assert.Equal("Ada Lovelace", one.ContactName);

        var whole = await api.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = approvalId }, Ct);
        Assert.Equal("Autumn outreach", whole.CampaignName);
        Assert.Equal("Ada Lovelace", whole.ContactName);
    }

    [Fact]
    public async Task A_person_with_one_name_or_none_is_named_by_what_there_is()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await SeedApprovalAsync(api, null, "Hopper");
        await SeedApprovalAsync(api, null, null);

        var page = await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, null, Ct);
        Assert.Equal(["Hopper", null], page.Items.Select(a => a.ContactName).OrderDescending());
    }

    [Fact]
    public async Task A_decision_carries_the_same_two_names()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach", now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        item.Status = WorkItemStatus.Processing;
        item.Contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Grace", LastName = "Hopper", CreatedAt = Noon, UpdatedAt = Noon };
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        db.Decisions.Add(new Decision { PublicId = PublicId.New("dec"), Campaign = campaign, WorkItem = item, Attempt = attempt, Question = "Keep calling this account?", Status = DecisionStatus.Pending, RaisedAt = Noon });
        await db.SaveChangesAsync(Ct);

        var page = await api.PostOkAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, null, Ct);
        var one = Assert.Single(page.Items);
        Assert.Equal("Autumn outreach", one.CampaignName);
        Assert.Equal("Grace Hopper", one.ContactName);
    }

    internal static async Task<string> SeedApprovalAsync(RuntimeApiFixture api, string? first, string? last)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach", now: Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        item.Status = WorkItemStatus.AwaitingApproval;
        item.Contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = first, LastName = last, CreatedAt = Noon, UpdatedAt = Noon };
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        var approval = new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = "campaign.enroll",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "campaign.enroll" },
            SubjectHash = "sha256:subject",
            Preview = new JsonObject { ["intent"] = "Put one person into a campaign at the provider." },
            PluginId = "stand-in-provider",
            BindingIdentity = "sha256:account",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_one",
            RoutingSnapshotId = "rts_one",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync(Ct);
        return approval.PublicId;
    }
}
