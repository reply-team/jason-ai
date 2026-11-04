using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Notifications;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A restart as it happens: one runtime records that its toast found no desktop and stops; a later one starts on the same
/// data directory with a desktop, reads that line back at its first scan, and is asked by both of the CLI's views. The
/// later runtime does not carry the earlier "no desktop" into its own status, so its toast comes with no outcome at all —
/// and a notification did go out, before it started. Nothing has reached the person since this runtime started, which is
/// not "yet".
/// </summary>
public sealed class NotificationsAfterARestartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Nothing waits, so nothing is sent: a launch here would be a toast shown by a test.</summary>
    private sealed class NoLaunch : IToastLauncher
    {
        public Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nothing waits in this test, so nothing may be launched.");
    }

    [Fact]
    public async Task After_a_restart_both_views_say_nothing_has_reached_the_person_since_this_runtime_started()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff, Ct);

        // The earlier runtime, whose toast found no desktop for the one notification it sent.
        var earlier = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        try
        {
            await using var scope = earlier.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var line = new NotificationLine(
                "ntf_earlier",
                1,
                0,
                null,
                [ChannelOutcome.NotAttempted("toast", ChannelKinds.Toast, NotAttemptedReasons.NoDesktop, "session 0")],
                null);
            new JournalWriter(TimeProvider.System).Append(db, Actors.Runtime, JournalKinds.NotificationSent, campaign: null, key: "notification", updated: line.ToJson());
            await db.SaveChangesAsync(Ct);
        }
        finally
        {
            await earlier.StopAsync();
        }

        // The later one: notifications on, a desktop, and the dispatch loop, whose first scan reads the line back.
        File.Delete(dir.Paths.UserSettingsFile);
        var later = await RuntimeHost.StartAsync(
            dir.Paths,
            TestRuntimeOptions.Quiet with
            {
                ConfigureServices = services =>
                {
                    TestRuntimeOptions.TurnTheOutsideOff(services);
                    services.PostConfigure<NotificationsOptions>(notifications => notifications.Enabled = true);
                    services.AddSingleton(Presence.Desktop);
                    services.AddSingleton<IToastLauncher>(new NoLaunch());
                },
            },
            Ct);
        try
        {
            var status = later.Services.GetRequiredService<NotificationStatus>();
            Assert.True(
                await DispatchHarness.EventuallyAsync(() => status.Snapshot().LastNotificationId == "ntf_earlier", Ct),
                "The later runtime never read the earlier line back, so nothing below is about a restart.");

            // system.info, as the CLI prints it: the line read back, the toast with no outcome, and the line older than
            // this runtime.
            var info = JsonNode.Parse((await RunAsync(dir, "runtime", "status")).Output)!.AsObject();
            var notifications = info["notifications"]!;
            Assert.Equal("ntf_earlier", (string?)notifications["last_notification_id"]);
            var toast = Assert.Single(notifications["channels"]!.AsArray(), channel => (string?)channel!["channel"] == "toast")!;
            Assert.Null(toast["outcome"]);
            Assert.True((DateTimeOffset)notifications["last_at"]! < (DateTimeOffset)info["started_at"]!);

            // Both views.
            var human = (await RunAsync(dir, "runtime", "status", "--human")).Output.Split('\n').Select(text => text.TrimEnd('\r'));
            Assert.Equal("Notify:     toast on, nothing sent since this runtime started", Assert.Single(human, text => text.StartsWith("Notify:", StringComparison.Ordinal)));

            var report = JsonNode.Parse((await RunAsync(dir, "status")).Output)!.AsObject();
            var check = Assert.Single(report["checks"]!.AsArray(), check => (string?)check!["name"] == "notifications")!;
            Assert.Equal(
                ("ok", "Notifications reach you by toast; none has reached you since this runtime started."),
                ((string?)check["state"], (string?)check["fact"]));
        }
        finally
        {
            await later.StopAsync();
        }
    }

    /// <summary>
    /// The CLI against the later runtime, with nothing of the machine's: no PATH of the person's, and the seams that
    /// would read the machine left at their refusals.
    /// </summary>
    private static async Task<(int Exit, string Output)> RunAsync(TempDataDir dir, params string[] args)
    {
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(args, new CliEnvironment(output, new StringWriter(), dir.Paths, SearchPath: Path.Combine(dir.Paths.Root, "nowhere")), Ct);
        return (exit, output.ToString().Trim());
    }
}
