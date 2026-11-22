using System.Net;
using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The verb a person lifts today's daily limits of launches with, from the side it is typed on: what is sent, what is
/// printed back to a person, and how a refusal exits.
/// </summary>
public class LaunchesCommandsTests
{
    private const string Lifted = """{"until":"2026-10-07T15:00:00+00:00","time_zone":"Asia/Tokyo","released":2,"already_lifted":false}""";

    /// <summary>The request carries the person and the reason, and nothing names a campaign or a role: a lift covers them all.</summary>
    [Fact]
    public async Task Lift_sends_who_lifts_and_why_and_nothing_else()
    {
        using var cli = new CliRun(Lifted);

        var exit = await cli.RunAsync("--actor", "human:marta@example.com", "launches", "lift", "--reason", "a launch day");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.LaunchesLift, "{\"reason\":\"a launch day\",\"actor\":{\"type\":\"human\",\"id\":\"marta@example.com\"}}");
    }

    [Fact]
    public async Task Lift_without_a_reason_sends_the_person_alone()
    {
        using var cli = new CliRun(Lifted);

        await cli.RunAsync("--actor", "human:marta@example.com", "launches", "lift");

        cli.AssertPosted(Operations.LaunchesLift, "{\"actor\":{\"type\":\"human\",\"id\":\"marta@example.com\"}}");
    }

    [Fact]
    public async Task Lift_without_an_actor_is_a_usage_error_and_sends_nothing()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("launches", "lift", "--reason", "a launch day");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.True(cli.Error.ToString().Contains("--actor", StringComparison.Ordinal), $"The usage error does not name --actor: {cli.Error}");
        Assert.True(cli.Requests == 0, $"A lift without an actor still sent {cli.Requests} request(s).");
    }

    /// <summary>The answer prints as the runtime gave it by default.</summary>
    [Fact]
    public async Task Lift_prints_the_answer_as_it_came()
    {
        using var cli = new CliRun(Lifted);

        await cli.RunAsync("--actor", "human:marta@example.com", "launches", "lift");

        Assert.Equal(Lifted + Environment.NewLine, cli.Text);
    }

    /// <summary>For a person: until when, in which zone, how much was let go, and whether today was lifted already.</summary>
    [Theory]
    [InlineData(false, 2, "Today's limits are lifted.", "2 held items")]
    [InlineData(true, 1, "Today's limits were lifted already.", "1 held item")]
    public async Task Lift_says_in_words_until_when_in_which_zone_and_what_was_let_go(bool already, int released, string first, string count)
    {
        using var cli = new CliRun($$"""{"until":"2026-10-07T15:00:00+00:00","time_zone":"Asia/Tokyo","released":{{released}},"already_lifted":{{(already ? "true" : "false")}}}""");

        var exit = await cli.RunAsync("--actor", "human:marta@example.com", "launches", "lift", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var shown in (string[])[first, "Until:         2026-10-07 15:00:00 UTC", "Time zone:     Asia/Tokyo", "Let go:        " + count])
        {
            Assert.True(cli.Text.Contains(shown, StringComparison.Ordinal), $"The lift does not say '{shown}':{Environment.NewLine}{cli.Text}");
        }
    }

    /// <summary>The help publishes the contract: what is lifted, until when, what a second lift does, and who may lift.</summary>
    [Fact]
    public async Task The_help_states_the_contract()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("launches", "lift", "--help");

        Assert.Equal(ExitCodes.Success, exit);
        var text = string.Join(' ', cli.Text.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        foreach (var words in (string[])[
            "--reason", "--actor", "Launches:MaxPerDay", "in every campaign and for every role", "Runtime:TimeZone",
            "let go at once", "a second lift the same day writes nothing", "lift_not_human"])
        {
            Assert.True(text.Contains(words, StringComparison.Ordinal), $"The help of launches lift does not say '{words}':{Environment.NewLine}{cli.Text}");
        }
    }

    [Theory]
    [InlineData("lift_not_human", HttpStatusCode.BadRequest)]
    [InlineData("actor_required", HttpStatusCode.BadRequest)]
    [InlineData("decided_inside_a_run", HttpStatusCode.Forbidden)]
    public async Task A_refusal_is_printed_verbatim_and_exits_1(string code, HttpStatusCode status)
    {
        var envelope = $$$"""{"error":{"code":"{{{code}}}","message":"No.","retryable":false}}""";
        using var cli = new CliRun(envelope, status: status);

        var exit = await cli.RunAsync("--actor", "human:marta@example.com", "launches", "lift");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(envelope + Environment.NewLine, cli.Text);
    }
}
