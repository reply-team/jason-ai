using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// The creator's own doubt about a send: the reserved context key <c>ask_person</c>, exactly <c>{"code": …}</c> with a code
/// of the closed list, accepted only on a send a person decides each one of and set at create only, never changed or taken
/// back; and its one sentence, <c>ask_person_note</c>, which is kept on the item outside the context and reaches no read of
/// the item and no line.
/// </summary>
public class AskPersonTests
{
    private const string Sentence = "SENTINEL_ASK_4d1e: the brief never mentions pricing for agencies.";

    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_send_created_asking_the_person_keeps_its_code_in_the_context_and_its_sentence_on_the_item_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var made = await Service(db).CreateAsync(Send(seed, new JsonObject { ["code"] = "sensitive_topic" }) with { AskPersonNote = $"  {Sentence}  " }, Ct);

        Assert.Equal("sensitive_topic", (string?)made.Context[Doubts.AskPersonKey]!["code"]);
        var stored = await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == made.Id, Ct);
        Assert.Equal(Sentence, stored.AskPersonNote);
        Assert.Equal("sensitive_topic", Doubts.AskPersonCode(stored));

        // No read of the item and no line says the sentence.
        Assert.DoesNotContain("SENTINEL_ASK_4d1e", JsonSerializer.Serialize(made, JasonJson.Options), StringComparison.Ordinal);
        var read = await Service(db).GetAsync(new WorkItemGetRequest(made.Id, IncludeSnapshots: true), Ct);
        Assert.DoesNotContain("SENTINEL_ASK_4d1e", JsonSerializer.Serialize(read, JasonJson.Options), StringComparison.Ordinal);
        var listed = await Service(db).ListAsync(new WorkItemListRequest(seed.Campaign.PublicId, null, null, null, null, null, null, null), Ct);
        Assert.Equal(made.Id, Assert.Single(listed.Items).Id);
        Assert.DoesNotContain("SENTINEL_ASK_4d1e", JsonSerializer.Serialize(listed, JasonJson.Options), StringComparison.Ordinal);
        var lines = await db.Journal.AsNoTracking().ToListAsync(Ct);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.DoesNotContain("SENTINEL_ASK_4d1e", JsonSerializer.Serialize(JournalWriter.ToDto(line, null), JasonJson.Options), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unsupported_claim")]
    [InlineData("not_in_memory")]
    [InlineData("off_brief")]
    [InlineData("sensitive_topic")]
    [InlineData("instruction_in_text")]
    [InlineData("new_kind_of_person")]
    [InlineData("wrong_recipient")]
    [InlineData("other")]
    public async Task Every_code_of_the_list_is_accepted_without_a_sentence(string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var made = await Service(db).CreateAsync(Send(seed, new JsonObject { ["code"] = code }), Ct);

        Assert.Equal(code, (string?)made.Context[Doubts.AskPersonKey]!["code"]);
        Assert.Null((await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == made.Id, Ct)).AskPersonNote);
    }

    /// <summary>Answers too: a doubt is never refused at create because of which send it is about.</summary>
    [Theory]
    [InlineData("message.send")]
    [InlineData("social_invitation.send")]
    [InlineData("conversation.reply")]
    public async Task It_is_accepted_on_each_send_a_person_decides_each_one_of_answers_included(string operation)
    {
        var errors = new ValidationErrors();

        Doubts.ValidateCreate(
            Shape(operation, WorkItemKind.ProviderOp, new JsonObject { [Doubts.AskPersonKey] = new JsonObject { ["code"] = "other" } }) with { AskPersonNote = "why" },
            errors);

        Assert.False(errors.Any);
    }

    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "a code outside the list", "context.ask_person.code", "invalid" },
        { "a code that is no string", "context.ask_person.code", "invalid" },
        { "no code", "context.ask_person.code", "invalid" },
        { "a member beside the code", "context.ask_person.by", "not_allowed" },
        { "the sentence inside the key", "context.ask_person.note", "not_allowed" },
        { "a key that is no object", "context.ask_person", "invalid" },
        { "a read, not a send", "context.ask_person", "not_applicable" },
        { "role work", "context.ask_person", "not_applicable" },
        { "a sentence without the key", "ask_person_note", "not_allowed" },
        { "a sentence too long", "ask_person_note", "too_long" },
        { "a sentence of two lines", "ask_person_note", "invalid" },
        { "a blank sentence", "ask_person_note", "invalid" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_ask_person_a_create_may_not_carry_is_refused_at_its_field_and_writes_nothing(string why, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        JsonNode? asked = why switch
        {
            "a code outside the list" => new JsonObject { ["code"] = "looks_off" },
            "a code that is no string" => new JsonObject { ["code"] = 3 },
            "no code" => new JsonObject(),
            "a member beside the code" => new JsonObject { ["code"] = "other", ["by"] = "composer" },
            "the sentence inside the key" => new JsonObject { ["code"] = "other", ["note"] = Sentence },
            "a key that is no object" => JsonValue.Create("other"),
            "a sentence without the key" => null,
            _ => new JsonObject { ["code"] = "other" },
        };
        var request = why switch
        {
            "a read, not a send" => Shape("conversation.get", WorkItemKind.ProviderOp, With(asked)) with { CampaignId = seed.Campaign.PublicId },
            "role work" => new WorkItemCreateRequest(seed.Campaign.PublicId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, With(asked), null, null, null),
            _ => Send(seed, asked),
        };
        request = why switch
        {
            "a sentence without the key" => request with { AskPersonNote = Sentence },
            "a sentence too long" => request with { AskPersonNote = new string('a', DoubtCodes.MaxNoteLength + 1) },
            "a sentence of two lines" => request with { AskPersonNote = "one line\nand another" },
            "a blank sentence" => request with { AskPersonNote = "   " },
            _ => request,
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(request, Ct));

        Assert.Contains(refused.Details!, detail => detail.Field == field && detail.Code == code);
        Assert.All(refused.Details!, detail => Assert.DoesNotContain("SENTINEL_ASK_4d1e", detail.Message, StringComparison.Ordinal));
        Assert.Empty(await db.WorkItems.AsNoTracking().Where(w => w.Kind == WorkItemKind.ProviderOp || w.Role == "researcher").ToListAsync(Ct));
    }

    public static TheoryData<string> Patches() => ["set", "change", "unset", "set to what it holds"];

    /// <summary>Set at create and never moved: a patch that names the key is refused, whatever it would do and whoever sends it.</summary>
    [Theory]
    [MemberData(nameof(Patches))]
    public async Task A_patch_that_names_ask_person_is_refused_and_changes_nothing(string patch)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var asked = patch == "set" ? null : new JsonObject { ["code"] = "off_brief" };
        var made = await Service(db).CreateAsync(asked is null ? Send(seed, null) with { Context = new JsonObject { [WorkItemService.InputKey] = Message() } } : Send(seed, asked), Ct);
        var request = new WorkItemUpdateRequest(
            made.Id,
            patch switch
            {
                "set" => new JsonObject { [Doubts.AskPersonKey] = new JsonObject { ["code"] = "other" } },
                "change" => new JsonObject { [Doubts.AskPersonKey] = new JsonObject { ["code"] = "other" } },
                "set to what it holds" => new JsonObject { [Doubts.AskPersonKey] = new JsonObject { ["code"] = "off_brief" } },
                _ => null,
            },
            patch == "unset" ? [Doubts.AskPersonKey] : null,
            default, default, default, default, default, default, default, default,
            new ActorRef(ActorType.Human, "ada"),
            Reason: null);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).UpdateAsync(request, Ct));

        Assert.Equal(patch == "unset" ? "unset[0]" : "set.ask_person", Assert.Single(refused.Details!).Field);
        db.ChangeTracker.Clear();
        var stored = await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == made.Id, Ct);
        Assert.Equal(asked is null ? null : "off_brief", Doubts.AskPersonCode(stored));
    }

    /// <summary>The sentence is part of what a keyed create asks for: the same key with another sentence is another request.</summary>
    [Fact]
    public async Task A_key_used_with_another_sentence_is_refused_as_reused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var request = Send(seed, new JsonObject { ["code"] = "other" }) with { AskPersonNote = Sentence, IdempotencyKey = "send-1" };

        var made = await Service(db).CreateAsync(request, Ct);
        var again = await Service(db).CreateAsync(request, Ct);
        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(db).CreateAsync(request with { AskPersonNote = "another sentence" }, Ct));

        Assert.Equal(made.Id, again.Id);
        Assert.Equal("idempotency_key_reused", refused.Code);
    }

    private static JsonObject With(JsonNode? asked)
    {
        var context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["conversation"] = new JsonObject { ["external_id"] = "thr-1" } } };
        if (asked is not null)
        {
            context[Doubts.AskPersonKey] = asked.DeepClone();
        }

        return context;
    }

    private static WorkItemCreateRequest Shape(string operation, WorkItemKind kind, JsonObject context) =>
        new("cmp_shape", kind, null, operation, null, null, null, null, null, null, null, null, context, null, null, null);

    private static WorkItemCreateRequest Send(Seed seed, JsonNode? asked)
    {
        var context = new JsonObject { [WorkItemService.InputKey] = Message() };
        if (asked is not null)
        {
            context[Doubts.AskPersonKey] = asked.DeepClone();
        }

        return new WorkItemCreateRequest(
            seed.Campaign.PublicId, WorkItemKind.ProviderOp, null, "message.send", seed.Contact.PublicId, null, null, null, null, null, null, null,
            context, null, new ActorRef(ActorType.Role, "composer"), null);
    }

    private static JsonObject Message() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = "Thursday at 10 works." },
    };

    private static WorkItemService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
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
