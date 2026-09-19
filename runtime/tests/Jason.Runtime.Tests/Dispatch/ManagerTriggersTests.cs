using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Whether a campaign's chronicle, read above its watermark, summons a manager — decided as a function over the
/// entries and nothing else. The one rule that decides whether the loop is a loop or a runaway is proved first:
/// what a check-in itself writes to the chronicle must never be what summons the next one.
/// </summary>
public class ManagerTriggersTests
{
    private static readonly IReadOnlySet<string> OnFailure = new HashSet<string>(StringComparer.Ordinal) { JournalKinds.WorkItemFailed };

    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> OnAnswer = new HashSet<string>(StringComparer.Ordinal) { JournalKinds.DecisionAnswered };

    private static bool Nobody(ChronicleLine line) => false;

    /// <summary>
    /// A manager whose host is missing fails at pre-flight, and the failure is a <c>workitem_failed</c> line like
    /// any other. If that line could summon a manager, the next one would fail the same way and summon a third,
    /// for as long as the runtime ran. So the caller says which lines are a check-in's own, and those are not
    /// read at all — not a cause, not counted — while the watermark still passes over them, or the same lines
    /// would be asked about on every scan.
    /// </summary>
    [Fact]
    public void A_check_in_of_our_own_making_never_summons_another()
    {
        var entries = new[]
        {
            Entry(11, JournalKinds.WorkItemFailed, workItemId: "wi_check_in", attemptId: "att_1"),
            Entry(12, JournalKinds.WorkItemFailed, actor: ActorType.Attempt, actorId: "att_1"),
        };

        var read = ManagerTriggers.Read(entries, OnFailure, Ours("wi_check_in", "att_1"), watermark: 10);

        Assert.Null(read.Cause);
        Assert.Equal(12, read.Watermark);
    }

    /// <summary>
    /// A person's line about a check-in's own attempt is read, because the exclusion is about a review
    /// summoning itself and a person acting on a review's work is not that. This is the case the whole
    /// continuation rests on: an answer to a review's own question names that review's attempt.
    /// </summary>
    [Fact]
    public void A_line_a_person_wrote_about_a_check_ins_attempt_is_a_cause()
    {
        var entries = new[]
        {
            Entry(11, JournalKinds.DecisionAnswered, workItemId: "wi_check_in", attemptId: "att_1", actor: ActorType.Human, actorId: "ada"),
        };

        var read = ManagerTriggers.Read(entries, OnAnswer, Ours("wi_check_in", "att_1"), watermark: 10);

        var cause = Assert.NotNull(read.Cause);
        Assert.Equal(JournalKinds.DecisionAnswered, cause.Kind);
        Assert.Equal("att_1", cause.AttemptId);
    }

    /// <summary>
    /// And the same kind written by the review's own attempt is not, so the exemption really is about who
    /// acted rather than about which kind of line it was.
    /// </summary>
    [Fact]
    public void The_same_kind_written_by_the_check_ins_own_attempt_is_not()
    {
        var entries = new[]
        {
            Entry(11, JournalKinds.DecisionAnswered, actor: ActorType.Attempt, actorId: "att_1"),
        };

        var read = ManagerTriggers.Read(entries, OnAnswer, Ours("wi_check_in", "att_1"), watermark: 10);

        Assert.Null(read.Cause);
        Assert.Equal(11, read.Watermark);
    }

    /// <summary>The exclusion is per line, not per read: a check-in's failure beside a real one hides only itself.</summary>
    [Fact]
    public void A_check_ins_own_line_is_skipped_and_the_next_real_one_is_the_cause()
    {
        var entries = new[]
        {
            Entry(11, JournalKinds.WorkItemFailed, workItemId: "wi_check_in"),
            Entry(12, JournalKinds.WorkItemFailed, workItemId: "wi_real", attemptId: "att_real"),
        };

        var read = ManagerTriggers.Read(entries, OnFailure, Ours("wi_check_in", "att_1"), watermark: 10);

        var cause = Assert.NotNull(read.Cause);
        Assert.Equal("jrn_00012", cause.JournalEntryId);
        Assert.Equal(1, cause.QualifyingCount);
    }

    [Fact]
    public void The_first_entry_of_a_trigger_kind_is_the_cause()
    {
        var entries = new[]
        {
            Entry(5, JournalKinds.CampaignUpdated),
            Entry(6, JournalKinds.WorkItemFailed, workItemId: "wi_first", attemptId: "att_first"),
            Entry(7, JournalKinds.WorkItemFailed, workItemId: "wi_second", attemptId: "att_second"),
        };

        var read = ManagerTriggers.Read(entries, OnFailure, Nobody, watermark: 4);

        var cause = Assert.NotNull(read.Cause);
        Assert.Equal(JournalKinds.WorkItemFailed, cause.Kind);
        Assert.Equal("jrn_00006", cause.JournalEntryId);
        Assert.Equal("wi_first", cause.WorkItemId);
        Assert.Equal("att_first", cause.AttemptId);
    }

    /// <summary>
    /// The manager about to be launched reads the whole chronicle, so everything in this read is accounted for
    /// by the check-in this read creates. Ten failures in a burst are one launch; a watermark left at the cause
    /// would make them ten.
    /// </summary>
    [Fact]
    public void The_watermark_moves_to_the_last_entry_read_not_to_the_cause()
    {
        var entries = new[]
        {
            Entry(5, JournalKinds.WorkItemFailed),
            Entry(6, JournalKinds.WorkItemFailed),
            Entry(7, JournalKinds.CampaignUpdated),
        };

        var read = ManagerTriggers.Read(entries, OnFailure, Nobody, watermark: 4);

        Assert.Equal("jrn_00005", Assert.NotNull(read.Cause).JournalEntryId);
        Assert.Equal(7, read.Watermark);
    }

    [Fact]
    public void The_watermark_never_moves_backwards()
    {
        var entries = new[] { Entry(5, JournalKinds.WorkItemFailed), Entry(6, JournalKinds.WorkItemFailed) };

        var read = ManagerTriggers.Read(entries, OnFailure, Nobody, watermark: 100);

        Assert.Equal(100, read.Watermark);
    }

    /// <summary>A number for the record: how many lines qualified, after the ones that are not read.</summary>
    [Fact]
    public void The_count_is_how_many_lines_qualified_after_exclusions()
    {
        var entries = new[]
        {
            Entry(1, JournalKinds.WorkItemFailed),
            Entry(2, JournalKinds.ContactsAdded),
            Entry(3, JournalKinds.WorkItemFailed, workItemId: "wi_check_in"),
            Entry(4, JournalKinds.WorkItemFailed),
            Entry(5, JournalKinds.WorkItemFailed),
        };

        var read = ManagerTriggers.Read(entries, OnFailure, Ours("wi_check_in", "att_1"), watermark: 0);

        Assert.Equal(3, Assert.NotNull(read.Cause).QualifyingCount);
    }

    /// <summary>No triggers is cadence only, on purpose — and the chronicle is still accounted for as it goes by.</summary>
    [Fact]
    public void No_triggers_means_no_cause_but_the_watermark_still_moves()
    {
        var entries = new[] { Entry(5, JournalKinds.WorkItemFailed), Entry(6, JournalKinds.WorkItemFailed) };

        var read = ManagerTriggers.Read(entries, Nothing, Nobody, watermark: 4);

        Assert.Null(read.Cause);
        Assert.Equal(6, read.Watermark);
    }

    [Fact]
    public void Nothing_read_leaves_the_watermark_where_it_was()
    {
        var read = ManagerTriggers.Read([], OnFailure, Nobody, watermark: 42);

        Assert.Null(read.Cause);
        Assert.Equal(42, read.Watermark);
    }

    /// <summary>
    /// The columns that say what happened — key, before, after, reason — cannot reach this function at all:
    /// the line it is given has nowhere to put them, and the query that builds one never selects them. The rule
    /// that the dispatcher reads no meaning used to be a discipline and is now a shape.
    /// </summary>
    [Fact]
    public void A_line_the_summon_reads_cannot_carry_meaning()
    {
        var carried = typeof(ChronicleLine)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            ["ActorId", "ActorType", "AttemptId", "Id", "Kind", "PublicId", "WorkItemId"],
            carried);
    }

    /// <summary>And the cause carries no more than the line did: identifiers, a kind and a number.</summary>
    [Fact]
    public void The_cause_is_identifiers_a_kind_and_a_number()
    {
        var entries = new[] { Entry(9, JournalKinds.WorkItemFailed, workItemId: "wi_a", attemptId: "att_a") };

        var read = ManagerTriggers.Read(entries, OnFailure, Nobody, watermark: 8);

        Assert.Equal(new ManagerCause(JournalKinds.WorkItemFailed, "jrn_00009", "wi_a", "att_a", DecisionId: null, 1), read.Cause);
    }

    /// <summary>
    /// The predicate the summon really computes, in the shape it really computes it: a line about a check-in,
    /// a line about one's attempt, or a line <em>written by</em> one's attempt — that last read from the actor,
    /// because the chronicle leaves the attempt column empty for a report and names the reporter instead. And
    /// none of it applied to a line a <em>person</em> wrote, because a person acting on a review's work is not
    /// the loop feeding itself.
    /// </summary>
    /// <remarks>
    /// A stand-in looser or stricter than production proves something production does not do, and this file
    /// has already been wrong that way once: it matched an actor where production did not, so the rule it
    /// proved was not the rule that shipped. It is a mirror of the shape in <c>Summoner.OursAsync</c>, and the
    /// two tests below are the cases that catch it drifting either way.
    /// </remarks>
    private static Func<ChronicleLine, bool> Ours(string checkInId, string attemptId) =>
        entry => entry.ActorType != ActorType.Human
            && (entry.WorkItemId == checkInId
                || entry.AttemptId == attemptId
                || (entry.ActorType == ActorType.Attempt && entry.ActorId == attemptId));

    /// <summary>
    /// A line as the summon is given one. There is nowhere in it to put a key, a reason or a document, which is
    /// the point: the rule that the dispatcher reads no meaning is now a shape rather than a discipline, and
    /// this factory could not break it if it tried.
    /// </summary>
    private static ChronicleLine Entry(
        int id,
        string kind,
        string? workItemId = null,
        string? attemptId = null,
        ActorType actor = ActorType.System,
        string? actorId = null)
        => new(id, $"jrn_{id:D5}", kind, workItemId, attemptId, actor, actorId);
}
