using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// An entry from the row to the host, through the real claim and the real launcher: a check-in the runtime created with
/// an entry is claimed by a scan, and the child it starts reads that entry's text in its envelope, from a role folder of
/// the person's own with no record of a deployment; the attempt's launch record names it.
/// </summary>
public class EntryLaunchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Text = "# Deep review\n\nRead every open thread before you decide.\n";

    [Fact]
    public async Task A_check_in_created_with_an_entry_is_launched_with_its_text_in_the_envelope()
    {
        var command = JsonSerializer.Serialize(Execution.FakeAgentHost.EntryCommand("echo-envelope"), JasonJson.Options);
        await using var host = await FakeHostRuntime.StartAsync(
            Ct,
            Dispatch.StartlessTriggers.Into(
                """{"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0,"ExitGraceSeconds":30,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":1}},"Roles":{"DefaultEntryCommand":"""
                    + command
                    + "}}"),
            prepare: paths =>
            {
                var folder = Directory.CreateDirectory(Path.Combine(paths.RoleSkillsDirectory, ManagerCheckIn.Role)).FullName;
                File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: manager\ndescription: one line\n---\n\nReview the campaign.\n");
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "entries")).FullName, "deep_review.md"), Text);
            });
        var campaign = await host.CampaignAsync(Ct);

        string id;
        using (var scope = host.Fixture.Resolve<IServiceScopeFactory>().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var row = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
            var checkIn = await ManagerCheckIn.CreateAsync(
                scope.ServiceProvider.GetRequiredService<WorkItemService>(), db, row, cause: null, new ManagerOptions(), Ct, "deep_review");
            await db.SaveChangesAsync(Ct);
            id = checkIn.PublicId;
        }

        host.Track(id);
        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        var item = await host.WaitAsync(id, w => w.Attempts!.Any(a => a.Launch?.ExitCode is not null), Ct, "a recorded launch");

        var attempt = Assert.Single(item.Attempts!);
        Assert.Equal("deep_review", item.Entry);
        Assert.Equal(new LaunchEntryRecordDto("deep_review", System.Text.Encoding.UTF8.GetByteCount(Text)), attempt.Launch!.Entry);
        var echoed = await File.ReadAllTextAsync(Path.Combine(host.Paths.AttemptWorkDirectory(id, attempt.Id), "stdout.log"), Ct);
        var envelope = JsonSerializer.Deserialize<LaunchEnvelope>(echoed, JasonJson.Options)!;
        Assert.Equal(LaunchEnvelope.CurrentVersion, envelope.EnvelopeVersion);
        Assert.Equal(new LaunchEntry("deep_review", Text), envelope.Entry);
    }
}
