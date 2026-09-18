using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// <c>result_format</c> says what shape an answer must have, and the runtime holds a finished answer to it. That
/// only works if the declaration is something a machine can apply, so it is a schema in the dialect this build
/// publishes — the same one the operation contracts are written in, so there is one shape language here and not
/// two. A planner learns what its declaration is worth while it is writing the item, not hours later when an
/// executor's answer is refused for a reason nobody can act on.
/// </summary>
public class ResultFormatSchemaTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_result_format_that_is_not_an_object_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).CreateAsync(AiRole(campaign, JsonValue.Create("a short summary, please")), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("result_format", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task A_rule_this_dialect_does_not_publish_is_refused_rather_than_ignored()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);
        var format = new JsonObject { ["type"] = "object", ["shape"] = "summary" };

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).CreateAsync(AiRole(campaign, format), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.StartsWith("result_format", detail.Field, StringComparison.Ordinal);
        Assert.Contains("shape", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_schema_of_the_dialect_is_accepted_and_read_back()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);
        var format = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["findings"] = new JsonObject { ["type"] = "array" } },
            ["required"] = new JsonArray("findings"),
        };

        var item = await NewService(db).CreateAsync(AiRole(campaign, format), Ct);

        Assert.Equal(format.ToJsonString(), item.ResultFormat!.ToJsonString());
    }

    [Fact]
    public async Task An_item_may_still_ask_for_no_particular_shape()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);

        var item = await NewService(db).CreateAsync(AiRole(campaign, null), Ct);

        Assert.Null(item.ResultFormat);
    }

    private static WorkItemCreateRequest AiRole(string campaignId, JsonNode? resultFormat) =>
        new(campaignId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, resultFormat, null, null);

    private static string Seed(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign();
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign.PublicId;
    }

    private static WorkItemService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }
}
