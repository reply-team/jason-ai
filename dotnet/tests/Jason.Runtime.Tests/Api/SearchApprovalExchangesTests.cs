using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A paid search of the provider's people waiting on a person, as every place that shows an approval reads it: a first
/// search under a definition that includes some people and leaves others out, beside a top-up of the audience it made;
/// each parked by the runtime's own claim and read whole, and the Work items page's reads of the first; then the top-up
/// approved and what waits read again.
/// </summary>
/// <remarks>
/// The plugin is a stand-in named <c>reply</c> that declares a definition in the shape of the Reply package's own
/// (terms to include or exclude, Reply's headcount bands), so a screen reads a definition a person would meet. Nothing is
/// searched: the claim parks both searches before any plugin runs.
/// </remarks>
public class SearchApprovalExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly object Ada = new { type = "human", id = "ada" };
    private static readonly string[] Now = ["processing", "scheduled", "awaiting_approval"];
    private static readonly string[] Planned = ["created"];
    private static readonly string[] Done = ["succeeded", "failed", "cancelled", "expired"];

    private static JsonObject Definition => new()
    {
        ["jobTitles"] = new JsonArray(
            Term("Head of Operations", "include"),
            Term("Operations Director", "include"),
            Term("Operations Intern", "exclude")),
        ["contactLocations"] = new JsonArray(Term("Portugal", "include")),
        ["industries"] = new JsonArray(Term("Logistics", "include"), Term("Staffing and Recruiting", "exclude")),
        ["companySizes"] = new JsonArray("fifty", "twoHundred"),
    };

    [Fact]
    public async Task A_search_and_a_top_up_wait_read_whole_with_their_work_items_and_the_top_up_is_approved()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        Route(api);
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring launch", actor = Ada }, Ct)).Id;
        // A draft campaign's searches wait unclaimed; started, its first search is claimed and parked for the person.
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign, actor = Ada }, Ct);

        var firstItem = await SearchAsync(api, campaign, new JsonObject
        {
            ["definition"] = Definition,
            ["count"] = 50,
            ["people_per_company"] = 2,
            ["include_without_email"] = false,
            ["find_phones"] = true,
        });
        Assert.Empty(await ClaimAsync(api));
        var topUp = await SearchAsync(api, campaign, new JsonObject { ["audience"] = 1, ["count"] = 20 });
        Assert.Empty(await ClaimAsync(api));

        // What waits, across every campaign and in the campaign's own chat.
        var pending = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "search-pending", new { }, Ct);
        Assert.Equal(
            [(firstItem, "audience.search", (string?)null), (topUp, "audience.search", null)],
            pending.Items.Select(a => (a.WorkItemId, a.Operation, a.ContactName)));
        var inCampaign = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "search-campaign-pending", new { status = "pending", campaign_id = campaign, limit = 50 }, Ct);
        Assert.Equal(pending.Items.Select(a => a.Id), inCampaign.Items.Select(a => a.Id));
        Assert.Empty((await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "search-campaign-none", new { status = "pending", campaign_id = campaign, limit = 50 }, Ct)).Items);

        // Each read whole: the summary rendered from the input, and the values it highlights.
        var first = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "search-first", new { approval_id = pending.Items[0].Id }, Ct);
        Assert.StartsWith("Search the provider for up to 50 people", (string?)first.Preview["summary"]);
        var highlights = first.Preview["highlights"]!.AsObject();
        Assert.True(JsonNode.DeepEquals(Definition, highlights["/definition"]), highlights.ToJsonString());
        Assert.Equal((2, false, true), ((int)highlights["/args/people_per_company"]!, (bool)highlights["/args/include_without_email"]!, (bool)highlights["/args/find_phones"]!));
        var second = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "search-top-up", new { approval_id = pending.Items[1].Id }, Ct);
        var topped = second.Preview["highlights"]!.AsObject();
        Assert.Equal(1, (int)topped["/args/audience"]!);
        Assert.True(JsonNode.DeepEquals(Definition, topped["/definition"]), "a top-up is searched by its audience's definition");

        // The first search's work item, as its panel on the Work items page reads it.
        var item = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "search-first", new { work_item_id = firstItem }, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, item.Status);

        // The Work items page: the Now tab and its counts, the first search's history and the approval it waits for.
        var listed = await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "search-now", new { status = Now, order = "created", limit = 50 }, Ct);
        Assert.Equal([firstItem, topUp], listed.Items.Select(row => row.Id));
        Assert.Equal(2, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "search-now", new { status = Now, order = "created" }, Ct)).Total);
        Assert.Equal(0, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "search-planned", new { status = Planned, order = "due" }, Ct)).Total);
        Assert.Equal(0, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "search-done", new { status = Done, order = "finished" }, Ct)).Total);
        await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "search-first", new { work_item_id = firstItem }, Ct);
        var waitsFor = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "search-first-item", new { work_item_id = firstItem, statuses = new[] { "pending" } }, Ct);
        Assert.Equal([first.Id], waitsFor.Items.Select(a => a.Id));

        // The top-up approved; what waits is the first search alone.
        var approved = await api.RecordAsync<ApprovalDto>(Operations.ApprovalApprove, "search-top-up", new { approval_id = second.Id, actor = Ada }, Ct);
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        var after = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "search-after-top-up", new { }, Ct);
        Assert.Equal([first.Id], after.Items.Select(a => a.Id));
    }

    private static JsonObject Term(string value, string type) => new() { ["value"] = value, ["type"] = type };

    /// <summary>A search as the planner creates one: its input in the work item's context.</summary>
    private static async Task<string> SearchAsync(RuntimeApiFixture api, string campaign, JsonObject input)
    {
        var context = new JsonObject { ["input"] = input };
        return (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new
        {
            campaign_id = campaign,
            kind = "provider_op",
            operation = "audience.search",
            context,
            actor = new { type = "role", id = "planner" },
        }, Ct)).Id;
    }

    /// <summary>One scan of the claim, as the dispatcher runs it.</summary>
    private static async Task<IReadOnlyList<ClaimedWork>> ClaimAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<Claimer>().ClaimAsync(db, 4, Ct);
    }

    /// <summary>The stand-in that searches, routed for every campaign under an account of its own.</summary>
    private static void Route(RuntimeApiFixture api)
    {
        var binding = new JsonObject { ["profile"] = "team" };
        var bindingSchema = JsonNode.Parse("""{ "type": "object", "properties": { "profile": { "type": "string" } } }""")!.AsObject();
        var plugin = TestPlugins.Loaded(
            "reply",
            ["audience.search", "audience.search_status", "audience.search_cancel", "audience.preview"],
            binding: bindingSchema) with { Root = Path.Combine(api.Paths.PluginsDirectory, "reply") };
        var definition = new AudienceDefinition(DefinitionSchema(), new Dictionary<string, DefinitionKey>(StringComparer.Ordinal));
        var plugins = api.Resolve<PluginRegistry>();
        plugins.Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Nine.UtcDateTime, SnapshotSource.Startup,
                [plugin with { Manifest = plugin.Manifest with { AudienceDefinition = definition } }]),
            new ReloadReport(Nine.UtcDateTime, SnapshotSource.Startup, Activated: true, [], []));
        api.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Nine,
            plugins.Snapshot.Id,
            new RouteSet(new Route("reply", binding, BindingIdentity.Of(binding)), RouteSet.Empty.Operations),
            new Dictionary<string, RouteSet>(StringComparer.Ordinal)));
    }

    private static JsonObject DefinitionSchema() => JsonNode.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "jobTitles": { "description": "The person's job title, each to include or exclude.", "type": "array", "items": { "$ref": "#/$defs/term" } },
            "contactLocations": { "description": "Where the person is, each to include or exclude.", "type": "array", "items": { "$ref": "#/$defs/term" } },
            "industries": { "description": "The industries the company is in, each to include or exclude.", "type": "array", "items": { "$ref": "#/$defs/term" } },
            "companySizes": { "description": "The company's headcount, as the provider's bands.", "type": "array", "items": { "enum": ["ten", "fifty", "twoHundred", "fiveHundred"] } }
          },
          "$defs": {
            "term": {
              "type": "object",
              "additionalProperties": false,
              "required": ["value", "type"],
              "properties": { "value": { "type": "string" }, "type": { "enum": ["include", "exclude"] } }
            }
          }
        }
        """)!.AsObject();
}
