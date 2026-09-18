using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// What a launched role is told about its own memory, and what it is not told. The envelope carries the address
/// of the note and never the note: a document copied in at launch would be what the role believed then, dressed
/// as what is true now, and the role would have no way to see the difference.
/// </summary>
public class RoleMemoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_envelope_points_at_the_role_memory_and_never_carries_it()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "local-host");
        var campaign = await host.CampaignAsync(Ct);

        // A note that already exists, with words in it that could only have come from the note itself.
        await host.Fixture.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            new
            {
                campaign_id = campaign,
                role = "researcher",
                note = new { gatekeeper = "the switchboard hangs up after six", still_open = new[] { "who signs" } },
            },
            Ct);

        var item = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "researcher",
                execution_profile = "local-host",
                max_attempts = 1,
                context = new { behaviour = "echo-envelope" },
            },
            Ct);

        await host.ScanAsync(Ct);

        // The host echoes its envelope and exits without reporting, which ends the attempt; what is being read
        // here is what the child was handed, so how the attempt ended is beside the point.
        var ended = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);
        var attempt = ended.Attempts![0];
        var echoed = await File.ReadAllTextAsync(
            Path.Combine(host.Paths.AttemptWorkDirectory(item.Id, attempt.Id), "stdout.log"), Ct);

        var envelope = JsonSerializer.Deserialize<LaunchEnvelope>(echoed, JasonJson.Options)!;
        Assert.NotNull(envelope.RoleMemory);
        Assert.Equal(campaign, envelope.RoleMemory.CampaignId);
        Assert.Equal("researcher", envelope.RoleMemory.Role);

        // The address, and nothing the note says — not a value and not a key.
        Assert.Contains("\"role_memory\":{\"campaign_id\":", echoed, StringComparison.Ordinal);
        foreach (var fragment in (string[])["switchboard", "gatekeeper", "still_open", "who signs"])
        {
            Assert.DoesNotContain(fragment, echoed, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Deterministic work has no memory to be given, and is told so in the envelope's own way: the field is
    /// there and null, exactly as <c>role</c> is null beside it. A reader never has to tell a missing field
    /// from a field this build does not write.
    /// </summary>
    [Fact]
    public void Work_that_is_not_a_role_is_told_about_no_memory_at_all()
    {
        var envelope = new LaunchEnvelope(
            LaunchEnvelope.CurrentVersion,
            "att_A",
            1,
            "wi_A",
            "cmp_A",
            null,
            WorkItemKind.ProviderOp,
            null,
            null,
            [],
            null,
            60,
            10,
            DateTimeOffset.UnixEpoch,
            "/work",
            new RuntimeLocation("/run/runtime.json", ApiVersion.Current));

        Assert.Null(envelope.RoleMemory);
        Assert.Contains("\"role\":null,", JsonSerializer.Serialize(envelope, JasonJson.Options), StringComparison.Ordinal);
        Assert.Contains("\"role_memory\":null", JsonSerializer.Serialize(envelope, JasonJson.Options), StringComparison.Ordinal);
    }

    private static async Task ProfileAsync(FakeHostRuntime host, string name) =>
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name, host = "claude_code", program = ProfileFixture.Program, host_version_verified = "stand-in" },
            Ct);
}
