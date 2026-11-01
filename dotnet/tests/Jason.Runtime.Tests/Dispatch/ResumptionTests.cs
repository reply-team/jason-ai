using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What a role's run that follows a wait is told: composed by the claim from rows — the attempt that waited, why the wait
/// ended, and how each item it named stands — for every way a role's wait can end, with the results inline while they fit.
/// </summary>
public class ResumptionTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_that_follows_no_wait_is_told_no_resumption()
    {
        using var harness = new DispatchHarness(Noon);
        await harness.SeedClaimableAsync(Ct);

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Null(claimed.Resumption);
    }

    [Fact]
    [Critical(Family.W2, "the run after a wait is told what it waited for")]
    public async Task A_wait_released_by_its_items_is_followed_by_a_run_told_how_each_one_ended()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: WaitMode.All, until: null, children:
        [
            w => (w.Status, w.Result, w.FinishedAt) = (WorkItemStatus.Succeeded, new JsonObject { ["found"] = 3 }, Noon.AddMinutes(1)),
            w => (w.Status, w.LastError, w.FinishedAt) = (WorkItemStatus.Failed, new AttemptErrorDto("provider_unavailable", "m", false), Noon.AddMinutes(2)),
        ]);
        harness.Clock.Now = harness.Clock.Now.AddMinutes(5);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));

        var resumption = claimed.Resumption!;
        Assert.Equal(seeded.Attempt, resumption.PreviousAttemptId);
        Assert.Equal(1, resumption.ResumptionNumber);
        Assert.Equal(new DateTimeOffset(Noon), resumption.WaitedSince);
        Assert.Equal(WaitReleaseReason.ChildrenEnded, resumption.WokeBecause);
        Assert.Equal(seeded.Children, resumption.Items.Select(i => i.WorkItemId));

        var found = resumption.Items[0];
        Assert.Equal((WorkItemKind.AiRole, "researcher", WorkItemStatus.Succeeded), (found.Kind, found.Role, found.Status));
        Assert.Equal(new DateTimeOffset(Noon.AddMinutes(1)), found.FinishedAt);
        Assert.Equal(3, (int?)found.Result!["found"]);
        Assert.False(found.ResultOmitted);

        var failed = resumption.Items[1];
        Assert.Equal((WorkItemStatus.Failed, "provider_unavailable"), (failed.Status, failed.LastErrorCode));
        Assert.Null(failed.Result);
        Assert.False(failed.ResultOmitted);

        // The wait now names the run it resumed.
        await using var db = harness.Open();
        var wait = await db.WorkItemWaits.AsNoTracking().Include(w => w.ResumedByAttempt).SingleAsync(Ct);
        Assert.Equal(claimed.AttemptPublicId, wait.ResumedByAttempt?.PublicId);
    }

    [Fact]
    public async Task A_wait_on_a_moment_is_followed_by_a_run_told_the_moment_came()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: null, until: Noon.AddDays(5), children: []);
        harness.Clock.Now = Noon.AddDays(5);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var resumption = Assert.Single(await harness.ClaimAsync(Ct)).Resumption!;

        Assert.Equal(WaitReleaseReason.MomentReached, resumption.WokeBecause);
        Assert.Equal(seeded.Attempt, resumption.PreviousAttemptId);
        Assert.Empty(resumption.Items);
    }

    [Fact]
    public async Task A_wait_that_ran_into_its_ceiling_is_followed_by_a_run_told_so_with_the_items_still_open()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: WaitMode.All, until: null, children: [Later]);
        harness.Clock.Now = Noon.AddDays(14);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var resumption = Assert.Single(await harness.ClaimAsync(Ct)).Resumption!;

        Assert.Equal(WaitReleaseReason.WaitCeiling, resumption.WokeBecause);
        Assert.Equal(WorkItemStatus.Created, Assert.Single(resumption.Items).Status);
        Assert.Equal(seeded.Children[0], resumption.Items[0].WorkItemId);
    }

    [Fact]
    public async Task A_wait_a_person_ended_is_followed_by_a_run_told_a_person_resumed_it()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: WaitMode.All, until: null, children: [Later]);
        await using (var db = harness.Open())
        {
            await NewWorkItemService(db, harness.Clock).ResumeAsync(
                new WorkItemResumeRequest(seeded.Parent, new ActorRef(ActorType.Human, "marta"), "go on without the research"),
                Ct);
        }

        var resumption = Assert.Single(await harness.ClaimAsync(Ct)).Resumption!;

        Assert.Equal(WaitReleaseReason.ResumedByActor, resumption.WokeBecause);
        Assert.Equal(WorkItemStatus.Created, Assert.Single(resumption.Items).Status);
    }

    [Fact]
    [Critical(Family.W2, "the results a resumption carries are bounded")]
    public async Task Results_go_in_in_the_order_named_while_they_fit_in_one_mebibyte_and_are_flagged_after()
    {
        var large = new string('a', 400_000);
        using var harness = new DispatchHarness(Noon);
        await SeedAsync(harness, mode: WaitMode.All, until: null, children:
        [
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["text"] = large }),
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["text"] = large }),
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["text"] = large }),
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["small"] = 1 }),
            w => w.Status = WorkItemStatus.Cancelled,
        ]);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var items = Assert.Single(await harness.ClaimAsync(Ct)).Resumption!.Items;

        Assert.Equal(
            [(true, false), (true, false), (false, true), (false, true), (false, false)],
            items.Select(i => (i.Result is not null, i.ResultOmitted)));
    }

    [Fact]
    public async Task A_retry_after_the_same_release_is_told_the_same_and_the_wait_keeps_the_first_run_it_resumed()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: WaitMode.Any, until: null, children: [w => w.Status = WorkItemStatus.Succeeded]);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var first = Assert.Single(await harness.ClaimAsync(Ct));
        await using (var db = harness.Open())
        {
            await db.Attempts.Where(a => a.PublicId == first.AttemptPublicId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Failed), Ct);
            await db.WorkItems.Where(w => w.PublicId == seeded.Parent).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Created), Ct);
        }

        var second = Assert.Single(await harness.ClaimAsync(Ct));

        var (told, retold) = (first.Resumption!, second.Resumption!);
        Assert.Equal(
            (told.PreviousAttemptId, told.ResumptionNumber, told.WaitedSince, told.WokeBecause),
            (retold.PreviousAttemptId, retold.ResumptionNumber, retold.WaitedSince, retold.WokeBecause));
        Assert.Equal(told.Items, retold.Items);
        Assert.Equal(seeded.Attempt, retold.PreviousAttemptId);
        await using var check = harness.Open();
        var wait = await check.WorkItemWaits.AsNoTracking().Include(w => w.ResumedByAttempt).SingleAsync(Ct);
        Assert.Equal(first.AttemptPublicId, wait.ResumedByAttempt?.PublicId);
    }

    [Fact]
    public async Task The_second_wait_of_an_item_is_its_second_resumption()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: null, until: Noon.AddHours(1), children: []);
        harness.Clock.Now = Noon.AddHours(1);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var first = Assert.Single(await harness.ClaimAsync(Ct));
        await using (var db = harness.Open())
        {
            await db.WorkItems.Where(w => w.PublicId == seeded.Parent).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Processing), Ct);
            await db.Attempts.Where(a => a.PublicId == first.AttemptPublicId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Running), Ct);
        }

        await WaitAsync(harness, seeded.Parent, [], null, harness.Clock.Now.UtcDateTime.AddHours(1));
        harness.Clock.Now = harness.Clock.Now.AddHours(1);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var resumption = Assert.Single(await harness.ClaimAsync(Ct)).Resumption!;

        Assert.Equal(2, resumption.ResumptionNumber);
        Assert.Equal(first.AttemptPublicId, resumption.PreviousAttemptId);
        Assert.Equal(new DateTimeOffset(Noon.AddHours(1)), resumption.WaitedSince);
    }

    [Fact]
    public async Task A_run_whose_last_wait_closed_some_other_way_is_told_no_resumption_although_an_earlier_wait_was_released()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: null, until: Noon.AddHours(1), children: []);
        harness.Clock.Now = Noon.AddHours(1);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var first = Assert.Single(await harness.ClaimAsync(Ct));
        await using (var db = harness.Open())
        {
            await db.WorkItems.Where(w => w.PublicId == seeded.Parent).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Processing), Ct);
            await db.Attempts.Where(a => a.PublicId == first.AttemptPublicId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Running), Ct);
        }

        // The run told about the first wait waits again; its item's due moment passes while it waits, which closes the
        // second wait expired, and a person then gives the item more time, which reopens it.
        await WaitAsync(harness, seeded.Parent, [], null, harness.Clock.Now.UtcDateTime.AddDays(3));
        await using (var db = harness.Open())
        {
            var second = await db.WorkItemWaits.OrderByDescending(w => w.Id).FirstAsync(Ct);
            Assert.True(second.Status == WaitStatus.Open, $"the second wait is {second.Status}");
            await db.WorkItemWaits.Where(w => w.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitStatus.Expired), Ct);
            await db.WorkItems.Where(w => w.PublicId == seeded.Parent).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Created), Ct);
        }

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Null(claimed.Resumption);
        await using var check = harness.Open();
        var released = await check.WorkItemWaits.AsNoTracking().Include(w => w.ResumedByAttempt).SingleAsync(w => w.Status == WaitStatus.Released, Ct);
        Assert.Equal(first.AttemptPublicId, released.ResumedByAttempt?.PublicId);
    }

    [Fact]
    public async Task Items_named_in_another_order_than_they_were_raised_are_told_and_cut_in_the_order_named()
    {
        var large = new string('a', 400_000);
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedAsync(harness, mode: WaitMode.All, until: null, namedInReverse: true, children:
        [
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["text"] = large }),
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["text"] = large }),
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["text"] = large }),
            w => (w.Status, w.Result) = (WorkItemStatus.Succeeded, new JsonObject { ["small"] = 1 }),
        ]);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var items = Assert.Single(await harness.ClaimAsync(Ct)).Resumption!.Items;

        // Named last-raised first: the small result and two large ones fit, and the first item raised is the one cut.
        Assert.Equal(seeded.Children, items.Select(i => i.WorkItemId));
        Assert.Equal(
            [(true, false), (true, false), (true, false), (false, true)],
            items.Select(i => (i.Result is not null, i.ResultOmitted)));
    }

    /// <summary>
    /// A named item that has not started and will not for a month: a campaign with work in flight is not claimed from, so an
    /// item still open while its waiter runs again is one that is not yet due.
    /// </summary>
    private static void Later(WorkItem item) => item.NotBefore = Noon.AddDays(30);

    private sealed record Seeded(string Parent, string Attempt, IReadOnlyList<string> Children);

    /// <summary>
    /// A planner in an active campaign raises one researcher per entry, as each entry leaves it, and ends its first run
    /// waiting on them, named in the order raised or, when asked, last-raised first; the ids come back in the order named.
    /// </summary>
    private static async Task<Seeded> SeedAsync(DispatchHarness harness, WaitMode? mode, DateTime? until, IReadOnlyList<Action<WorkItem>> children, bool namedInReverse = false)
    {
        string parentId;
        List<string> childIds;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            var parent = WorkItemFactory.NewAiRole(campaign, "planner", Noon, w => w.Status = WorkItemStatus.Processing);
            WorkItemFactory.NewAttempt(parent, 1, AttemptStatus.Running, Noon);
            var kids = children.Select(configure => WorkItemFactory.NewAiRole(campaign, "researcher", Noon, configure)).ToList();
            db.Campaigns.Add(campaign);
            db.WorkItems.Add(parent);
            db.WorkItems.AddRange(kids);
            await db.SaveChangesAsync(Ct);
            (parentId, childIds) = (parent.PublicId, [.. kids.Select(k => k.PublicId)]);
            if (namedInReverse)
            {
                childIds.Reverse();
            }
        }

        var attempt = await WaitAsync(harness, parentId, childIds, mode, until);
        return new Seeded(parentId, attempt, childIds);
    }

    /// <summary>The item's running attempt ends waiting, through <see cref="AttemptOutcomes.Wait"/>; its id comes back.</summary>
    private static async Task<string> WaitAsync(DispatchHarness harness, string itemId, IReadOnlyList<string> on, WaitMode? mode, DateTime? until)
    {
        await using var scope = harness.Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var item = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.PublicId == itemId, Ct);
        var attempt = item.Attempts.Single(a => a.Status == AttemptStatus.Running);
        var targets = await db.WorkItems.Where(w => on.Contains(w.PublicId)).ToListAsync(Ct);
        scope.ServiceProvider.GetRequiredService<AttemptOutcomes>()
            .Wait(db, item, attempt, [.. on.Select(id => targets.Single(t => t.PublicId == id))], mode, until, Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }

    private static WorkItemService NewWorkItemService(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
}
