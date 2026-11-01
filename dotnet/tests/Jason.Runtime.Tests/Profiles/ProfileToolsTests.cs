using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// The tools a person grants by name on a profile: <c>web_search</c> and <c>web_fetch</c>, and nothing else in this
/// version. A grant is part of the revision, so it is written, frozen and read back the way every other field of one is,
/// and a word outside the vocabulary is refused where it is typed rather than discovered at a launch.
/// </summary>
public class ProfileToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What was granted is what comes back, on the revision and on a later read of it, in the API's spelling.</summary>
    [Fact]
    public async Task A_profile_is_granted_the_web_tools_it_names()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "researching", host = "claude_code", program = "claude", tools = new[] { "web_search", "web_fetch" } },
            Ct);

        Assert.Equal([AgentTool.WebSearch, AgentTool.WebFetch], created.Revision.Tools);

        var (status, body) = await api.PostRawAsync(Operations.ProfileGet, "{\"name\":\"researching\"}", Ct);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["web_search", "web_fetch"],
            JsonNode.Parse(body)!["revision"]!["tools"]!.AsArray().Select(tool => (string?)tool));
    }

    /// <summary>A profile that names no tools is granted none, and says so with an empty list rather than with nothing.</summary>
    [Fact]
    public async Task A_profile_that_names_no_tools_is_granted_none()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("ungranted"), Ct);

        Assert.Empty(created.Revision.Tools);
        var (_, body) = await api.PostRawAsync(Operations.ProfileGet, "{\"name\":\"ungranted\"}", Ct);
        Assert.Empty(JsonNode.Parse(body)!["revision"]!["tools"]!.AsArray());
    }

    /// <summary>
    /// The vocabulary is closed, and the refusal names it. The host's own spelling of a tool is not the vocabulary's: the
    /// grant is a word the person gives, and which rules a host composes from it is the host's business, not a profile's.
    /// </summary>
    [Theory]
    // Critical: a word outside the vocabulary is refused where it is written, never stored for a launch to meet.
    [InlineData("Bash", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S6)])]
    [InlineData("WebSearch")]
    [InlineData("WebFetch(domain:example.com)")]
    [InlineData("web-search")]
    [InlineData("Web_Search")]
    [InlineData("mcp__web__search")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_tool_outside_the_vocabulary_is_refused_with_the_vocabulary(string tool)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "overreaching", host = "claude_code", program = "claude", tools = new[] { tool } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("tools[0]", detail.Field);
        Assert.Equal("invalid", detail.Code);
        Assert.Contains("web_search, web_fetch", detail.Message, StringComparison.Ordinal);

        var (status, _) = await api.PostRawAsync(Operations.ProfileGet, "{\"name\":\"overreaching\"}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    /// <summary>An entry that is not a string is refused by its place in the list, not by the type it failed to bind to.</summary>
    [Fact]
    public async Task A_tool_entry_that_is_not_a_string_is_refused_by_field()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostRawAsync(
            Operations.ProfileCreate,
            "{\"name\":\"shapeless\",\"host\":\"claude_code\",\"program\":\"claude\",\"tools\":[7,{\"tool\":\"web_fetch\"}]}",
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, error.Status);
        var details = JsonNode.Parse(error.Body)!["error"]!["details"]!.AsArray();
        Assert.Equal(["tools[0]", "tools[1]"], details.Select(detail => (string?)detail!["field"]));
    }

    /// <summary>
    /// A tool named twice is refused rather than folded into one. A repeated word is likelier a slip for the other word
    /// than emphasis, and folding it would grant less than the person meant without saying so; a refusal costs a retype.
    /// </summary>
    [Fact]
    public async Task A_tool_named_twice_is_refused_rather_than_folded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "repeated", host = "claude_code", program = "claude", tools = new[] { "web_search", "web_search" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal("tools[1]", detail.Field);
        Assert.Equal("duplicate", detail.Code);
    }

    /// <summary>A list longer than the vocabulary cannot be a grant, and is refused by its count as one problem.</summary>
    [Fact]
    public async Task A_tools_list_longer_than_the_vocabulary_is_refused_as_one_problem()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var tools = Enumerable.Range(0, 10_000).Select(index => "tool-" + index.ToString(CultureInfo.InvariantCulture)).ToArray();

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate, new { name = "flooded", host = "claude_code", program = "claude", tools }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal("tools", detail.Field);
        Assert.Equal("too_long", detail.Code);
        Assert.Contains("web_search, web_fetch", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Granting and withdrawing are edits, so each is a revision of its own; a patch that names another field carries the
    /// grant forward, and the revision that ran before the grant still says it had none.
    /// </summary>
    [Fact]
    public async Task An_edit_grants_and_withdraws_tools_in_a_revision_of_its_own()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("granted-later"), Ct);

        var granted = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "granted-later", tools = new[] { "web_search" }, reason = "The person said yes." }, Ct);
        Assert.Equal(2, granted.CurrentRevision);
        Assert.Equal([AgentTool.WebSearch], granted.Revision.Tools);

        var carried = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "granted-later", program = "claude-next" }, Ct);
        Assert.Equal(3, carried.CurrentRevision);
        Assert.Equal([AgentTool.WebSearch], carried.Revision.Tools);

        var withdrawn = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "granted-later", tools = Array.Empty<string>() }, Ct);
        Assert.Equal(4, withdrawn.CurrentRevision);
        Assert.Empty(withdrawn.Revision.Tools);

        var first = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "granted-later", revision = 1 }, Ct);
        Assert.Empty(first.Revision.Tools);
        var second = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "granted-later", revision = 2 }, Ct);
        Assert.Equal([AgentTool.WebSearch], second.Revision.Tools);

        var page = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind = JournalKinds.ProfileRevised }, Ct);
        var grant = page.Items.Single(entry => (int?)entry.New?["revision"] == 2);
        Assert.Equal(["tools"], grant.New?["fields"]?.AsArray().Select(field => (string?)field));
    }

    /// <summary>A null is a withdrawal, as an empty list is: absent leaves the grant alone, null clears it, and each is its own request.</summary>
    [Fact]
    public async Task A_null_grant_withdraws_the_tools_in_a_revision_of_its_own()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("nulled"), Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileUpdate, new { name = "nulled", tools = new[] { "web_search" } }, Ct);

        var (status, body) = await api.PostRawAsync(Operations.ProfileUpdate, "{\"name\":\"nulled\",\"tools\":null}", Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        var cleared = JsonNode.Parse(body)!;
        Assert.Equal(3, (int)cleared["current_revision"]!);
        Assert.Empty(cleared["revision"]!["tools"]!.AsArray());
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "nulled" }, Ct);
        Assert.Equal(3, read.CurrentRevision);
        Assert.Empty(read.Revision.Tools);
    }

    /// <summary>An edit is held to the vocabulary exactly as a creation is, and a refused one writes nothing.</summary>
    [Fact]
    public async Task A_patch_is_held_to_the_vocabulary()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("patched-tools"), Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "patched-tools", tools = new[] { "web_fetch", "Bash" } }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("tools[1]", Assert.Single(error.Details!).Field);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "patched-tools" }, Ct);
        Assert.Equal(1, read.CurrentRevision);
        Assert.Empty(read.Revision.Tools);
    }

    /// <summary>
    /// A grant and a deny that name the same tool by the host's own name are refused as a pair: the host's deny wins
    /// over the grant, so the revision would say a tool was granted that no launch of it can use. The refusal is on the
    /// grant, and its message names the deny it collides with.
    /// </summary>
    [Theory]
    [InlineData("web_fetch", "WebFetch")]
    [InlineData("web_search", "WebSearch")]
    public async Task A_grant_of_a_tool_the_deny_names_is_refused_as_a_pair(string tool, string rule)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "contradicting", host = "claude_code", program = "claude", deny = new[] { "Write", rule }, tools = new[] { tool } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("tools[0]", detail.Field);
        Assert.Equal("denied", detail.Code);
        Assert.Contains("deny[1]", detail.Message, StringComparison.Ordinal);
        Assert.Contains($"'{rule}'", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An edit is held to the pair as it would be launched: a patch that names only one of the two fields meets the
    /// other as the profile already has it, the message says which index is the profile's rather than the request's,
    /// and a refused patch writes nothing.
    /// </summary>
    [Fact]
    public async Task A_patch_that_makes_the_grant_and_the_deny_collide_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "denies-fetch", host = "claude_code", program = "claude", deny = new[] { "WebFetch" } },
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "grants-search", host = "claude_code", program = "claude", tools = new[] { "web_search" } },
            Ct);

        var granting = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "denies-fetch", tools = new[] { "web_search", "web_fetch" } },
            HttpStatusCode.BadRequest,
            Ct);
        var denying = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "grants-search", deny = new[] { "WebSearch" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(("tools[1]", "denied"), (Assert.Single(granting.Details!).Field, granting.Details![0].Code));
        Assert.Equal(("tools[0]", "denied"), (Assert.Single(denying.Details!).Field, denying.Details![0].Code));
        Assert.StartsWith("tools[1] grants web_fetch, which the profile's deny[0] ", granting.Details[0].Message, StringComparison.Ordinal);
        Assert.StartsWith("the profile's tools[0] grants web_search, which deny[0] ", denying.Details[0].Message, StringComparison.Ordinal);
        var fetchRead = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "denies-fetch" }, Ct);
        var searchRead = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "grants-search" }, Ct);
        Assert.Equal((1, 1), (fetchRead.CurrentRevision, searchRead.CurrentRevision));
    }

    /// <summary>
    /// Only the host's bare name for a granted tool collides. A deny that is a pattern is not refused and is written
    /// beside the grant — how much of the grant it leaves is the host's to decide — and so is the bare name of a tool
    /// the profile does not grant.
    /// </summary>
    [Theory]
    [InlineData("WebFetch(domain:example.com)")]
    [InlineData("WebSearch")]
    public async Task A_pattern_deny_or_another_tools_name_is_written_beside_the_grant(string rule)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "beside", host = "claude_code", program = "claude", deny = new[] { rule }, tools = new[] { "web_fetch" } },
            Ct);

        Assert.Equal([AgentTool.WebFetch], created.Revision.Tools);
        Assert.Equal([rule], created.Revision.Deny);
    }

    /// <summary>
    /// The pair is the revision as it would be launched, so one patch that grants a tool and clears the deny that
    /// named it is a revision with no collision, and it is written.
    /// </summary>
    [Fact]
    public async Task A_patch_that_grants_a_tool_and_clears_its_deny_together_is_written()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "opening", host = "claude_code", program = "claude", deny = new[] { "WebFetch" } },
            Ct);

        var updated = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "opening", tools = new[] { "web_fetch" }, deny = Array.Empty<string>() },
            Ct);

        Assert.Equal(2, updated.CurrentRevision);
        Assert.Equal([AgentTool.WebFetch], updated.Revision.Tools);
        Assert.Empty(updated.Revision.Deny);
    }

    /// <summary>
    /// The match is the host's name exactly as the host spells it. A deny spelled another way is not refused and is
    /// written beside the grant; what the host makes of that spelling is the host's, and nothing here says.
    /// </summary>
    [Fact]
    public async Task A_deny_that_spells_the_name_in_another_case_is_written_beside_the_grant()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "lower-case", host = "claude_code", program = "claude", deny = new[] { "webfetch" }, tools = new[] { "web_fetch" } },
            Ct);

        Assert.Equal([AgentTool.WebFetch], created.Revision.Tools);
        Assert.Equal(["webfetch"], created.Revision.Deny);
    }

    /// <summary>
    /// A disabled profile is edited like any other, so an edit of one is held to the same pair: it is refused on the
    /// grant and writes nothing.
    /// </summary>
    [Fact]
    public async Task An_edit_of_a_disabled_profile_is_held_to_the_same_pair()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "resting", host = "claude_code", program = "claude", tools = new[] { "web_fetch" } },
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "resting", reason = "The host is being upgraded." }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "resting", deny = new[] { "WebFetch" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(("tools[0]", "denied"), (Assert.Single(error.Details!).Field, error.Details![0].Code));
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "resting" }, Ct);
        Assert.Equal(1, read.CurrentRevision);
    }
}
