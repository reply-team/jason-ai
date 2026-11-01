using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Notes;

/// <summary>
/// The note operations at their three levels over the API, each exchange recorded under <c>docs/api/fixtures/</c> for the
/// consumers that render them: the client's level, which names neither a campaign nor a project, a project's, and a
/// campaign's, with the two refusals the levels add.
/// </summary>
public class RoleNoteEndpointTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly object Researcher = new { type = "role", id = "researcher" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Notes_are_read_written_and_listed_at_each_level_over_the_api()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var acme = await RoleNoteLevelTests.NewProjectAsync(api, "Acme Corp");
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme }, Ct)).Id;

        var empty = await api.RecordAsync<RoleNoteDto>(Operations.RoleNoteGet, "client-empty", new { role = "researcher" }, Ct);
        Assert.True(empty is { CampaignId: null, ProjectId: null, UpdatedAt: null } && empty.Note.Count == 0, $"The client's empty note read as {empty}");

        var client = await api.RecordAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            "client",
            new { role = "researcher", note = new { craft = "open with the question the prospect already asked" }, actor = Researcher, reason = "What three campaigns taught." },
            Ct);
        Assert.Equal(Read(client), Read(await api.RecordAsync<RoleNoteDto>(Operations.RoleNoteGet, "client", new { role = "researcher" }, Ct)));

        var project = await api.RecordAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            "project",
            new { project_id = acme, role = "copywriter", note = new { voice = "short sentences; the product's name once" }, actor = new { type = "role", id = "copywriter" } },
            Ct);
        Assert.Equal((null, acme), (project.CampaignId, project.ProjectId));
        Assert.Equal(Read(project), Read(await api.RecordAsync<RoleNoteDto>(Operations.RoleNoteGet, "project", new { project_id = acme, role = "copywriter" }, Ct)));

        var own = await api.RecordAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            "campaign",
            new { campaign_id = campaign, role = "researcher", note = new { gatekeeper = "the switchboard hangs up after six" }, actor = Researcher },
            Ct);
        Assert.Equal((campaign, null), (own.CampaignId, own.ProjectId));

        var clients = await api.RecordAsync<Page<RoleNoteSummaryDto>>(Operations.RoleNoteList, "client", new { }, Ct);
        Assert.Equal(["researcher"], clients.Items.Select(n => n.Role));
        var projects = await api.RecordAsync<Page<RoleNoteSummaryDto>>(Operations.RoleNoteList, "project", new { project_id = acme }, Ct);
        Assert.Equal(["copywriter"], projects.Items.Select(n => n.Role));

        var both = await api.RecordErrorAsync(
            Operations.RoleNoteSet,
            "two-levels",
            new { campaign_id = campaign, project_id = acme, role = "researcher", note = new { craft = "anything" } },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.Equal(["campaign_id", "project_id"], both.Details!.Select(d => d.Field));
        var unknown = await api.RecordErrorAsync(
            Operations.RoleNoteGet, "unknown-project", new { project_id = "prj_01K7QN00000000000000000099", role = "researcher" }, HttpStatusCode.NotFound, Ct);
        Assert.Equal("project_not_found", unknown.Code);
    }

    /// <summary>A note as a reader compares two answers: where it is, what it holds and when it was written.</summary>
    private static (string?, string?, string, string, string?, DateTimeOffset?) Read(RoleNoteDto note) =>
        (note.CampaignId, note.ProjectId, note.Role, note.Note.ToJsonString(), note.NoteHash, note.UpdatedAt);
}
