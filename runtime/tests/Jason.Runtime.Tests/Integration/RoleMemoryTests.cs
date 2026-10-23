using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;
using Jason.Runtime.Harness;

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

        // The campaign's project, so the role's notes at that level are found as its campaign's are.
        var recorded = await host.Fixture.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = campaign }, Ct);
        Assert.Equal(recorded.ProjectId, envelope.RoleMemory.ProjectId);

        // The address, and nothing the note says — not a value and not a key.
        Assert.Contains("\"role_memory\":{\"campaign_id\":", echoed, StringComparison.Ordinal);
        foreach (var fragment in (string[])["switchboard", "gatekeeper", "still_open", "who signs"])
        {
            Assert.DoesNotContain(fragment, echoed, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The person's three memory files are named by path where their folders are Jason's — the root by its marker, the
    /// campaign's project and the campaign by the folders recorded on them — and nothing a file says reaches the envelope.
    /// </summary>
    [Fact]
    public async Task The_envelope_names_the_memory_files_of_the_root_and_the_recorded_folders_and_never_their_words()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "local-host");
        var campaign = await host.CampaignAsync(Ct);
        var (root, project, folder) = await FoldersAsync(host, campaign);

        // Words that could only have come from the files themselves.
        await File.WriteAllTextAsync(Path.Combine(root, "AGENTS.md"), "We write as Marta, plainly.", Ct);
        await File.WriteAllTextAsync(Path.Combine(project, "AGENTS.md"), "Acme sells to clinics.", Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "AGENTS.md"), "This quarter: dentists in Lyon.", Ct);

        var (envelope, echoed) = await LaunchAsync(host, campaign);

        Assert.Equal(
            new MemoryFileLocations(Path.Combine(root, "AGENTS.md"), Path.Combine(project, "AGENTS.md"), Path.Combine(folder, "AGENTS.md")),
            envelope.MemoryFiles);
        foreach (var fragment in (string[])["Marta", "plainly", "clinics", "dentists", "Lyon"])
        {
            Assert.DoesNotContain(fragment, echoed, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// What is known at the launch is what is named: a campaign's folder removed since it was recorded is still named,
    /// since the record is what says the folder is Jason's and a stale path is the host's to find; a root whose marker is
    /// gone is not, since nothing but the marker says the root is Jason's.
    /// </summary>
    [Fact]
    public async Task A_recorded_folder_gone_since_is_still_named_and_a_root_without_its_marker_is_not()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "local-host");
        var campaign = await host.CampaignAsync(Ct);
        var (root, project, folder) = await FoldersAsync(host, campaign);

        Directory.Delete(folder, recursive: true);
        File.Delete(HarnessMarker.PathIn(root));

        var (envelope, _) = await LaunchAsync(host, campaign);

        Assert.Equal(new MemoryFileLocations(null, Path.Combine(project, "AGENTS.md"), Path.Combine(folder, "AGENTS.md")), envelope.MemoryFiles);
        Assert.False(Directory.Exists(folder), $"the launch made the campaign's folder again at '{folder}'");
    }

    /// <summary>
    /// Deterministic work has no memory to be given, and is told so in the envelope's own way: the field is
    /// there and null, exactly as <c>role</c> is null beside it. A reader never has to tell a missing field
    /// from a field this build does not write.
    /// <para>
    /// This is the contract's default rather than a path through the runtime — the only command that writes an
    /// envelope is the one that runs <c>ai_role</c> work, so nothing launched today can reach the null branch.
    /// It is pinned anyway because the field is part of a published contract that hosts other than this one
    /// read, and "absent" and "null" are different answers to a host that checks.
    /// </para>
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

    /// <summary>The root the runtime made its folders under, and the folders recorded for the campaign's project and the campaign.</summary>
    private static async Task<(string Root, string Project, string Campaign)> FoldersAsync(FakeHostRuntime host, string campaignId)
    {
        var campaign = await host.Fixture.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = campaignId }, Ct);
        var project = await host.Fixture.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { project_id = campaign.ProjectId }, Ct);
        Assert.NotNull(campaign.FolderPath);
        Assert.NotNull(project.FolderPath);
        return (host.Fixture.Resolve<HarnessFolders>().Root, project.FolderPath, campaign.FolderPath);
    }

    /// <summary>One role's attempt that echoes its envelope and ends, and the envelope as it was read and as it was written.</summary>
    private static async Task<(LaunchEnvelope Envelope, string Echoed)> LaunchAsync(FakeHostRuntime host, string campaign)
    {
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

        var ended = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);
        var echoed = await File.ReadAllTextAsync(
            Path.Combine(host.Paths.AttemptWorkDirectory(item.Id, ended.Attempts![0].Id), "stdout.log"), Ct);
        return (JsonSerializer.Deserialize<LaunchEnvelope>(echoed, JasonJson.Options)!, echoed);
    }

    private static async Task ProfileAsync(FakeHostRuntime host, string name) =>
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name, host = "claude_code", program = ProfileFixture.Program, host_version_verified = "0.0.0-stand-in" },
            Ct);
}
