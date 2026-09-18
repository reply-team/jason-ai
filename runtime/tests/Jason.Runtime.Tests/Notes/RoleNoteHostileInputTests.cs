using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Notes;

namespace Jason.Runtime.Tests.Notes;

/// <summary>
/// A note is written by an agent, which is the least trustworthy writer in the system: it is a language model
/// composing JSON from what it read on a web page. Every input here is built by the test rather than read from
/// a fixture, so what is refused is visible in the source of the test that refuses it.
/// </summary>
public class RoleNoteHostileInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The cap is measured on the canonical form, so the two sides of it are computable and the test computes
    /// them: one byte under is a note, one byte over is refused. Measured rather than approximated, because a
    /// role that is told "too large" has to be able to make its note smaller and know when it has.
    /// </summary>
    [Fact]
    public async Task A_note_is_taken_up_to_the_cap_and_refused_one_byte_past_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);

        // {"filler":"…"} — two braces, the quoted key, the colon and two quotes around the value.
        const int Overhead = 13;
        var exact = await api.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteSet, Body(campaign, "researcher", Filler(RoleNoteService.MaxNoteBytes - Overhead)), Ct);
        Assert.Equal(RoleNoteService.MaxNoteBytes, exact.NoteBytes);

        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet,
            Body(campaign, "researcher", Filler(RoleNoteService.MaxNoteBytes - Overhead + 1)),
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("role_note_too_large", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains(RoleNoteService.MaxNoteBytes.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);

        // The refusal wrote nothing: the note that was there is the note that is there.
        var held = await RoleNoteTests.GetAsync(api, campaign, "researcher");
        Assert.Equal(exact.NoteHash, held.NoteHash);
    }

    /// <summary>
    /// The cap counts the canonical form's bytes, and that form escapes everything outside ASCII — six bytes
    /// for a character that UTF-8 spells in two. A role writing in Cyrillic, Greek or Japanese therefore gets
    /// roughly a third of the characters the figure suggests, and is refused while its note is a quarter of
    /// 64 KiB as a file. That is a fact about the one canonical form this runtime names everything by — a
    /// report's assertion and a route's binding are hashed in the same one — so it is documented rather than
    /// special-cased here, and pinned here so the documentation cannot quietly become false.
    /// </summary>
    [Fact]
    public async Task A_note_outside_ascii_is_measured_on_its_escaped_form()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);
        const int Overhead = 13;
        const char Cyrillic = 'д';

        // 10,000 characters: 20,000 bytes as UTF-8, 60,000 as the escaped form the cap is measured on.
        var written = await api.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteSet, Body(campaign, "researcher", Filler(10_000, Cyrillic)), Ct);
        Assert.Equal((6 * 10_000) + Overhead, written.NoteBytes);

        // And so 12,000 of them — 24 KiB of text by any file manager's count — is past a 64 KiB cap.
        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet, Body(campaign, "researcher", Filler(12_000, Cyrillic)), HttpStatusCode.BadRequest, Ct);
        Assert.Equal("role_note_too_large", error.Code);

        // The receipt is what a role has to steer by: the number it reports is the number the cap compares.
        Assert.True(written.NoteBytes < RoleNoteService.MaxNoteBytes);
    }

    /// <summary>A note past the cap several times over is the same refusal, and the runtime still answers.</summary>
    [Fact]
    public async Task A_note_of_a_megabyte_is_refused_rather_than_stored_or_truncated()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet, Body(campaign, "researcher", Filler(1024 * 1024)), HttpStatusCode.BadRequest, Ct);

        Assert.Equal("role_note_too_large", error.Code);
        Assert.Empty((await RoleNoteTests.GetAsync(api, campaign, "researcher")).Note);
    }

    /// <summary>
    /// A note is an object. An array, a sentence or a number is a caller who has misunderstood the verb, and the
    /// answer names the field rather than refusing the request as unreadable.
    /// </summary>
    [Theory]
    [InlineData("""["a list of things I learned"]""")]
    [InlineData("\"a sentence about the campaign\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task A_note_that_is_not_an_object_is_refused_by_name(string note)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);

        var (status, body) = await api.PostRawAsync(
            Operations.RoleNoteSet,
            $$"""{"campaign_id":"{{campaign}}","role":"researcher","note":{{note}}}""",
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("\"field\":\"note\"", body, StringComparison.Ordinal);
    }

    /// <summary>A note nested past what any parser will read is refused as a request, not a stack overflow.</summary>
    [Fact]
    public async Task A_note_nested_a_thousand_deep_is_refused_and_the_runtime_answers_afterwards()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);
        var note = string.Concat(Enumerable.Repeat("""{"a":""", 1000)) + "1" + new string('}', 1000);

        var (status, _) = await api.PostRawAsync(
            Operations.RoleNoteSet, $$"""{"campaign_id":"{{campaign}}","role":"researcher","note":{{note}}}""", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty((await RoleNoteTests.GetAsync(api, campaign, "researcher")).Note);
    }

    /// <summary>
    /// A note exists only for a role the roster knows. A name nobody registered is a 404 rather than a note
    /// filed under a typo, which nothing would ever read again.
    /// </summary>
    [Theory]
    [InlineData(Operations.RoleNoteGet)]
    [InlineData(Operations.RoleNoteSet)]
    public async Task A_role_nobody_registered_has_no_memory(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);

        var error = await api.PostErrorAsync(
            operation,
            new { campaign_id = campaign, role = "chief-vibes-officer", note = new { anything = true } },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("role_not_found", error.Code);
    }

    [Theory]
    [InlineData(Operations.RoleNoteGet)]
    [InlineData(Operations.RoleNoteSet)]
    [InlineData(Operations.RoleNoteList)]
    public async Task A_campaign_nobody_created_has_no_memory(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            operation,
            new { campaign_id = "cmp_01JASONNEVERCREATEDTHIS", role = "researcher", note = new { anything = true } },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("campaign_not_found", error.Code);
    }

    /// <summary>
    /// A role name is held to the roster's own shape before anything is loaded, so a name carrying a quote, a
    /// path or a wildcard is a validation failure rather than a query built out of it.
    /// </summary>
    [Theory]
    [InlineData("researcher'; DROP TABLE role_notes; --")]
    [InlineData("../../../etc/passwd")]
    [InlineData("%")]
    [InlineData("Researcher")]
    [InlineData("")]
    public async Task A_role_name_that_could_never_have_been_registered_is_a_validation_failure(string role)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RoleNoteSet,
            new { campaign_id = campaign, role, note = new { anything = true } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("role", Assert.Single(error.Details!).Field);
    }

    /// <summary>
    /// The receipt is the runtime's, not the note's. A role that writes keys named after the receipt's fields
    /// gets them back as part of its own document, and the hash, the size and the author beside it are still
    /// computed here — a note cannot claim to be bigger, older or somebody else's than it is.
    /// </summary>
    [Fact]
    public async Task A_note_that_names_the_receipts_own_fields_does_not_become_the_receipt()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);

        var written = await api.PostOkAsync<RoleNoteDto>(
            Operations.RoleNoteSet,
            new
            {
                campaign_id = campaign,
                role = "researcher",
                note = new
                {
                    note_hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                    note_bytes = 1,
                    updated_by = new { type = "system", id = "runtime" },
                    campaign_id = "cmp_somebody_elses",
                },
                actor = new { type = "role", id = "researcher" },
            },
            Ct);

        Assert.NotEqual("sha256:0000000000000000000000000000000000000000000000000000000000000000", written.NoteHash);
        Assert.True(written.NoteBytes > 1);
        Assert.Equal(campaign, written.CampaignId);
        Assert.Equal(ActorType.Role, written.UpdatedBy!.Type);
        Assert.Equal("researcher", written.UpdatedBy.Id);

        // And the document itself came back whole: those keys are the role's to use.
        Assert.Equal("cmp_somebody_elses", (string?)written.Note["campaign_id"]);
    }

    /// <summary>
    /// A campaign's memory is asked for one campaign at a time. A listing with no campaign is a validation
    /// failure rather than every note in the installation, which is a way to sweep up what roles have written
    /// about people.
    /// </summary>
    [Fact]
    public async Task A_listing_without_a_campaign_is_refused_rather_than_answering_with_all_of_them()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await RoleNoteTests.NewCampaignAsync(api);
        await RoleNoteTests.SetAsync(api, campaign, "researcher", new { held = "something" });

        var error = await api.PostErrorAsync(Operations.RoleNoteList, new { limit = 100 }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
    }

    private static JsonObject Filler(int length, char character = 'x') => new() { ["filler"] = new string(character, length) };

    private static JsonObject Body(string campaign, string role, JsonObject note) => new()
    {
        ["campaign_id"] = campaign,
        ["role"] = role,
        ["note"] = note,
    };
}
