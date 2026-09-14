using Jason.Contracts.Api;
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
        await service.AddAsync(new SuppressionAddRequest("linkedin", "https://example.com/in/ada", null, "asked to stop"), TestContext.Current.CancellationToken);

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
