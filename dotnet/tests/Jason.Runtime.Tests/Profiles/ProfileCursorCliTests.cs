using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// What a profile of the Cursor CLI host is held to where it is written, with the host that ships: a web search can be
/// neither granted nor denied, since the person's own setting decides it; a fetch is a grant like any other; arguments
/// take their values joined by an equals sign; and on Windows the launcher script the host is installed as may be named
/// by its full path, although it is a script, and by no other script and no bare name.
/// </summary>
public class ProfileCursorCliTests
{
    private const string Launcher = "C:\\Users\\someone\\AppData\\Local\\cursor-agent\\agent.cmd";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grant_of_web_search_is_refused_naming_the_host_and_the_persons_own_setting()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "searching", host = "cursor_cli", program = "agent", tools = new[] { "web_search" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("cursor_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("autoAcceptWebSearch", detail.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("web_search")]
    [InlineData("WebSearch")]
    public async Task A_deny_of_web_search_in_either_spelling_is_refused_naming_the_host(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "undeniable", host = "cursor_cli", program = "agent", deny = new[] { "Shell(rm)", word } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[1]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("cursor_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("autoAcceptWebSearch", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_grant_of_a_fetch_and_the_five_deny_heads_are_taken()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "fetching",
                host = "cursor_cli",
                program = "agent",
                tools = new[] { "web_fetch" },
                deny = new[] { "Shell(rm)", "Read(**/.env)", "Write(**)", "WebFetch(domain:example.test)", "Mcp(server:tool)" },
            },
            Ct);

        Assert.Equal([AgentTool.WebFetch], created.Revision.Tools);
        Assert.Equal(5, created.Revision.Deny.Count);
    }

    [Fact]
    public async Task A_deny_that_is_not_one_of_the_five_heads_is_refused_naming_the_host_and_the_word()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "wrong-head", host = "cursor_cli", program = "agent", deny = new[] { "Bash(rm *)" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("cursor_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("Bash(rm *)", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bare_word_among_the_arguments_is_refused_because_it_would_be_the_prompt()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "bare-word", host = "cursor_cli", program = "agent", args = new[] { "--model", "made-up-model" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("cursor_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("stdin", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_flag_with_its_value_joined_by_an_equals_sign_is_taken()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "joined", host = "cursor_cli", program = "agent", args = new[] { "--model=made-up-model" } },
            Ct);

        Assert.Equal(["--model=made-up-model"], created.Revision.Args);
    }

    [Fact]
    public async Task The_launcher_script_is_taken_by_its_full_path_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The host is installed as a launcher script on Windows only.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "scripted", host = "cursor_cli", program = Launcher }, Ct);

        Assert.Equal(Launcher, created.Revision.Program);
    }

    [Fact]
    public async Task The_launcher_script_is_taken_by_a_patch_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The host is installed as a launcher script on Windows only.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "patched", host = "cursor_cli", program = "agent" }, Ct);

        var updated = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "patched", program = Launcher }, Ct);

        Assert.Equal(Launcher, updated.Revision.Program);
    }

    [Theory]
    [InlineData("agent.cmd")]
    [InlineData(".\\agent.cmd")]
    [InlineData("./agent.cmd")]
    [InlineData("sub/agent.cmd")]
    [InlineData("C:\\Users\\someone\\AppData\\Local\\cursor-agent\\agent.cmd.")]
    [InlineData("C:\\Users\\someone\\AppData\\Local\\cursor-agent\\claude.cmd")]
    [InlineData("C:\\Users\\someone\\AppData\\Local\\cursor-agent\\agent.ps1")]
    public async Task Any_other_script_and_a_bare_launcher_name_stay_refused(string program)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "other-script", host = "cursor_cli", program },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("program", "shell_script"), (detail.Field, detail.Code));
    }

    /// <summary>
    /// The host's parser expands <c>-fp</c> into two flags and reads <c>-ehttp://...</c> as <c>-e</c> with a value, and a
    /// lone dash as the prompt, so none of them may be written as an argument, at create or at an update. A short flag
    /// of the reserved set is refused as itself.
    /// </summary>
    [Theory]
    [InlineData("-fp")]
    [InlineData("-pf")]
    [InlineData("-ehttp://127.0.0.1:9")]
    [InlineData("-k")]
    [InlineData("-")]
    public async Task A_short_flag_cluster_a_short_flag_with_a_joined_value_and_a_lone_dash_are_refused_at_create_and_at_update(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "clustered", host = "cursor_cli", program = "agent", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.StartsWith("args", Assert.Single(created.Details!).Field, StringComparison.Ordinal);

        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "plain", host = "cursor_cli", program = "agent" }, Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "plain", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.StartsWith("args", Assert.Single(updated.Details!).Field, StringComparison.Ordinal);
    }

    /// <summary>
    /// A patch that names only the host is held to the new host's rule for the program the profile already has: the
    /// launcher script of this host is not a program of another, and no other script is one of this.
    /// </summary>
    [Fact]
    public async Task A_patch_moving_a_launcher_profile_to_another_host_is_refused_for_the_stored_program()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The host is installed as a launcher script on Windows only.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "scripted", host = "cursor_cli", program = Launcher }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "scripted", host = "claude_code" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("program", "shell_script"), (detail.Field, detail.Code));
        Assert.Contains("the profile's program", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_patch_moving_a_stored_script_to_this_host_is_refused_unless_the_script_is_its_launcher()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await StoreAsync(api, "older-script", "C:\\tools\\run.cmd", "AAAA");

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "older-script", host = "cursor_cli" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("program", "shell_script"), (detail.Field, detail.Code));
        Assert.Contains("the profile's program", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_patch_moving_a_stored_launcher_path_to_this_host_is_taken_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The host is installed as a launcher script on Windows only.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await StoreAsync(api, "older-launcher", Launcher, "BBBB");

        var updated = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "older-launcher", host = "cursor_cli" }, Ct);

        Assert.Equal(Launcher, updated.Revision.Program);
    }

    /// <summary>A profile as a build before the rule would have stored it, so a patch meets what is already there.</summary>
    private static async Task StoreAsync(RuntimeApiFixture api, string name, string program, string idEnd)
    {
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var now = DateTime.UtcNow;
        var profile = new ExecutionProfile { PublicId = "prf_01K0000000000000000000" + idEnd, Name = name, CurrentRevision = 1, CreatedAt = now, UpdatedAt = now };
        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = 1,
            Host = AgentHostKind.ClaudeCode,
            Program = program,
            HostVersionVerified = "stand-in",
            CreatedByType = ActorType.Human,
            CreatedAt = now,
        });
        db.ExecutionProfiles.Add(profile);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task The_launcher_script_is_refused_under_a_host_that_is_not_installed_as_one()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "wrong-host", host = "claude_code", program = Launcher },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("program", "shell_script"), (detail.Field, detail.Code));
    }
}
