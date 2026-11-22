using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// An item loaded with everything the mapper needs carries two collections once its attempts are loaded too: the
/// person's held memberships and the item's attempts. Every shape the runtime loads it in says how it is to be split, so
/// EF Core has nothing to warn about in the runtime's log, and each shape still loads exactly what it loaded before.
/// </summary>
public partial class WorkItemQueriesTests
{
    /// <summary>
    /// The shapes the runtime loads an item in — by its id with its attempts, a listing's page with its live attempt, and
    /// a set of items without their attempts — log no warning of EF Core's, and load the person's held memberships, every
    /// campaign's, and the attempts asked for.
    /// </summary>
    [Fact]
    public async Task Every_shape_an_item_is_loaded_in_logs_no_warning_and_loads_what_it_asks_for()
    {
        using var database = new TestDatabase();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var elsewhere = WorkItemFactory.NewCampaign("Elsewhere", now: Noon);
        var person = new Contact { PublicId = Jason.Contracts.Ids.PublicId.New("cnt"), CreatedAt = Noon, UpdatedAt = Noon };
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = person;
        await using (var db = database.Open())
        {
            db.Campaigns.AddRange(campaign, elsewhere);
            db.Contacts.Add(person);
            db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = person, State = MembershipState.Paused, AddedAt = Noon, UpdatedAt = Noon });
            db.CampaignContacts.Add(new CampaignContact { Campaign = elsewhere, Contact = person, State = MembershipState.Paused, AddedAt = Noon, UpdatedAt = Noon });
            db.WorkItems.Add(item);
            db.Attempts.Add(WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon.AddMinutes(-9)));
            db.Attempts.Add(WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Running, Noon.AddMinutes(-1)));
            await db.SaveChangesAsync(Ct);
        }

        var warnings = new List<string>();
        var options = new DbContextOptionsBuilder<JasonDbContext>(database.Options)
            .LogTo((_, level) => level >= LogLevel.Warning, (EventData said) => warnings.Add(said.EventId.Name ?? said.EventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Options;
        await using var read = new JasonDbContext(options);

        var byId = await read.WorkItems.AsNoTracking().WithNavigation().Include(w => w.Attempts)
            .FirstOrDefaultAsync(w => w.PublicId == item.PublicId, Ct);
        var page = await read.WorkItems.OrderBy(w => w.Id).AsNoTracking().WithNavigation()
            .Include(w => w.Attempts.Where(a => a.Status == AttemptStatus.Scheduled || a.Status == AttemptStatus.Running))
            .Take(2)
            .ToListAsync(Ct);
        var ids = new[] { item.Id };
        var set = await read.WorkItems.WithNavigation().Where(w => ids.Contains(w.Id)).ToListAsync(Ct);

        Assert.Empty(warnings);
        Assert.Equal(2, byId!.Contact!.Memberships.Count);
        Assert.Equal(2, byId.Attempts.Count);
        Assert.Equal(AttemptStatus.Running, Assert.Single(Assert.Single(page).Attempts).Status);
        Assert.Equal(2, Assert.Single(set).Contact!.Memberships.Count);
        Assert.False(WorkItemQueries.IsEligible(byId, Noon));
    }
}
