using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// A person's add of a pair while the runtime's own stop puts the same pair on the register: the add read the register
/// before the stop had saved, so it staged an entry; the register keeps one entry per pair, and the save that lands second,
/// the person's, is refused. That refusal says the pair is already there, so the add answers as a repeat does — the entry
/// already there, and no line of its own — and the context it ran on is fit to be asked again.
/// </summary>
public class SuppressionAddRaceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private const string Address = "ada@northwind.example";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_add_whose_pair_a_stop_put_on_the_register_first_answers_the_stop_s_entry()
    {
        using var database = new TestDatabase();
        var clock = new FixedClock(Noon);
        var stop = new BeforeTheSave(
            context => context.ChangeTracker.Entries<Suppression>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                // An opt-out's stop, on a context of its own, saved between the person's read of the register and its save.
                await using var other = database.Open();
                await SuppressionService.StageAsync(other, new JournalWriter(clock), Noon.UtcDateTime, Actors.OptOut, [("email", Address)], "Said stop.", Ct);
                await other.SaveChangesAsync(Ct);
            });
        await using var db = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(stop).Options);
        var service = new SuppressionService(db, new JournalWriter(clock), clock);

        var answer = await service.AddAsync(new SuppressionAddRequest("email", "Ada@Northwind.example", null, "Asked by hand."), Ct);

        Assert.True(stop.Fired);
        await using (var read = database.Open())
        {
            var entry = await read.Suppressions.AsNoTracking().SingleAsync(Ct);
            Assert.Equal((entry.PublicId, "email", Address, "Said stop."), (answer.Id, answer.Channel, answer.Value, answer.Reason));
            var line = await read.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct);
            Assert.Equal("Said stop.", line.Reason);
        }

        // Nothing the refused save staged is left on the context: the next request on it saves only its own.
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State != EntityState.Unchanged);
        var next = await service.AddAsync(new SuppressionAddRequest("email", "grace@northwind.example", null, "Asked by hand."), Ct);
        Assert.Equal("grace@northwind.example", next.Value);
        await using (var read = database.Open())
        {
            Assert.Equal(2, await read.Suppressions.CountAsync(Ct));
            Assert.Equal(2, await read.Journal.CountAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct));
        }
    }

    /// <summary>Only the register's index of a pair answers with the entry there; any other refusal of the save is still thrown.</summary>
    [Fact]
    public async Task An_add_refused_for_anything_but_the_pair_is_still_refused()
    {
        using var database = new TestDatabase();
        var clock = new FixedClock(Noon);
        var rival = new BeforeTheSave(
            context => context.ChangeTracker.Entries<Suppression>().Any(entry => entry.State == EntityState.Added),
            async context =>
            {
                // Another pair, saved first under the very public id the add is about to save.
                var staged = context.ChangeTracker.Entries<Suppression>().Single(entry => entry.State == EntityState.Added).Entity.PublicId;
                await using var other = database.Open();
                other.Suppressions.Add(new Suppression { PublicId = staged, Channel = "email", Value = "grace@northwind.example", Reason = "Another pair.", CreatedAt = Noon.UtcDateTime });
                await other.SaveChangesAsync(Ct);
            });
        await using var db = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(rival).Options);
        var service = new SuppressionService(db, new JournalWriter(clock), clock);

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => service.AddAsync(new SuppressionAddRequest("email", Address, null, "Asked by hand."), Ct));

        Assert.True(rival.Fired);
        Assert.IsType<SqliteException>(refused.InnerException);
        Assert.False(SuppressionService.HeldFirstElsewhere(refused));
    }

    /// <summary>The stop's entry is lifted again before the re-read: nothing is there to answer, so the add is a lost race, not a failure.</summary>
    [Fact]
    public async Task An_add_whose_pair_is_lifted_again_before_the_re_read_is_answered_as_a_concurrent_update()
    {
        using var database = new TestDatabase();
        var clock = new FixedClock(Noon);
        var stop = new BeforeTheSave(
            context => context.ChangeTracker.Entries<Suppression>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var other = database.Open();
                await SuppressionService.StageAsync(other, new JournalWriter(clock), Noon.UtcDateTime, Actors.OptOut, [("email", Address)], "Said stop.", Ct);
                await other.SaveChangesAsync(Ct);
            });
        var lift = new AfterTheRefusal(async () =>
        {
            await using var other = database.Open();
            other.Suppressions.RemoveRange(await other.Suppressions.ToListAsync(Ct));
            await other.SaveChangesAsync(Ct);
        });
        await using var db = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(stop, lift).Options);
        var service = new SuppressionService(db, new JournalWriter(clock), clock);

        var lost = await Assert.ThrowsAsync<DomainException>(() => service.AddAsync(new SuppressionAddRequest("email", Address, null, "Asked by hand."), Ct));

        Assert.True(stop.Fired);
        Assert.Equal("concurrent_update", lost.Code);
    }

    /// <summary>Another writer's save, run once, the first time a save of this context is about to begin that the predicate recognises.</summary>
    private sealed class BeforeTheSave(Func<DbContext, bool> when, Func<DbContext, Task> act) : SaveChangesInterceptor
    {
        private int _fired;

        public bool Fired => _fired == 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            if (when(eventData.Context!) && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await act(eventData.Context!).ConfigureAwait(false);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Another writer's act, run once, when a save of this context has been refused.</summary>
    private sealed class AfterTheRefusal(Func<Task> act) : SaveChangesInterceptor
    {
        private int _fired;

        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await act().ConfigureAwait(false);
            }

            await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
        }
    }
}
