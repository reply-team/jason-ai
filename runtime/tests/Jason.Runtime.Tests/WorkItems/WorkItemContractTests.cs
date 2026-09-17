using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// What the published operation contracts make of a <c>provider_op</c> work item. A planner learns here, while
/// the item is being written, what it would otherwise learn hours later from a failed attempt.
/// </summary>
public class WorkItemContractTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_operation_this_build_publishes_no_contract_for_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId, "contacts.enroll"), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("unknown", detail.Code);
        Assert.Contains("campaign.get", detail.Message, StringComparison.Ordinal);
        Assert.Contains("list_membership.add", detail.Message, StringComparison.Ordinal);
        Assert.Contains("campaign.enroll", detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("contacts.enroll", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_the_vocabulary_refuses_is_reported_once_and_not_measured_against_the_catalog()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId, "Campaign.Get"), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task An_operation_whose_arguments_are_all_optional_needs_no_input()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(ProviderOp(campaign.PublicId, "campaign.get"), Ct);

        Assert.Equal("campaign.get", item.Operation);
        Assert.Empty(item.Context);
    }

    [Fact]
    public async Task An_operation_that_declares_required_arguments_is_refused_without_an_input()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId, "list_membership.add"), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
        Assert.Contains("list", detail.Message, StringComparison.Ordinal);
        Assert.Contains("channel", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_input_that_is_explicitly_nothing_reads_as_no_arguments_at_all()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with { Context = new JsonObject { ["input"] = null } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task Arguments_the_contract_accepts_are_kept_under_the_reserved_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(
            ProviderOp(campaign.PublicId, "list_membership.add") with
            {
                Context = new JsonObject { ["brief"] = "the weekly import", ["input"] = Membership() },
            },
            Ct);

        Assert.Equal("list_membership.add", item.Operation);
        Assert.Equal("lst_7", (string?)item.Context["input"]!["list"]!["external_id"]);
        Assert.Equal("the weekly import", (string?)item.Context["brief"]);
    }

    [Fact]
    public async Task An_argument_of_the_wrong_type_is_reported_at_the_pointer_that_holds_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Membership();
        input["channel"] = 7;

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/channel", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task Every_problem_in_the_arguments_is_reported_at_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with
                {
                    Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = 7, ["hurry"] = true } },
                },
                Ct));

        Assert.Equal(3, error.Details!.Count);
        Assert.All(error.Details, detail => Assert.Equal("invalid", detail.Code));
        Assert.Contains(error.Details, detail => detail.Field == "context.input/list");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/channel");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/hurry");
    }

    [Fact]
    public async Task An_input_that_is_not_an_object_is_reported_against_the_input_itself()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "campaign.get") with { Context = new JsonObject { ["input"] = "cmp_provider_7" } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task The_enrollment_contract_insists_on_the_three_choices_nobody_may_default()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "campaign.enroll") with
                {
                    Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = "email" } },
                },
                Ct));

        Assert.Equal(3, error.Details!.Count);
        Assert.Contains(error.Details, detail => detail.Field == "context.input/collision");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/start");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/first_touch");
    }

    [Fact]
    public async Task An_ai_role_item_carries_whatever_its_planner_wrote()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(
            new WorkItemCreateRequest(
                campaign.PublicId,
                WorkItemKind.AiRole,
                "researcher",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new JsonObject { ["input"] = "whatever the brief says" },
                null,
                null,
                null),
            Ct);

        Assert.Equal("whatever the brief says", (string?)item.Context["input"]);
    }

    [Fact]
    public async Task Rewriting_the_arguments_is_measured_against_the_items_own_operation()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);
        var replacement = Membership();
        replacement["channel"] = 7;

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = replacement } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/channel", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task Arguments_the_contract_accepts_replace_the_ones_the_item_held()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);
        var replacement = Membership();
        replacement["list"]!["external_id"] = "lst_9";

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = replacement } }, Ct);

        Assert.Equal("lst_9", (string?)updated.Context["input"]!["list"]!["external_id"]);
    }

    [Fact]
    public async Task Removing_the_arguments_an_operation_needs_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Unset = ["input"] }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task Writing_nothing_into_the_reserved_key_is_the_same_as_taking_the_arguments_away()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = null } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task An_unset_wins_over_a_set_of_the_same_key_exactly_as_the_patch_applies_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(
                Patch(item.PublicId) with { Set = new JsonObject { ["input"] = Membership() }, Unset = ["input"] },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task A_patch_that_leaves_the_arguments_alone_is_not_measured_again()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", input: null);
        var service = NewService(db);

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(5) }, Ct);

        Assert.Equal(5, updated.Priority);
    }

    [Fact]
    public async Task An_ai_role_items_context_is_still_its_own_business()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        var service = NewService(db);

        var updated = await service.UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["input"] = "a sentence, not arguments" } },
            Ct);

        Assert.Equal("a sentence, not arguments", (string?)updated.Context["input"]);
    }

    [Fact]
    public async Task An_item_whose_operation_this_build_no_longer_publishes_is_left_to_fail_at_the_claim()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "contacts.enroll", input: null);
        var service = NewService(db);

        var updated = await service.UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["input"] = new JsonObject { ["campaign"] = "cmp_provider_7" } } },
            Ct);

        Assert.Equal("cmp_provider_7", (string?)updated.Context["input"]!["campaign"]);
    }

    private static WorkItemService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static WorkItemCreateRequest ProviderOp(string? campaignId, string operation) =>
        new(campaignId, WorkItemKind.ProviderOp, null, operation, null, null, null, null, null, null, null, null, null, null, null, null);

    private static WorkItemUpdateRequest Patch(string workItemId) =>
        new(workItemId, null, null, default, default, default, default, default, default, default, null, null);

    /// <summary>A complete set of <c>list_membership.add</c> arguments, as a planner would write them.</summary>
    private static JsonObject Membership() => new()
    {
        ["list"] = new JsonObject { ["external_id"] = "lst_7" },
        ["channel"] = "email",
    };

    private static Campaign Seed(JasonDbContext db, Campaign campaign)
    {
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static WorkItem SeedProviderOp(JasonDbContext db, string operation, JsonObject? input)
    {
        var item = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(), operation);
        if (input is not null)
        {
            item.Context = new JsonObject { ["input"] = input };
        }

        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
    }
}
