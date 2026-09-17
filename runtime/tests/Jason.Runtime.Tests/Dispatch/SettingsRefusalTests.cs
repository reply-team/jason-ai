using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What a broken settings file costs. The operator's path to a tick or a budget is an editor, and an editor is
/// where a typo happens; every part of the runtime that reads those settings has to answer the same way when it
/// meets one, or a mistyped number turns into a runtime that cannot be told what its own children did.
/// </summary>
public class SettingsRefusalTests
{
    /// <summary>A tick short enough that several of them pass inside a test, and a drain short enough to stop.</summary>
    private const string FastTick = """{"Dispatcher":{"TickSeconds":1,"DrainSeconds":1}}""";

    private const string Launchable = """
        {"Dispatcher":{"TickSeconds":1,"DrainSeconds":1,"RetryDelaySeconds":0},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    /// <summary>A tick of zero: the validator's own sentence names the field, so the refusal is the file's.</summary>
    private const string Invalid = """{"Dispatcher":{"TickSeconds":0,"DrainSeconds":1}}""";

    /// <summary>The same mistyped tick in a file that still says how a role is launched: one edit, one mistake.</summary>
    private const string InvalidLaunchable = """
        {"Dispatcher":{"TickSeconds":0,"DrainSeconds":1},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    /// <summary>
    /// A plugin memory limit below the floor the validator holds. It breaks a different section of the same
    /// file: the dispatcher's own settings are untouched, so a runtime that answered every request from the
    /// dispatcher's seam alone would still be broken by this one.
    /// </summary>
    private const string InvalidPlugins = """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"MemoryMb":0}}}""";

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// While the settings file is invalid, the operations a running child depends on must still answer. A child
    /// that has finished cannot report it through a 500: it has no way to tell "the runtime is broken" from "my
    /// answer was rejected", so it retries into the same wall until its lease runs out and the item is called
    /// ambiguous — the one ending this runtime is built never to invent.
    /// </summary>
    [Fact]
    public async Task A_refused_settings_edit_does_not_turn_a_heartbeat_into_a_server_error()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var seeded = SeedRunningAttempt(fixture.Paths);

        File.WriteAllText(fixture.Paths.UserSettingsFile, Invalid);
        Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));

        var beat = await fixture.PostOkAsync<HeartbeatResponse>(
            Operations.WorkItemHeartbeat, new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct);

        Assert.Equal(seeded.AttemptId, beat.AttemptId);
        Assert.True(beat.LockUntil > DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The same for the answer that matters most. Only the failure branch of a completion reads the settings —
    /// how many attempts this kind of work gets, and how long to wait before the next one — so a successful
    /// completion would prove nothing here.
    /// </summary>
    [Fact]
    public async Task A_refused_settings_edit_does_not_turn_a_failed_completion_into_a_server_error()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var seeded = SeedRunningAttempt(fixture.Paths);

        File.WriteAllText(fixture.Paths.UserSettingsFile, Invalid);
        Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));

        var dto = await fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemComplete,
            new WorkItemCompleteRequest(
                seeded.ItemId,
                seeded.AttemptId,
                CompletionStatus.Failed,
                Result: null,
                new CompletionErrorDto("provider_unavailable", "The provider was not there.", Details: null),
                Reason: null),
            Ct);

        // The last settings that validated say this kind gets three attempts, so the item is released for another.
        Assert.Equal(WorkItemStatus.Created, dto.Status);
    }

    /// <summary>
    /// The complaint is about the edit, not about the reading of it. Every part of a scan now reads the same
    /// seam, so a single broken file is said once however many readers meet it — and the scan itself no longer
    /// falls over, which is the line this test counts to zero.
    /// </summary>
    [Fact]
    public async Task A_refused_settings_edit_is_said_once_and_not_once_a_tick()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
            Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, Invalid);

            // Several ticks' worth of reads, so "said once" is a claim about the edit and not about one read.
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals >= 5, Ct, 20000));
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            Assert.Equal(1, Occurrences(logs, "settings were refused"));

            // The refusal is logged at Error, so this count pins the reader against a line that is really there.
            // Without it the zero below would pass on a reader that matched nothing, before the fix and after.
            Assert.Equal(1, ErrorLines(logs, "settings were refused"));
            Assert.Contains("\"Section\":\"Dispatcher\"", logs, StringComparison.Ordinal);
            Assert.Equal(0, ErrorLines(logs, "Dispatcher scan failed"));
        }
    }

    /// <summary>
    /// Three sections are guarded now, and an operator told only that "settings were refused" would have the
    /// whole file to search. The complaint names the section it came from and the setting inside it, and it is
    /// still said once however many reads meet the same edit.
    /// </summary>
    [Fact]
    public async Task A_refused_edit_names_the_section_it_came_from_and_is_said_once()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(RuntimeApiFixture.DispatcherOff));
        await using (fixture)
        {
            var plugins = fixture.Resolve<LiveSettings<PluginsOptions>>();

            File.WriteAllText(fixture.Paths.UserSettingsFile, InvalidPlugins);
            Assert.True(await TestOptions.RefusedAsync(plugins, Ct));

            // Four more reads of the same broken file: "said once" is a claim about the edit, not about one read.
            for (var read = 0; read < 4; read++)
            {
                plugins.TryCurrent(out _);
            }

            Assert.Equal(5, plugins.Refusals);
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            Assert.Equal(1, ErrorLines(logs, "settings were refused"));
            Assert.Contains("\"Section\":\"Plugins\"", logs, StringComparison.Ordinal);
            Assert.Equal(1, ErrorLines(logs, "Plugins:Limits:MemoryMb"));
        }
    }

    /// <summary>
    /// And the work keeps moving meanwhile. One piece of work reads the settings four times over — the lease
    /// sweep, the claim, the handler that starts what was claimed, and the routine that ends it — so this is the
    /// test that says all of them fall back together, rather than one of them throwing the whole scan away.
    /// </summary>
    [Fact]
    public async Task Repairing_the_file_lets_the_work_through_again()
    {
        // A launch that fails is still a whole piece of work: claimed, started, and ended through the one
        // routine that decides whether an attempt is worth repeating — which is the reader this asserts on.
        var command = new FakeCommand(WorkItemKind.AiRole, _ => throw new InvalidOperationException("the host is not there"));
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings(Launchable),
            configureServices: services => services.AddSingleton<ICommand>(command));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
            Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, InvalidLaunchable);
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));

            var item = SeedClaimable(fixture.Paths);
            Assert.True(await DispatchHarness.EventuallyAsync(
                () => StatusOf(fixture.Paths, item) == WorkItemStatus.Failed, Ct, 20000));

            // And the file is the authority again the moment it is put right.
            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":2,"DrainSeconds":1}}""");
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Current.TickSeconds == 2, Ct));
        }
    }

    /// <summary>
    /// A runtime that has never read settings the validator accepted has nothing to fall back to. A loop can
    /// live with that — it tries again next tick — but an endpoint has to answer, so the seam says plainly that
    /// it cannot, instead of throwing something the API surface has no word for.
    /// </summary>
    [Fact]
    public void A_runtime_that_never_read_a_good_value_answers_that_it_cannot_rather_than_throwing()
    {
        var settings = TestOptions.NothingValidated();

        Assert.False(settings.TryCurrent(out _));
        Assert.Equal(1, settings.Refusals);
    }

    [Fact]
    public async Task A_heartbeat_with_nothing_to_fall_back_to_is_refused_with_a_code_and_not_a_crash()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<DomainException>(
            () => NothingValidatedService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct));

        Assert.Equal("settings_unreadable", error.Code);
        Assert.Equal(503, error.StatusCode);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task A_failed_completion_with_nothing_to_fall_back_to_is_refused_with_a_code_and_not_a_crash()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var request = new WorkItemCompleteRequest(
            seeded.ItemId,
            seeded.AttemptId,
            CompletionStatus.Failed,
            Result: null,
            new CompletionErrorDto("provider_unavailable", "The provider was not there.", Details: null),
            Reason: null);

        var error = await Assert.ThrowsAsync<DomainException>(() => NothingValidatedService(ctx).CompleteAsync(request, Ct));

        Assert.Equal("settings_unreadable", error.Code);
        Assert.Equal(503, error.StatusCode);
        Assert.True(error.Retryable);
    }

    /// <summary>
    /// And a child that succeeded is never held up by a file it has nothing to do with: a successful completion
    /// reads no settings, so the same runtime that refuses the failure above takes this one.
    /// </summary>
    [Fact]
    public async Task A_successful_completion_needs_no_settings_and_is_taken_anyway()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var request = new WorkItemCompleteRequest(
            seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, Result: null, Error: null, Reason: null);

        var dto = await NothingValidatedService(ctx).CompleteAsync(request, Ct);

        Assert.Equal(WorkItemStatus.Succeeded, dto.Status);
    }

    /// <summary>
    /// The section an operator edits to raise a plugin's memory, or to give a child longer to stop, is read on
    /// the way into every work item — it bounds the lease a provider operation may be given. A typo there used
    /// to answer a creation with a 500, which tells a planner that the runtime broke rather than the file, and
    /// there is nothing an agent can do with that sentence. The work item is written from the last settings that
    /// validated, exactly as a tick is.
    /// </summary>
    [Fact]
    public async Task A_refused_plugins_edit_does_not_turn_a_work_item_into_a_server_error()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(RuntimeApiFixture.DispatcherOff));
        var plugins = fixture.Resolve<LiveSettings<PluginsOptions>>();
        var campaign = await fixture.PostOkAsync<CampaignDto>(
            Operations.CampaignCreate, new CampaignCreateRequest("outreach", null, null, null), Ct);

        File.WriteAllText(fixture.Paths.UserSettingsFile, InvalidPlugins);
        Assert.True(await TestOptions.RefusedAsync(plugins, Ct));

        var item = await fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new WorkItemCreateRequest(
                campaign.Id, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null),
            Ct);

        Assert.Equal(WorkItemStatus.Created, item.Status);
    }

    private static ExecutorService NothingValidatedService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var settings = TestOptions.NothingValidated();
        return new ExecutorService(db, clock, new AttemptOutcomes(new JournalWriter(clock), clock, settings), settings);
    }

    private static async Task<(string ItemId, string AttemptId)> SeedAsync(TestDatabase database)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, attempt.PublicId);
    }

    /// <summary>An item already being worked on, leased from now, so a running loop leaves it alone.</summary>
    private static (string ItemId, string AttemptId) SeedRunningAttempt(JasonPaths paths)
    {
        var now = DateTime.UtcNow;
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var campaign = WorkItemFactory.NewCampaign(now: now);
        var item = WorkItemFactory.NewAiRole(campaign, now: now, configure: w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        db.SaveChanges();
        return (item.PublicId, attempt.PublicId);
    }

    private static string SeedClaimable(JasonPaths paths)
    {
        var now = DateTime.UtcNow;
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var campaign = WorkItemFactory.NewCampaign(now: now);
        var item = WorkItemFactory.NewAiRole(campaign, now: now);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item.PublicId;
    }

    private static WorkItemStatus StatusOf(JasonPaths paths, string publicId)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        return db.WorkItems.AsNoTracking().Where(w => w.PublicId == publicId).Select(w => w.Status).Single();
    }

    /// <summary>
    /// Error lines mentioning the needle. Serilog's compact format spells the level "@l" and leaves it out at
    /// Information, so a reader looking for "level" matches nothing and would make every count here vacuous.
    /// </summary>
    private static int ErrorLines(string logs, string needle) =>
        logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains("\"@l\":\"Error\"", StringComparison.Ordinal)
                && line.Contains(needle, StringComparison.Ordinal));

    private static Action<JasonPaths> Settings(string json) => paths => File.WriteAllText(paths.UserSettingsFile, json);

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReadLogs(JasonPaths paths) =>
        string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(File.ReadAllText));
}
