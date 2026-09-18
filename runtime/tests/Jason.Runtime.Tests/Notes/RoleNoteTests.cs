using System.Net;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Notes;

/// <summary>
/// What a role remembers about one campaign: a document it writes for its own future sessions, and nothing the
/// runtime believes. The three verbs are deliberately dull — read it, replace it whole, see which ones exist —
/// because a role's memory is not a place where anything is negotiated.
/// </summary>
public class RoleNoteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The chronicle says a note was written, how big it was and what it hashed to, so a reader can tell a note
    /// that changed from one that did not — and never what it said. A note is a role's working memory: it holds
    /// half-formed guesses about people, and the journal is the one table nobody can edit afterwards.
    /// </summary>
    [Fact]
    public async Task The_journal_records_that_a_note_was_set_and_never_what_it_said()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await NewCampaignAsync(api);

        var note = await api.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            new
            {
                campaign_id = campaign,
                role = "researcher",
                note = new { unreachable_on = "the switchboard hangs up after six", who_answered = "a person called Mira" },
                actor = new { type = "role", id = "researcher" },
                reason = "What the first pass found.",
            },
            Ct);

        var entry = Assert.Single(await KindsAsync(api, JournalKinds.RoleNoteSet));
        Assert.Equal(campaign, entry.CampaignId);
        Assert.Equal("researcher", entry.Key);
        Assert.Equal("researcher", (string?)entry.New?["role"]);
        Assert.Equal(campaign, (string?)entry.New?["campaign_id"]);
        Assert.Equal(note.NoteBytes, (long?)entry.New?["bytes"]);
        Assert.Equal(note.NoteHash, (string?)entry.New?["hash"]);
        Assert.Equal(ActorType.Role, entry.Actor.Type);
        Assert.Equal("researcher", entry.Actor.Id);

        // The whole line, rendered as it leaves the runtime: not one word of the note is in it, keys included.
        var line = JsonSerializer.Serialize(entry, JasonJson.Options);
        foreach (var fragment in (string[])["switchboard", "Mira", "unreachable_on", "who_answered", "six"])
        {
            Assert.DoesNotContain(fragment, line, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The first thing a launched role does is read its memory, and on the first run there is none. That is an
    /// empty note, not an error: a role would otherwise have to tell "nothing yet" from "something went wrong"
    /// before it could start, and every role would write that code.
    /// </summary>
    [Fact]
    public async Task A_role_that_has_never_written_a_note_reads_an_empty_one_rather_than_an_error()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await NewCampaignAsync(api);

        var note = await api.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteGet, new { campaign_id = campaign, role = "researcher" }, Ct);

        Assert.Equal(campaign, note.CampaignId);
        Assert.Equal("researcher", note.Role);
        Assert.Empty(note.Note);
        Assert.Null(note.UpdatedAt);
        Assert.Null(note.UpdatedBy);
        Assert.Equal(0, note.NoteBytes);
        Assert.Empty(await KindsAsync(api, JournalKinds.RoleNoteSet));
    }

    /// <summary>
    /// Written whole and read back whole. There is no patch verb: a role that could merge into its own memory
    /// would need to reason about what another session of itself meant by a key, and the cheapest correct rule
    /// is that the last writer owns the document.
    /// </summary>
    [Fact]
    public async Task A_note_is_replaced_whole_and_comes_back_exactly_as_it_was_written()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await NewCampaignAsync(api);

        await SetAsync(api, campaign, "researcher", new { pass = 1, open_questions = new[] { "who signs" } });
        var written = await SetAsync(api, campaign, "researcher", new { pass = 2 });

        var read = await api.PostOkAsync<RoleNoteDto>(Operations.RoleNoteGet, new { campaign_id = campaign, role = "researcher" }, Ct);
        Assert.Equal("""{"pass":2}""", read.Note.ToJsonString());
        Assert.Equal(written.NoteHash, read.NoteHash);
        Assert.Equal(written.UpdatedAt, read.UpdatedAt);
        Assert.Null(read.Note["open_questions"]);

        // Two writes, two lines, newest first as the chronicle lists them: it shows the memory changing without
        // showing what it holds.
        var entries = await KindsAsync(api, JournalKinds.RoleNoteSet);
        Assert.Equal(2, entries.Count);
        var (second, first) = (entries[0], entries[1]);
        Assert.Null(first.Old);
        Assert.NotNull(second.Old?["hash"]);
        Assert.Equal((string?)first.New?["hash"], (string?)second.Old?["hash"]);
        Assert.NotEqual((string?)second.Old?["hash"], (string?)second.New?["hash"]);
    }

    /// <summary>
    /// One document per campaign and role. Two roles in one campaign remember different things, and one role in
    /// two campaigns does not carry what it learned about one into the other — which is the whole of what
    /// "campaign-scoped" means here.
    /// </summary>
    [Fact]
    public async Task A_note_belongs_to_one_campaign_and_one_role_and_reaches_no_further()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var first = await NewCampaignAsync(api);
        var second = await NewCampaignAsync(api);

        await SetAsync(api, first, "researcher", new { learned = "here" });
        await SetAsync(api, first, "planner", new { learned = "differently" });

        Assert.Equal("""{"learned":"here"}""", (await GetAsync(api, first, "researcher")).Note.ToJsonString());
        Assert.Equal("""{"learned":"differently"}""", (await GetAsync(api, first, "planner")).Note.ToJsonString());
        Assert.Empty((await GetAsync(api, second, "researcher")).Note);
    }

    /// <summary>A campaign's memory, listed without reading any of it: which roles have written, how much, when.</summary>
    [Fact]
    public async Task A_listing_names_the_roles_that_have_written_and_never_carries_a_note()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await NewCampaignAsync(api);
        var elsewhere = await NewCampaignAsync(api);
        await SetAsync(api, campaign, "researcher", new { held = "a phrase that must not be listed" });
        await SetAsync(api, campaign, "planner", new { held = "nor this one" });
        await SetAsync(api, elsewhere, "researcher", new { held = "another campaign entirely" });

        var (_, body) = await api.PostAsync(Operations.RoleNoteList, new { campaign_id = campaign }, Ct);
        var page = JsonSerializer.Deserialize<Page<RoleNoteSummaryDto>>(body, JasonJson.Options)!;

        Assert.Equal(["planner", "researcher"], page.Items.Select(n => n.Role).Order(StringComparer.Ordinal));
        Assert.All(page.Items, summary => Assert.True(summary.NoteBytes > 0));
        Assert.All(page.Items, summary => Assert.StartsWith("sha256:", summary.NoteHash, StringComparison.Ordinal));
        foreach (var fragment in (string[])["phrase", "nor this one", "another campaign"])
        {
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// An archived campaign takes no more writes, like every other write against one. Reading stays open: the
    /// memory of a finished campaign is exactly what somebody looking back at it wants.
    /// </summary>
    [Fact]
    public async Task An_archived_campaign_refuses_a_note_and_still_answers_with_one()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await NewCampaignAsync(api);
        await SetAsync(api, campaign, "researcher", new { written = "while it was open" });
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = campaign }, Ct);

        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet,
            new { campaign_id = campaign, role = "researcher", note = new { written = "afterwards" } },
            HttpStatusCode.Conflict,
            Ct);
        Assert.Equal("campaign_archived", error.Code);

        var read = await GetAsync(api, campaign, "researcher");
        Assert.Equal("""{"written":"while it was open"}""", read.Note.ToJsonString());
    }

    /// <summary>The runtime's own actor is refused here as everywhere else: a note is somebody's, never nobody's.</summary>
    [Fact]
    public async Task A_caller_cannot_claim_to_be_the_runtime()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await NewCampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet,
            new { campaign_id = campaign, role = "researcher", note = new { x = 1 }, actor = new { type = "system", id = "runtime" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
    }

    internal static async Task<string> NewCampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Notes" }, Ct);
        return campaign.Id;
    }

    internal static Task<RoleNoteDto> SetAsync(RuntimeApiFixture api, string campaign, string role, object note) =>
        api.PostOkAsync<RoleNoteDto>(Operations.RoleNoteSet, new { campaign_id = campaign, role, note }, Ct);

    internal static Task<RoleNoteDto> GetAsync(RuntimeApiFixture api, string campaign, string role) =>
        api.PostOkAsync<RoleNoteDto>(Operations.RoleNoteGet, new { campaign_id = campaign, role }, Ct);

    private static async Task<IReadOnlyList<JournalEntryDto>> KindsAsync(RuntimeApiFixture api, string kind)
    {
        var page = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind }, Ct);
        return page.Items;
    }
}
