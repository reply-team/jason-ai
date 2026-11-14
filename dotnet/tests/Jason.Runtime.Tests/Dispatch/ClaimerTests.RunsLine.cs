using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Wherever the claim asks whether a person said something, a line that carries an attempt is the run's, whatever actor it
/// names: a verdict a person is named on from inside a run vouches as that run, and a standing approval's set line that
/// carries an attempt sets nothing.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>
    /// A verdict recorded under a person's name from inside the run that read the reply is that run's verdict: it releases
    /// under a standing approval only the draft a run of the same work made, as a run's own verdict does, and a draft a person
    /// wrote under it waits for a person.
    /// </summary>
    [Theory]
    [InlineData("a person drafts under it", StandingApprovals.Why.NotTheVerdictsWork)]
    [InlineData("the run drafts under it", null)]
    [Critical(Family.S2, "a standing approval: a person named from inside a run vouches as that run")]
    public async Task Standing_AVerdictNamingAPersonFromInsideARun_IsTheRunsVerdict(string drafted, string? parks)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        string verdict;
        using (RunScope.Open(reading.Run.PublicId))
        {
            verdict = await VerdictAsync(db, reading, Operator);
        }

        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.PublicId == verdict, Ct);
        Assert.Equal((ActorType.Human, reading.Run.PublicId), (line.ActorType, line.AttemptId));
        await StandAsync(db, reading.Campaign);
        Drafted(db, reading, verdict, by: drafted == "a person drafts under it" ? Operator : null);
        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await AssertReleasedOrParkedAsync(database, claimed, parks);
    }

    /// <summary>
    /// A standing approval's set line that carries an attempt was written from inside a run, which the door refuses; whatever
    /// actor it names, it sets nothing, so the draft it would have covered waits for a person.
    /// </summary>
    [Fact]
    public async Task Standing_ASetLineCarryingAnAttempt_CoversNothing()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        new JournalWriter(new FixedClock(new DateTimeOffset(Noon.AddHours(-1)))).Append(
            db,
            Operator,
            JournalKinds.StandingApprovalSet,
            reading.Campaign,
            key: "interested",
            updated: StandingApprovals.Line("interested", StandingApprovals.DefaultOperations, expiresAt: null, maxReleases: null),
            attempt: reading.Run);
        Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await AssertReleasedOrParkedAsync(database, claimed, StandingApprovals.Why.NoStandingApproval);
    }

    /// <summary>
    /// A line recorded under the desktop's chat marker is the chat's, not the person's own, as a line that carries an attempt
    /// is the run's. A verdict a person is named on in the chat vouches for nobody: the reply drafted under it waits for a
    /// person, its preview saying the verdict is not authentic; the same verdict from a terminal releases it.
    /// </summary>
    [Theory]
    [InlineData(true, StandingApprovals.Why.VerdictNotAuthentic)]
    [InlineData(false, null)]
    [Critical(Family.S2, "a standing approval: a person named in the desktop's chat vouches for nobody")]
    public async Task Standing_AVerdictNamingAPersonInTheChat_VouchesForNobody(bool inTheChat, string? parks)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        string verdict;
        using (inTheChat ? ConfinedScope.Open() : null)
        {
            verdict = await VerdictAsync(db, reading, Operator);
        }

        await StandAsync(db, reading.Campaign);
        Drafted(db, reading, verdict, by: Operator);
        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await AssertReleasedOrParkedAsync(database, claimed, parks);
    }

    /// <summary>
    /// A standing approval set in the desktop's chat is the chat's, and sets nothing: its set line carries the marker, and
    /// the draft it would have covered waits for a person.
    /// </summary>
    [Theory]
    [InlineData(true, StandingApprovals.Why.NoStandingApproval)]
    [InlineData(false, null)]
    [Critical(Family.S2, "a standing approval: one set in the desktop's chat sets nothing")]
    public async Task Standing_ASetInTheChat_CoversNothing(bool inTheChat, string? parks)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, Operator);
        using (inTheChat ? ConfinedScope.Open() : null)
        {
            await StandAsync(db, reading.Campaign);
        }

        Drafted(db, reading, verdict, by: Operator);
        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await AssertReleasedOrParkedAsync(database, claimed, parks);
    }
}
