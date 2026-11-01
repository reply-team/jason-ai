using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Discovery;
using Jason.Runtime.Discovery;
using Jason.Runtime.Tests.Hosting;

namespace Jason.Runtime.Tests.Discovery;

/// <summary>
/// A descriptor this account may not read is no runtime to talk to, as a missing or unreadable one is: the reader's own
/// contract. It used to escape every command as an unhandled refusal. And the commands that say there is no runtime say
/// which it is, so a person is not sent to start a runtime when the file is there and only closed to them.
/// </summary>
public sealed class DescriptorReaderDenialTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.S5, "another account's runtime is not ours")]
    public void A_descriptor_this_account_may_not_read_is_no_runtime()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "The superuser reads any file, so nothing here can be denied.");

        using var data = new TempDataDir();
        new DescriptorPublisher(data.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", Ct);
        using var denied = Denial.Of(data.Paths.DescriptorFile);

        Assert.Null(new DescriptorReader(data.Paths).Read());
    }

    public static TheoryData<string[]> Commands => new()
    {
        new[] { "campaign", "list" },
        new[] { "runtime", "status" },
        new[] { "runtime", "stop" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task A_command_says_the_descriptor_is_there_but_closed_to_this_account(string[] command)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "The superuser reads any file, so nothing here can be denied.");

        using var data = new TempDataDir();
        new DescriptorPublisher(data.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", Ct);
        using var denied = Denial.Of(data.Paths.DescriptorFile);

        var (exit, error) = await RunAsync(data, command);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.NoDescriptor, (string?)error["code"]);
        Assert.Equal($"A descriptor at '{data.Paths.DescriptorFile}' exists but could not be read by this account.", (string?)error["message"]);

        // Not opened by asking again: an agent that branches on retryable would ask forever.
        Assert.False((bool?)error["retryable"]);
    }

    /// <summary>And a descriptor that is not there is still said to be not there, in the words it always was.</summary>
    [Theory]
    [MemberData(nameof(Commands))]
    public async Task A_command_still_says_a_missing_descriptor_is_missing(string[] command)
    {
        using var data = new TempDataDir();

        var (exit, error) = await RunAsync(data, command);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.NoDescriptor, (string?)error["code"]);
        Assert.Equal($"No runtime endpoint descriptor at '{data.Paths.DescriptorFile}'. Is the runtime running?", (string?)error["message"]);

        // A runtime that is starting publishes one, so asking again is what to do.
        Assert.True((bool?)error["retryable"]);
    }

    private static async Task<(int Exit, JsonNode Error)> RunAsync(TempDataDir data, string[] command)
    {
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(command, new CliEnvironment(output, new StringWriter(), data.Paths), Ct);
        return (exit, JsonNode.Parse(output.ToString())!["error"]!);
    }
}
