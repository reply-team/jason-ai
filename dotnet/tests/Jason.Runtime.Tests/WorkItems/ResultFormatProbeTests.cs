using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// A work item's <c>result_format</c> is a schema whoever made the item wrote, and its patterns are probed when it is
/// written, as a plugin's binding patterns are when its package loads: a pattern that cannot finish matching the probes
/// on a warm engine, twice, is refused there as <c>schema_pattern_invalid</c> — final, because the schema has to change —
/// rather than left to time out against the answer at the end of the work.
/// </summary>
public sealed class ResultFormatProbeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Format() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["code"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[A-Z]{2}-[0-9]{2}$" } },
    };

    [Fact]
    public async Task A_result_format_whose_pattern_never_finishes_the_probe_is_refused_when_the_item_is_created()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db, TimingOut.Always()).CreateAsync(Create(campaign.PublicId) with { ResultFormat = Format() }, Ct));

        Assert.Equal((400, "validation_failed", false), (error.StatusCode, error.Code, error.Retryable));
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("result_format/properties/code/pattern", "schema_pattern_invalid"), (detail.Field, detail.Code));
        Assert.Empty(await db.WorkItems.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task A_result_format_whose_pattern_times_out_once_is_measured_warm_and_the_item_is_created()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);
        var timing = new TimingOut(1);

        var item = await NewService(db, timing).CreateAsync(Create(campaign.PublicId) with { ResultFormat = Format() }, Ct);

        Assert.Equal(WorkItemKind.AiRole, item.Kind);
        Assert.True(timing.TimedOut >= 1, $"The probe never met the timing: {timing.Calls} calls.");
    }

    [Fact]
    public async Task A_patch_that_names_a_result_format_whose_pattern_never_finishes_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedItem(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db, TimingOut.Always()).UpdateAsync(Patch(item.PublicId) with { ResultFormat = Optional<JsonNode?>.Of(Format()) }, Ct));

        Assert.Equal((400, "validation_failed"), (error.StatusCode, error.Code));
        Assert.Equal(("result_format/properties/code/pattern", "schema_pattern_invalid"), (Assert.Single(error.Details!).Field, error.Details![0].Code));
    }

    /// <summary>
    /// A patch that does not name the result format probes nothing: what is already stored was probed when it was written.
    /// The item holds a patterned result format, so a stray probe of the stored one would meet the timing.
    /// </summary>
    [Fact]
    public async Task A_patch_that_does_not_name_the_result_format_probes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedItem(db, item => item.ResultFormat = Format());
        var timing = TimingOut.Always();

        await NewService(db, timing).UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(3) }, Ct);

        Assert.Equal(0, timing.Calls);
    }

    private static WorkItemService NewService(JasonDbContext db, PatternTiming timing)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings(), timing);
    }

    private static WorkItemCreateRequest Create(string campaignId) =>
        new(campaignId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static WorkItemUpdateRequest Patch(string workItemId) =>
        new(workItemId, null, null, default, default, default, default, default, default, default, default, null, null);

    private static Campaign Seed(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign();
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static WorkItem SeedItem(JasonDbContext db, Action<WorkItem>? configure = null)
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: configure);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
    }
}
