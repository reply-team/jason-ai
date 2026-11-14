using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// The critic the runtime creates for a send under an automatic send mode, and the reserved context key that names the send
/// it reviews, <c>send_review</c>: the runtime's alone, refused at create and in every patch, on every item, whoever sends it.
/// </summary>
public class SendReviewQueueTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_send_created_under_an_automatic_cold_mode_waits_on_a_critic_the_runtime_created_in_the_same_save()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, cold: "automatic", warm: null);
        var due = Noon.AddDays(2);

        var made = await Service(db).CreateAsync(Send(seed) with { Priority = 7, DueAt = due }, Ct);

        var critic = await db.WorkItems.AsNoTracking().Include(w => w.Contact).SingleAsync(w => w.Kind == WorkItemKind.AiRole, Ct);
        Assert.Equal(
            ("critic", ActorType.System, "runtime", seed.Contact.PublicId, 7, due.UtcDateTime, WorkItemStatus.Created),
            (critic.Role, critic.CreatedByType, critic.CreatedById, critic.Contact!.PublicId, critic.Priority, critic.DueAt, critic.Status));
        Assert.Equal(
            new JsonObject { ["send_review"] = new JsonObject { ["work_item_id"] = made.Id, ["operation"] = "message.send" } }.ToJsonString(),
            critic.Context.ToJsonString());
        Assert.Null(critic.NotBefore);
        Assert.Null(critic.IdempotencyKey);

        Assert.Equal(WorkItemStatus.Waiting, made.Status);
        Assert.Equal([critic.PublicId], made.Wait!.On.Select(on => on.WorkItemId));
        var lines = await db.Journal.AsNoTracking().OrderBy(line => line.Id).ToListAsync(Ct);
        Assert.Equal(
            [(JournalKinds.WorkItemCreated, made.Id), (JournalKinds.WorkItemCreated, critic.PublicId), (JournalKinds.WorkItemWaiting, made.Id)],
            lines.Select(line => (line.Kind, line.WorkItemId!)));
        Assert.Equal((ActorType.System, "runtime", "a send to review before it is claimed"), (lines[1].ActorType, lines[1].ActorId, lines[1].Reason));
        Assert.Equal((ActorType.System, "runtime"), (lines[2].ActorType, lines[2].ActorId));
    }

    /// <summary>
    /// The create reads the mode the claim would read for this contact: a contact who has not answered is cold, and a mode
    /// that releases only warm contacts would release nothing, so no critic is run for it.
    /// </summary>
    [Fact]
    public async Task A_cold_contact_under_an_automatic_warm_mode_alone_is_given_no_critic()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, cold: "every_message", warm: "automatic");

        var made = await Service(db).CreateAsync(Send(seed), Ct);

        Assert.Equal(WorkItemStatus.Created, made.Status);
        Assert.Empty(await CriticsAsync(db));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("every_message", "every_message")]
    [InlineData("every_message", null)]
    [InlineData(null, "every_message")]
    public async Task Under_every_message_or_with_nothing_set_nothing_is_queued_and_the_send_is_not_held(string? cold, string? warm)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, cold, warm);

        var made = await Service(db).CreateAsync(Send(seed), Ct);

        Assert.Equal(WorkItemStatus.Created, made.Status);
        Assert.Null(made.Wait);
        Assert.Empty(await CriticsAsync(db));
        Assert.Empty(await db.WorkItemWaits.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>The mode in force is the one a release would go under: a value nobody named set releases nothing.</summary>
    [Theory]
    [InlineData("automatic set by a person, over a manual installation", true)]
    [InlineData("every message set by a person, over an automatic installation", false)]
    [InlineData("automatic with no setter, over a manual installation", false)]
    [InlineData("an automatic installation set by a role", false)]
    public async Task A_campaign_own_mode_decides_over_the_installation(string modes, bool queued)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == seed.Campaign.Id, Ct);
        switch (modes)
        {
            case "automatic set by a person, over a manual installation":
                await ModesAsync(db, "every_message", "every_message");
                (campaign.SendModeCold, campaign.SendModeColdSetBy) = (SendMode.Automatic, "ada");
                break;
            case "every message set by a person, over an automatic installation":
                await ModesAsync(db, "automatic", "automatic");
                (campaign.SendModeCold, campaign.SendModeColdSetBy, campaign.SendModeWarm, campaign.SendModeWarmSetBy) = (SendMode.EveryMessage, "ada", SendMode.EveryMessage, "ada");
                break;
            case "automatic with no setter, over a manual installation":
                await ModesAsync(db, "every_message", "every_message");
                (campaign.SendModeCold, campaign.SendModeColdSetBy) = (SendMode.Automatic, null);
                break;
            default:
                await ModesAsync(db, "automatic", "automatic", new ActorRef(ActorType.Role, "composer"));
                break;
        }

        await db.SaveChangesAsync(Ct);

        var made = await Service(db).CreateAsync(Send(seed), Ct);

        Assert.Equal(queued ? WorkItemStatus.Waiting : WorkItemStatus.Created, made.Status);
        Assert.Equal(queued ? 1 : 0, (await CriticsAsync(db)).Count);
    }

    public static TheoryData<string, bool> Operations() => new()
    {
        { "message.send", true },
        { "conversation.reply", true },
        { "social_invitation.send", true },
        { "campaign.enroll", false },
        { "conversation.get", false },
        { "role work", false },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Each_send_a_person_decides_each_one_of_is_reviewed_and_no_other_work_is(string work, bool queued)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, "automatic", "automatic");
        var request = work == "role work"
            ? Role(seed, "copywriter")
            : Send(seed) with { Operation = work, Context = new JsonObject { [WorkItemService.InputKey] = Input(work) } };

        var made = await Service(db).CreateAsync(request, Ct);

        Assert.Equal(queued ? WorkItemStatus.Waiting : WorkItemStatus.Created, made.Status);
        var critics = await CriticsAsync(db);
        Assert.Equal(queued ? 1 : 0, critics.Count);
        if (queued)
        {
            Assert.Equal(work, (string?)critics[0].Context[SendReview.Key]![SendReview.OperationMember]);
        }
    }

    [Fact]
    public async Task A_send_that_asks_the_person_is_not_reviewed_first()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, "automatic", "automatic");

        var made = await Service(db).CreateAsync(
            Send(seed) with { Context = new JsonObject { [WorkItemService.InputKey] = Message(), [Doubts.AskPersonKey] = new JsonObject { ["code"] = "other" } } },
            Ct);

        Assert.Equal(WorkItemStatus.Created, made.Status);
        Assert.Empty(await CriticsAsync(db));
    }

    /// <summary>A send written in the desktop's chat always waits for the person, so a critic would be a run for nothing.</summary>
    [Fact]
    [Critical(Family.S2, "a send written in the chat is never given a critic: it waits for the person")]
    public async Task A_send_written_in_the_chat_is_not_reviewed_first()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, "automatic", "automatic");

        WorkItemDto made;
        using (ConfinedScope.Open())
        {
            made = await Service(db).CreateAsync(Send(seed), Ct);
        }

        Assert.Equal(WorkItemStatus.Created, made.Status);
        Assert.Empty(await CriticsAsync(db));
        Assert.Equal(true, (bool?)(await db.Journal.AsNoTracking().SingleAsync(line => line.Kind == JournalKinds.WorkItemCreated, Ct)).New!["confined"]);

        // The same send outside the chat is given its critic.
        var outside = await Service(db).CreateAsync(Send(seed), Ct);
        Assert.Equal(WorkItemStatus.Waiting, outside.Status);
        Assert.Single(await CriticsAsync(db));
    }

    [Fact]
    public async Task A_send_about_nobody_is_not_reviewed_first()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, "automatic", "automatic");

        var made = await Service(db).CreateAsync(Send(seed) with { ContactId = null }, Ct);

        Assert.Equal(WorkItemStatus.Created, made.Status);
        Assert.Empty(await CriticsAsync(db));
    }

    /// <summary>A critic nothing could start would only fail at its claim and wake the manager: none is made, and the send is not held.</summary>
    [Fact]
    public async Task No_critic_is_queued_where_none_could_be_launched()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, "automatic", "automatic");

        var made = await Service(db, new RolesOptions()).CreateAsync(Send(seed), Ct);

        Assert.Equal(WorkItemStatus.Created, made.Status);
        Assert.Empty(await CriticsAsync(db));

        // A profile the campaign names makes it launchable, as it makes a responder.
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == seed.Campaign.Id, Ct);
        campaign.ExecutionProfile = "house";
        await db.SaveChangesAsync(Ct);
        var second = await Service(db, new RolesOptions()).CreateAsync(Send(seed), Ct);
        Assert.Equal(WorkItemStatus.Waiting, second.Status);
        Assert.Single(await CriticsAsync(db));
    }

    [Fact]
    public async Task A_caller_wait_on_is_kept_and_the_critic_follows_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var first = await Service(db).CreateAsync(Role(seed, "copywriter"), Ct);
        await ModesAsync(db, "automatic", null);

        var made = await Service(db).CreateAsync(Send(seed) with { WaitOn = [first.Id] }, Ct);

        var critic = Assert.Single(await CriticsAsync(db));
        Assert.Equal([first.Id, critic.PublicId], made.Wait!.On.Select(on => on.WorkItemId));
        Assert.Equal(WaitMode.All, made.Wait.Mode);
        var waiting = await db.Journal.AsNoTracking().SingleAsync(line => line.Kind == JournalKinds.WorkItemWaiting, Ct);
        Assert.Equal((ActorType.Role, "composer"), (waiting.ActorType, waiting.ActorId));
    }

    /// <summary>The caller's bound is the caller's: the runtime's one item goes beyond it, and the create never answers with a failure of its own.</summary>
    [Fact]
    public async Task A_hundred_items_a_caller_waits_on_and_the_critic_make_a_wait_of_a_hundred_and_one()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == seed.Campaign.Id, Ct);
        var named = new List<string>();
        for (var n = 0; n < WorkItemWaitRequest.MaxWaitItems; n++)
        {
            var item = WorkItemFactory.NewAiRole(campaign);
            db.WorkItems.Add(item);
            named.Add(item.PublicId);
        }

        await db.SaveChangesAsync(Ct);
        await ModesAsync(db, "automatic", "automatic");

        var made = await Service(db).CreateAsync(Send(seed) with { WaitOn = named }, Ct);

        var critic = Assert.Single(await CriticsAsync(db));
        Assert.Equal(WorkItemWaitRequest.MaxWaitItems + 1, made.Wait!.On.Count);
        Assert.Equal([.. named, critic.PublicId], made.Wait.On.Select(on => on.WorkItemId));
    }

    /// <summary>The modes are read once for this, at create; the claim reads them again when it decides.</summary>
    [Fact]
    public async Task A_mode_changed_after_the_create_neither_adds_nor_removes_the_critic()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ModesAsync(db, "automatic", "automatic");
        var reviewed = await Service(db).CreateAsync(Send(seed), Ct);

        await ModesAsync(db, "every_message", "every_message");
        var plain = await Service(db).CreateAsync(Send(seed), Ct);
        await ModesAsync(db, "automatic", "automatic");

        db.ChangeTracker.Clear();
        var critic = Assert.Single(await CriticsAsync(db));
        Assert.Equal(reviewed.Id, SendReview.Named(critic.Context));
        Assert.Equal(WorkItemStatus.Waiting, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == reviewed.Id, Ct)).Status);
        Assert.Equal(WorkItemStatus.Created, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == plain.Id, Ct)).Status);
    }

    public static TheoryData<string, bool> Creates() => new()
    {
        { "a send", false },
        { "a send", true },
        { "role work", false },
        { "role work", true },
    };

    [Theory]
    [MemberData(nameof(Creates))]
    public async Task A_create_naming_send_review_is_refused_at_its_field_and_writes_nothing(string work, bool keyed)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var review = SendReview.Context("wi_01K000000000000000000000AB", "message.send")[SendReview.Key]!.DeepClone();
        var request = work == "a send"
            ? Send(seed) with { Context = new JsonObject { [WorkItemService.InputKey] = Message(), [SendReview.Key] = review } }
            : Role(seed, "critic") with { Context = new JsonObject { [SendReview.Key] = review } };
        request = keyed ? request with { IdempotencyKey = "made-once" } : request;

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(request, Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("context.send_review", "not_allowed"), (detail.Field, detail.Code));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.WorkItems.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await db.Journal.AsNoTracking().Where(line => line.Kind == JournalKinds.WorkItemCreated).ToListAsync(Ct));
    }

    public static TheoryData<string, string> Patches() => new()
    {
        { "set", "a send" },
        { "unset", "a send" },
        { "set", "role work" },
        { "unset", "role work" },
        { "set", "the critic" },
        { "unset", "the critic" },
        { "set", "the critic, by its own run" },
        { "unset", "the critic, by its own run" },
    };

    /// <summary>Written by the runtime and never moved: a patch that names the key is refused, whatever it would do and whoever sends it.</summary>
    [Theory]
    [MemberData(nameof(Patches))]
    public async Task A_patch_that_names_send_review_is_refused_and_changes_nothing(string patch, string work)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        string target;
        var actor = new ActorRef(ActorType.Human, "ada");
        if (work.StartsWith("the critic", StringComparison.Ordinal))
        {
            await ModesAsync(db, "automatic", "automatic");
            await Service(db).CreateAsync(Send(seed), Ct);
            var critic = await db.WorkItems.SingleAsync(w => w.Role == "critic", Ct);
            target = critic.PublicId;
            if (work == "the critic, by its own run")
            {
                critic.Status = WorkItemStatus.Processing;
                var attempt = WorkItemFactory.NewAttempt(critic, 1, AttemptStatus.Running, Noon.UtcDateTime);
                db.Attempts.Add(attempt);
                await db.SaveChangesAsync(Ct);
                actor = new ActorRef(ActorType.Attempt, attempt.PublicId);
            }
        }
        else
        {
            target = (await Service(db).CreateAsync(work == "a send" ? Send(seed) : Role(seed, "critic"), Ct)).Id;
        }

        db.ChangeTracker.Clear();
        var before = (await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == target, Ct)).Context.ToJsonString();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).UpdateAsync(Patch(target, patch, actor), Ct));

        Assert.Equal(patch == "unset" ? ("unset[0]", "not_allowed") : ("set.send_review", "not_allowed"), (Assert.Single(refused.Details!).Field, refused.Details![0].Code));
        db.ChangeTracker.Clear();
        Assert.Equal(before, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == target, Ct)).Context.ToJsonString());
        Assert.Empty(await db.Journal.AsNoTracking().Where(line => line.Kind == JournalKinds.WorkItemContextUpdated).ToListAsync(Ct));
    }

    private static async Task<List<WorkItem>> CriticsAsync(JasonDbContext db) =>
        await db.WorkItems.AsNoTracking().Where(w => w.Kind == WorkItemKind.AiRole && w.Role == "critic" && w.CreatedByType == ActorType.System).OrderBy(w => w.Id).ToListAsync(Ct);

    /// <summary>The installation's two send modes as a person sets them, or as <paramref name="by"/> does; null leaves one unset.</summary>
    private static async Task ModesAsync(JasonDbContext db, string? cold, string? warm, ActorRef? by = null)
    {
        var setter = by ?? new ActorRef(ActorType.Human, "ada");
        var rows = await db.Preferences.Where(p => p.Key == SendModes.ColdKey || p.Key == SendModes.WarmKey).ToListAsync(Ct);
        db.Preferences.RemoveRange(rows);
        foreach (var (key, mode) in new[] { (SendModes.ColdKey, cold), (SendModes.WarmKey, warm) })
        {
            if (mode is not null)
            {
                db.Preferences.Add(new Preference
                {
                    Key = key,
                    Value = new JsonObject { ["mode"] = mode },
                    ValueHash = "sha256:" + new string('0', 64),
                    UpdatedByType = setter.Type,
                    UpdatedById = setter.Id,
                    CreatedAt = Noon.UtcDateTime,
                    UpdatedAt = Noon.UtcDateTime,
                });
            }
        }

        await db.SaveChangesAsync(Ct);
    }

    private static JsonObject Input(string operation) => operation switch
    {
        "message.send" => Message(),
        "conversation.reply" => new JsonObject { ["channel"] = "linkedin", ["thread"] = new JsonObject { ["external_id"] = "t_1" }, ["body"] = "Thanks for connecting." },
        "social_invitation.send" => new JsonObject { ["channel"] = "linkedin", ["sender"] = new JsonObject { ["external_id"] = "s_34", ["address"] = "https://www.linkedin.com/in/ann-owner" }, ["note"] = "Hi Ada." },
        "campaign.enroll" => new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c_1" }, ["channel"] = "email", ["collision"] = "skip", ["start"] = new JsonObject { ["position"] = "first_step" }, ["first_touch"] = "authored_delay" },
        "conversation.get" => new JsonObject { ["thread"] = new JsonObject { ["external_id"] = "t_1" }, ["limit"] = 20, ["offset"] = 0 },
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static WorkItemUpdateRequest Patch(string id, string patch, ActorRef actor) => new(
        id,
        patch == "set" ? SendReview.Context("wi_01K000000000000000000000AB", "message.send") : null,
        patch == "unset" ? [SendReview.Key] : null,
        default, default, default, default, default, default, default, default,
        actor,
        Reason: null);

    private static WorkItemCreateRequest Send(Seed seed) => new(
        seed.Campaign.PublicId, WorkItemKind.ProviderOp, null, "message.send", seed.Contact.PublicId, null, null, null, null, null, null, null,
        new JsonObject { [WorkItemService.InputKey] = Message() }, null, new ActorRef(ActorType.Role, "composer"), null);

    private static WorkItemCreateRequest Role(Seed seed, string role) => new(
        seed.Campaign.PublicId, WorkItemKind.AiRole, role, null, seed.Contact.PublicId, null, null, null, null, null, null, null,
        null, null, new ActorRef(ActorType.Role, "composer"), null);

    private static JsonObject Message() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = "Thursday at 10 works." },
    };

    /// <summary>The service as the runtime builds it, where a critic can be launched unless <paramref name="roles"/> says otherwise.</summary>
    private static WorkItemService Service(JasonDbContext db, RolesOptions? roles = null)
    {
        var clock = new FixedClock(Noon);
        var journal = new JournalWriter(clock);
        return new WorkItemService(
            db,
            journal,
            clock,
            TestCanceller.New(clock),
            TestOptions.PluginSettings(),
            outcomes: new AttemptOutcomes(journal, clock, TestOptions.Settings()),
            roles: TestOptions.RoleSettings(roles ?? new RolesOptions { DefaultEntryCommand = ["agent"] }));
    }

    private static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var at = Noon.UtcDateTime;
        var campaign = WorkItemFactory.NewCampaign(now: at);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = at, UpdatedAt = at };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = MembershipState.Enrolled, AddedAt = at, UpdatedAt = at });
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact);
    }

    private sealed record Seed(Campaign Campaign, Contact Contact);
}
