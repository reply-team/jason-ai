using System.Net;
using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>jason system drain</c> and <c>jason system resume</c>: plain verbs of <c>system.drain</c> and <c>system.resume</c>,
/// an empty body sent and the answer printed as it came, so a runtime a routine left draining is given back its work
/// from the terminal.
/// </summary>
public class SystemCommandsTests
{
    private const string Draining = "{\"state\":\"draining\",\"running_attempts\":2}";
    private const string Running = "{\"state\":\"running\",\"running_attempts\":0}";

    [Theory]
    [InlineData("drain", Operations.SystemDrain, Draining)]
    [InlineData("resume", Operations.SystemResume, Running)]
    public async Task Each_verb_sends_its_operation_and_prints_the_exact_response(string verb, string operation, string answer)
    {
        using var cli = new CliRun(answer);

        var exit = await cli.RunAsync("system", verb);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(operation, "{}");
        Assert.Equal(1, cli.Requests);
        Assert.Equal(answer + Environment.NewLine, cli.Text);
        Assert.Equal(string.Empty, cli.Error.ToString());
    }

    [Theory]
    [InlineData("drain", Draining, "The dispatcher is draining; 2 attempts running.")]
    [InlineData("resume", Running, "The dispatcher is running; 0 attempts running.")]
    [InlineData("resume", "{\"state\":\"running\",\"running_attempts\":1}", "The dispatcher is running; 1 attempt running.")]
    public async Task Human_says_the_state_and_the_running_attempts(string verb, string answer, string said)
    {
        using var cli = new CliRun(answer);

        var exit = await cli.RunAsync("system", verb, "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(said + Environment.NewLine, cli.Text);
    }

    /// <summary>An error is the runtime's own envelope, verbatim, exit 1; no runtime is exit 3 with nothing sent.</summary>
    [Fact]
    public async Task An_error_is_printed_verbatim_with_exit_1_and_no_runtime_is_exit_3()
    {
        const string Refused = "{\"error\":{\"code\":\"internal_error\",\"message\":\"no\",\"retryable\":true}}";
        using var refused = new CliRun(Refused, status: HttpStatusCode.InternalServerError);

        Assert.Equal(ExitCodes.ApiError, await refused.RunAsync("system", "resume"));
        Assert.Equal(Refused + Environment.NewLine, refused.Text);

        using var none = new CliRun(descriptor: false);

        Assert.Equal(ExitCodes.RuntimeUnavailable, await none.RunAsync("system", "drain"));
        Assert.Equal(0, none.Requests);
    }

    /// <summary>The help of each says what it does and which routines send the same operation.</summary>
    [Theory]
    [InlineData("drain", "claim no new work", "'jason update apply' and 'jason install' send it before they stop a runtime")]
    [InlineData("resume", "claim work again", "'jason install' sends it to a runtime it drained and could not stop")]
    public async Task Help_says_what_it_does_and_who_else_sends_it(string verb, string does, string who)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("system", verb, "--help");

        Assert.Equal(ExitCodes.Success, exit);
        var help = string.Join(' ', cli.Text.Split((char[])['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(does, help, StringComparison.Ordinal);
        Assert.Contains(who, help, StringComparison.Ordinal);
    }
}
