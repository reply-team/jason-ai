using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

public class WorkItemEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_created_item_comes_back_in_snake_case_and_ready_to_run()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);

        var (status, body) = await api.PostAsync(
            Operations.WorkItemCreate,
            new { CampaignId = campaign.Id, Kind = "ai_role", Role = "researcher" },
            Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"status\":\"created\"", body, StringComparison.Ordinal);
        Assert.Contains("\"eligible\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"ai_role\"", body, StringComparison.Ordinal);
        Assert.Contains("\"attempt_count\":0", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_without_a_campaign_is_a_validation_failure_that_names_the_field()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.WorkItemCreate,
            new { Kind = "ai_role", Role = "researcher" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("campaign_id", error.Details![0].Field);
    }

    [Fact]
    public async Task A_property_written_twice_anywhere_in_the_body_is_refused_as_an_invalid_request()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);

        // The duplicate sits inside the work item's context, which the serializer hands on as a node without
        // reading it, so the first read of that node was deep inside the validator — a deterministic fault in the
        // caller's own body coming back as a retryable failure of the runtime.
        const string request = """
            {"campaign_id":"CAMPAIGN","kind":"provider_op","operation":"campaign.get",
             "context":{"input":{"campaign":{"external_id":"a","external_id":"b"}}}}
            """;

        var (status, body) = await api.PostRawAsync(
            Operations.WorkItemCreate,
            request.Replace("CAMPAIGN", campaign.Id, StringComparison.Ordinal),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, JasonJson.Options)!.Error;
        Assert.Equal("invalid_request", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("external_id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_wrong_in_twenty_thousand_places_answers_with_a_bounded_list_and_a_count()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var input = new JsonObject();
        for (var index = 0; index < 20_000; index++)
        {
            input["unknown_" + index.ToString(CultureInfo.InvariantCulture)] = index;
        }

        var (status, body) = await api.PostAsync(
            Operations.WorkItemCreate,
            new
            {
                CampaignId = campaign.Id,
                Kind = "provider_op",
                Operation = "campaign.get",
                Context = new JsonObject { ["input"] = input },
            },
            Ct);

        // Twenty thousand pointers say nothing the first fifty do not, and cost megabytes to say it.
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, JasonJson.Options)!.Error;
        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(50, error.Details!.Count);

        // One count of how many there were, said once. The message used to carry two — the problems the sentence
        // did not name, and the details the answer did not list — which disagree, being counted from different
        // places, and left a reader working out which number was about what.
        Assert.Contains("19995 more", error.Message, StringComparison.Ordinal);
        Assert.Contains("first 50 are listed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("19950", error.Message, StringComparison.Ordinal);
        Assert.True(error.Message.Length < 500, $"the message is {error.Message.Length} characters long.");
        Assert.True(body.Length < 32 * 1024, $"the answer is {body.Length} bytes long.");
    }

    [Fact]
    public async Task An_unknown_item_is_a_not_found_envelope()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.WorkItemGet,
            new { WorkItemId = "wi_01JASONNOTHERE" },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("work_item_not_found", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task A_listing_takes_an_array_of_statuses()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        await CreateAsync(api, campaign);

        var page = await api.PostOkAsync<Page<WorkItemSummaryDto>>(
            Operations.WorkItemList,
            new { CampaignId = campaign.Id, Status = new[] { "failed", "expired" } },
            Ct);

        Assert.Empty(page.Items);

        var all = await api.PostOkAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, new { CampaignId = campaign.Id }, Ct);
        Assert.Single(all.Items);
    }

    [Fact]
    public async Task Snapshots_are_asked_for_explicitly()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var item = await CreateAsync(api, campaign);
        await SeedAttemptAsync(api, item.Id);

        var plain = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { WorkItemId = item.Id }, Ct);
        Assert.Null(Assert.Single(plain.Attempts!).ContextSnapshot);

        var (_, body) = await api.PostAsync(Operations.WorkItemGet, new { WorkItemId = item.Id, IncludeSnapshots = true }, Ct);
        Assert.Contains("\"context_snapshot\":{", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_patch_answers_with_the_item_it_changed()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var item = await CreateAsync(api, campaign);

        var updated = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            new { WorkItemId = item.Id, Priority = 5, Set = new { brief = "find the founders" } },
            Ct);

        Assert.Equal(5, updated.Priority);
        Assert.Equal("find the founders", (string?)updated.Context["brief"]);
    }

    [Fact]
    public async Task Cancelling_twice_answers_the_same_thing_twice()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var item = await CreateAsync(api, campaign);

        var first = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCancel, new { WorkItemId = item.Id, Reason = "not needed" }, Ct);
        var second = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCancel, new { WorkItemId = item.Id }, Ct);

        Assert.Equal(WorkItemStatus.Cancelled, first.Status);
        Assert.Equal(WorkItemStatus.Cancelled, second.Status);
        Assert.Equal(first.FinishedAt, second.FinishedAt);
    }

    [Fact]
    public async Task A_succeeded_item_refuses_to_be_cancelled()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var item = await CreateAsync(api, campaign);
        await FinishAsync(api, item.Id);

        var error = await api.PostErrorAsync(Operations.WorkItemCancel, new { WorkItemId = item.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("workitem_terminal", error.Code);
    }

    [Fact]
    public async Task The_chronicle_can_be_read_by_work_item()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var item = await CreateAsync(api, campaign);
        var other = await CreateAsync(api, campaign);

        var page = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { WorkItemId = item.Id }, Ct);

        var entry = Assert.Single(page.Items);
        Assert.Equal("workitem_created", entry.Kind);
        Assert.Equal(item.Id, entry.WorkItemId);
        Assert.Equal(campaign.Id, entry.CampaignId);
        Assert.NotEqual(other.Id, entry.WorkItemId);
    }

    [Fact]
    public async Task Archiving_the_campaign_over_http_cancels_the_work_it_still_had()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await ActiveCampaignAsync(api);
        var item = await CreateAsync(api, campaign);

        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { CampaignId = campaign.Id, Reason = "done" }, Ct);

        var reloaded = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { WorkItemId = item.Id }, Ct);
        Assert.Equal(WorkItemStatus.Cancelled, reloaded.Status);
    }

    private static async Task<CampaignDto> ActiveCampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm founders" }, Ct);
        return await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { CampaignId = campaign.Id }, Ct);
    }

    private static Task<WorkItemDto> CreateAsync(RuntimeApiFixture api, CampaignDto campaign) =>
        api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { CampaignId = campaign.Id, Kind = "ai_role", Role = "researcher", Context = new { brief = "as claimed" } },
            Ct);

    /// <summary>Straight into the database behind the runtime: the dispatcher that would claim an item arrives later in this wave.</summary>
    private static async Task SeedAttemptAsync(RuntimeApiFixture api, string workItemId)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == workItemId, Ct);
        item.Status = WorkItemStatus.Processing;
        db.Attempts.Add(new Attempt
        {
            PublicId = PublicId.New("att"),
            WorkItem = item,
            Number = 1,
            Command = item.Kind,
            Status = AttemptStatus.Running,
            ContextSnapshot = item.Context.DeepClone().AsObject(),
            ClaimedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LockUntil = DateTime.UtcNow.AddHours(1),
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task FinishAsync(RuntimeApiFixture api, string workItemId)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == workItemId, Ct);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(Ct);
    }
}
