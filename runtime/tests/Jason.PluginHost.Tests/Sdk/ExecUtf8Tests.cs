using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;
using Jason.Runtime.Tests;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// host.exec speaks to the program it starts in UTF-8, whatever the console's code page. The whole invocation runs
/// through the mode in this process, so the legacy page the collection sets is the page host.exec would otherwise have
/// written in, and the program is a real one that reads and writes UTF-8 as a node program does.
/// </summary>
[Collection(LegacyConsolePageCollection.Name)]
public sealed class ExecUtf8Tests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The program is sent the text on stdin and says it back on stdout and stderr. Written in the console's page, é
    /// reached it as one byte UTF-8 cannot read and an emoji as two question marks; a name or a message a plugin sent a
    /// provider's program arrived that way.
    /// </summary>
    [Fact]
    public async Task A_program_is_sent_its_input_and_heard_in_utf_8_whatever_the_console_s_page()
    {
        var (exit, stdout, stderr) = await ModeRun.RunAsync(
            InvocationFactory.Create(
                ModeRun.FakeProviderRoot,
                "exec.run",
                new JsonObject
                {
                    ["executable"] = "dotnet",
                    ["args"] = new JsonArray(FakeProviderCli.Dll, "echo-stdin"),
                    ["stdin"] = LegacyConsolePage.Text,
                },
                builder => builder.Grants = new InvocationGrants(new ExecGrants([new ExecutableGrant("dotnet", Executables.Dotnet)]), null, null)),
            Ct);

        Assert.True(exit == HostExitCodes.Completed, $"The host exited {exit}: {string.Join(" | ", stderr)}");
        var outcome = ModeRun.Outcome(stdout);
        Assert.True(outcome.Status == OutcomeStatus.Succeeded, $"The outcome is {outcome.Status}: {outcome.Error?.Code}: {outcome.Error?.Message}");
        Assert.Equal(
            (0, LegacyConsolePage.Text, LegacyConsolePage.Text),
            (outcome.Result!["exit_code"]!.GetValue<int>(), outcome.Result["stdout"]!.GetValue<string>(), outcome.Result["stderr"]!.GetValue<string>()));
    }
}
