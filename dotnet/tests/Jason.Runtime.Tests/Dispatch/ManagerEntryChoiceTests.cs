using Jason.Contracts.Execution;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Which entry a check-in is created with, decided as a function over a kind, one comparison of identifiers and
/// whether the scheduled review is due. Every row of the table <c>docs/campaign-manager.md</c> §5 prints is a case
/// here, so a row that changed its answer is a red test before it is a page that lies.
/// </summary>
public class ManagerEntryChoiceTests
{
    [Fact]
    public void The_campaigns_first_start_is_the_start_entry()
    {
        var entry = ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.CampaignStarted, FirstStart: true, ReviewDue: false));

        Assert.Equal("start", entry);
    }

    [Fact]
    public void A_later_start_is_the_resume_entry()
    {
        var entry = ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.CampaignStarted, FirstStart: false, ReviewDue: false));

        Assert.Equal("resume", entry);
    }

    [Fact]
    public void A_thread_that_moved_is_the_inbound_reply_entry()
    {
        var entry = ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.InboundReplyObserved, FirstStart: false, ReviewDue: false));

        Assert.Equal("inbound_reply", entry);
    }

    /// <summary>Every other kind a review can be caused by is the general text, whether or not the cadence was due as well.</summary>
    [Fact]
    public void Any_other_cause_names_no_entry()
    {
        var others = ManagerOptions.Default
            .Where(kind => kind != JournalKinds.CampaignStarted && kind != JournalKinds.InboundReplyObserved)
            .ToList();

        Assert.NotEmpty(others);
        foreach (var kind in others)
        {
            Assert.Null(ManagerEntryChoice.Choose(new ManagerEntryInputs(kind, FirstStart: false, ReviewDue: false)));
            Assert.Null(ManagerEntryChoice.Choose(new ManagerEntryInputs(kind, FirstStart: false, ReviewDue: true)));
        }
    }

    [Fact]
    public void No_cause_and_the_review_due_is_the_quick_look()
    {
        var entry = ManagerEntryChoice.Choose(new ManagerEntryInputs(CauseKind: null, FirstStart: false, ReviewDue: true));

        Assert.Equal("quick_look", entry);
    }

    /// <summary>A cause wins over the cadence: a start that came with a due review is still a start.</summary>
    [Fact]
    public void A_cause_wins_over_a_due_review()
    {
        Assert.Equal("start", ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.CampaignStarted, FirstStart: true, ReviewDue: true)));
        Assert.Equal("inbound_reply", ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.InboundReplyObserved, FirstStart: false, ReviewDue: true)));
    }

    /// <summary>Nothing caused and nothing due is no check-in at all, so it names no entry either.</summary>
    [Fact]
    public void No_cause_and_nothing_due_names_no_entry()
    {
        Assert.Null(ManagerEntryChoice.Choose(new ManagerEntryInputs(CauseKind: null, FirstStart: false, ReviewDue: false)));
    }

    /// <summary>A first start is a fact about a start line alone: on any other kind it changes nothing.</summary>
    [Fact]
    public void Being_the_first_start_means_nothing_on_another_kind()
    {
        Assert.Equal("inbound_reply", ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.InboundReplyObserved, FirstStart: true, ReviewDue: false)));
        Assert.Null(ManagerEntryChoice.Choose(new ManagerEntryInputs(JournalKinds.WorkItemFailed, FirstStart: true, ReviewDue: false)));
    }

    /// <summary>Every name the function can give is one the manager carries, so the one writer never refuses it.</summary>
    [Fact]
    public void Every_name_it_gives_is_one_the_manager_carries()
    {
        foreach (var name in new[] { ManagerEntryChoice.Start, ManagerEntryChoice.Resume, ManagerEntryChoice.InboundReply, ManagerEntryChoice.QuickLook })
        {
            Assert.True(RoleEntries.Carries(ManagerCheckIn.Role, name), name);
        }
    }
}
