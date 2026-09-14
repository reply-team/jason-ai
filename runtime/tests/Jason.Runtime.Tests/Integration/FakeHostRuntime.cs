using System.Diagnostics;
using System.Globalization;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Tests.Dispatch;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A runtime whose dispatcher is real, whose clock the test owns, and whose work is done by real child
/// processes. The loop itself is idle — one tick an hour — so every scan in a test is a scan the test asked
/// for; the children, though, run in real time, which is why waiting for a state is a poll rather than a tick.
/// Whatever the test leaves behind is cancelled and killed when it is disposed.
/// </summary>
internal sealed class FakeHostRuntime : IAsyncDisposable
{
    /// <summary>Short budgets: a lost attempt has to be provable by moving the clock a few seconds.</summary>
    public const string Settings = """
        {"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0,"ExitGraceSeconds":30,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":2}}}
        """;

    public static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly List<string> _items = [];

    private FakeHostRuntime(RuntimeApiFixture fixture, FixedClock clock)
    {
        Fixture = fixture;
        Clock = clock;
    }

    public RuntimeApiFixture Fixture { get; }

    public FixedClock Clock { get; }

    public JasonPaths Paths => Fixture.Paths;

    public static async Task<FakeHostRuntime> StartAsync(CancellationToken ct, string settings = Settings)
    {
        var clock = new FixedClock(Noon);
        var fixture = await RuntimeApiFixture.StartAsync(
            ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, settings),
            clock: clock);

        // The loop scans once the moment it starts; work seeded after that is the test's own to hand out.
        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), ct));
        return new FakeHostRuntime(fixture, clock);
    }

    /// <summary>The attempts whose child process this runtime still holds.</summary>
    public RunningAttemptRegistry Registry => Fixture.Resolve<RunningAttemptRegistry>();

    public Task<ScanReport> ScanAsync(CancellationToken ct) => Fixture.Resolve<ScanRunner>().ScanOnceAsync(ct);

    public async Task<string> CampaignAsync(CancellationToken ct, string name = "Work")
    {
        var campaign = await Fixture.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name }, ct);
        await Fixture.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, ct);
        return campaign.Id;
    }

    /// <summary>A contact of this campaign, so an item can be about somebody.</summary>
    public async Task<string> ContactAsync(string campaignId, string email, CancellationToken ct)
    {
        var contact = await Fixture.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Ada", channels = new[] { new { channel = "email", value = email } } },
            ct);
        await Fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaignId, contacts = new[] { new { contact_id = contact.Id } } },
            ct);
        return contact.Id;
    }

    /// <summary>A role launched through the fake agent host, behaving as the arguments say.</summary>
    public async Task<RoleDto> RoleAsync(string name, CancellationToken ct, params string[] behaviour) =>
        await Fixture.PostOkAsync<RoleDto>(
            Operations.RoleAdd,
            new { name, entry_command = Execution.FakeAgentHost.EntryCommand(behaviour) },
            ct);

    public async Task<WorkItemDto> CreateAsync(object body, CancellationToken ct)
    {
        var item = await Fixture.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, body, ct);
        Track(item.Id);
        return item;
    }

    /// <summary>An item the test created some other way — through the CLI, say — that still has to be cleaned up.</summary>
    public void Track(string workItemId) => _items.Add(workItemId);

    public Task<WorkItemDto> GetAsync(string workItemId, CancellationToken ct) =>
        Fixture.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = workItemId }, ct);

    public Task<WorkItemDto> WaitForStatusAsync(string workItemId, WorkItemStatus status, CancellationToken ct) =>
        WaitAsync(workItemId, item => item.Status == status, ct, $"status {status}");

    /// <summary>Polls the item until it looks the way the test expects, because a child process takes its time.</summary>
    public async Task<WorkItemDto> WaitAsync(string workItemId, Func<WorkItemDto, bool> expected, CancellationToken ct, string what)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var deadline = DateTime.UtcNow.Add(Patience);
        WorkItemDto item;
        do
        {
            item = await GetAsync(workItemId, ct);
            if (expected(item))
            {
                return item;
            }

            await Task.Delay(200, ct);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Work item {workItemId} never reached {what}; it is {item.Status} after {item.AttemptCount} failed attempts.");
        return item;
    }

    /// <summary>
    /// Which process ran an attempt. The handler writes the launch down when the run is over — that is when it
    /// knows the exit code — so a test asking for the pid waits for the run to be accounted for first.
    /// </summary>
    public async Task<int> LaunchedPidAsync(string workItemId, int number, CancellationToken ct)
    {
        var item = await WaitAsync(
            workItemId,
            w => w.Attempts!.Any(a => a.Number == number && a.Launch?.Pid is not null),
            ct,
            $"a recorded launch for attempt {number}");
        return item.Attempts!.Single(a => a.Number == number).Launch!.Pid!.Value;
    }

    /// <summary>Polls an asynchronous condition without ever blocking a thread on it.</summary>
    public static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, CancellationToken ct, int timeoutMilliseconds = 10_000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }

    public static string StderrOf(JasonPaths paths, string workItemId, AttemptDto attempt)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(attempt);
        return ReadShared(Path.Combine(paths.AttemptWorkDirectory(workItemId, attempt.Id), "stderr.log"));
    }

    /// <summary>
    /// An executor reports through the API and only then exits, so the launcher may still be holding its output
    /// open when the work already looks finished. Reading it has to accept that.
    /// </summary>
    public static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The child of an attempt has to be gone; a test that proved a kill may not leave one behind.</summary>
    public static async Task AssertGoneAsync(int pid, CancellationToken ct)
    {
        Assert.True(
            await DispatchHarness.EventuallyAsync(() => !IsRunning(pid), ct, timeoutMilliseconds: 10_000),
            string.Create(CultureInfo.InvariantCulture, $"Process {pid} was still running."));
    }

    public static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Ask first, the way an operator would: every open item is cancelled, which stops the child that is
        // running it.
        foreach (var id in _items)
        {
            try
            {
                var item = await GetAsync(id, CancellationToken.None);
                if (item.Status is not (WorkItemStatus.Succeeded or WorkItemStatus.Failed or WorkItemStatus.Cancelled))
                {
                    await Fixture.PostAsync(Operations.WorkItemCancel, new { work_item_id = id }, CancellationToken.None);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or ObjectDisposedException)
            {
                // The test stopped this runtime itself; the pids below are still collected and killed.
            }
        }

        // Then push time far enough forward that one scan stops anything still holding on — a lost lease or a
        // process lingering after its attempt was already finished.
        try
        {
            Clock.Advance(TimeSpan.FromHours(1));
            await ScanAsync(CancellationToken.None);
            await DispatchHarness.EventuallyAsync(
                () => Fixture.Resolve<RunningAttemptRegistry>().Count == 0,
                CancellationToken.None,
                timeoutMilliseconds: 10_000);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }

        // Whatever is left is killed by pid, which is on the attempt once its run has been accounted for.
        foreach (var id in _items)
        {
            try
            {
                var item = await GetAsync(id, CancellationToken.None);
                foreach (var attempt in item.Attempts ?? [])
                {
                    if (attempt.Launch?.Pid is { } pid)
                    {
                        Kill(pid);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or ObjectDisposedException)
            {
            }
        }

        await Fixture.DisposeAsync();
    }
}
