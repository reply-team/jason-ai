using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The Cursor CLI host as a person's profile launches it, with the stand-in under the host's own word on the search
/// path: what the work directory holds where the host reads its permissions, and what an attempt becomes when the host
/// says it is not signed in. <see cref="HostOnPathTests"/> already launches this host with the others and proves the
/// stand-in is started and the skill is where the host reads it; these two rows are the ones only this host has.
/// </summary>
public class CursorCliOnPathTests
{
    private const string Role = "cursor-researcher";

    private const string Brief = "canary-brief-1b9e4c";

    private static readonly System.Text.Json.JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The host reads its permissions from <c>.cursor/cli.json</c> in the directory it works in, so that file is where the
    /// profile's deny stands, beside the write deny the host always has and the callback the role is allowed to run. It is a
    /// file a role can read, so nothing of the launch envelope and no byte of the runtime's token is in it.
    /// </summary>
    [Fact]
    public async Task The_work_directory_holds_the_permission_file_with_the_callback_and_the_profiles_deny_and_nothing_of_the_launch()
    {
        using var stub = StubHost.OnPath(AgentHostKind.CursorCli);
        await using var host = await FakeHostRuntime.StartAsync(
            Ct,
            prepare: paths =>
            {
                var skill = Directory.CreateDirectory(Path.Combine(paths.RoleSkillsDirectory, Role)).FullName;
                File.WriteAllText(Path.Combine(skill, "SKILL.md"), $"---\nname: {Role}\ndescription: finds the decision maker\n---\n\nAsk the runtime for the brief.\n");
            },
            configureServices: services => services.AddSingleton(stub.SearchPath));
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "cursor", host = "cursor_cli", program = "agent", deny = new[] { "Shell(rm)" }, host_version_verified = "0.0.0-stand-in" },
            Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync(Role, Ct, "silent");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = Role, execution_profile = "cursor", context = new { behaviour = "researcher", brief = Brief } },
            Ct);
        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        var attempt = done.Attempts![0];
        var directory = Path.Combine(attempt.Launch!.WorkDir, ".cursor");
        var file = Path.Combine(directory, "cli.json");
        Assert.Equal(["cli.json"], Directory.GetFiles(directory).Select(Path.GetFileName));

        var text = File.ReadAllText(file);
        var permissions = JsonNode.Parse(text)!["permissions"]!;
        var allow = permissions["allow"]!.AsArray().Select(rule => (string)rule!).ToArray();
        var deny = permissions["deny"]!.AsArray().Select(rule => (string)rule!).ToArray();
        Assert.Contains($"Shell({ProgramResolver.DefaultCliCommand})", allow);
        Assert.Contains("Write(**)", deny);
        Assert.Contains("Shell(rm)", deny);
        Assert.DoesNotContain(host.Fixture.Runtime.Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Brief, text, StringComparison.Ordinal);
        Assert.DoesNotContain(item.Id, text, StringComparison.Ordinal);
        Assert.DoesNotContain(attempt.Id, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host that exits non-zero saying the account is not signed in ends the attempt as <c>host_not_available</c> with the
    /// host's one fixed sentence. What the host wrote, which can hold the person's address, is in the attempt's own log
    /// and in no error and no journal entry.
    /// </summary>
    [Fact]
    public async Task A_host_that_says_it_is_not_signed_in_ends_the_attempt_with_the_fixed_sentence_and_no_byte_of_what_it_wrote()
    {
        using var stub = StubHost.OnPath(AgentHostKind.CursorCli);
        await using var host = await FakeHostRuntime.StartAsync(Ct, configureServices: services => services.AddSingleton(stub.SearchPath));
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "cursor", host = "cursor_cli", program = "agent", host_version_verified = "0.0.0-stand-in" },
            Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("signed-out", Ct, "silent");
        const string Tail = "Authentication+required+for+person@example.test.+Please+run+'agent+login'+first.";

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "signed-out", execution_profile = "cursor", context = new { behaviour = "say --stderr " + Tail } },
            Ct);
        await host.ScanAsync(Ct);
        var failed = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);

        const string Sentence = "Cursor CLI is not signed in on this account: run `agent login` as yourself.";
        Assert.Equal("host_not_available", failed.LastError!.Code);
        Assert.False(failed.LastError.Retriable);
        Assert.Equal(Sentence, failed.LastError.Message);

        var chronicle = await host.Fixture.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { work_item_id = item.Id, limit = 50 }, Ct);
        var written = System.Text.Json.JsonSerializer.Serialize(chronicle, Plain);
        Assert.Contains(Sentence, written, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.test", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Authentication", written, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.test", System.Text.Json.JsonSerializer.Serialize(failed.LastError, Plain), StringComparison.Ordinal);
    }
}
