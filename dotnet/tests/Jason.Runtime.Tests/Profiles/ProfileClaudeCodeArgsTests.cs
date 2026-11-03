using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// The host that ships reads a short flag as its parser does: a single-dash word of more than one letter is several flags,
/// or a flag with a value joined to it, and a lone dash is the prompt. So no such word may be a profile's argument, at
/// create or at an update, where a reserved short flag such as <c>-p</c> or <c>-r</c> could otherwise stand inside one.
/// </summary>
public class ProfileClaudeCodeArgsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("-pf")]
    [InlineData("-fp")]
    [InlineData("-rhttp://x")]
    [InlineData("-")]
    [InlineData("--")]
    public async Task A_short_flag_cluster_a_short_flag_with_a_joined_value_a_lone_dash_and_a_double_dash_are_refused_at_create_and_at_update(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "clustered", host = "claude_code", program = "claude", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);
        var createdDetail = Assert.Single(created.Details!);
        Assert.Equal(("args", "invalid"), (createdDetail.Field, createdDetail.Code));
        Assert.Contains("claude_code", createdDetail.Message, StringComparison.Ordinal);

        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "plain", host = "claude_code", program = "claude" }, Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "plain", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);
        var updatedDetail = Assert.Single(updated.Details!);
        Assert.Equal(("args", "invalid"), (updatedDetail.Field, updatedDetail.Code));
    }

    /// <summary>A profile written before the rule has a flag and its value as two tokens: it still validates, at create and at an update.</summary>
    [Fact]
    public async Task A_flag_and_its_value_as_two_tokens_still_validate_at_create_and_at_update()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "two-tokens", host = "claude_code", program = "claude", args = new[] { "--model", "opus" } },
            Ct);
        var updated = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "two-tokens", args = new[] { "--model", "sonnet" } },
            Ct);

        Assert.Equal(["--model", "opus"], created.Revision.Args);
        Assert.Equal(["--model", "sonnet"], updated.Revision.Args);
    }

    /// <summary>A short flag that is not reserved, a long flag with its value joined, and a long flag beside its value are as they were.</summary>
    [Fact]
    public async Task A_lone_short_flag_and_long_flags_are_still_taken()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "plain", host = "claude_code", program = "claude", args = new[] { "-d", "--model=made-up", "--name", "word" } },
            Ct);

        Assert.Equal(["-d", "--model=made-up", "--name", "word"], created.Revision.Args);
    }
}
