using Jason.Contracts.Api;
using Jason.Contracts.Channels;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Contacts;

public class SuppressionServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static SuppressionService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock);

    [Fact]
    [Critical(Family.S1, "the register stores keys")]
    public async Task An_entry_is_stored_under_the_normalized_value_of_its_channel()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var added = await service.AddAsync(new SuppressionAddRequest("Email", " Foo@Bar.com ", null, "unsubscribed"), TestContext.Current.CancellationToken);

        Assert.StartsWith("sup_", added.Id, StringComparison.Ordinal);
        Assert.Equal("email", added.Channel);
        Assert.Equal("foo@bar.com", added.Value);
        Assert.Equal("unsubscribed", added.Reason);
        Assert.Equal(Noon, added.CreatedAt);

        var entry = Assert.Single(await db.Journal.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JournalKinds.SuppressionAdded, entry.Kind);
        Assert.Null(entry.CampaignId);
        Assert.Equal("email", entry.Key);
        Assert.Equal("foo@bar.com", (string?)entry.New!.AsObject()["value"]);
    }

    [Fact]
    public async Task Adding_the_same_pair_again_answers_with_the_entry_that_is_already_there()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var first = await service.AddAsync(new SuppressionAddRequest("email", "foo@bar.com", null, "unsubscribed"), TestContext.Current.CancellationToken);

        var again = await service.AddAsync(new SuppressionAddRequest("email", "FOO@BAR.com", null, "asked twice"), TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal("unsubscribed", again.Reason);
        Assert.Single(await db.Suppressions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.Journal.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A LinkedIn profile link names a person by its slug, so a stop written under one spelling and asked about under
    /// another is the same stop — its case, a doubled slash, a query, another of LinkedIn's hosts, no scheme or an escape
    /// make no second one — and the value a contact's channel is stored under, by the same rule, is refused whichever
    /// spelling it was written in.
    /// </summary>
    [Fact]
    [Critical(Family.F1, "one stop per LinkedIn person")]
    public async Task A_linkedin_stop_is_one_stop_under_any_spelling_of_the_profile()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        string[] spellings =
        [
            "https://www.linkedin.com/in/Marta-Example",
            "https://WWW.linkedin.com/in/MARTA-example//",
            "https://www.linkedin.com/in/marta-example?trk=public_profile",
            "uk.linkedin.com/in/marta%2Dexample/",
        ];

        var first = await service.AddAsync(new SuppressionAddRequest("linkedin", spellings[0], null, "said stop"), TestContext.Current.CancellationToken);
        foreach (var spelling in spellings.Skip(1))
        {
            var again = await service.AddAsync(new SuppressionAddRequest("linkedin", spelling, null, "asked twice"), TestContext.Current.CancellationToken);
            Assert.True(again.Id == first.Id, $"'{spelling}' was recorded as a stop of its own, '{again.Value}'.");
        }

        Assert.Equal("https://www.linkedin.com/in/marta-example", first.Value);
        Assert.Single(await db.Suppressions.ToListAsync(TestContext.Current.CancellationToken));

        foreach (var spelling in spellings)
        {
            var stored = ChannelRules.KeyOf("linkedin", spelling);
            Assert.True(await service.IsSuppressedAsync(db, "linkedin", stored!, TestContext.Current.CancellationToken), $"'{stored}', stored for '{spelling}', is not refused.");
        }
    }

    /// <summary>A stop is lifted by any spelling of the profile it was recorded under.</summary>
    [Fact]
    [Critical(Family.F2, "the lift finds the key")]
    public async Task A_linkedin_stop_is_lifted_by_another_spelling_of_its_profile()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        await service.AddAsync(new SuppressionAddRequest("linkedin", "https://www.linkedin.com/in/marta-example", null, "said stop"), TestContext.Current.CancellationToken);

        var removed = await service.RemoveAsync(
            new SuppressionRemoveRequest("linkedin", "https://uk.linkedin.com/in/Marta-Example/?trk=feed", null, "asked to be in touch again"),
            TestContext.Current.CancellationToken);

        Assert.Equal(new SuppressionRemovedDto("linkedin", "https://www.linkedin.com/in/marta-example", true), removed);
        Assert.Empty(await db.Suppressions.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An entry written before the rule, under a text the rule now refuses — the start that re-keyed the register left it
    /// as it stood — is found by that exact text: listed by it, and lifted by it, so no stop is beyond the API's reach.
    /// The text is looked up as it stands before the rule is asked, so it is found whatever the rule refuses it for: not
    /// a profile link, or longer than a value may be given.
    /// </summary>
    [Theory]
    [Critical(Family.F2, "a pre-key entry is lifted by its own text")]
    [InlineData(ChannelRefusal.Invalid)]
    [InlineData(ChannelRefusal.TooLong)]
    public async Task An_entry_under_a_text_the_rule_now_refuses_is_listed_and_lifted_by_that_text(ChannelRefusal refusedFor)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var left = "https://www.linkedin.com/pub/marta-example/" + (refusedFor == ChannelRefusal.TooLong ? new string('x', ChannelRules.MaxValueLength) : "1/2/3");
        Assert.Null(ChannelRules.NormalizeValue("linkedin", left, out var refusal));
        Assert.True(refusal == refusedFor, $"the text is refused as {refusal}.");
        db.Suppressions.Add(new Suppression { PublicId = "sup_left", Channel = "linkedin", Value = left, Reason = "said stop", CreatedAt = Noon.UtcDateTime });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var listed = await service.ListAsync(new SuppressionListRequest("linkedin", " " + left + " ", null, null), TestContext.Current.CancellationToken);
        Assert.Equal("sup_left", Assert.Single(listed.Items).Id);

        var removed = await service.RemoveAsync(new SuppressionRemoveRequest("linkedin", left, null, "the stop was recorded twice"), TestContext.Current.CancellationToken);
        Assert.Equal(new SuppressionRemovedDto("linkedin", left, true), removed);
        Assert.Empty(await db.Suppressions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.Journal.Where(line => line.Kind == JournalKinds.SuppressionRemoved).ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A text the rule refuses that no entry holds is what it always was: a value of the wrong form.</summary>
    [Fact]
    public async Task A_text_the_rule_refuses_that_no_entry_holds_is_still_invalid()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var removing = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RemoveAsync(new SuppressionRemoveRequest("linkedin", "https://www.linkedin.com/pub/marta-example/1/2/3", null, "lift it"), TestContext.Current.CancellationToken));
        var listing = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(new SuppressionListRequest("linkedin", "https://www.linkedin.com/pub/marta-example/1/2/3", null, null), TestContext.Current.CancellationToken));

        Assert.Equal(("value", "invalid"), (Assert.Single(removing.Details!).Field, Assert.Single(removing.Details!).Code));
        Assert.Equal(("value", "invalid"), (Assert.Single(listing.Details!).Field, Assert.Single(listing.Details!).Code));
    }

    [Fact]
    public async Task A_channel_and_a_value_are_both_required_and_both_are_checked_at_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddAsync(new SuppressionAddRequest(null, null, null, null), TestContext.Current.CancellationToken));

        Assert.Equal(["channel", "value"], ex.Details!.Select(detail => detail.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Critical(Family.S1, "a lift needs a reason")]
    public async Task Lifting_a_suppression_without_a_reason_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        await service.AddAsync(new SuppressionAddRequest("email", "foo@bar.com", null, "unsubscribed"), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RemoveAsync(new SuppressionRemoveRequest("email", "foo@bar.com", null, "  "), TestContext.Current.CancellationToken));

        var detail = Assert.Single(ex.Details!);
        Assert.Equal("reason", detail.Field);
        Assert.Equal("required", detail.Code);
        Assert.Single(await db.Suppressions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lifting_a_suppression_removes_the_row_and_says_so_in_the_chronicle()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        await service.AddAsync(new SuppressionAddRequest("email", "foo@bar.com", null, "unsubscribed"), TestContext.Current.CancellationToken);

        var removed = await service.RemoveAsync(new SuppressionRemoveRequest("Email", " FOO@bar.com ", null, "they asked to be reinstated"), TestContext.Current.CancellationToken);

        Assert.Equal(new SuppressionRemovedDto("email", "foo@bar.com", true), removed);
        Assert.Empty(await db.Suppressions.ToListAsync(TestContext.Current.CancellationToken));

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.SuppressionRemoved).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null(entry.CampaignId);
        Assert.Equal("email", entry.Key);
        Assert.Equal("they asked to be reinstated", entry.Reason);
        Assert.Equal("foo@bar.com", (string?)entry.New!.AsObject()["value"]);
    }

    [Fact]
    public async Task Lifting_a_suppression_that_is_not_there_is_a_quiet_no()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var removed = await service.RemoveAsync(new SuppressionRemoveRequest("email", "foo@bar.com", null, "housekeeping"), TestContext.Current.CancellationToken);

        Assert.Equal(new SuppressionRemovedDto("email", "foo@bar.com", false), removed);
        Assert.Empty(await db.Journal.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_listing_filters_by_channel_and_by_the_normalized_value()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        await service.AddAsync(new SuppressionAddRequest("email", "foo@bar.com", null, "unsubscribed"), TestContext.Current.CancellationToken);
        await service.AddAsync(new SuppressionAddRequest("email", "other@bar.com", null, "bounced"), TestContext.Current.CancellationToken);
        await service.AddAsync(new SuppressionAddRequest("linkedin", "https://www.linkedin.com/in/ada-example", null, "asked to stop"), TestContext.Current.CancellationToken);

        var all = await service.ListAsync(new SuppressionListRequest(null, null, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(3, all.Items.Count);

        var byChannel = await service.ListAsync(new SuppressionListRequest("Email", null, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(2, byChannel.Items.Count);

        var byValue = await service.ListAsync(new SuppressionListRequest("email", "FOO@BAR.com", null, null), TestContext.Current.CancellationToken);
        Assert.Equal("foo@bar.com", Assert.Single(byValue.Items).Value);
    }

    [Fact]
    public async Task A_value_filter_without_a_channel_is_a_usage_error()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(new SuppressionListRequest(null, "foo@bar.com", null, null), TestContext.Current.CancellationToken));

        Assert.Equal("value", Assert.Single(ex.Details!).Field);
    }

    [Fact]
    public async Task A_listing_walks_its_pages_in_creation_order()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            created.Add((await service.AddAsync(new SuppressionAddRequest("email", $"a{index}@bar.com", null, "bounced"), TestContext.Current.CancellationToken)).Id);
        }

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await service.ListAsync(new SuppressionListRequest(null, null, 2, cursor), TestContext.Current.CancellationToken);
            seen.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(created.Order(StringComparer.Ordinal), seen);
    }
}
