using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The loop end to end, against real child processes: something happens, or enough time passes, and a manager
/// is launched to look at the campaign. Everything here goes through the parts that ship — the chronicle, the
/// summon, the claim, the launcher and the API — so what is proved is that they fit together, not that each of
/// them behaves as its own test says.
/// </summary>
public class ManagerLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every role launchable through the stand-in host, which is how the built-in manager gets a command
    /// without anybody registering a role called "manager" that already exists. A role with a command of its
    /// own — the ones the tests add — still wins over this.
    /// </summary>
    private static string Settings(int reviewSeconds, params string[] managerOptions)
    {
        var command = string.Join(
            ",",
            new[] { "dotnet", Execution.FakeAgentHost.Dll, "manager" }.Concat(managerOptions).Select(part => JsonSerializer.Serialize(part)));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"Dispatcher\":{{\"TickSeconds\":3600,\"RetryDelaySeconds\":0,\"ExitGraceSeconds\":30,"
            + $"\"AiRole\":{{\"TimeoutSeconds\":60,\"HeartbeatSeconds\":10,\"MaxAttempts\":2}}}},"
            + $"\"Roles\":{{\"DefaultEntryCommand\":[{command}]}},"
            + $"\"Manager\":{{\"ReviewSeconds\":{reviewSeconds},\"TimeoutSeconds\":60}}}}");
    }

    /// <summary>
    /// A failure is the manager's inbox. The item fails for real — a child that crashes, an attempt that ends,
    /// a line in the chronicle — and the next scan turns that line into a review, claims it, and launches a
    /// manager that answers in the shape a check-in is held to.
    /// </summary>
    [Fact]
    public async Task A_failed_item_puts_a_manager_on_the_queue_and_a_succeeded_one_does_not()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 86_400));
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-crash", Ct, "crash");
        var doomed = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-crash", max_attempts = 1 },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(doomed.Id, WorkItemStatus.Failed, Ct);
        var failedAttempt = (await host.GetAsync(doomed.Id, Ct)).Attempts!.Single();

        // The next scan reads that line, summons a review, and hands it out in the same pass.
        var report = await host.ScanAsync(Ct);
        Assert.Equal(1, report.Summoned);
        Assert.Equal(1, report.Claimed);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal("triggered", (string?)checkIn.Context["review_intent"]);
        Assert.Equal("workitem_failed", (string?)checkIn.Context["trigger"]);
        Assert.Equal(failedAttempt.Id, (string?)checkIn.Context["cause"]!["attempt_id"]);

        // The chain the review belongs to is the chain of the thing it is about — handed on one hop, exactly
        // as it stands. Nothing in this chain ever ran under a profile (the role's own entry command launched
        // it), so what is handed on is root: the crashed item's own record, not a record invented for the
        // review. That a pinned profile comes through as inherited is proved against a database in
        // ManagerCheckInTests; what matters here is that the record comes from the cause at all.
        Assert.Equal(LineageState.Root, checkIn.Lineage!.State);

        var finished = await host.WaitForStatusAsync(checkIn.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal("nothing", (string?)finished.Result!["outcome"]);
        Assert.NotNull((string?)finished.Result["summary"]);
    }

    /// <summary>
    /// The chain, where there is one to keep. The failed item ran under a profile, so its attempt pinned one,
    /// and the review of that failure inherits it — the profile, the revision and the attempt it came from.
    /// </summary>
    /// <remarks>
    /// Asserting <c>Root</c> for a triggered review, as the sibling test above does, cannot tell lineage read
    /// from the cause from lineage read from the creating actor: both answer root when nothing in the chain
    /// ever had a profile. This one can only pass if the record came from the cause.
    /// <para>
    /// What the check-in then does with that inherited profile is not this test's business: it resolves to a
    /// stand-in host launched with a composed command line that names no behaviour, so it ends as a failed
    /// attempt. The chain is what is being proved, and the chain is written when the item is created.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_review_of_work_that_ran_under_a_profile_inherits_it()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 86_400));
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "stand-in",
                host = "claude_code",
                program = ProfileFixture.Program,
                deny = new[] { "Write" },
                host_version_verified = "stand-in",
            },
            Ct);

        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");
        var doomed = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "profiled",
                execution_profile = "stand-in",
                max_attempts = 1,
                context = new { behaviour = "crash" },
            },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(doomed.Id, WorkItemStatus.Failed, Ct);
        var attempt = (await host.GetAsync(doomed.Id, Ct)).Attempts!.Single();
        Assert.Equal("stand-in", attempt.Provenance!.Agent!.ProfileName);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Summoned);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal(LineageState.Inherited, checkIn.Lineage!.State);
        Assert.Equal("stand-in", checkIn.Lineage.ProfileName);
        Assert.Equal(attempt.Provenance.Agent.ProfileRevision, checkIn.Lineage.ProfileRevision);
        Assert.Equal(attempt.Id, checkIn.Lineage.FromAttemptId);
    }

    /// <summary>
    /// And the other half of the same rule: work that went well is not something to wake anybody for. Reviewing
    /// every successful step was rejected as cost without value, and this is where that decision lives.
    /// </summary>
    [Fact]
    public async Task A_succeeded_item_summons_nobody()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 86_400));
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-succeed", Ct, "succeed");
        var good = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-succeed" },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(good.Id, WorkItemStatus.Succeeded, Ct);

        var report = await host.ScanAsync(Ct);

        Assert.Equal(0, report.Summoned);
        Assert.Equal(0, report.Claimed);
    }

    /// <summary>
    /// Silence is the case the cadence exists for: work that simply sits there produces no line at all, so
    /// nothing would ever summon a review of it.
    /// </summary>
    [Fact]
    public async Task A_due_review_is_created_with_nothing_in_the_chronicle_to_cause_it()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 3_600));
        var campaign = await host.CampaignAsync(Ct);

        // Not yet: the campaign went live a moment ago.
        Assert.Equal(0, (await host.ScanAsync(Ct)).Summoned);

        host.Clock.Advance(TimeSpan.FromSeconds(3_600));
        var report = await host.ScanAsync(Ct);
        Assert.Equal(1, report.Summoned);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal("scheduled", (string?)checkIn.Context["review_intent"]);
        Assert.Null(checkIn.Context["trigger"]);
        Assert.Equal(LineageState.Root, checkIn.Lineage!.State);

        var finished = await host.WaitForStatusAsync(checkIn.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal("nothing", (string?)finished.Result!["outcome"]);

        // And the loop keeps going: that review is over, the next one falls due, and the campaign is looked at
        // again without anybody asking. That a second one is never created while the first is still open is
        // asserted where no child process can finish it first — in the summoner's own tests.
        host.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(1, (await host.ScanAsync(Ct)).Summoned);
    }

    /// <summary>
    /// A review is work, so it waits its turn like work. The campaign is busy, the check-in is created and not
    /// claimed, and nothing about being a review jumps the queue.
    /// </summary>
    [Fact]
    public async Task A_check_in_for_a_busy_campaign_is_queued_rather_than_skipped()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 3_600));
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-hang", Ct, "hang");
        // A budget long enough to outlive the clock move below: what is being tested is a campaign that is
        // busy when the review falls due, not one whose lease ran out while nobody was looking.
        var busy = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-hang", timeout_seconds = 86_400, heartbeat_seconds = 0 },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitAsync(busy.Id, item => item.Status == WorkItemStatus.Processing, Ct, "processing");

        host.Clock.Advance(TimeSpan.FromSeconds(3_600));
        var report = await host.ScanAsync(Ct);

        Assert.Equal(1, report.Summoned);
        Assert.Equal(0, report.Claimed);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal(WorkItemStatus.Created, checkIn.Status);
    }

    /// <summary>
    /// The round trip, end to end and against real child processes. A role runs, finds a question it cannot
    /// answer, leaves it behind and ends; a person answers it; and the next scan creates the review that
    /// carries on — belonging to the chain of the attempt that asked.
    /// </summary>
    /// <remarks>
    /// The chain is what discriminates, and only together with the revision. The escalating attempt ran under
    /// a profile and pinned it, and root work here would have resolved to a profile too — so only
    /// <c>Inherited</c> carrying that exact profile and revision is lineage taken from the cause.
    /// <c>Root</c> carrying the same profile is lineage taken from the actor, which is the mistake the whole
    /// rule exists to prevent.
    /// </remarks>
    [Fact]
    public async Task An_answered_decision_releases_the_next_review_with_the_chain_intact()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 86_400));
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "stand-in",
                host = "claude_code",
                program = ProfileFixture.Program,
                deny = new[] { "Write" },
                host_version_verified = "stand-in",
            },
            Ct);

        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("asking", Ct, "silent");
        var asked = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "asking",
                execution_profile = "stand-in",
                max_attempts = 1,
                context = new { behaviour = "manager --escalate" },
            },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(asked.Id, WorkItemStatus.Succeeded, Ct);
        var escalating = (await host.GetAsync(asked.Id, Ct)).Attempts!.Single();
        Assert.Equal("stand-in", escalating.Provenance!.Agent!.ProfileName);

        // The question outlived the attempt that asked it, which is the whole reason it is a row.
        var waiting = await host.Fixture.PostOkAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, new { }, Ct);
        var question = Assert.Single(waiting.Items!);
        Assert.Equal(DecisionStatus.Pending, question.Status);
        Assert.Equal(asked.Id, question.WorkItemId);

        // And its causal references still resolve — they are identifiers, so what they name is read now.
        var detail = await host.Fixture.PostOkAsync<DecisionDto>(Operations.DecisionGet, new { decision_id = question.Id }, Ct);
        Assert.NotEmpty(detail.References!);
        foreach (var reference in detail.References!)
        {
            Assert.True(
                reference.Kind switch
                {
                    DecisionReferenceKind.WorkItem => reference.Id == asked.Id,
                    DecisionReferenceKind.Attempt => reference.Id == escalating.Id,
                    _ => false,
                },
                $"the {reference.Kind} reference '{reference.Id}' names nothing this campaign has.");
        }

        // Nothing has been summoned by the asking: raising a question is not an event the loop reacts to.
        Assert.Equal(0, (await host.ScanAsync(Ct)).Summoned);

        await host.Fixture.PostOkAsync<DecisionDto>(
            Operations.DecisionAnswer,
            new { decision_id = question.Id, answer = "stop after this one", option = "stop", actor = new { type = "human", id = "ada" } },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Summoned);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal("triggered", (string?)checkIn.Context["review_intent"]);
        Assert.Equal(JournalKinds.DecisionAnswered, (string?)checkIn.Context["trigger"]);
        Assert.Equal(question.Id, (string?)checkIn.Context["cause"]!["decision_id"]);
        Assert.Equal(escalating.Id, (string?)checkIn.Context["cause"]!["attempt_id"]);

        Assert.Equal(LineageState.Inherited, checkIn.Lineage!.State);
        Assert.Equal("stand-in", checkIn.Lineage.ProfileName);
        Assert.Equal(escalating.Provenance.Agent.ProfileRevision, checkIn.Lineage.ProfileRevision);
        Assert.Equal(escalating.Id, checkIn.Lineage.FromAttemptId);
    }

    /// <summary>
    /// What a manager believes when its own note disagrees with the campaign. The note is what a role
    /// remembered and the runtime is what is true, and a review that reported the note would be reporting
    /// something that stopped being so before it was woken.
    /// </summary>
    /// <remarks>
    /// The envelope cannot hand a note's content to a role even in principle: what it carries is the note's
    /// <em>address</em> — a campaign and a role — so the note and the state are read the same way, through the
    /// same API, and cannot be confused for one another by the thing reading them.
    /// </remarks>
    [Fact]
    public async Task A_manager_reports_the_state_where_its_note_disagrees_with_it()
    {
        // The address, by shape: two identifiers and nowhere to put a note.
        Assert.Equal(
            ["CampaignId", "Role"],
            typeof(RoleMemoryLocation).GetProperties().Select(property => property.Name).Order());

        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 3_600, "--note-check"));
        var campaign = await host.CampaignAsync(Ct);

        // What the manager remembered last time, and what is true now. The campaign is active.
        await host.Fixture.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            new
            {
                campaign_id = campaign,
                role = ManagerCheckIn.Role,
                note = new { campaign_status = "paused" },
                actor = new { type = "human", id = "ada" },
            },
            Ct);

        host.Clock.Advance(TimeSpan.FromSeconds(3_600));
        Assert.Equal(1, (await host.ScanAsync(Ct)).Summoned);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        var finished = await host.WaitForStatusAsync(checkIn.Id, WorkItemStatus.Succeeded, Ct);

        // It read both and reported the state, naming what it had believed so the disagreement is visible.
        var summary = (string?)finished.Result!["summary"];
        Assert.Contains("active", summary!, StringComparison.Ordinal);
        Assert.DoesNotContain("paused", summary, StringComparison.Ordinal);

        var said = FakeHostRuntime.StderrOf(host.Paths, checkIn.Id, finished.Attempts!.Single());
        Assert.Contains("believed=paused state=active", said, StringComparison.Ordinal);
    }

    private static async Task<WorkItemDto> CheckInAsync(FakeHostRuntime host, string campaignId)
    {
        var page = await host.Fixture.PostOkAsync<Page<WorkItemDto>>(
            Operations.WorkItemList,
            new { campaign_id = campaignId, role = ManagerCheckIn.Role },
            Ct);

        // Read back one by one: a listing leaves lineage out, and lineage is half of what these tests are about.
        return await host.GetAsync(Assert.Single(page.Items!).Id, Ct);
    }
}
