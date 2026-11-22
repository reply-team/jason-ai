using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A work item's entry over the Runtime API: only the runtime writes one, and the reads show it. The API's rule for a
/// member a request does not have is that it is skipped, at the top of a body, like any member the operation does not
/// take (<see cref="MemberMatchingTests"/>); so an <c>entry</c> a caller sends on <c>workitem.create</c> or
/// <c>workitem.update</c> is accepted and never reaches the column.
/// </summary>
public class WorkItemEntryApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_entry_a_caller_sends_on_create_is_skipped_and_never_reaches_the_column()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);

        var made = await PostOkAsync(
            api, Operations.WorkItemCreate, $$$"""{"campaign_id":"{{{campaign}}}","kind":"ai_role","role":"manager","entry":"deep_review","context":{"note":"look"}}""");

        Assert.True(made.ContainsKey("entry"), $"workitem.create answered no entry member: {made.ToJsonString()}");
        Assert.Null(made["entry"]);
        Assert.Null(await StoredEntryAsync(api, (string)made["id"]!));
    }

    [Fact]
    public async Task An_entry_a_caller_sends_on_update_is_skipped_and_never_reaches_the_column()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var made = await PostOkAsync(api, Operations.WorkItemCreate, $$"""{"campaign_id":"{{campaign}}","kind":"ai_role","role":"manager"}""");
        var id = (string)made["id"]!;

        var updated = await PostOkAsync(api, Operations.WorkItemUpdate, $$"""{"work_item_id":"{{id}}","entry":"start","priority":3}""");

        Assert.Equal(3, (int?)updated["priority"]);
        Assert.Null(updated["entry"]);
        Assert.Null(await StoredEntryAsync(api, id));
    }

    /// <summary>
    /// <c>workitem.get</c> and <c>workitem.list</c> say every item's entry: the one the runtime created a check-in with,
    /// and null for an item that has none, as a member that is there rather than one left out.
    /// </summary>
    [Fact]
    public async Task Get_and_list_show_the_entry_and_null_where_an_item_has_none()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var plain = (string)(await PostOkAsync(api, Operations.WorkItemCreate, $$"""{"campaign_id":"{{campaign}}","kind":"ai_role","role":"researcher"}"""))["id"]!;
        var checkIn = await CheckInAsync(api, campaign, "quick_look");

        var gotCheckIn = await PostOkAsync(api, Operations.WorkItemGet, $$"""{"work_item_id":"{{checkIn}}"}""");
        var gotPlain = await PostOkAsync(api, Operations.WorkItemGet, $$"""{"work_item_id":"{{plain}}"}""");
        Assert.Equal("quick_look", (string?)gotCheckIn["entry"]);
        Assert.True(gotPlain.ContainsKey("entry"), $"workitem.get left the entry out: {gotPlain.ToJsonString()}");
        Assert.Null(gotPlain["entry"]);

        var listed = (await PostOkAsync(api, Operations.WorkItemList, $$"""{"campaign_id":"{{campaign}}"}"""))["items"]!.AsArray();
        var byId = listed.ToDictionary(item => (string)item!["id"]!, item => item!.AsObject(), StringComparer.Ordinal);
        Assert.Equal("quick_look", (string?)byId[checkIn]["entry"]);
        Assert.True(byId[plain].ContainsKey("entry"), $"workitem.list left the entry out: {byId[plain].ToJsonString()}");
        Assert.Null(byId[plain]["entry"]);
    }

    /// <summary>A check-in the runtime itself creates with an entry, through the one internal writer.</summary>
    private static async Task<string> CheckInAsync(RuntimeApiFixture api, string campaignId, string entry)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var items = scope.ServiceProvider.GetRequiredService<WorkItemService>();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignId, Ct);
        var item = await ManagerCheckIn.CreateAsync(items, db, campaign, cause: null, new ManagerOptions(), Ct, entry);
        await db.SaveChangesAsync(Ct);
        return item.PublicId;
    }

    private static async Task<string?> StoredEntryAsync(RuntimeApiFixture api, string workItemId)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return (await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == workItemId, Ct)).Entry;
    }

    private static async Task<JsonObject> PostOkAsync(RuntimeApiFixture api, string operation, string body)
    {
        var (status, text) = await api.PostRawAsync(operation, body, Ct);
        Assert.True(status == HttpStatusCode.OK, $"{operation} answered {(int)status}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await PostOkAsync(api, Operations.CampaignCreate, """{"name":"Work"}""");
        await PostOkAsync(api, Operations.CampaignStart, $$"""{"campaign_id":"{{campaign["id"]}}"}""");
        return (string)campaign["id"]!;
    }
}
