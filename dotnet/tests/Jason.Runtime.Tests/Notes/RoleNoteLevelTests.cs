using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Notes;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Notes;

/// <summary>
/// A role's notes at three levels: the client's — its general craft, naming no campaign and no project — a project's,
/// and a campaign's. One verb family serves all three, and each call names exactly one level: a campaign, a project,
/// or neither. A reader composes them client, then project, then campaign; nothing here composes anything, because the
/// runtime never reads a note.
/// </summary>
public class RoleNoteLevelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A note is at one level, so a request that names a campaign and a project is refused, naming both fields: neither
    /// is the one it meant more than the other, and a campaign's project is the campaign's, never said twice.
    /// </summary>
    [Theory]
    [InlineData(Operations.RoleNoteGet)]
    [InlineData(Operations.RoleNoteSet)]
    [InlineData(Operations.RoleNoteList)]
    public async Task A_request_naming_a_campaign_and_a_project_is_refused_naming_both(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);
        var project = await NewProjectAsync(api, "Acme Corp");

        var error = await api.PostErrorAsync(
            operation,
            new { campaign_id = campaign, project_id = project, role = "researcher", note = new { anything = true } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(["campaign_id/not_allowed", "project_id/not_allowed"], error.Details!.Select(d => $"{d.Field}/{d.Code}"));
        await AssertNothingWrittenAsync(api);
    }

    /// <summary>
    /// A blank id names no level at all — it is what an empty variable makes — and is refused by its field rather than
    /// read as the client's level, where a write would replace the role's general craft.
    /// </summary>
    [Theory]
    [InlineData(Operations.RoleNoteGet, "campaign_id")]
    [InlineData(Operations.RoleNoteSet, "campaign_id")]
    [InlineData(Operations.RoleNoteList, "campaign_id")]
    [InlineData(Operations.RoleNoteGet, "project_id")]
    [InlineData(Operations.RoleNoteSet, "project_id")]
    [InlineData(Operations.RoleNoteList, "project_id")]
    public async Task A_blank_id_is_refused_rather_than_read_as_the_client_s_level(string operation, string field)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostRawAsync(
            operation, $$$"""{"{{{field}}}":"  ","role":"researcher","note":{"anything":true}}""", Ct);

        Assert.True(status == HttpStatusCode.BadRequest, $"A blank {field} on {operation} answered {(int)status}: {body}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, JasonJson.Options)!.Error;
        Assert.Equal([$"{field}/invalid"], error.Details!.Select(d => $"{d.Field}/{d.Code}"));
        await AssertNothingWrittenAsync(api);
    }

    /// <summary>
    /// An id with spaces around it names the level the bare id names, a project's as a campaign's: each verb reads, writes
    /// and lists the very note the bare id reaches, and answers with the id bare. Held for both, so a project's id never
    /// reads as a project nobody made where a campaign's would have found its campaign.
    /// </summary>
    [Theory]
    [InlineData("campaign_id")]
    [InlineData("project_id")]
    public async Task An_id_with_spaces_around_it_names_the_level_the_bare_id_names(string field)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var project = await NewProjectAsync(api, "Acme Corp");
        var id = field == "project_id"
            ? project
            : (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = project }, Ct)).Id;
        var padded = $"  {id}  ";
        string? Named(RoleNoteDto note) => field == "project_id" ? note.ProjectId : note.CampaignId;
        await SetAsync(api, new JsonObject { [field] = id, ["role"] = "researcher", ["note"] = new JsonObject { ["pass"] = 1 } });

        var read = await GetAsync(api, new JsonObject { [field] = padded, ["role"] = "researcher" });
        var written = await SetAsync(api, new JsonObject { [field] = padded, ["role"] = "researcher", ["note"] = new JsonObject { ["pass"] = 2 } });
        var listed = await api.PostOkAsync<Page<RoleNoteSummaryDto>>(Operations.RoleNoteList, new JsonObject { [field] = padded }, Ct);

        Assert.True(Named(read) == id && read.Note.ToJsonString() == """{"pass":1}""", $"A get by '{padded}' read {Json(read)}");
        Assert.True(Named(written) == id, $"A set by '{padded}' was answered at {Json(written)}");
        Assert.Equal("""{"pass":2}""", (await GetAsync(api, new JsonObject { [field] = id, ["role"] = "researcher" })).Note.ToJsonString());
        Assert.Equal([$"{id}:researcher"], listed.Items.Select(n => $"{n.CampaignId ?? n.ProjectId}:{n.Role}"));
    }

    [Theory]
    [InlineData(Operations.RoleNoteGet)]
    [InlineData(Operations.RoleNoteSet)]
    [InlineData(Operations.RoleNoteList)]
    public async Task A_project_nobody_made_has_no_memory(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            operation,
            new { project_id = "prj_01K7QN00000000000000000099", role = "researcher", note = new { anything = true } },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("project_not_found", error.Code);
        await AssertNothingWrittenAsync(api);
    }

    /// <summary>
    /// A role that has never written reads an empty note at every level, as it does for a campaign: reading its memory is
    /// the first thing a launched role does, and its general craft is empty on a new installation.
    /// </summary>
    [Fact]
    public async Task A_role_that_has_never_written_reads_an_empty_note_at_the_client_s_and_a_project_s_level()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var project = await NewProjectAsync(api, "Acme Corp");

        var client = await GetAsync(api, new { role = "researcher" });
        var atProject = await GetAsync(api, new { project_id = project, role = "researcher" });

        Assert.True(
            client is { CampaignId: null, ProjectId: null, Role: "researcher", NoteHash: null, NoteBytes: 0, CreatedAt: null, UpdatedAt: null, UpdatedBy: null }
                && client.Note.Count == 0,
            $"The client's empty note read as {Json(client)}");
        Assert.True(
            atProject is { CampaignId: null, Role: "researcher", NoteHash: null, NoteBytes: 0, UpdatedAt: null, UpdatedBy: null }
                && atProject.ProjectId == project && atProject.Note.Count == 0,
            $"The project's empty note read as {Json(atProject)}");
        await AssertNothingWrittenAsync(api);
    }

    /// <summary>
    /// The same role keeps one note at each level, and each is read only where it was written: the client's is not the
    /// project's, the project's is not its campaign's, and another project reads nothing of either. Composing them is the
    /// reader's; a runtime that answered one level with another's note would have composed for it.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "role notes stay at their level")]
    public async Task A_note_is_read_only_at_the_level_it_was_written_at()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await NewProjectAsync(api, "Acme Corp");
        var other = await NewProjectAsync(api, "Other Corp");
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme }, Ct)).Id;

        var client = await SetAsync(api, new { role = "researcher", note = new { level = "client" } });
        var project = await SetAsync(api, new { project_id = acme, role = "researcher", note = new { level = "project" } });
        var own = await SetAsync(api, new { campaign_id = campaign, role = "researcher", note = new { level = "campaign" } });

        Assert.True(client is { CampaignId: null, ProjectId: null }, $"The client's note was answered at {Json(client)}");
        Assert.True(project is { CampaignId: null } && project.ProjectId == acme, $"The project's note was answered at {Json(project)}");
        Assert.True(own is { ProjectId: null } && own.CampaignId == campaign, $"The campaign's note was answered at {Json(own)}");

        Assert.Equal("""{"level":"client"}""", (await GetAsync(api, new { role = "researcher" })).Note.ToJsonString());
        Assert.Equal("""{"level":"project"}""", (await GetAsync(api, new { project_id = acme, role = "researcher" })).Note.ToJsonString());
        Assert.Equal("""{"level":"campaign"}""", (await GetAsync(api, new { campaign_id = campaign, role = "researcher" })).Note.ToJsonString());
        Assert.Empty((await GetAsync(api, new { project_id = other, role = "researcher" })).Note);
        Assert.Empty((await GetAsync(api, new { role = "planner" })).Note);
    }

    /// <summary>
    /// Each level lists its own notes and no other's: the client's listing is the client's notes, never every note in the
    /// installation, which would be a way to sweep up what roles have written about people.
    /// </summary>
    [Fact]
    public async Task Each_level_lists_its_own_notes_and_never_another_s()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await NewProjectAsync(api, "Acme Corp");
        var campaign = await RoleNoteTests.NewCampaignAsync(api);
        await SetAsync(api, new { role = "researcher", note = new { held = "the client's craft" } });
        await SetAsync(api, new { role = "planner", note = new { held = "the client's planning" } });
        await SetAsync(api, new { project_id = acme, role = "copywriter", note = new { held = "the project's voice" } });
        await SetAsync(api, new { campaign_id = campaign, role = "critic", note = new { held = "the campaign's pattern" } });

        var (_, clientBody) = await api.PostAsync(Operations.RoleNoteList, new { }, Ct);
        var (_, projectBody) = await api.PostAsync(Operations.RoleNoteList, new { project_id = acme }, Ct);
        var (_, campaignBody) = await api.PostAsync(Operations.RoleNoteList, new { campaign_id = campaign }, Ct);

        Assert.Equal(["client:planner", "client:researcher"], Listed(clientBody));
        Assert.Equal([$"{acme}:copywriter"], Listed(projectBody));
        Assert.Equal([$"{campaign}:critic"], Listed(campaignBody));
        foreach (var body in (string[])[clientBody, projectBody, campaignBody])
        {
            Assert.DoesNotContain("held", body, StringComparison.Ordinal);
            Assert.DoesNotContain("craft", body, StringComparison.Ordinal);
        }
    }

    /// <summary>The client's listing pages by cursor like every other listing, keyed on the role.</summary>
    [Fact]
    public async Task The_client_s_listing_pages_by_cursor()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        foreach (var role in (string[])["analyst", "critic", "planner"])
        {
            await SetAsync(api, new { role, note = new { written = role } });
        }

        var page = await api.PostOkAsync<Page<RoleNoteSummaryDto>>(Operations.RoleNoteList, new { limit = 2 }, Ct);
        Assert.Equal(["analyst", "critic"], page.Items.Select(n => n.Role));
        var rest = await api.PostOkAsync<Page<RoleNoteSummaryDto>>(Operations.RoleNoteList, new { limit = 2, cursor = page.NextCursor }, Ct);
        Assert.True(rest.Items.Select(n => n.Role).SequenceEqual(["planner"]) && rest.NextCursor is null, $"The second page was {Json(rest)}");
    }

    /// <summary>
    /// At the client's and a project's level as at a campaign's, a note is replaced whole and cleared by an empty object,
    /// which leaves a note that was written — its moment and its author kept — rather than one that never was.
    /// </summary>
    [Theory]
    [InlineData("client")]
    [InlineData("project")]
    public async Task A_note_is_replaced_whole_and_cleared_by_an_empty_object_at_every_level(string level)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var address = await AddressAsync(api, level);

        await SetAsync(api, address.With(new { pass = 1, open_questions = new[] { "who signs" } }));
        await SetAsync(api, address.With(new { pass = 2 }));
        Assert.Equal("""{"pass":2}""", (await GetAsync(api, address.Read())).Note.ToJsonString());

        var cleared = await SetAsync(api, address.With(new { }));
        var read = await GetAsync(api, address.Read());
        Assert.True(
            read.Note.Count == 0 && read.UpdatedAt is not null && read.NoteHash == cleared.NoteHash,
            $"A note cleared at the {level}'s level read as {Json(read)}");
    }

    /// <summary>The cap is the campaign note's at every level: one byte past it is refused, and the note there is kept.</summary>
    [Theory]
    [InlineData("client")]
    [InlineData("project")]
    public async Task The_cap_holds_at_every_level(string level)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var address = await AddressAsync(api, level);
        const int Overhead = 13;
        var kept = await SetAsync(api, address.With(new { filler = new string('x', RoleNoteService.MaxNoteBytes - Overhead) }));
        Assert.Equal(RoleNoteService.MaxNoteBytes, kept.NoteBytes);

        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet,
            address.With(new { filler = new string('x', RoleNoteService.MaxNoteBytes - Overhead + 1) }),
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("role_note_too_large", error.Code);
        Assert.Equal(kept.NoteHash, (await GetAsync(api, address.Read())).NoteHash);
    }

    /// <summary>
    /// A note at the client's or a project's level is a line of the installation's, naming no campaign — the project's
    /// line names the project — and, as at a campaign, says the size and the hash, never a word of the note.
    /// </summary>
    [Fact]
    public async Task The_journal_says_a_client_s_or_a_project_s_note_was_set_and_never_what_it_said()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await NewProjectAsync(api, "Acme Corp");

        var client = await SetAsync(api, new { role = "researcher", note = new { gatekeeper = "the switchboard hangs up after six" }, actor = new { type = "role", id = "researcher" } });
        var project = await SetAsync(api, new { project_id = acme, role = "researcher", note = new { who_answered = "a person called Mira" }, reason = "What the pass found." });

        var entries = (await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind = JournalKinds.RoleNoteSet }, Ct)).Items;
        Assert.Equal(2, entries.Count);
        var (atProject, atClient) = (entries[0], entries[1]);

        Assert.True(atClient is { CampaignId: null, Key: "researcher", Old: null }, $"The client's line was {Json(atClient)}");
        Assert.Equal(
            new JsonObject { ["role"] = "researcher", ["bytes"] = client.NoteBytes, ["hash"] = client.NoteHash }.ToJsonString(),
            atClient.New?.ToJsonString());
        Assert.True(atProject is { CampaignId: null, Key: "researcher", Reason: "What the pass found." }, $"The project's line was {Json(atProject)}");
        Assert.Equal(
            new JsonObject { ["project_id"] = acme, ["role"] = "researcher", ["bytes"] = project.NoteBytes, ["hash"] = project.NoteHash }.ToJsonString(),
            atProject.New?.ToJsonString());

        var lines = JsonSerializer.Serialize(entries, JasonJson.Options);
        foreach (var fragment in (string[])["switchboard", "Mira", "gatekeeper", "who_answered", "six"])
        {
            Assert.DoesNotContain(fragment, lines, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Two sessions of one role writing the client's first note at once: the index of one per role at that level decides,
    /// and the loser is told to read and write again — not handed an internal error, which a caller honouring
    /// <c>retryable</c> would never send again.
    /// </summary>
    [Theory]
    [InlineData("client")]
    [InlineData("project")]
    public async Task A_first_note_lost_to_another_writer_at_its_level_is_answered_as_a_concurrent_update(string level)
    {
        using var database = new TestDatabase();
        string? project = null;
        if (level == "project")
        {
            using var setup = database.Open();
            project = (await new ProjectService(setup, new JournalWriter(TimeProvider.System), TimeProvider.System, TestHarness.FoldersFor(setup))
                .CreateAsync(new ProjectCreateRequest("Acme Corp", null, null, null), Ct)).Id;
        }

        var rival = new CompetingInsert(
            database.File,
            _ => $"""
                INSERT INTO role_notes (campaign_id, project_id, role, note_json, note_hash, note_bytes, updated_by_type, created_at, updated_at)
                VALUES (NULL, {(project is null ? "NULL" : $"(SELECT id FROM projects WHERE public_id = '{project}')")}, 'researcher', '{"{}"}', 'sha256:rival', 2, 'role', '2026-10-22 12:00:00', '2026-10-22 12:00:00');
                """);
        using var db = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(rival).Options);
        var service = new RoleNoteService(db, new JournalWriter(TimeProvider.System), TimeProvider.System);

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => service.SetAsync(new RoleNoteSetRequest(null, "researcher", new JsonObject { ["mine"] = true }, null, null, project), Ct));

        Assert.True(rival.Fired);
        Assert.True(refused is { Code: "concurrent_update", Retryable: true }, $"The lost race was answered {refused.Code}, retryable {refused.Retryable}");
    }

    internal static async Task<string> NewProjectAsync(RuntimeApiFixture api, string name) =>
        (await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name }, Ct)).Id;

    private static Task<RoleNoteDto> SetAsync(RuntimeApiFixture api, object body) => api.PostOkAsync<RoleNoteDto>(Operations.RoleNoteSet, body, Ct);

    private static Task<RoleNoteDto> GetAsync(RuntimeApiFixture api, object body) => api.PostOkAsync<RoleNoteDto>(Operations.RoleNoteGet, body, Ct);

    private static async Task<Address> AddressAsync(RuntimeApiFixture api, string level) =>
        new(level == "project" ? await NewProjectAsync(api, "Acme Corp") : null);

    /// <summary>Nothing reached the store: no note at any level, and no line saying one was set.</summary>
    private static async Task AssertNothingWrittenAsync(RuntimeApiFixture api)
    {
        var lines = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind = JournalKinds.RoleNoteSet }, Ct);
        Assert.True(lines.Items.Count == 0, $"A refused request left lines: {Json(lines)}");
        Assert.Empty((await api.PostOkAsync<Page<RoleNoteSummaryDto>>(Operations.RoleNoteList, new { }, Ct)).Items);
    }

    private static List<string> Listed(string body) =>
        [.. JsonSerializer.Deserialize<Page<RoleNoteSummaryDto>>(body, JasonJson.Options)!.Items
            .Select(n => $"{n.CampaignId ?? n.ProjectId ?? "client"}:{n.Role}")];

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JasonJson.Options);

    /// <summary>A note's level as a request names it: a project's id, or nothing for the client's.</summary>
    private sealed record Address(string? ProjectId)
    {
        public JsonObject Read() => ProjectId is null
            ? new JsonObject { ["role"] = "researcher" }
            : new JsonObject { ["project_id"] = ProjectId, ["role"] = "researcher" };

        public JsonObject With(object note)
        {
            var body = Read();
            body["note"] = JsonSerializer.SerializeToNode(note, JasonJson.Options);
            return body;
        }
    }
}
