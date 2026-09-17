using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// Ingestion is idempotent, which is a promise to the reporter rather than to the runtime: an agent whose
/// connection dropped has no way to know whether its report landed, and the only safe thing it can do is send it
/// again. Sending it again must not invent a second effect.
/// </summary>
public class ReportDedupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_submissions_of_the_same_effect_admit_one_report_and_answer_the_same_id()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var keyed = ReportedWorld.Submission();
        keyed["idempotency_key"] = "the-follow-up-i-sent";

        var first = await service.SubmitAsync(keyed.DeepClone().AsObject(), Ct);
        var again = await service.SubmitAsync(keyed.DeepClone().AsObject(), Ct);

        Assert.Equal(ReportDedupOutcome.Admitted, first.Dedup.Outcome);
        Assert.Null(first.Dedup.Matched);
        Assert.Equal(ReportDedupOutcome.Duplicate, again.Dedup.Outcome);
        Assert.Equal(ReportDedupMatch.Key, again.Dedup.Matched);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.ReceivedAt, again.ReceivedAt);
        Assert.Equal(1, await db.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// A reporter who gave no key still gets the promise: what they said is the key, canonically. Two sends of
    /// one sentence are one effect.
    /// </summary>
    [Fact]
    public async Task Two_submissions_with_no_key_are_matched_by_what_the_reporter_said()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var first = await service.SubmitAsync(ReportedWorld.Submission(), Ct);
        var again = await service.SubmitAsync(ReportedWorld.Submission(), Ct);

        Assert.Equal(ReportDedupOutcome.Duplicate, again.Dedup.Outcome);
        Assert.Equal(ReportDedupMatch.Content, again.Dedup.Matched);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(1, await db.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// Two people describing the same effect are two assertions. Collapsing them would throw one reporter's word
    /// away, and provenance is the whole reason this row exists.
    /// </summary>
    [Fact]
    public async Task Two_reporters_who_describe_the_same_effect_are_two_reports_because_each_is_their_own_word()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var mine = await service.SubmitAsync(ReportedWorld.Submission(new ActorRef(ActorType.Human, "person-1")), Ct);
        var theirs = await service.SubmitAsync(ReportedWorld.Submission(new ActorRef(ActorType.Human, "person-2")), Ct);

        Assert.Equal(ReportDedupOutcome.Admitted, theirs.Dedup.Outcome);
        Assert.NotEqual(mine.Id, theirs.Id);
        Assert.Equal(2, await db.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// The same thing really can happen twice. The reporter is the only one who knows, so the escape hatch is
    /// theirs: a key each, and both are kept.
    /// </summary>
    [Fact]
    public async Task The_same_effect_reported_twice_on_purpose_is_two_reports_when_each_carries_its_own_key()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var morning = ReportedWorld.Submission();
        morning["idempotency_key"] = "first-send";
        var afternoon = ReportedWorld.Submission();
        afternoon["idempotency_key"] = "second-send";

        var first = await service.SubmitAsync(morning, Ct);
        var second = await service.SubmitAsync(afternoon, Ct);

        Assert.Equal(ReportDedupOutcome.Admitted, second.Dedup.Outcome);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await db.Reports.CountAsync(Ct));
    }

    /// <summary>Nothing happened the second time, so the chronicle says nothing the second time.</summary>
    [Fact]
    public async Task A_duplicate_writes_no_second_journal_line()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        await service.SubmitAsync(ReportedWorld.Submission(), Ct);
        await service.SubmitAsync(ReportedWorld.Submission(), Ct);

        Assert.Equal(1, await db.Journal.CountAsync(e => e.Kind == "external_effect_reported", Ct));
    }

    /// <summary>Two callers, two connections, one row: neither of them gets to create the second effect.</summary>
    [Fact]
    public async Task Two_callers_who_both_submit_the_same_report_end_with_one_row_and_one_id()
    {
        using var database = new TestDatabase();
        await using var one = database.Open();
        await using var two = database.Open();

        var submitted = ReportedWorld.Submission();
        submitted["idempotency_key"] = "same-key";

        var first = await ReportedWorld.Service(one).SubmitAsync(submitted.DeepClone().AsObject(), Ct);
        var second = await ReportedWorld.Service(two).SubmitAsync(submitted.DeepClone().AsObject(), Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await one.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// What actually decides a repeat is the index, not the read the service does first — which matters because
    /// the read cannot see a row that another connection has not committed yet. Written straight at the tables,
    /// because that is the only way to arrive with both rows in hand at once.
    /// </summary>
    [Theory]
    [InlineData("a-key", "a-key", "sha256:different-words")]
    [InlineData(null, null, "sha256:the-same-words")]
    public async Task The_index_refuses_a_second_report_from_one_reporter_for_the_same_key_or_the_same_words(
        string? first, string? second, string hash)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        db.Reports.Add(Row("rpt_first", first, hash));
        await db.SaveChangesAsync(Ct);

        db.Reports.Add(Row("rpt_second", second, hash));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Reports.CountAsync(Ct));
    }

    private static Report Row(string publicId, string? key, string hash) => new()
    {
        PublicId = publicId,
        ReporterType = ActorType.Human,
        ReporterId = "person-1",
        Effect = "email_sent",
        Tool = "some-other-cli",
        Summary = "what the reporter said happened",
        Assertion = new JsonObject { ["effect"] = "email_sent" },
        AssertionHash = hash,
        IdempotencyKey = key,
        ReceivedAt = ReportedWorld.Noon,
    };
}
