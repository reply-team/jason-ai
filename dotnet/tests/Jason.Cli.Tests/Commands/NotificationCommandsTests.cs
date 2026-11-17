using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The <c>notification</c> noun from the side it is typed on: one channel turned on or off, one verb per operation, and
/// what a person reading the answer is shown. Which channels exist and whether one could deliver are the runtime's to say.
/// </summary>
public class NotificationCommandsTests
{
    /// <summary>The answer both verbs carry: the report's notifications section, the toast on and a plugin channel that cannot deliver.</summary>
    private const string Report =
        """
        {"enabled":true,"channels":[
        {"channel":"toast","kind":"toast","on":true,"outcome":"silenced","reason":"disabled_for_application","code":null,"class":null,"detail":null,"at":"2026-11-17T09:00:00.000Z","presence":"available","presence_detail":null,"readiness":null,"readiness_detail":null},
        {"channel":"ntfy","kind":"plugin","on":false,"outcome":null,"reason":null,"code":null,"class":null,"detail":null,"at":null,"presence":null,"presence_detail":null,"readiness":"binding_invalid","readiness_detail":"/topic required"}],
        "last_notification_id":null,"last_at":null}
        """;

    [Theory]
    [InlineData("enable", Operations.NotificationEnable)]
    [InlineData("disable", Operations.NotificationDisable)]
    public async Task Each_verb_posts_its_one_channel_with_the_actor_and_the_reason(string verb, string operation)
    {
        using var cli = new CliRun(Report);

        var exit = await cli.RunAsync("notification", verb, "toast", "--reason", "Too many banners.", "--actor", "human:person-1");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(operation, "{\"channel\":\"toast\",\"reason\":\"Too many banners.\",\"actor\":{\"type\":\"human\",\"id\":\"person-1\"}}");
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public async Task A_verb_without_a_channel_is_a_usage_error(string verb)
    {
        using var cli = new CliRun();

        Assert.Equal(ExitCodes.Usage, await cli.RunAsync("notification", verb));
    }

    /// <summary>A refusal is the runtime's answer, printed as it came, and an understood refusal is exit 1.</summary>
    [Fact]
    public async Task A_refusal_is_printed_as_the_runtime_said_it()
    {
        using var cli = new CliRun(
            "{\"error\":{\"code\":\"notifications_off\",\"message\":\"Every notification is off: Notifications:Enabled is false in the settings file.\",\"retryable\":false}}",
            status: System.Net.HttpStatusCode.Conflict);

        var exit = await cli.RunAsync("notification", "enable", "toast");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("notifications_off", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// For a person, each channel on its own line: on or off, for one that could not deliver what is missing, and for a
    /// toast the operating system held off, that it did: the person's own switch there, which is not this one.
    /// </summary>
    [Fact]
    public async Task For_a_person_each_channel_is_said_on_a_line_of_its_own()
    {
        using var cli = new CliRun(Report);

        var exit = await cli.RunAsync("notification", "disable", "ntfy", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Split('\n');
        Assert.Equal(
            [
                "Notifications: on",
                "toast   on   the operating system did not show the last one (disabled_for_application)",
                "ntfy    off  cannot deliver: binding_invalid (/topic required)",
            ],
            lines.Select(line => line.TrimEnd()));
    }

    [Fact]
    public async Task For_a_person_every_notification_off_is_said_first()
    {
        using var cli = new CliRun(Report.Replace("{\"enabled\":true", "{\"enabled\":false", StringComparison.Ordinal));

        var exit = await cli.RunAsync("notification", "disable", "toast", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.StartsWith("Notifications: off (Notifications:Enabled is false in the settings file)", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each verb's help says the two kinds of channel, where a person sees them, and that notifications as a whole are
    /// switched in the settings file, not here.
    /// </summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public async Task Each_verbs_help_says_the_two_kinds_of_channel_and_what_is_not_switched_here(string verb)
    {
        using var cli = new CliRun();

        Assert.Equal(ExitCodes.Success, await cli.RunAsync("notification", verb, "--help"));

        var help = string.Join(' ', cli.Text.Split((char[])['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("toast", help, StringComparison.Ordinal);
        Assert.Contains("notification plugin", help, StringComparison.Ordinal);
        Assert.Contains("jason runtime status --human", help, StringComparison.Ordinal);
        Assert.Contains("Notifications:Enabled", help, StringComparison.Ordinal);
        Assert.Contains("not here", help, StringComparison.Ordinal);
    }
}
