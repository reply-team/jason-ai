using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Notifications;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Decisions;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Notifications.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// Nothing about the work reaches any channel. A campaign, a contact, an approval's subject and preview, and a question
/// with its option are seeded, each carrying one marker; a notification about them is sent through the toast as every
/// platform composes it and through a notification plugin run by the real plugin host, which logs everything it was
/// handed. The marker is in none of it — and, as the control beside it, it is in the rows the seed wrote.
/// </summary>
/// <remarks>
/// Green on arrival by construction: the pointer is made from two integers, so there is nothing for a channel to carry.
/// What this holds is that it stays that way, for every channel there is.
/// </remarks>
public sealed class PointerOnlyTests
{
    private const string Marker = "7f3c";

    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Self : IPluginHostLocator
    {
        public IReadOnlyList<string> Command { get; } = ["C:\\jason\\jason.exe"];
    }

    /// <summary>Keeps every launch and answers it as a program that showed the toast would.</summary>
    private sealed class RecordingLauncher : IToastLauncher
    {
        private const string Shown = """{"protocol_version":1,"registration":"present","registration_hr":null,"calls":[],"failed_step":null,"show_hr":"0x00000000","setting_after_hr":"0x00000000","setting_after":0}""";

        private readonly ConcurrentQueue<ToastLaunch> _launches = new();

        public IReadOnlyList<ToastLaunch> Launches => [.. _launches];

        public Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken)
        {
            _launches.Enqueue(launch);
            var probe = launch.Command.Contains("org.freedesktop.DBus.NameHasOwner");
            return Task.FromResult(new LaunchResult(true, false, 0, probe ? "(true,)" : Shown, string.Empty));
        }
    }

    [Fact]
    public async Task Nothing_about_the_work_reaches_any_channel()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => TestPlugins.Write(
            paths,
            "seer",
            """
            manifest_version: 1
            id: seer
            version: 1.0.0
            kind: notification
            contracts:
              protocol: [1]
              operations: [1]
            """,
            "export function invoke(operation, input, context) { host.log('info', 'seen', { input: input, context: context }); return { result: { receipt: 'seen' } }; }"));
        await SeedAsync(api);

        var launcher = new RecordingLauncher();
        var environment = new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus" };
        DesktopFacts Facts(DesktopPlatform platform, params string[] programs) =>
            new(platform, 2, environment, program => programs.Contains(program) ? "/usr/bin/" + program : null, _ => false);
        ToastChannel Toast(DesktopFacts facts) =>
            new(launcher, new Self(), facts, DesktopPresence.Find(facts), api.Resolve<ILogger<ToastChannel>>());

        var plugin = new PluginNotificationChannel("seer", new NotificationPluginEntry(), api.Resolve<PluginRegistry>(), api.Resolve<IServiceScopeFactory>());
        var logs = new RecordingLogs();
        var sender = new NotificationSender(
            api.Resolve<IServiceScopeFactory>(),
            new FakeChannels(
                Toast(Facts(DesktopPlatform.Windows)),
                Toast(Facts(DesktopPlatform.MacOs)),
                Toast(Facts(DesktopPlatform.Linux, "notify-send", "gdbus")),
                Toast(Facts(DesktopPlatform.Linux, "gdbus")),
                plugin),
            new NotifierState(),
            new NotificationStatus(),
            // Notifications on for this sender alone: the runtime the fixture started keeps them off, and a sender that
            // finds every channel off says nothing.
            new LiveSettings<NotificationsOptions>(
                new TestOptionsMonitor<NotificationsOptions>(new NotificationsOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveSettings<NotificationsOptions>>.Instance,
                NotificationsOptions.Section),
            TimeProvider.System,
            new Logger<NotificationSender>(LoggerFactory.Create(builder => builder.AddProvider(logs))));

        var line = await sender.SendAsync(new NotificationRequest("ntf_01K0pointer", null, null, new Burst(2, Noon, Noon)), Ct);

        // Every channel was given the pointer, and every one of them said it was shown.
        Assert.NotNull(line);
        Assert.Equal((1, 1), (line.Approvals, line.Decisions));
        Assert.All(line.Channels, outcome => Assert.Equal(ChannelResult.Delivered, outcome.Result));

        // The toast, as every platform composes it: command, stdin and environment.
        Assert.Equal(6, launcher.Launches.Count);
        Assert.Contains(launcher.Launches, launch => launch.Command.Contains("org.freedesktop.DBus.NameHasOwner"));
        foreach (var launch in launcher.Launches)
        {
            Assert.DoesNotContain(Marker, string.Join(' ', launch.Command), StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, launch.Stdin ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, string.Join(' ', launch.Environment.Select(pair => pair.Key + "=" + pair.Value)), StringComparison.Ordinal);
        }

        // The plugin, by its own account of what it was handed: input and context, binding included.
        var seen = PluginLogs(api);
        Assert.Contains("\"seen\"", seen, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, seen, StringComparison.Ordinal);
        var context = SeenContext(seen);
        Assert.Null(context["campaign_id"]);
        Assert.Null(context["work_item_id"]);
        Assert.Null(context["attempt_id"]);

        // The line, the sender's own log, and the runtime's.
        Assert.DoesNotContain(Marker, line.ToJson().ToJsonString(), StringComparison.Ordinal);
        await using (var db = Open(api))
        {
            var written = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.NotificationSent).Select(e => e.New).ToListAsync(Ct);
            Assert.DoesNotContain(Marker, string.Join('\n', written.Select(node => node?.ToJsonString())), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(logs.All, entry => entry.Contains(Marker, StringComparison.Ordinal));
        Assert.DoesNotContain(Marker, RuntimeLogs(api), StringComparison.Ordinal);

        // The control: the marker is where the seed put it, so a search that matched nothing proves something.
        await using (var db = Open(api))
        {
            Assert.Contains(Marker, (await db.Campaigns.AsNoTracking().Select(c => c.Name).ToListAsync(Ct)).Single(name => name.Contains(Marker, StringComparison.Ordinal)), StringComparison.Ordinal);
            Assert.Contains(await db.ContactChannels.AsNoTracking().Select(c => c.Value).ToListAsync(Ct), value => value.Contains(Marker, StringComparison.Ordinal));
            var approval = await db.Approvals.AsNoTracking().SingleAsync(Ct);
            Assert.Contains("subject-" + Marker, approval.Subject.ToJsonString(), StringComparison.Ordinal);
            Assert.Contains("preview-" + Marker, approval.Preview.ToJsonString(), StringComparison.Ordinal);
            var decision = await db.Decisions.AsNoTracking().SingleAsync(Ct);
            Assert.Equal("question-" + Marker, decision.Question);
            Assert.Contains("option-" + Marker, decision.Options!.ToJsonString(), StringComparison.Ordinal);
        }
    }

    private static JasonDbContext Open(RuntimeApiFixture api) =>
        api.Runtime.Services.CreateScope().ServiceProvider.GetRequiredService<JasonDbContext>();

    /// <summary>A parked enrollment and a raised question, each carrying the marker wherever a person's words go.</summary>
    private static async Task SeedAsync(RuntimeApiFixture api)
    {
        await using var db = Open(api);
        var campaign = WorkItemFactory.NewCampaign(name: "Acme Renewal Q4 " + Marker, now: Noon);
        db.Campaigns.Add(campaign);

        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"ada.lovelace.{Marker}@example.com", IsPrimary = true });
        db.Contacts.Add(contact);

        var parked = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        parked.Contact = contact;
        parked.Status = WorkItemStatus.AwaitingApproval;
        db.WorkItems.Add(parked);
        db.Approvals.Add(new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItem = parked,
            CampaignId = campaign.Id,
            Operation = "campaign.enroll",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "campaign.enroll", ["subject"] = "subject-" + Marker },
            SubjectHash = "sha256:" + Marker,
            Preview = new JsonObject { ["intent"] = "preview-" + Marker },
            PluginId = "stand-in-provider",
            BindingIdentity = "sha256:account",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_one",
            RoutingSnapshotId = "rts_one",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
        });

        var asking = WorkItemFactory.NewAiRole(campaign, now: Noon);
        asking.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = PublicId.New("att"),
            WorkItem = asking,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };
        db.WorkItems.Add(asking);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        await new DecisionService(db, new JournalWriter(TimeProvider.System), TimeProvider.System).RaiseAsync(
            new DecisionRaiseRequest(asking.PublicId, attempt.PublicId, "question-" + Marker, [new DecisionOption("option-" + Marker, null)], null, null),
            Ct);
    }

    /// <summary>Everything the plugin's invocations wrote in their own directories.</summary>
    private static string PluginLogs(RuntimeApiFixture api)
    {
        var work = api.Resolve<JasonPaths>().PluginWorkDirectory;
        return string.Concat(Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Select(Jason.Runtime.Tests.Integration.FakeHostRuntime.ReadShared));
    }

    /// <summary>The context the plugin logged, read back from its <c>seen</c> line.</summary>
    private static JsonObject SeenContext(string logs)
    {
        foreach (var text in logs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (JsonNode.Parse(text) is JsonObject entry && entry.ToJsonString().Contains("\"seen\"", StringComparison.Ordinal))
            {
                var context = FindContext(entry);
                if (context is not null)
                {
                    return context;
                }
            }
        }

        Assert.Fail("The plugin never logged what it was handed.");
        return null!;

        static JsonObject? FindContext(JsonNode? node) => node switch
        {
            JsonObject members when members["context"] is JsonObject context && context.ContainsKey("binding") => context,
            JsonObject members => members.Select(member => FindContext(member.Value)).FirstOrDefault(found => found is not null),
            _ => null,
        };
    }

    private static string RuntimeLogs(RuntimeApiFixture api)
    {
        var directory = api.Resolve<JasonPaths>().LogsDirectory;
        return Directory.Exists(directory)
            ? string.Concat(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(Jason.Runtime.Tests.Integration.FakeHostRuntime.ReadShared))
            : string.Empty;
    }
}
