using System.Text.Json;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// What these two verbs leave on stdout: one compact JSON document, which is the whole of the CLI's contract
/// with whatever is reading it.
/// </summary>
/// <remarks>
/// Both verbs stop the runtime on their way through, and the stopping is done by the code <c>jason runtime
/// stop</c> runs — a verb that answers its caller on stdout. Nobody typed it here, so its acknowledgement
/// arrived in front of the update's own report and a reader was handed two documents where it was promised one:
/// <c>ConvertFrom-Json</c> returns an array, <c>jq</c> refuses outright. Both CI end-to-end jobs failed on it,
/// and no test in this repository could see it, because the tests that drive an update drive the applier
/// directly, where nothing prints. These go through the command line.
/// </remarks>
public class UpdateOutputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_update_against_a_running_runtime_prints_one_document()
    {
        using var installation = new FakeInstallation().WithRuntime();

        var exit = await CliApp.RunAsync(
            ["update", "apply", "--feed", installation.Feed.ToString(), "--drain-seconds", "0"],
            installation.Env,
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = Only<UpdateApplyResponse>(installation);
        Assert.Equal(installation.To.ToString(), answer.To);
        Assert.Equal("complete", answer.Step);
    }

    [Fact]
    public async Task A_rollback_against_a_running_runtime_prints_one_document()
    {
        using var installation = new FakeInstallation().WithRuntime();
        await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);
        installation.Out.GetStringBuilder().Clear();
        Assert.True(installation.Running, "this test is about the stop, so there has to be something to stop");

        var exit = await CliApp.RunAsync(["update", "rollback"], installation.Env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(installation.From.ToString(), Only<UpdateApplyResponse>(installation).To);
    }

    /// <summary>
    /// And the failure path, which is the one that was still wrong after the obvious half was fixed: a stop that
    /// fails prints the stop's own error envelope, and then the verb prints its refusal. Two documents again,
    /// and this time the first one is the less useful of the two — it has no update code and no remedy.
    /// </summary>
    [Theory]
    [InlineData("apply")]
    [InlineData("rollback")]
    public async Task A_stop_that_fails_prints_one_document_too(string verb)
    {
        using var installation = new FakeInstallation().WithRuntime();
        if (verb == "rollback")
        {
            await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);
            installation.Out.GetStringBuilder().Clear();
        }

        installation.RefusesToStop = true;

        var exit = await CliApp.RunAsync(
            verb == "apply"
                ? ["update", "apply", "--feed", installation.Feed.ToString(), "--drain-seconds", "0"]
                : ["update", "rollback"],
            installation.Env,
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);

        // One document, and it is the verb's own: the code says what to do about an update, not what an
        // unrelated verb made of an HTTP status.
        var refusal = Only<ErrorResponse>(installation);
        Assert.Equal(UpdateCodes.RuntimeUnreachable, refusal.Error.Code);

        // What the stop had to say is kept, where diagnostics go.
        Assert.Contains("the runtime would not stop", installation.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The one line stdout is allowed, parsed as what it claims to be.</summary>
    private static T Only<T>(FakeInstallation installation)
    {
        var lines = installation.Out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var only = Assert.Single(lines);
        return JsonSerializer.Deserialize<T>(only, JasonJson.Options)!;
    }
}
