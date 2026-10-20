using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A child that speaks to Jason is read and written as UTF-8 by the product's own rule, never by the console's code
/// page. Each row crosses a real process boundary with <see cref="LegacyConsolePage.Text"/>, with this process's console
/// on a legacy page the collection sets, so a stream whose encoding was left to the console shows here on any runner.
/// </summary>
/// <remarks>
/// The page this process is set to reaches the product's code that runs in this process, such as the dispatcher's
/// launcher. A child started without a window of its own is given a fresh console on the system's legacy page instead,
/// so the plugin host's own process is held to that page, which its row names; the row through this process's page is
/// in the plugin host's tests.
/// </remarks>
[Collection(LegacyConsolePageCollection.Name)]
public class Utf8ChildStreamsTests
{
    private const string Text = LegacyConsolePage.Text;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A plugin's program is sent the text on stdin and says it back on stdout and stderr, through the real plugin host:
    /// what the plugin handed host.exec reaches the program as it was written, and what the program said reaches the
    /// plugin. Written in the console's page, é reached a program reading UTF-8 as one byte it could not read, and an
    /// emoji as two question marks.
    /// </summary>
    [Fact]
    public async Task A_plugin_s_program_is_sent_its_input_and_heard_in_utf_8_through_the_real_plugin_host()
    {
        await using var api = await PluginInvokerTests.StartAsync();

        var result = await PluginInvokerTests.InvokeAsync(
            api,
            PluginInvokerTests.Request("exec.run", new JsonObject
            {
                ["executable"] = FakeProviderCli.ExecutableName,
                ["args"] = new JsonArray("echo-stdin"),
                ["stdin"] = Text,
            }),
            Ct);

        var answer = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).Result!;
        var heard = (answer["exit_code"]!.GetValue<int>(), answer["stdout"]!.GetValue<string>(), answer["stderr"]!.GetValue<string>());
        Assert.True(
            heard == (0, Text, Text),
            $"Through the plugin host's own console, on the system's page {LegacyConsolePage.SystemPage}, the program exited {heard.Item1} and said '{heard.Item2}' and '{heard.Item3}'.");
    }

    /// <summary>
    /// The agent writes a line on stdout and one on stderr and dies. The transcript keeps the first and the attempt
    /// ends with the second, letter for letter: an agent writes UTF-8, and read in the console's page every letter
    /// outside ASCII came back as a replacement character.
    /// </summary>
    [Fact]
    public async Task An_agent_s_transcript_and_the_words_its_attempt_ended_with_keep_every_letter()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-say", Ct, "say", "--stdout", "said " + Text, "--stderr", "ended " + Text);
        var created = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-say" }, Ct);

        await host.ScanAsync(Ct);
        var item = await host.WaitAsync(
            created.Id,
            each => each.Status == WorkItemStatus.Created && each.AttemptCount == 1,
            Ct,
            "a first attempt that ended and a release");

        var attempt = Assert.Single(item.Attempts!);
        var transcript = await File.ReadAllTextAsync(Path.Combine(host.Paths.AttemptWorkDirectory(item.Id, attempt.Id), "stdout.log"), Ct);
        Assert.Contains("said " + Text, transcript, StringComparison.Ordinal);
        Assert.Contains("ended " + Text, attempt.Error!.Trace!, StringComparison.Ordinal);
    }
}
