using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Notifications;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A pilot run by one planner over a week, with nobody's session open after the planner was created: it sends ten
/// people to research, has a message sent to each of them once a person approves it, holds for replies, has the
/// results read, and asks the person whether to go on. Everything that moves the planner from one step to the next is
/// the runtime: its scan releases a wait when what it waited for has ended or its moment has come, and its claim runs
/// the planner again, told why. The planner, the researchers and the analyst are the stand-in host; the sends go
/// through a stand-in plugin; the person approves and answers through the API; the clock is the test's.
/// </summary>
/// <remarks>
/// What this proves is that the wait is carried by a deterministic owner and that nothing on the waiting side wakes
/// anything. Every run of the planner ends — its child exits — before the work it waits for runs; while it waits it has
/// no attempt, no child and no lease; the release of each wait is a line the runtime writes, never the planner's and
/// never a person's; and before the moment of a hold, a scan launches nothing at all. The steps are lettered, and each
/// asserts the fact it stands for.
/// </remarks>
public sealed class PlannerChainTests
{
    private const string Planner = "chain-planner";

    private const string Researcher = "chain-researcher";

    private const string Analyst = "chain-analyst";

    private const string Mail = "stand-in-mail";

    private const int Pilot = 10;

    private static readonly ActorRef Marta = new(ActorType.Human, "marta");

    private static readonly DateTimeOffset ResearchUntil = new(FakeHostRuntime.Noon.AddDays(3));

    private static readonly DateTimeOffset SendUntil = new(FakeHostRuntime.Noon.AddDays(7));

    private static readonly DateTimeOffset HoldUntil = new(FakeHostRuntime.Noon.AddDays(5));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The runtime's settings: the fake host's short limits, a review cadence of a week, so that no review falls inside
    /// the pilot by the calendar alone, and the stand-in host as the manager, for the review the answer summons.
    /// </summary>
    private static string Settings()
    {
        var manager = string.Join(",", Execution.FakeAgentHost.EntryCommand("manager").Select(part => JsonSerializer.Serialize(part)));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"Dispatcher\":{{\"TickSeconds\":3600,\"RetryDelaySeconds\":0,\"ExitGraceSeconds\":30,"
            + $"\"AiRole\":{{\"TimeoutSeconds\":60,\"HeartbeatSeconds\":10,\"MaxAttempts\":2}}}},"
            + $"\"Roles\":{{\"DefaultEntryCommand\":[{manager}]}},"
            + $"\"Manager\":{{\"ReviewSeconds\":604800,\"TimeoutSeconds\":60}}}}");
    }

    /// <summary>A provider that sends what it is given and says so, in the shape the contract asks of a send.</summary>
    private const string MailMain = """
        export function invoke(operation, input) {
          const contact = input.contacts[0];
          const providerContact = "p_" + contact.id.slice(4).toLowerCase();
          return {
            result: {
              items: [{
                contact_id: contact.id,
                status: "sent",
                channel: input.args.channel,
                format: input.args.message.format,
                sender_external_id: input.args.sender.external_id,
                message: { external_id: "<" + contact.id + "@mail.example.com>", sent_at: null },
                external_ids: { contact: providerContact },
              }],
            },
            external_ids: { contact: providerContact },
          };
        }
        """;

    /// <summary>Nothing reaches a desktop here; a launch would be a toast shown by a test.</summary>
    private sealed class NoLaunch : IToastLauncher
    {
        public Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This runtime has no desktop, so nothing may be launched.");
    }

    [Fact]
    public async Task A_planner_runs_a_pilot_over_a_week_moved_only_by_the_runtime()
    {
        await using var host = await FakeHostRuntime.StartAsync(
            Ct,
            Settings(),
            prepare: paths =>
            {
                TestPlugins.Write(paths, Mail, TestPlugins.Manifest(Mail, "[message.send]"), MailMain);
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(Mail));
            },
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
                services.PostConfigure<NotificationsOptions>(notifications => notifications.Enabled = true);
                services.AddSingleton(Presence.NoDesktop(ChannelPresence.SessionZero));
                services.AddSingleton<IToastLauncher>(new NoLaunch());
            });

        var campaign = await host.CampaignAsync(Ct, "Pilot");
        var contacts = new List<string>();
        for (var i = 1; i <= Pilot; i++)
        {
            contacts.Add(await host.ContactAsync(campaign, string.Create(CultureInfo.InvariantCulture, $"person{i}@example.com"), Ct));
        }

        await host.RoleAsync(Planner, Ct, ChainPlannerBehaviour);
        await host.RoleAsync(Researcher, Ct, "succeed");
        await host.RoleAsync(Analyst, Ct, "succeed", "--result", """{"sent":10,"replied":3}""");

        // A. The person's one act before the pilot: the planner, with its brief. Its first run raises a researcher for
        // each person and waits on all of them; its child has exited by the time the wait is read.
        var planner = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = Planner,
                actor = Marta,
                reason = "a pilot of ten, then the rest",
                context = new JsonObject
                {
                    ["contacts"] = new JsonArray([.. contacts.Select(c => JsonValue.Create(c))]),
                    ["researcher_role"] = Researcher,
                    ["analyst_role"] = Analyst,
                    ["research_until"] = ResearchUntil.ToString("O", CultureInfo.InvariantCulture),
                    ["send_until"] = SendUntil.ToString("O", CultureInfo.InvariantCulture),
                    ["hold_until"] = HoldUntil.ToString("O", CultureInfo.InvariantCulture),
                },
            },
            Ct);
        var p = await UntilAsync(host, planner.Id, w => w.Status == WorkItemStatus.Waiting && w.Attempts!.Count == 1, "A: the first run waiting");
        var research = OnAll(p, "A");
        Assert.Equal(WaitMode.All, p.Wait!.Mode);
        Assert.Equal(ResearchUntil, p.Wait.Until);
        await AssertRaisedByAsync(host, research, Run(p, 1).Id, kind: WorkItemKind.AiRole, contacts, "A");
        await AssertNoChildAsync(host, p, "A");

        // B. The researchers run, one per campaign per scan. Until the last of them ends, the planner gets no attempt: the
        // scans that run its children never launch it.
        await ScanUntilAsync(
            host,
            async () =>
            {
                var states = await StatusesAsync(host, research);
                if (states.Any(s => !Ended(s)))
                {
                    var waiting = await host.GetAsync(planner.Id, Ct);
                    Assert.True(
                        waiting.Attempts!.Count == 1,
                        $"B: the planner was launched with research unfinished; it has {waiting.Attempts.Count} attempts.");
                }

                var now = await host.GetAsync(planner.Id, Ct);
                return now.Status == WorkItemStatus.Waiting && now.Attempts!.Count == 2;
            },
            "B: the research done and the second run waiting");
        Assert.All(await StatusesAsync(host, research), s => Assert.Equal(WorkItemStatus.Succeeded, s));

        // C. The second run was told what it waited for: the researchers in the order it named them, each with its
        // status and its result. It raised a send for each person, and each send is parked for a person's approval.
        p = await host.GetAsync(planner.Id, Ct);
        var told = Told(host, p, 2);
        Assert.Equal(Run(p, 1).Id, (string?)told["previous_attempt_id"]);
        Assert.Equal(1, (int)told["resumption_number"]!);
        Assert.Equal("children_ended", (string?)told["woke_because"]);
        var researched = told["items"]!.AsArray();
        Assert.Equal(research, researched.Select(i => (string)i!["work_item_id"]!));
        Assert.All(researched, i =>
        {
            Assert.Equal("succeeded", (string?)i!["status"]);
            Assert.Equal("done", (string?)i["result"]!["summary"]);
            Assert.False((bool)i["result_omitted"]!);
        });

        var sends = OnAll(p, "C");
        Assert.Equal(SendUntil, p.Wait!.Until);
        await AssertRaisedByAsync(host, sends, Run(p, 2).Id, kind: WorkItemKind.ProviderOp, contacts, "C");
        await AssertNoChildAsync(host, p, "C");
        var approvals = await ScanUntilAsync(
            host,
            async () => (await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { campaign_id = campaign }, Ct)).Items.Count == Pilot,
            "C: ten sends parked");
        Assert.All(await StatusesAsync(host, sends), s => Assert.Equal(WorkItemStatus.AwaitingApproval, s));

        // D. Once the burst has settled the person is told, once, that ten approvals wait — a pointer, written whether or
        // not a desktop was there to show it.
        host.Clock.Advance(TimeSpan.FromHours(2));
        await host.ScanAsync(Ct);
        var notified = await EventuallyLinesAsync(host, JournalKinds.NotificationSent, 1, "D: the notification");
        Assert.Equal(Pilot, (int)notified[0].New!["counts"]!["approvals"]!);

        // E. The person approves each send, with no session of theirs; the sends run, and the third run, told they are
        // done, holds until a moment and nothing else.
        foreach (var approval in (await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { campaign_id = campaign }, Ct)).Items)
        {
            await host.Fixture.PostOkAsync<ApprovalDto>(
                Operations.ApprovalApprove,
                new { approval_id = approval.Id, actor = Marta, reason = "the message reads well" },
                Ct);
        }

        await ScanUntilAsync(
            host,
            async () =>
            {
                var now = await host.GetAsync(planner.Id, Ct);
                return now.Status == WorkItemStatus.Waiting && now.Attempts!.Count == 3;
            },
            "E: the sends done and the third run holding");
        Assert.All(await StatusesAsync(host, sends), s => Assert.Equal(WorkItemStatus.Succeeded, s));
        p = await host.GetAsync(planner.Id, Ct);
        told = Told(host, p, 3);
        Assert.Equal(2, (int)told["resumption_number"]!);
        Assert.Equal("children_ended", (string?)told["woke_because"]);
        Assert.Equal(sends, told["items"]!.AsArray().Select(i => (string)i!["work_item_id"]!));
        Assert.All(told["items"]!.AsArray(), i => Assert.Equal("sent", (string?)i!["result"]!["items"]![0]!["status"]));
        Assert.Empty(p.Wait!.On);
        Assert.Null(p.Wait.Mode);
        Assert.Equal(HoldUntil, p.Wait.Until);
        await AssertNoChildAsync(host, p, "E");

        // F. Before the moment, a scan launches nothing: the hold is a row, and nobody is holding it open.
        host.Clock.Advance(TimeSpan.FromDays(1));
        await host.ScanAsync(Ct);
        Assert.True(await host.Fixture.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)), "F: a handler never finished.");
        p = await host.GetAsync(planner.Id, Ct);
        Assert.True(
            p.Status == WorkItemStatus.Waiting && p.Attempts!.Count == 3,
            $"F: before its moment the planner is {p.Status} with {p.Attempts!.Count} attempts.");
        Assert.Equal(0, host.Registry.Count);

        // G. The moment comes. The fourth run is told so, with nothing to read, raises the analyst and waits on it; the
        // fifth is told the analyst's result, asks the person whether to go on, and finishes with what it read.
        host.Clock.Advance(HoldUntil - host.Clock.GetUtcNow());
        await ScanUntilAsync(
            host,
            async () => (await host.GetAsync(planner.Id, Ct)).Status == WorkItemStatus.Succeeded,
            "G: the planner finished");
        p = await host.GetAsync(planner.Id, Ct);
        Assert.Equal(5, p.Attempts!.Count);
        told = Told(host, p, 4);
        Assert.Equal(("moment_reached", 3), ((string?)told["woke_because"], (int)told["resumption_number"]!));
        Assert.Empty(told["items"]!.AsArray());

        told = Told(host, p, 5);
        Assert.Equal(("children_ended", 4), ((string?)told["woke_because"], (int)told["resumption_number"]!));
        var analysis = Assert.Single(told["items"]!.AsArray())!;
        Assert.Equal(Analyst, (string?)analysis["role"]);
        Assert.Equal(10, (int)analysis["result"]!["sent"]!);
        Assert.True(JsonNode.DeepEquals(analysis["result"], p.Result!["analysis"]), $"G: the planner finished with {p.Result!.ToJsonString()}.");

        var question = await host.Fixture.PostOkAsync<DecisionDto>(Operations.DecisionGet, new { decision_id = (string?)p.Result["decision"] }, Ct);
        Assert.Equal((planner.Id, DecisionStatus.Pending), (question.WorkItemId, question.Status));
        Assert.Equal(["go on", "change", "stop"], question.Options!.Select(o => o.Label));
        Assert.Equal((string?)analysis["work_item_id"], Assert.Single(question.References!).Id);

        // H. The waits were carried by the runtime and by nothing else. Each run but the last ended by waiting, and its
        // child exited; each release is a line the runtime wrote, in the order the chain took; nobody resumed anything.
        Assert.Equal(
            [AttemptStatus.Yielded, AttemptStatus.Yielded, AttemptStatus.Yielded, AttemptStatus.Yielded, AttemptStatus.Succeeded],
            p.Attempts.OrderBy(a => a.Number).Select(a => a.Status));
        Assert.All(p.Attempts, a => Assert.True(a.Launch?.ExitCode == 0, $"H: {a.Id} has launch {a.Launch}."));
        var resumed = await LinesAsync(host, JournalKinds.WorkItemResumed, planner.Id);
        Assert.Equal(
            ["children_ended", "children_ended", "moment_reached", "children_ended"],
            resumed.Select(line => (string?)line.New!["because"]));
        Assert.All(resumed, line => Assert.Equal(new ActorRef(ActorType.System, "dispatcher"), line.Actor));
        Assert.Equal(4, (await LinesAsync(host, JournalKinds.WorkItemWaiting, planner.Id)).Count);

        // And the answer is the person's, which wakes a review of the campaign rather than the finished planner.
        await host.Fixture.PostOkAsync<DecisionDto>(
            Operations.DecisionAnswer,
            new { decision_id = question.Id, answer = "go on with the same message", option = "go on", actor = Marta, reason = "three replies of ten is worth more" },
            Ct);
        await ScanUntilAsync(
            host,
            async () => (await CheckInsAsync(host, campaign)).Any(c => (string?)c.Context["trigger"] == JournalKinds.DecisionAnswered),
            "H: a review of the answer");
        Assert.Equal(WorkItemStatus.Succeeded, (await host.GetAsync(planner.Id, Ct)).Status);
    }

    /// <summary>The stand-in host's behaviour that plays the planner.</summary>
    private const string ChainPlannerBehaviour = "chain-planner";

    private static bool Ended(WorkItemStatus status) =>
        status is WorkItemStatus.Succeeded or WorkItemStatus.Failed or WorkItemStatus.Cancelled;

    /// <summary>The items a wait names, each tracked so the test ends them, with the wait asserted to want all of them.</summary>
    private static List<string> OnAll(WorkItemDto planner, string step)
    {
        Assert.True(planner.Wait is { } wait && wait.On.Count == Pilot, $"{step}: the planner waits on {planner.Wait?.On.Count} items.");
        Assert.Equal(planner.Attempts![0].Id, planner.Wait!.AttemptId);
        return [.. planner.Wait.On.Select(i => i.WorkItemId)];
    }

    /// <summary>Each child was raised by the planner's run, about one of the people, in their order.</summary>
    private static async Task AssertRaisedByAsync(FakeHostRuntime host, List<string> children, string attemptId, WorkItemKind kind, List<string> contacts, string step)
    {
        var made = new List<string>();
        foreach (var id in children)
        {
            host.Track(id);
            var child = await host.GetAsync(id, Ct);
            Assert.True(child.Kind == kind, $"{step}: {id} is {child.Kind}.");
            Assert.True(
                child.CreatedBy.Type == ActorType.Attempt && child.CreatedBy.Id == attemptId,
                $"{step}: {id} was created by {child.CreatedBy}.");
            made.Add(child.ContactId ?? "(nobody)");
        }

        Assert.Equal(contacts, made);
    }

    /// <summary>A waiting planner holds nothing: its last run has exited, and no child of this runtime is alive.</summary>
    private static async Task AssertNoChildAsync(FakeHostRuntime host, WorkItemDto planner, string step)
    {
        var last = (await host.WaitAsync(planner.Id, w => w.Attempts![0].Launch?.ExitCode is not null, Ct, $"{step}: the run accounted for")).Attempts![0];
        Assert.True(last.Status == AttemptStatus.Yielded && last.Launch!.ExitCode == 0, $"{step}: the run ended {last.Status}, launch {last.Launch}.");
        Assert.True(
            await FakeHostRuntime.EventuallyAsync(() => Task.FromResult(host.Registry.Count == 0), Ct),
            $"{step}: {host.Registry.Count} children still held while the planner waits.");
    }

    private static async Task<List<WorkItemStatus>> StatusesAsync(FakeHostRuntime host, List<string> ids)
    {
        var statuses = new List<WorkItemStatus>();
        foreach (var id in ids)
        {
            statuses.Add((await host.GetAsync(id, Ct)).Status);
        }

        return statuses;
    }

    /// <summary>The planner's run by its number; the item lists its attempts newest first.</summary>
    private static AttemptDto Run(WorkItemDto planner, int number) =>
        planner.Attempts!.SingleOrDefault(a => a.Number == number)
        ?? throw new InvalidOperationException($"the planner has no run {number}: {string.Join(", ", planner.Attempts!.Select(a => a.Number))}");

    /// <summary>What a run was told: the first line its child wrote, which is the envelope's resumption as it read it.</summary>
    private static JsonObject Told(FakeHostRuntime host, WorkItemDto planner, int number)
    {
        var attempt = Run(planner, number);
        var stdout = FakeHostRuntime.ReadShared(Path.Combine(host.Paths.AttemptWorkDirectory(planner.Id, attempt.Id), "stdout.log"));
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();
        return JsonNode.Parse(line) as JsonObject ?? throw new InvalidOperationException($"run {number} was told {line}");
    }

    /// <summary>Waits for an item to look as expected, scanning and letting each scan's handlers finish.</summary>
    private static async Task<WorkItemDto> UntilAsync(FakeHostRuntime host, string id, Func<WorkItemDto, bool> expected, string what)
    {
        await ScanUntilAsync(host, async () => expected(await host.GetAsync(id, Ct)), what);
        return await host.GetAsync(id, Ct);
    }

    /// <summary>
    /// Scans, and lets what each scan launched finish, until the condition holds. Each scan hands out at most one item of
    /// the campaign, so a chain of ten takes ten of them; the bound is generous and the clock does not move.
    /// </summary>
    private static async Task<bool> ScanUntilAsync(FakeHostRuntime host, Func<Task<bool>> done, string what)
    {
        for (var scan = 0; scan < 60; scan++)
        {
            await host.ScanAsync(Ct);
            Assert.True(await host.Fixture.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)), $"{what}: a handler never finished.");
            if (await done())
            {
                return true;
            }
        }

        Assert.Fail($"{what}: not reached after 60 scans.");
        return false;
    }

    /// <summary>The lines of a kind in the order they were written: journal ids are monotonic, a held clock is not.</summary>
    private static async Task<List<JournalEntryDto>> LinesAsync(FakeHostRuntime host, string kind, string? workItemId = null) =>
        [.. (await host.Fixture.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { kind, work_item_id = workItemId, limit = 100 },
            Ct)).Items.OrderBy(line => line.Id, StringComparer.Ordinal)];

    /// <summary>The notification is delivered off the scan, so its line is written a moment after the scan returns.</summary>
    private static async Task<List<JournalEntryDto>> EventuallyLinesAsync(FakeHostRuntime host, string kind, int count, string what)
    {
        var lines = new List<JournalEntryDto>();
        Assert.True(
            await FakeHostRuntime.EventuallyAsync(
                async () =>
                {
                    lines = await LinesAsync(host, kind);
                    return lines.Count >= count;
                },
                Ct),
            $"{what}: {lines.Count} {kind} lines.");
        Assert.True(lines.Count == count, $"{what}: {lines.Count} {kind} lines, {count} expected.");
        return lines;
    }

    /// <summary>The campaign's reviews, each read whole, since a listing leaves out the context that says why it was summoned.</summary>
    private static async Task<List<WorkItemDto>> CheckInsAsync(FakeHostRuntime host, string campaign)
    {
        var page = await host.Fixture.PostOkAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, new { campaign_id = campaign, role = ManagerCheckIn.Role, limit = 100 }, Ct);
        var reviews = new List<WorkItemDto>();
        foreach (var item in page.Items)
        {
            host.Track(item.Id);
            reviews.Add(await host.GetAsync(item.Id, Ct));
        }

        return reviews;
    }
}
