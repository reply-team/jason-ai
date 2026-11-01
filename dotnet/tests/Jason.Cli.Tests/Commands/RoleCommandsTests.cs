using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class RoleCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_passes_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "list", "--limit", "5", "--cursor", "cm9sX0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleList, "{\"limit\":5,\"cursor\":\"cm9sX0E\"}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleList, "{}");
    }

    [Fact]
    public async Task Add_keeps_the_entry_command_in_the_order_it_was_written()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "add", "fake", "--entry-command", "dotnet", "--entry-command", "host.dll", "--entry-command", "succeed");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleAdd, "{\"name\":\"fake\",\"entry_command\":[\"dotnet\",\"host.dll\",\"succeed\"]}");
    }

    [Fact]
    public async Task Add_sends_the_defaults_the_description_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "role",
            "add",
            "fake",
            "--entry-command",
            "dotnet",
            "--profile-defaults",
            "{\"model\":\"small\"}",
            "--description",
            "A stand-in agent host",
            "--reason",
            "for the integration tests",
            "--actor",
            "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RoleAdd,
            "{\"name\":\"fake\",\"entry_command\":[\"dotnet\"],\"profile_defaults\":{\"model\":\"small\"},\"description\":\"A stand-in agent host\","
                + "\"reason\":\"for the integration tests\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Add_sends_the_execution_profile_the_role_is_registered_with()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "add", "fake", "--execution-profile", "local-claude");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleAdd, "{\"name\":\"fake\",\"execution_profile\":\"local-claude\"}");
    }

    [Fact]
    public async Task Set_profile_names_the_role_and_the_profile()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "set-profile", "researcher", "--execution-profile", "local-claude", "--reason", "the laptop runs this one");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RoleSetProfile,
            "{\"name\":\"researcher\",\"execution_profile\":\"local-claude\",\"reason\":\"the laptop runs this one\"}");
    }

    [Fact]
    public async Task Set_profile_clears_the_policy_with_an_explicit_null()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "set-profile", "researcher", "--clear");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleSetProfile, "{\"name\":\"researcher\",\"execution_profile\":null}");
    }

    /// <summary>Setting and clearing at once asks two contradictory things, and the CLI can tell without the runtime.</summary>
    [Fact]
    public async Task Set_profile_refuses_a_call_that_both_names_a_profile_and_clears_one()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "set-profile", "researcher", "--execution-profile", "local-claude", "--clear");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--execution-profile", cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>And a call that names neither asks for nothing at all, which is not what somebody typing this verb wants.</summary>
    [Fact]
    public async Task Set_profile_refuses_a_call_that_says_neither()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "set-profile", "researcher");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--execution-profile", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"name\":\"from the file\",\"entry_command\":[\"dotnet\",\"host.dll\"],\"profile_defaults\":{\"model\":\"small\"}}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "add", "fake", "--file", file.Path);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RoleAdd,
            "{\"name\":\"fake\",\"entry_command\":[\"dotnet\",\"host.dll\"],\"profile_defaults\":{\"model\":\"small\"}}");
    }

    [Fact]
    public async Task Profile_defaults_that_are_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "add", "fake", "--profile-defaults", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--profile-defaults", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reserved_actor_is_refused_on_the_read_only_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("role", "list", "--actor", "system");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_registry_as_a_table()
    {
        var page = JsonSerializer.Serialize(
            new Page<RoleDto>(
                [
                    new RoleDto("rol_A", "manager", true, "Runs the campaign.", [], new JsonObject(), null, Moment, Moment),
                    new RoleDto("rol_B", "fake", false, null, ["dotnet", "host.dll", "succeed"], new JsonObject(), "local-claude", Moment, Moment),
                ],
                null),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("role", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            NAME     BUILTIN  PROFILE       ENTRY COMMAND            ID
            -------  -------  ------------  -----------------------  -----
            manager  yes      -             -                        rol_A
            fake     no       local-claude  dotnet host.dll succeed  rol_B
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_renders_one_role()
    {
        var role = JsonSerializer.Serialize(
            new RoleDto("rol_B", "fake", false, "A stand-in agent host", ["dotnet", "host.dll", "succeed"], new JsonObject { ["model"] = "small" }, "local-claude", Moment, Moment),
            JasonJson.Options);
        using var cli = new CliRun(role);

        var exit = await cli.RunAsync("role", "add", "fake", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            Id:            rol_B
            Name:          fake
            Builtin:       no
            Description:   A stand-in agent host
            Entry command: dotnet host.dll succeed
            Defaults:      model
            Profile:       local-claude
            Created:       2026-09-14 10:00:00 UTC
            Updated:       2026-09-14 10:00:00 UTC
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("role", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }
}
