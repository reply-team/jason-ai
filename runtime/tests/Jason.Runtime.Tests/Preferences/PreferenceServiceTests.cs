using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Preferences;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Preferences;

/// <summary>
/// The named preferences: a person's word about the client, one value per key for the whole installation, checked against
/// the key's published schema, set and taken back by a person only, and said in the chronicle by its size and hash — never
/// by what it holds, because the chronicle could never forget it.
/// </summary>
public class PreferenceServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly ActorRef Marta = new(ActorType.Human, "Marta");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_sets_a_preference_and_the_chronicle_says_its_size_and_hash_never_its_value()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var value = Sender("Marta Quillfeather");
        var measured = CanonicalJson.Measure(value)!.Value;

        var set = await NewService(db).SetAsync(new PreferenceSetRequest("sender.identity", value, Marta, "  the person's answer  "), Ct);

        Assert.Equal(new PreferenceDto("sender.identity", set.Value, measured.Hash, measured.Bytes, Noon, Noon, Marta), set);
        Assert.True(JsonNode.DeepEquals(value, set.Value), $"The value read back as {set.Value?.ToJsonString()}.");
        Assert.Matches("^sha256:[0-9a-f]{64}$", set.ValueHash);

        var row = await db.Preferences.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("sender.identity", measured.Hash, measured.Bytes, ActorType.Human, "Marta"), (row.Key, row.ValueHash, row.ValueBytes, row.UpdatedByType, row.UpdatedById));

        var line = Assert.Single(await db.Journal.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(JournalKinds.PreferenceSet, line.Kind);
        Assert.Equal("sender.identity", line.Key);
        Assert.Null(line.CampaignId);
        Assert.Null(line.Old);
        Assert.Equal($$"""{"bytes":{{measured.Bytes}},"hash":"{{measured.Hash}}"}""", line.New!.ToJsonString());
        Assert.Equal("the person's answer", line.Reason);
        Assert.Equal((ActorType.Human, "Marta"), (line.ActorType, line.ActorId));
    }

    /// <summary>A value is replaced whole; the line says the size and hash before and after, and the row keeps when it was first set.</summary>
    [Fact]
    public async Task Setting_again_replaces_the_value_and_says_the_size_and_hash_before_and_after()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var first = await service.SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Plain and warm."), Marta, null), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        var second = await service.SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Plain, warm and brief."), new ActorRef(ActorType.Human, "Sam"), null), Ct);

        Assert.Equal((Noon, Noon.AddMinutes(5), new ActorRef(ActorType.Human, "Sam")), (second.CreatedAt!.Value, second.UpdatedAt!.Value, second.UpdatedBy));
        Assert.Equal("Plain, warm and brief.", (string?)second.Value!["summary"]);
        Assert.Single(await db.Preferences.AsNoTracking().ToListAsync(Ct));

        var line = (await db.Journal.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync(Ct))[^1];
        Assert.Equal(JournalKinds.PreferenceSet, line.Kind);
        Assert.Equal($$"""{"bytes":{{first.ValueBytes}},"hash":"{{first.ValueHash}}"}""", line.Old!.ToJsonString());
        Assert.Equal($$"""{"bytes":{{second.ValueBytes}},"hash":"{{second.ValueHash}}"}""", line.New!.ToJsonString());
    }

    /// <summary>
    /// The value a key already has, in whatever order its members were written, changes nothing and says nothing: the
    /// hash is over the canonical form, so a set sent again after its answer was lost is never a second line.
    /// </summary>
    [Fact]
    public async Task Setting_the_value_a_key_already_has_writes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var first = await service.SetAsync(new PreferenceSetRequest("sender.identity", JsonNode.Parse("""{"senders":[{"name":"Marta","gender_forms":"feminine"}]}"""), Marta, null), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        var again = await service.SetAsync(new PreferenceSetRequest("sender.identity", JsonNode.Parse("""{"senders":[{"gender_forms":"feminine","name":"Marta"}]}"""), new ActorRef(ActorType.Human, "Sam"), null), Ct);

        Assert.Equal((first.ValueHash, first.UpdatedAt, first.UpdatedBy), (again.ValueHash, again.UpdatedAt, again.UpdatedBy));
        Assert.Single(await db.Journal.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task An_unknown_key_is_refused_naming_the_keys_this_build_knows()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).SetAsync(new PreferenceSetRequest("tone", Tone("Plain."), Marta, null), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("key", "unknown"), (detail.Field, detail.Code));
        Assert.Contains("sender.identity, tone_of_voice", detail.Message, StringComparison.Ordinal);
        await NothingWrittenAsync(db);
    }

    [Theory]
    [InlineData(null, """{"summary":"Plain."}""", "key/required")]
    [InlineData("  ", """{"summary":"Plain."}""", "key/required")]
    [InlineData("tone_of_voice", null, "value/required")]
    [InlineData("tone_of_voice", """["Plain."]""", "value/invalid")]
    [InlineData("tone_of_voice", "\"Plain.\"", "value/invalid")]
    public async Task A_key_and_a_value_are_both_required_and_a_value_is_an_object(string? key, string? value, string expected)
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).SetAsync(new PreferenceSetRequest(key, value is null ? null : JsonNode.Parse(value), Marta, null), Ct));

        Assert.Equal([expected], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        await NothingWrittenAsync(db);
    }

    /// <summary>What the schema refuses is said at its own pointer under <c>value</c>, every problem at once.</summary>
    [Fact]
    public async Task A_value_the_key_s_schema_refuses_is_refused_with_each_problem_under_value()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).SetAsync(new PreferenceSetRequest("tone_of_voice", JsonNode.Parse("""{"formality":"casual","rules":[]}"""), Marta, null), Ct));

        Assert.Equal(
            ["value/formality/invalid", "value/rules/invalid", "value/summary/invalid"],
            refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}").Order(StringComparer.Ordinal));
        await NothingWrittenAsync(db);
    }

    /// <summary>The cap is the role note's, over the same canonical form: a value the schema accepts can still be too large.</summary>
    [Fact]
    public async Task A_value_past_64_KiB_in_its_canonical_form_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var senders = new JsonArray();
        for (var index = 0; index < 20; index++)
        {
            senders.Add(new JsonObject { ["name"] = "Marta", ["gender_forms"] = "feminine", ["details"] = new string('é', 2000) });
        }

        var value = new JsonObject { ["senders"] = senders };
        Assert.True(CanonicalJson.Measure(value)!.Value.Bytes > 64 * 1024, "The value meant to be too large is not.");

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).SetAsync(new PreferenceSetRequest("sender.identity", value, Marta, null), Ct));

        Assert.Equal(["value/too_large"], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        await NothingWrittenAsync(db);
    }

    /// <summary>The cap is inclusive: exactly 64 KiB of canonical form is kept, one byte more is refused and nothing is written.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task The_cap_is_exactly_64_KiB_of_canonical_form(int bytesOver, bool kept)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var target = (64 * 1024) + bytesOver;
        var value = SenderValueOfCanonicalBytes(target);
        var measured = CanonicalJson.Measure(value)!.Value.Bytes;
        Assert.True(measured == target, $"The value meant to be {target} bytes is {measured}.");

        if (kept)
        {
            await NewService(db).SetAsync(new PreferenceSetRequest("sender.identity", value, Marta, null), Ct);
            Assert.Single(await db.Preferences.AsNoTracking().ToListAsync(Ct));
            return;
        }

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).SetAsync(new PreferenceSetRequest("sender.identity", value, Marta, null), Ct));

        Assert.Equal(["value/too_large"], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        await NothingWrittenAsync(db);
    }

    /// <summary>A sender value whose canonical form is exactly <paramref name="target"/> bytes: escaped letters (six bytes each) and plain ones (one) make up the last sender's details.</summary>
    private static JsonObject SenderValueOfCanonicalBytes(int target)
    {
        static JsonObject Value(string lastDetails)
        {
            var senders = new JsonArray();
            for (var index = 0; index < 5; index++)
            {
                senders.Add(new JsonObject { ["name"] = "Marta", ["gender_forms"] = "feminine", ["details"] = new string('é', 2000) });
            }

            senders.Add(new JsonObject { ["name"] = "Marta", ["gender_forms"] = "feminine", ["details"] = lastDetails });
            return new JsonObject { ["senders"] = senders };
        }

        var missing = (int)(target - CanonicalJson.Measure(Value(string.Empty))!.Value.Bytes);
        return Value(new string('é', missing / 6) + new string('a', missing % 6));
    }

    [Theory]
    [InlineData(ActorType.Role, "copywriter")]
    [InlineData(ActorType.Attempt, "att_01K7QN0000000000000000000A")]
    public async Task Only_a_person_sets_or_unsets_a_preference(ActorType type, string id)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        await service.SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Plain."), Marta, null), Ct);
        var lines = await db.Journal.CountAsync(Ct);

        var set = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Loud."), new ActorRef(type, id), null), Ct));
        var unset = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.UnsetAsync(new PreferenceUnsetRequest("tone_of_voice", new ActorRef(type, id), null), Ct));

        Assert.Equal(("preference_not_human", "preference_not_human"), (set.Code, unset.Code));
        Assert.Equal(lines, await db.Journal.CountAsync(Ct));
        Assert.Equal("Plain.", (string?)(await db.Preferences.AsNoTracking().SingleAsync(Ct)).Value["summary"]);
    }

    /// <summary>As a decision is: what is recorded has to name the person whose word it is.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_preference_set_or_unset_without_naming_the_person_is_refused(bool humanWithoutId)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var actor = humanWithoutId ? new ActorRef(ActorType.Human) : null;

        var set = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Plain."), actor, null), Ct));
        var unset = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.UnsetAsync(new PreferenceUnsetRequest("tone_of_voice", actor, null), Ct));

        Assert.Equal(("actor_required", "actor_required"), (set.Code, unset.Code));
        await NothingWrittenAsync(db);
    }

    [Fact]
    public async Task A_reason_past_its_length_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Plain."), Marta, new string('r', 2001)), Ct));

        Assert.Equal(["reason/too_long"], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        await NothingWrittenAsync(db);
    }

    /// <summary>Unsetting deletes the row, so the value is gone; the line keeps that it was there, by its size and hash.</summary>
    [Fact]
    public async Task Unsetting_removes_the_value_and_says_what_size_and_hash_were_there()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var set = await service.SetAsync(new PreferenceSetRequest("sender.identity", Sender("Marta Quillfeather"), Marta, null), Ct);

        var unset = await service.UnsetAsync(new PreferenceUnsetRequest("sender.identity", Marta, "taken back"), Ct);

        Assert.Equal(new PreferenceDto("sender.identity", null, null, null, null, null, null), unset);
        Assert.Empty(await db.Preferences.AsNoTracking().ToListAsync(Ct));
        var line = (await db.Journal.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync(Ct))[^1];
        Assert.Equal((JournalKinds.PreferenceUnset, "sender.identity", "taken back"), (line.Kind, line.Key, line.Reason));
        Assert.Null(line.CampaignId);
        Assert.Equal($$"""{"bytes":{{set.ValueBytes}},"hash":"{{set.ValueHash}}"}""", line.Old!.ToJsonString());
        Assert.Null(line.New);
    }

    [Fact]
    public async Task Unsetting_a_key_that_is_not_set_writes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var unset = await NewService(db).UnsetAsync(new PreferenceUnsetRequest("tone_of_voice", Marta, null), Ct);

        Assert.Equal(new PreferenceDto("tone_of_voice", null, null, null, null, null, null), unset);
        await NothingWrittenAsync(db);
    }

    [Fact]
    public async Task Unsetting_an_unknown_key_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db).UnsetAsync(new PreferenceUnsetRequest("sender", Marta, null), Ct));

        Assert.Equal(["key/unknown"], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    /// <summary>Every key the catalog knows, set or not, so a reader learns what is missing in the same call.</summary>
    [Fact]
    public async Task The_list_is_every_key_the_catalog_knows_by_key_with_nulls_where_unset()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var tone = await service.SetAsync(new PreferenceSetRequest("tone_of_voice", Tone("Plain."), Marta, null), Ct);

        var page = await service.ListAsync(new PreferenceListRequest(null, null), Ct);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(new PreferenceDto("sender.identity", null, null, null, null, null, null), page.Items[0]);
        Assert.Equal(tone with { Value = null }, page.Items[1] with { Value = null });
        Assert.True(JsonNode.DeepEquals(tone.Value, page.Items[1].Value), $"The listed value is {page.Items[1].Value?.ToJsonString()}.");
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task The_list_pages_by_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);

        var first = await service.ListAsync(new PreferenceListRequest(1, null), Ct);
        var second = await service.ListAsync(new PreferenceListRequest(1, first.NextCursor), Ct);

        Assert.Equal(["sender.identity"], first.Items.Select(item => item.Key));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["tone_of_voice"], second.Items.Select(item => item.Key));
        Assert.Null(second.NextCursor);
    }

    private static async Task NothingWrittenAsync(JasonDbContext db)
    {
        Assert.Empty(await db.Preferences.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await db.Journal.AsNoTracking().ToListAsync(Ct));
    }

    private static JsonObject Sender(string name) =>
        new() { ["senders"] = new JsonArray(new JsonObject { ["name"] = name, ["gender_forms"] = "feminine", ["title"] = "Head of Partnerships" }) };

    private static JsonObject Tone(string summary) => new() { ["summary"] = summary };

    private static PreferenceService NewService(JasonDbContext db, TimeProvider? clock = null)
    {
        clock ??= new FixedClock(Noon);
        return new PreferenceService(db, new JournalWriter(clock), clock);
    }
}
