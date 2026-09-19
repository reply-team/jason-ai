using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Journal;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The dispatcher doing its job against real processes. Every executor here is the fake agent host, started by
/// the real launcher from a role's entry command, reporting through the real API over the loopback: what is
/// asserted is what the runtime would see from an agent host that behaved this way — the good one, and each of
/// the bad ones the runtime has to survive.
/// </summary>
public class DispatcherWithFakeHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_host_that_does_the_work_reports_a_result_and_leaves_its_output_behind()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        var contact = await host.ContactAsync(campaign, "ada@example.test", Ct);
        var role = await host.RoleAsync("fake-succeed", Ct, "succeed");
        var created = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-succeed", contact_id = contact },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(created.Id, WorkItemStatus.Succeeded, Ct);

        // The executor reports through the API and exits afterwards, so how it was run is written down a
        // moment after the work is already finished.
        var item = await host.WaitAsync(
            created.Id,
            w => w.Attempts!.Any(a => a.Launch?.ExitCode is not null),
            Ct,
            "a recorded launch");

        // The final report replaced the progress the host had written halfway through.
        Assert.Equal("done", (string?)item.Result!["summary"]);
        Assert.Equal(0, item.AttemptCount);
        Assert.Null(item.CurrentAttemptId);
        var attempt = Assert.Single(item.Attempts!);
        Assert.Equal(AttemptStatus.Succeeded, attempt.Status);
        Assert.NotNull(attempt.Launch!.Pid);
        Assert.Equal(0, attempt.Launch.ExitCode);
        Assert.Equal(role.EntryCommand, attempt.Launch.EntryCommand);
        Assert.NotNull(attempt.LastHeartbeatAt);

        var workDir = host.Paths.AttemptWorkDirectory(item.Id, attempt.Id);
        Assert.True(File.Exists(Path.Combine(workDir, "stdout.log")));
        var stderr = FakeHostRuntime.StderrOf(host.Paths, item.Id, attempt);
        Assert.Contains("token-in-env=false", stderr, StringComparison.Ordinal);
        Assert.Contains("data-dir=set", stderr, StringComparison.Ordinal);

        var chronicle = await host.Fixture.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { work_item_id = item.Id, limit = 50 },
            Ct);
        Assert.Equal(
            new[] { JournalKinds.WorkItemSucceeded, JournalKinds.WorkItemProcessing, JournalKinds.WorkItemScheduled, JournalKinds.WorkItemCreated },
            chronicle.Items.Select(e => e.Kind).ToArray());
        Assert.All(chronicle.Items.Take(3), entry => Assert.Equal(attempt.Id, entry.AttemptId));
        Assert.Null(chronicle.Items[^1].AttemptId);
    }

    [Fact]
    public async Task A_host_that_dies_is_retried_and_then_given_up_on_at_the_limit()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-crash", Ct, "crash");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-crash" }, Ct);

        await host.ScanAsync(Ct);
        var released = await host.WaitAsync(
            created.Id,
            item => item.Status == WorkItemStatus.Created && item.AttemptCount == 1,
            Ct,
            "a first failure and a release");
        Assert.Null(released.RetryAfter);

        await host.ScanAsync(Ct);
        var item = await host.WaitForStatusAsync(created.Id, WorkItemStatus.Failed, Ct);

        Assert.Equal(2, item.AttemptCount);
        Assert.Equal(AttemptErrors.ExecutorExited, item.LastError!.Code);
        Assert.True(item.LastError.Retriable);

        // The trace is the executor's own words, and it stays on the attempt: the item carries the summary.
        Assert.Null(item.LastError.Trace);
        Assert.Equal([2, 1], item.Attempts!.Select(a => a.Number).ToArray());
        Assert.Contains("behaviour=crash", item.Attempts![0].Error!.Trace!, StringComparison.Ordinal);
        Assert.Equal(3, item.Attempts![0].Launch!.ExitCode);
    }

    [Fact]
    public async Task A_host_that_dies_once_and_works_the_second_time_finishes_the_work()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        var behaviour = Path.Combine(host.Paths.Root, "behaviour.txt");
        await File.WriteAllTextAsync(behaviour, "crash", Ct);
        await host.RoleAsync("fake-script", Ct, "script", behaviour);
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-script" }, Ct);

        await host.ScanAsync(Ct);
        await host.WaitAsync(created.Id, item => item.AttemptCount == 1, Ct, "a first failure");

        await File.WriteAllTextAsync(behaviour, "succeed", Ct);
        await host.ScanAsync(Ct);
        var item = await host.WaitForStatusAsync(created.Id, WorkItemStatus.Succeeded, Ct);

        Assert.Equal(1, item.AttemptCount);
        Assert.Equal(2, item.Attempts!.Count);
        Assert.Equal("done", (string?)item.Result!["summary"]);
    }

    [Fact]
    public async Task A_host_that_keeps_reporting_is_left_alone_until_the_work_is_cancelled()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-hang", Ct, "hang");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-hang" }, Ct);

        await host.ScanAsync(Ct);

        // The clock never moves, so every beat carries the same instant; that one arrived at all is the point.
        var running = await host.WaitAsync(
            created.Id,
            item => item.Status == WorkItemStatus.Processing && item.Attempts![0].LastHeartbeatAt is not null,
            Ct,
            "a processing item with a heartbeat");
        var attempt = running.Attempts![0];

        host.Clock.Advance(TimeSpan.FromSeconds(15));
        await host.ScanAsync(Ct);
        Assert.Equal(WorkItemStatus.Processing, (await host.GetAsync(created.Id, Ct)).Status);

        var cancelled = await host.Fixture.PostOkAsync<WorkItemDto>(Operations.WorkItemCancel, new { work_item_id = created.Id }, Ct);
        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
        Assert.Equal(AttemptStatus.Cancelled, cancelled.Attempts![0].Status);
        await FakeHostRuntime.AssertGoneAsync(await host.LaunchedPidAsync(created.Id, 1, Ct), Ct);

        // Whatever the child managed to say on its way out is no longer listened to.
        var stale = await host.Fixture.PostErrorAsync(
            Operations.WorkItemHeartbeat,
            new { work_item_id = created.Id, attempt_id = attempt.Id },
            HttpStatusCode.Conflict,
            Ct);
        Assert.Equal("stale_attempt", stale.Code);
        Assert.False(stale.Retryable);
    }

    [Fact]
    public async Task A_host_that_goes_quiet_loses_its_attempt_and_then_the_work()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-mute", Ct, "mute");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-mute" }, Ct);

        await host.ScanAsync(Ct);
        await host.WaitForStatusAsync(created.Id, WorkItemStatus.Processing, Ct);

        // One interval for the beat to arrive in and one of grace, and then a second past both. The same scan
        // takes the attempt back and hands the work out again, because nothing here asks for a retry delay.
        host.Clock.Advance(TimeSpan.FromSeconds(21));
        await host.ScanAsync(Ct);

        var released = await host.GetAsync(created.Id, Ct);
        Assert.Equal(1, released.AttemptCount);
        var lost = released.Attempts!.Single(a => a.Number == 1);
        Assert.Equal(AttemptStatus.Failed, lost.Status);
        Assert.Equal(AttemptErrors.HeartbeatMissed, lost.Error!.Code);
        Assert.True(lost.Error.Retriable);
        await FakeHostRuntime.AssertGoneAsync(await host.LaunchedPidAsync(created.Id, 1, Ct), Ct);

        await host.WaitAsync(
            created.Id,
            w => w.Attempts!.Any(a => a.Number == 2 && a.Status == AttemptStatus.Running),
            Ct,
            "a second attempt of its own");
        host.Clock.Advance(TimeSpan.FromSeconds(21));
        await host.ScanAsync(Ct);

        var item = await host.GetAsync(created.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.Equal(2, item.AttemptCount);
        Assert.Equal(AttemptErrors.HeartbeatMissed, item.LastError!.Code);
    }

    [Fact]
    public async Task An_attempt_that_spends_its_whole_budget_loses_its_lease()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-mute", Ct, "mute");
        var created = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-mute", heartbeat_seconds = 0, timeout_seconds = 30 },
            Ct);

        await host.ScanAsync(Ct);
        await host.WaitForStatusAsync(created.Id, WorkItemStatus.Processing, Ct);

        host.Clock.Advance(TimeSpan.FromSeconds(31));
        await host.ScanAsync(Ct);

        // Without a heartbeat the budget is the only thing watching, and it is spent.
        var item = await host.GetAsync(created.Id, Ct);
        var lost = item.Attempts!.Single(a => a.Number == 1);
        Assert.Equal(AttemptStatus.Failed, lost.Status);
        Assert.Equal(AttemptErrors.LeaseExpired, lost.Error!.Code);
        Assert.True(lost.Error.Retriable);
        Assert.Equal(1, item.AttemptCount);
        await FakeHostRuntime.AssertGoneAsync(await host.LaunchedPidAsync(created.Id, 1, Ct), Ct);
    }

    [Fact]
    public async Task A_host_that_exits_without_reporting_loses_its_attempt_whatever_its_exit_code_says()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-silent", Ct, "silent");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-silent" }, Ct);

        await host.ScanAsync(Ct);
        var item = await host.WaitAsync(
            created.Id,
            w => w.Status == WorkItemStatus.Created && w.AttemptCount == 1,
            Ct,
            "a failed first attempt and a release");

        Assert.Equal(AttemptErrors.ExecutorExited, item.Attempts![0].Error!.Code);
        Assert.Equal(0, item.Attempts![0].Launch!.ExitCode);

        var cancelled = await host.Fixture.PostOkAsync<WorkItemDto>(Operations.WorkItemCancel, new { work_item_id = created.Id }, Ct);
        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task A_host_that_reports_twice_is_told_the_second_report_is_stale()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-stale", Ct, "stale");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-stale" }, Ct);

        await host.ScanAsync(Ct);
        var item = await host.WaitForStatusAsync(created.Id, WorkItemStatus.Succeeded, Ct);
        var attempt = Assert.Single(item.Attempts!);

        // The host asserted the refusal itself and exited cleanly; the first report is the one that stands.
        Assert.True(
            await FakeHostRuntime.EventuallyAsync(
                () => Task.FromResult(FakeHostRuntime.StderrOf(host.Paths, item.Id, attempt).Contains("stale_attempt observed", StringComparison.Ordinal)),
                Ct));
        Assert.Equal("done", (string?)item.Result!["summary"]);
    }

    [Fact]
    public async Task A_provider_operation_fails_where_it_is_noticed_because_nothing_routes_it_yet()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        var created = await host.CreateAsync(
            new { campaign_id = campaign, kind = "provider_op", operation = "campaign.get" },
            Ct);

        // Failed inside the claim, so there is nothing to wait for and nothing was ever launched.
        Assert.Equal(0, (await host.ScanAsync(Ct)).Claimed);

        var item = await host.GetAsync(created.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.Equal(AttemptErrors.NoRoute, item.LastError!.Code);
        Assert.False(item.LastError.Retriable);
        var attempt = Assert.Single(item.Attempts!);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.Launch);
    }

    [Fact]
    public async Task A_role_nothing_can_start_fails_until_a_default_entry_command_is_configured()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.Fixture.PostOkAsync<RoleDto>(Operations.RoleAdd, new { name = "nocmd", entry_command = Array.Empty<string>() }, Ct);
        var orphan = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "nocmd" }, Ct);

        await host.ScanAsync(Ct);
        var failed = await host.GetAsync(orphan.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, failed.Status);
        Assert.Equal(AttemptErrors.RoleNotLaunchable, failed.LastError!.Code);
        Assert.False(failed.LastError.Retriable);

        // One line of settings makes the whole builtin roster launchable, and it applies without a restart.
        var command = JsonSerializer.Serialize(Execution.FakeAgentHost.EntryCommand("succeed"), JasonJson.Options);
        await File.WriteAllTextAsync(
            host.Paths.UserSettingsFile,
            """{"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":2}},"Roles":{"DefaultEntryCommand":"""
                + command
                + "}}",
            Ct);
        var roles = host.Fixture.Resolve<IOptionsMonitor<RolesOptions>>();
        Assert.True(
            await FakeHostRuntime.EventuallyAsync(() => Task.FromResult(roles.CurrentValue.DefaultEntryCommand.Count == 3), Ct));

        var builtin = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "researcher" }, Ct);
        await host.ScanAsync(Ct);
        Assert.Equal(WorkItemStatus.Succeeded, (await host.WaitForStatusAsync(builtin.Id, WorkItemStatus.Succeeded, Ct)).Status);
    }

    [Fact]
    public async Task A_process_that_lingers_after_reporting_is_stopped_once_its_grace_is_spent()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-hang", Ct, "hang");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-hang" }, Ct);

        await host.ScanAsync(Ct);
        var running = await host.WaitForStatusAsync(created.Id, WorkItemStatus.Processing, Ct);
        var attempt = running.Attempts![0];

        // Somebody else finished the work on this attempt's behalf; the child has not noticed yet.
        var completed = await host.Fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemComplete,
            new { work_item_id = created.Id, attempt_id = attempt.Id, status = "succeeded" },
            Ct);
        Assert.Equal(WorkItemStatus.Succeeded, completed.Status);
        Assert.Contains(attempt.Id, host.Registry.AttemptIds);

        host.Clock.Advance(TimeSpan.FromSeconds(31));
        await host.ScanAsync(Ct);
        await FakeHostRuntime.AssertGoneAsync(await host.LaunchedPidAsync(created.Id, 1, Ct), Ct);
        Assert.Empty(host.Registry.AttemptIds);
    }

    /// <summary>
    /// A child whose runtime is gone stops watching for it after two missed beats, which gives a successor about
    /// two seconds to publish its descriptor. That is a policy, and a test that depends on outliving a slow
    /// successor has to say so rather than hope: this one waits four beats with no runtime at all, and the child
    /// is still there because it was told to be patient.
    /// </summary>
    /// <remarks>
    /// The window is real and it has been paid for: the neighbouring test below failed on a loaded CI runner and
    /// again in a reviewer's own full run, both times because the successor took longer than two seconds to come
    /// up. The fix is the fake host's own patience, not a reordering of the test — the runtime and the child are
    /// behaving correctly in both runs, and it is the stand-in's give-up rule that is too short for a machine
    /// running four test assemblies at once.
    /// </remarks>
    [Fact]
    public async Task A_child_waits_longer_than_two_beats_for_a_successor_when_it_is_told_to()
    {
        const string Settings = """
            {"Dispatcher":{"TickSeconds":3600,"DrainSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":600,"HeartbeatSeconds":10,"MaxAttempts":2}}}
            """;

        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-patient", Ct, "succeed", "--heartbeats", "60", "--patience", "12");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-patient" }, Ct);

        await host.ScanAsync(Ct);
        var running = await host.WaitForStatusAsync(created.Id, WorkItemStatus.Processing, Ct);
        var attemptId = running.CurrentAttemptId;

        // The pid from the child's own start line, not from the attempt's launch record: that record is written
        // when the run is over, and the whole question here is about a child that is still running.
        var pid = await host.RunningPidAsync(created.Id, Ct);

        await host.Fixture.Runtime.StopAsync();

        // Four beats with no descriptor anywhere: twice what the watchdog tolerates by default, and the point of
        // the option. Real time, because the watchdog is the one thing here that does not run on the test clock.
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);
        Assert.True(
            FakeHostRuntime.IsRunning(pid),
            "the child gave up on its runtime before the successor arrived, so a patience it was given was not "
            + "honoured. Its own diagnostics say why: "
            + FakeHostRuntime.StderrOf(host.Paths, created.Id, running.Attempts![0]));

        await using var successor = await RuntimeHost.StartAsync(
            host.Paths,
            TestRuntimeOptions.Quiet with { Clock = host.Clock },
            Ct);
        using var http = new HttpClient { BaseAddress = successor.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", successor.Token);

        var recovered = await ReadAsync(http, created.Id, Ct);
        Assert.Equal(attemptId, recovered.CurrentAttemptId);

        // And it was worth waiting for: the work it was launched for is finished against the new runtime.
        Assert.True(
            await FakeHostRuntime.EventuallyAsync(
                async () => (await ReadAsync(http, created.Id, Ct)).Status == WorkItemStatus.Succeeded,
                Ct,
                timeoutMilliseconds: 30_000));
    }

    [Fact]
    public async Task A_child_that_outlives_its_runtime_finishes_its_work_against_the_next_one()
    {
        const string Settings = """
            {"Dispatcher":{"TickSeconds":3600,"DrainSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":600,"HeartbeatSeconds":10,"MaxAttempts":2}}}
            """;

        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings);
        var campaign = await host.CampaignAsync(Ct);

        // Long enough at the work that the runtime it was launched by is gone before it reports.
        // Patience, for the reason the test above proves: the successor below is started by this test
        // process, and on a loaded machine that takes longer than the two beats a host waits by default.
        await host.RoleAsync("fake-slow", Ct, "succeed", "--heartbeats", "60", "--patience", "12");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-slow" }, Ct);

        await host.ScanAsync(Ct);
        var running = await host.WaitForStatusAsync(created.Id, WorkItemStatus.Processing, Ct);
        var attemptId = running.CurrentAttemptId;

        // The drain waits its second for a child that is not finished, and then leaves it alone: the lease is
        // still good and the attempt id it holds is still the fence.
        await host.Fixture.Runtime.StopAsync();

        await using var successor = await RuntimeHost.StartAsync(
            host.Paths,
            TestRuntimeOptions.Quiet with { Clock = host.Clock },
            Ct);
        using var http = new HttpClient { BaseAddress = successor.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", successor.Token);

        // Startup recovery releases what was only scheduled; work already running is left to its lease.
        var recovered = await ReadAsync(http, created.Id, Ct);
        Assert.Equal(WorkItemStatus.Processing, recovered.Status);
        Assert.Equal(attemptId, recovered.CurrentAttemptId);

        // The host reads the descriptor before every call, so it finds the new instance and its new token by
        // itself, and the work it was launched for is completed rather than lost.
        Assert.True(
            await FakeHostRuntime.EventuallyAsync(
                async () => (await ReadAsync(http, created.Id, Ct)).Status == WorkItemStatus.Succeeded,
                Ct,
                timeoutMilliseconds: 30_000));

        var finished = await ReadAsync(http, created.Id, Ct);
        Assert.Equal("done", (string?)finished.Result!["summary"]);
        Assert.Equal(0, finished.AttemptCount);
        Assert.NotNull(finished.Attempts![0].LastHeartbeatAt);
    }

    private static Task<WorkItemDto> ReadAsync(HttpClient http, string workItemId, CancellationToken ct) =>
        PostAsync<WorkItemDto>(http, Operations.WorkItemGet, new { work_item_id = workItemId }, ct);

    private static async Task<T> PostAsync<T>(HttpClient http, string operation, object body, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JasonJson.Options), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(operation), content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<T>(text, JasonJson.Options)!;
    }
}
