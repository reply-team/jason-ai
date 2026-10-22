using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The log files are read by agents and pasted into bug reports. The dispatcher must be traceable through them
/// — which item, which attempt — without any of what the work was actually about leaking out with it. Verbose
/// logging is deliberately on here: the rule has to hold at the noisiest level, not only at the default one.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class DispatcherLoggingTests
{
    private const string ContextKey = "canary_key_7f3a";
    private const string ContextValue = "canary-value-9c1d";
    private const string ResultValue = "canary-result-2b8e";
    private const string StderrTail = "canary-stderr-4d5c";

    private const string Settings = """
        {"Logging":{"MinimumLevel":"Debug"},
         "Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0},
         "Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    private const string ProviderCampaign = "cmp_42";

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.B3, "the dispatcher logs ids only")]
    public async Task The_dispatcher_logs_ids_and_nothing_the_work_was_about()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(dir.Paths.UserSettingsFile, Settings, Ct);

        var command = new CanaryCommand();
        var runtime = await RuntimeHost.StartAsync(
            dir.Paths,
            TestRuntimeOptions.Quiet with
            {
                Clock = new FixedClock(Noon),

                // Replacing the hook rather than adding to it, which is why the transport that refuses the
                // release feed is a property of its own: this line cannot take it away. What the hook turns off
                // is turned off again here, so the command is the only thing this test adds.
                ConfigureServices = services =>
                {
                    TestRuntimeOptions.TurnTheOutsideOff(services);
                    services.AddSingleton<ICommand>(command);
                },
            },
            Ct);

        string itemId;
        string token = runtime.Token;
        try
        {
            // The loop scans the moment it starts; seeding after that scan keeps the next one the test's own.
            Assert.True(await DispatchHarness.FirstScanDoneAsync(runtime.Services.GetRequiredService<DispatcherStatus>(), Ct));
            itemId = Seed(dir.Paths);
            var runner = runtime.Services.GetRequiredService<ScanRunner>();
            Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
            Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(dir.Paths, itemId) == WorkItemStatus.Created, Ct));
            Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
            Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(dir.Paths, itemId) == WorkItemStatus.Failed, Ct));
        }
        finally
        {
            // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
            await runtime.StopAsync();
        }

        List<string> attemptIds;
        await using (var db = Open(dir.Paths))
        {
            attemptIds = await db.Attempts.AsNoTracking().Select(a => a.PublicId).ToListAsync(Ct);
        }

        var logs = string.Concat(await Task.WhenAll(
            Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(file => File.ReadAllTextAsync(file, Ct))));

        Assert.Contains(itemId, logs, StringComparison.Ordinal);
        Assert.Equal(2, attemptIds.Count);
        Assert.All(attemptIds, id => Assert.Contains(id, logs, StringComparison.Ordinal));

        Assert.DoesNotContain(ContextKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ContextValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ResultValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(StderrTail, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("context_snapshot", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person reading the log for one campaign has to be able to follow its attempts, and the lines that matter
    /// most are the ones that name no ids of their own: the plugin invocation says which plugin and which
    /// operation, and nothing about whose work it was. Every line written while an attempt is claimed, started
    /// and run carries the campaign, the work item and the attempt, whatever its own message says. Read from the
    /// file the real sink wrote, because that is the only place a person ever reads them.
    /// </summary>
    [Fact]
    public async Task Every_line_of_a_provider_attempt_names_its_campaign_work_item_and_attempt()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Outbound", "Active");
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, Settings);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
                TestRoutes.WriteGlobal(
                    paths,
                    TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = workspace.Root }));
            },
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

        // The loop scans the moment it starts; seeding after that scan keeps the next one the test's own.
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Flow" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var created = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign.Id,
                kind = "provider_op",
                operation = "campaign.get",
                context = new JsonObject
                {
                    ["input"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = ProviderCampaign } },
                },
            },
            Ct);

        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)));

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = created.Id }, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, read.Status);
        var attempt = Assert.Single(read.Attempts!);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var lines = (await Task.WhenAll(
                Directory.GetFiles(api.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(file => File.ReadAllLinesAsync(file, Ct))))
            .SelectMany(file => file)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();

        // The four lines of an attempt's life: the claim, the start, and the plugin invocation's two.
        var named = new[]
        {
            LineOf(lines, "Dispatcher claimed work item "),
            LineOf(lines, "Attempt {AttemptId} of work item {WorkItemId} started"),
            LineOf(lines, " started as pid "),
            LineOf(lines, " ended: exit "),
        };

        Assert.All(named, line =>
        {
            Assert.Equal(campaign.Id, (string?)line["CampaignId"]);
            Assert.Equal(created.Id, (string?)line["WorkItemId"]);
            Assert.Equal(attempt.Id, (string?)line["AttemptId"]);
        });

        // The scope is the attempt's and ends with it: the scan's own summary is written after the claim's scope
        // has closed, and says nothing of any one attempt.
        var scan = Assert.Single(lines, line => ((string?)line["@mt"])?.StartsWith("Dispatcher scan ", StringComparison.Ordinal) == true && (int?)line["Claimed"] == 1);
        Assert.False(scan.ContainsKey("AttemptId"));
        Assert.False(scan.ContainsKey("CampaignId"));
    }

    /// <summary>
    /// The same for agent work. The launcher's two lines — the child started, the child ended — name the attempt
    /// and the work item in their own messages and the campaign nowhere, so a log read for one campaign finds them
    /// only through the scope the handler holds while the child runs. A real child, started by the real launcher from
    /// a role's entry command, and the lines read from the file the real sink wrote.
    /// </summary>
    [Fact]
    public async Task Every_line_of_a_role_launch_names_its_campaign_work_item_and_attempt()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-succeed", Ct, "succeed");
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-succeed" }, Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);

        // The launch is written down after the child has ended, and the launcher's last line before that.
        var item = await host.WaitAsync(created.Id, w => w.Attempts!.Any(a => a.Launch?.ExitCode is not null), Ct, "a recorded launch");
        var attempt = Assert.Single(item.Attempts!);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await host.Fixture.Runtime.StopAsync();
        var lines = (await Task.WhenAll(
                Directory.GetFiles(host.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(file => File.ReadAllLinesAsync(file, Ct))))
            .SelectMany(file => file)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();

        var launched = new[]
        {
            Assert.Single(lines, line => (string?)line["@mt"] == "Attempt {AttemptId} of work item {WorkItemId} started as pid {Pid}"),
            Assert.Single(lines, line => (string?)line["@mt"] == "Attempt {AttemptId} of work item {WorkItemId} ended with exit code {ExitCode}"),
        };

        Assert.All(launched, line =>
        {
            Assert.Equal(campaign, (string?)line["CampaignId"]);
            Assert.Equal(created.Id, (string?)line["WorkItemId"]);
            Assert.Equal(attempt.Id, (string?)line["AttemptId"]);
        });
    }

    /// <summary>
    /// The summoner's lines are about a campaign, not about an attempt, and name it in their own messages. The summon
    /// runs here through the scan, the way the runtime runs it, so a scope the scan opened around it would show on its
    /// lines; what is held is the exact text and that the scan gives the summon no scope.
    /// </summary>
    [Fact]
    public async Task The_summoners_lines_name_their_campaign_themselves_and_carry_no_attempt_scope()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        var campaign = await SeedDueCampaignAsync(harness);

        Assert.Equal(1, (await harness.Runner.ScanOnceAsync(Ct)).Summoned);

        var summoned = Assert.Single(harness.Logs.Lines, line => line.Message.StartsWith("Dispatcher summoned", StringComparison.Ordinal));
        Assert.Equal($"Dispatcher summoned a manager for campaign {campaign}: scheduled", summoned.Message);
        Assert.Empty(summoned.Scope);
    }

    /// <summary>
    /// The same for the line that survives a failed summon, run through the scan too: its campaign is in its message,
    /// and nowhere else.
    /// </summary>
    [Fact]
    public async Task A_failed_summon_still_names_its_campaign_in_its_message_alone()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        var campaign = await SeedDueCampaignAsync(harness);
        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the writer was busy"));

        Assert.Equal(0, (await harness.Runner.ScanOnceAsync(Ct)).Summoned);

        var failed = Assert.Single(harness.Logs.Lines, line => line.Message.StartsWith("The summon for campaign", StringComparison.Ordinal));
        Assert.Equal($"The summon for campaign {campaign} failed; the rest of the scan continues without it", failed.Message);
        Assert.Empty(failed.Scope);
    }

    /// <summary>
    /// An attempt that ends at the claim never reaches a handler, so no line a handler writes exists to carry it:
    /// the claim writes one itself, under the attempt's scope, or a person asking the log what became of a work
    /// item finds nothing for the most common reason work does not run. Its message says the code and never the
    /// attempt's own error text, which names what the work was about.
    /// </summary>
    [Fact]
    public async Task A_provider_attempt_refused_at_the_claim_writes_one_line_under_its_scope_with_the_code()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        await using (var db = harness.Open())
        {
            db.Campaigns.Add(campaign);
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Empty(await harness.ClaimAsync(Ct));

        var stored = await harness.ReadItemAsync(item.PublicId, Ct);
        var attempt = Assert.Single(stored.Attempts);
        Assert.Equal(AttemptErrors.NoRoute, attempt.Error!.Code);

        var line = Assert.Single(harness.Logs.Lines, each => each.Scope.ContainsKey("AttemptId"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal($"Attempt {attempt.PublicId} of work item {item.PublicId} ended at the claim: no_route", line.Message);
        Assert.Equal(1, harness.Logs.Count(line.Message));
        Assert.Equal(campaign.PublicId, line.Scope["CampaignId"]);
        Assert.Equal(item.PublicId, line.Scope["WorkItemId"]);
        Assert.Equal(attempt.PublicId, line.Scope["AttemptId"]);
        Assert.False(harness.Logs.Mentions(attempt.Error.Message));
    }

    /// <summary>Agent work refused at the claim ends the same way and is said the same way.</summary>
    [Fact]
    public async Task An_agent_attempt_that_cannot_be_launched_writes_the_same_line()
    {
        using var harness = new DispatchHarness(Noon, roles: new RolesOptions());
        var item = await harness.SeedClaimableAsync(Ct);

        Assert.Empty(await harness.ClaimAsync(Ct));

        var stored = await harness.ReadItemAsync(item.PublicId, Ct);
        var attempt = Assert.Single(stored.Attempts);
        Assert.Equal(AttemptErrors.RoleNotLaunchable, attempt.Error!.Code);

        var line = Assert.Single(harness.Logs.Lines, each => each.Scope.ContainsKey("AttemptId"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal($"Attempt {attempt.PublicId} of work item {item.PublicId} ended at the claim: role_not_launchable", line.Message);
        Assert.Equal(1, harness.Logs.Count(line.Message));
        Assert.Equal(item.Campaign!.PublicId, line.Scope["CampaignId"]);
        Assert.Equal(item.PublicId, line.Scope["WorkItemId"]);
        Assert.Equal(attempt.PublicId, line.Scope["AttemptId"]);
        Assert.False(harness.Logs.Mentions(attempt.Error.Message));
    }

    /// <summary>
    /// Agent work is refused by a different part of the claim than a provider operation is — the planner answers for
    /// it — and what it refuses with is said exactly as every other refusal is.
    /// </summary>
    [Fact]
    public async Task An_agent_attempt_refused_for_its_profile_writes_the_same_line()
    {
        using var harness = new DispatchHarness(
            Noon,
            roles: new RolesOptions { DefaultEntryCommand = ["agent-host"], DefaultExecutionProfile = "gone" });
        var item = await harness.SeedClaimableAsync(Ct);

        Assert.Empty(await harness.ClaimAsync(Ct));

        var stored = await harness.ReadItemAsync(item.PublicId, Ct);
        var attempt = Assert.Single(stored.Attempts);
        Assert.Equal(AttemptErrors.ProfileNotFound, attempt.Error!.Code);

        var line = Assert.Single(harness.Logs.Lines, each => each.Scope.ContainsKey("AttemptId"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal($"Attempt {attempt.PublicId} of work item {item.PublicId} ended at the claim: profile_not_found", line.Message);
        Assert.Equal(1, harness.Logs.Count(line.Message));
        Assert.Equal(item.Campaign!.PublicId, line.Scope["CampaignId"]);
        Assert.False(harness.Logs.Mentions(attempt.Error.Message));
    }

    /// <summary>
    /// A claim takes one item from each campaign, so a runtime with several campaigns whose work is refused ends several
    /// attempts in one scan. Each is said under its own scope: one scope held across them would put the first campaign's
    /// id on every line after it, and a log read for the second campaign would not find its attempt.
    /// </summary>
    [Fact]
    public async Task Two_attempts_the_claim_ends_in_one_scan_are_each_said_under_their_own_scope()
    {
        using var harness = new DispatchHarness(Noon);
        var first = WorkItemFactory.NewCampaign(now: Noon);
        var second = WorkItemFactory.NewCampaign("Other", now: Noon);
        var firstItem = WorkItemFactory.NewProviderOp(first, now: Noon);
        var secondItem = WorkItemFactory.NewProviderOp(second, now: Noon);
        await using (var db = harness.Open())
        {
            db.Campaigns.AddRange(first, second);
            db.WorkItems.AddRange(firstItem, secondItem);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Empty(await harness.ClaimAsync(Ct));

        var lines = harness.Logs.Lines.Where(each => each.Scope.ContainsKey("AttemptId")).ToList();
        Assert.Equal(2, lines.Count);
        foreach (var (campaign, item) in new[] { (first, firstItem), (second, secondItem) })
        {
            var attempt = Assert.Single((await harness.ReadItemAsync(item.PublicId, Ct)).Attempts);
            var line = Assert.Single(lines, each => (string?)each.Scope["WorkItemId"] == item.PublicId);
            Assert.Equal($"Attempt {attempt.PublicId} of work item {item.PublicId} ended at the claim: no_route", line.Message);
            Assert.Equal(1, harness.Logs.Count(line.Message));
            Assert.Equal(campaign.PublicId, line.Scope["CampaignId"]);
            Assert.Equal(attempt.PublicId, line.Scope["AttemptId"]);
        }
    }

    /// <summary>An attempt the claim hands to a handler has not ended, and says nothing of an end.</summary>
    [Fact]
    public async Task An_attempt_the_claim_hands_out_writes_no_line_about_an_end()
    {
        using var harness = new DispatchHarness(Noon);
        await harness.SeedClaimableAsync(Ct);

        Assert.Single(await harness.ClaimAsync(Ct));

        Assert.DoesNotContain(harness.Logs.All, message => message.Contains("ended at the claim", StringComparison.Ordinal));
    }

    /// <summary>The one line whose template contains the fragment.</summary>
    private static JsonObject LineOf(IReadOnlyList<JsonObject> lines, string fragment) =>
        Assert.Single(lines, line => ((string?)line["@mt"])?.Contains(fragment, StringComparison.Ordinal) == true);

    /// <summary>An active campaign whose manager review is due the moment the harness clock has moved an hour.</summary>
    private static async Task<string> SeedDueCampaignAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        campaign.ManagerReviewAnchor = Noon;
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        return campaign.PublicId;
    }

    private static string Seed(JasonPaths paths)
    {
        using var db = Open(paths);
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.Context[ContextKey] = ContextValue;
            w.MaxAttempts = 2;
        });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item.PublicId;
    }

    private static WorkItemStatus StatusOf(JasonPaths paths, string publicId)
    {
        using var db = Open(paths);
        return db.WorkItems.AsNoTracking().Where(w => w.PublicId == publicId).Select(w => w.Status).Single();
    }

    private static JasonDbContext Open(JasonPaths paths) => new(JasonDbContext.CreateOptions(paths.DatabaseFile));

    /// <summary>Reports the loudest thing an executor can: a crash carrying its own output and a result.</summary>
    private sealed class CanaryCommand : ICommand
    {
        public WorkItemKind Kind => WorkItemKind.AiRole;

        public Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            Assert.Equal(ContextValue, (string?)context.ContextSnapshot[ContextKey]);
            return Task.FromResult<CommandOutcome>(new CommandOutcome.Exited(
                3,
                $"the executor said {StderrTail} and {ResultValue}",
                new AttemptLaunchDto(context.EntryCommand, context.WorkDir, 1234, 3)));
        }
    }
}
