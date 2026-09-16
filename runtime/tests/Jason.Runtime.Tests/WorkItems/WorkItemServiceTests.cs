using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

public class WorkItemServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_ai_role_item_in_an_active_campaign_is_created_eligible_and_journaled()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var item = await service.CreateAsync(Request(campaign.PublicId) with { Reason = "kickoff" }, Ct);

        Assert.StartsWith("wi_", item.Id, StringComparison.Ordinal);
        Assert.Equal(campaign.PublicId, item.CampaignId);
        Assert.Equal(WorkItemKind.AiRole, item.Kind);
        Assert.Equal("researcher", item.Role);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.True(item.Eligible);
        Assert.Equal(0, item.AttemptCount);
        Assert.Null(item.CurrentAttemptId);
        Assert.Equal(ActorType.Human, item.CreatedBy.Type);
        Assert.Null(item.CreatedBy.Id);
        Assert.Empty(item.Attempts!);
        Assert.Equal(Noon, item.CreatedAt);

        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(JournalKinds.WorkItemCreated, entry.Kind);
        Assert.Equal(item.Id, entry.WorkItemId);
        Assert.Equal(campaign.Id, entry.CampaignId);
        Assert.Null(entry.AttemptId);
        Assert.Equal("kickoff", entry.Reason);
        Assert.Equal("ai_role", (string?)entry.New!["kind"]);
        Assert.Equal("researcher", (string?)entry.New!["role"]);
    }

    [Fact]
    public async Task A_role_may_create_work_and_is_recorded_as_its_author()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var item = await service.CreateAsync(Request(campaign.PublicId) with { Actor = new ActorRef(ActorType.Role, "planner") }, Ct);

        Assert.Equal(ActorType.Role, item.CreatedBy.Type);
        Assert.Equal("planner", item.CreatedBy.Id);
    }

    [Fact]
    public async Task Work_created_by_a_running_executor_is_traceable_to_its_attempt()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var parent = WorkItemFactory.NewAiRole(campaign);
        var attempt = WorkItemFactory.NewAttempt(parent, 1, AttemptStatus.Running, Noon.UtcDateTime);
        db.WorkItems.Add(parent);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var item = await service.CreateAsync(Request(campaign.PublicId) with { Actor = new ActorRef(ActorType.Attempt, attempt.PublicId) }, Ct);

        Assert.Equal(ActorType.Attempt, item.CreatedBy.Type);
        Assert.Equal(attempt.PublicId, item.CreatedBy.Id);
    }

    [Fact]
    public async Task An_attempt_this_runtime_does_not_know_cannot_claim_to_have_asked_for_work()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(Request(campaign.PublicId) with { Actor = new ActorRef(ActorType.Attempt, "att_01JASONNOTHERE") }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("actor.id", detail.Field);
        Assert.Equal("unknown", detail.Code);
    }

    [Fact]
    public async Task A_draft_campaign_takes_work_that_is_not_yet_eligible()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign(status: CampaignStatus.Draft));
        var service = NewService(db, new FixedClock(Noon));

        var item = await service.CreateAsync(Request(campaign.PublicId), Ct);

        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.False(item.Eligible);
    }

    [Fact]
    public async Task An_archived_campaign_takes_no_more_work()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign(status: CampaignStatus.Archived));
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(campaign.PublicId), Ct));

        Assert.Equal("campaign_archived", error.Code);
    }

    [Fact]
    public async Task An_unknown_campaign_is_a_not_found()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(Request("cmp_01JASONNOTHERE"), Ct));

        Assert.Equal("campaign_not_found", error.Code);
    }

    [Fact]
    public async Task A_campaign_id_is_required()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("campaign_id", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task A_role_nobody_registered_cannot_be_given_work()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(campaign.PublicId) with { Role = "archivist" }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("role", detail.Field);
        Assert.Equal("unknown", detail.Code);
    }

    [Fact]
    public async Task A_provider_operation_carries_no_result_format()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId) with { ResultFormat = new JsonObject { ["type"] = "object" } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("result_format", detail.Field);
        Assert.Equal("not_allowed", detail.Code);
    }

    [Fact]
    public async Task A_provider_operation_without_an_operation_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(ProviderOp(campaign.PublicId) with { Operation = null }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Theory]
    [InlineData("Contacts.Enroll")]
    [InlineData("contacts..enroll")]
    [InlineData("1contacts.enroll")]
    [InlineData("contacts.enroll.")]
    public async Task An_operation_name_outside_the_vocabulary_is_refused(string operation)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(ProviderOp(campaign.PublicId) with { Operation = operation }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task An_ai_role_item_carries_no_operation()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(Request(campaign.PublicId) with { Operation = "contacts.enroll" }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("not_allowed", detail.Code);
    }

    [Fact]
    public async Task A_deadline_before_the_start_of_the_window_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(Request(campaign.PublicId) with { NotBefore = Noon.AddHours(2), DueAt = Noon.AddHours(1) }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("due_at", detail.Field);
        Assert.Equal("due_before_start", detail.Code);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(86401)]
    public async Task A_timeout_outside_the_allowed_range_is_refused(int seconds)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(campaign.PublicId) with { TimeoutSeconds = seconds }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("timeout_seconds", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task A_heartbeat_faster_than_the_floor_is_refused_while_zero_turns_it_off()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(campaign.PublicId) with { HeartbeatSeconds = 5 }, Ct));
        var detail = Assert.Single(error.Details!);
        Assert.Equal("heartbeat_seconds", detail.Field);
        Assert.Equal("invalid", detail.Code);

        var item = await service.CreateAsync(Request(campaign.PublicId) with { HeartbeatSeconds = 0 }, Ct);
        Assert.Equal(0, item.HeartbeatSeconds);
    }

    [Fact]
    public async Task More_attempts_than_the_ceiling_allows_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(campaign.PublicId) with { MaxAttempts = 11 }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("max_attempts", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task Work_about_somebody_outside_the_campaign_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var contact = NewContact(db);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(campaign.PublicId) with { ContactId = contact.PublicId }, Ct));

        Assert.Equal("contact_not_member", error.Code);
    }

    [Fact]
    public async Task Work_about_somebody_excluded_from_the_campaign_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var contact = NewContact(db);
        Enrol(db, campaign, contact, MembershipState.Excluded);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(campaign.PublicId) with { ContactId = contact.PublicId }, Ct));

        Assert.Equal("contact_not_member", error.Code);
    }

    [Fact]
    public async Task Work_about_a_member_carries_the_contact()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var contact = NewContact(db);
        Enrol(db, campaign, contact, MembershipState.Enrolled);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var item = await service.CreateAsync(Request(campaign.PublicId) with { ContactId = contact.PublicId }, Ct);

        Assert.Equal(contact.PublicId, item.ContactId);
    }

    [Fact]
    public async Task An_archived_contact_takes_no_new_work()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var contact = NewContact(db, archived: true);
        Enrol(db, campaign, contact, MembershipState.Enrolled);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(campaign.PublicId) with { ContactId = contact.PublicId }, Ct));

        Assert.Equal("contact_archived", error.Code);
    }

    [Fact]
    public async Task An_unknown_contact_is_a_not_found()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(Request(campaign.PublicId) with { ContactId = "cnt_01JASONNOTHERE" }, Ct));

        Assert.Equal("contact_not_found", error.Code);
    }

    [Fact]
    public async Task A_context_larger_than_a_role_can_read_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => service.CreateAsync(Request(campaign.PublicId) with { Context = new JsonObject { ["brief"] = new string('x', 300 * 1024) } }, Ct));

        Assert.Equal("context_too_large", error.Code);
    }

    [Fact]
    public async Task A_result_format_larger_than_the_cap_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(Request(campaign.PublicId) with { ResultFormat = new JsonObject { ["doc"] = new string('x', 20 * 1024) } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("result_format", detail.Field);
        Assert.Equal("too_large", detail.Code);
    }

    [Fact]
    public async Task An_unknown_work_item_is_a_not_found()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(new WorkItemGetRequest("wi_01JASONNOTHERE", null), Ct));

        Assert.Equal("work_item_not_found", error.Code);
    }

    [Fact]
    public async Task Reading_an_item_returns_its_attempts_newest_first_without_their_snapshots()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign, configure: w => w.Context = new JsonObject { ["brief"] = "find the founders" });
        item.Status = WorkItemStatus.Processing;
        var first = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon.UtcDateTime);
        var second = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Running, Noon.UtcDateTime);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var plain = await service.GetAsync(new WorkItemGetRequest(item.PublicId, null), Ct);

        Assert.Equal(new[] { second.PublicId, first.PublicId }, plain.Attempts!.Select(a => a.Id).ToArray());
        Assert.All(plain.Attempts!, attempt => Assert.Null(attempt.ContextSnapshot));
        Assert.Equal(second.PublicId, plain.CurrentAttemptId);

        var detailed = await service.GetAsync(new WorkItemGetRequest(item.PublicId, true), Ct);

        Assert.All(detailed.Attempts!, attempt => Assert.Equal("find the founders", (string?)attempt.ContextSnapshot!["brief"]));
    }

    [Fact]
    public async Task A_listing_stays_inside_the_campaign_it_was_asked_about()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var mine = WorkItemFactory.NewCampaign("Mine");
        var other = WorkItemFactory.NewCampaign("Other");
        db.WorkItems.Add(WorkItemFactory.NewAiRole(mine));
        db.WorkItems.Add(WorkItemFactory.NewAiRole(other));
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var page = await service.ListAsync(new WorkItemListRequest(mine.PublicId, null, null, null, null, null, null, null), Ct);

        var only = Assert.Single(page.Items);
        Assert.Equal(mine.PublicId, only.CampaignId);
    }

    [Fact]
    public async Task A_listing_filters_by_contact_status_kind_and_role()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var contact = NewContact(db);
        var researcher = WorkItemFactory.NewAiRole(campaign);
        var copywriter = WorkItemFactory.NewAiRole(campaign, "copywriter", configure: w => w.Status = WorkItemStatus.Failed);
        var enrolment = WorkItemFactory.NewProviderOp(campaign);
        enrolment.Contact = contact;
        var expired = WorkItemFactory.NewAiRole(campaign, "analyst", configure: w => w.Status = WorkItemStatus.Expired);
        db.WorkItems.AddRange(researcher, copywriter, enrolment, expired);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var byContact = await service.ListAsync(new WorkItemListRequest(null, contact.PublicId, null, null, null, null, null, null), Ct);
        Assert.Equal(enrolment.PublicId, Assert.Single(byContact.Items).Id);

        var byStatus = await service.ListAsync(
            new WorkItemListRequest(null, null, [WorkItemStatus.Failed, WorkItemStatus.Expired], null, null, null, null, null),
            Ct);
        Assert.Equal(
            new[] { copywriter.PublicId, expired.PublicId }.Order(StringComparer.Ordinal).ToArray(),
            byStatus.Items.Select(i => i.Id).Order(StringComparer.Ordinal).ToArray());

        var byKind = await service.ListAsync(new WorkItemListRequest(null, null, null, WorkItemKind.ProviderOp, null, null, null, null), Ct);
        Assert.Equal(enrolment.PublicId, Assert.Single(byKind.Items).Id);

        var byRole = await service.ListAsync(new WorkItemListRequest(null, null, null, null, "copywriter", null, null, null), Ct);
        Assert.Equal(copywriter.PublicId, Assert.Single(byRole.Items).Id);
    }

    [Fact]
    public async Task The_eligible_filter_splits_the_queue_from_everything_that_cannot_run_yet()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var active = WorkItemFactory.NewCampaign();
        var paused = WorkItemFactory.NewCampaign("Paused", CampaignStatus.Paused);
        var ready = WorkItemFactory.NewAiRole(active);
        var claimed = WorkItemFactory.NewAiRole(active, configure: w => w.Status = WorkItemStatus.Scheduled);
        var later = WorkItemFactory.NewAiRole(active, configure: w => w.NotBefore = Noon.UtcDateTime.AddHours(1));
        var backingOff = WorkItemFactory.NewAiRole(active, configure: w => w.RetryAfter = Noon.UtcDateTime.AddMinutes(5));
        var stalled = WorkItemFactory.NewAiRole(paused);
        db.WorkItems.AddRange(ready, claimed, later, backingOff, stalled);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var eligible = await service.ListAsync(new WorkItemListRequest(null, null, null, null, null, true, null, null), Ct);
        Assert.Equal(ready.PublicId, Assert.Single(eligible.Items).Id);
        Assert.True(Assert.Single(eligible.Items).Eligible);

        var rest = await service.ListAsync(new WorkItemListRequest(null, null, null, null, null, false, null, null), Ct);
        Assert.Equal(
            new[] { claimed.PublicId, later.PublicId, backingOff.PublicId, stalled.PublicId }.Order(StringComparer.Ordinal).ToArray(),
            rest.Items.Select(i => i.Id).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_cursor_walks_the_whole_queue_without_gaps_or_repeats()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        for (var index = 0; index < 250; index++)
        {
            db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign));
        }

        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await service.ListAsync(new WorkItemListRequest(null, null, null, null, null, null, 100, cursor), Ct);
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(250, seen.Count);
        Assert.Equal(250, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task A_limit_outside_the_allowed_range_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.ListAsync(new WorkItemListRequest(null, null, null, null, null, null, 0, null), Ct));

        Assert.Equal("limit", Assert.Single(error.Details!).Field);
    }

    private static WorkItemService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.Plugins());

    private static WorkItemCreateRequest Request(string? campaignId) =>
        new(campaignId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static WorkItemCreateRequest ProviderOp(string? campaignId) =>
        new(campaignId, WorkItemKind.ProviderOp, null, "campaign.get", null, null, null, null, null, null, null, null, null, null, null, null);

    private static Campaign Seed(JasonDbContext db, Campaign campaign)
    {
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static Contact NewContact(JasonDbContext db, bool archived = false)
    {
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = "Ada",
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
            ArchivedAt = archived ? Noon.UtcDateTime : null,
        };
        db.Contacts.Add(contact);
        return contact;
    }

    private static void Enrol(JasonDbContext db, Campaign campaign, Contact contact, MembershipState state) =>
        db.CampaignContacts.Add(new CampaignContact
        {
            Campaign = campaign,
            Contact = contact,
            State = state,
            AddedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        });
}
