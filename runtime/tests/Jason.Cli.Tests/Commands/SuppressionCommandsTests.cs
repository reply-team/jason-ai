using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class SuppressionCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_sends_the_channel_the_value_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("suppression", "add", "--channel", "email", "--value", "ada@example.com", "--reason", "they replied stop");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.SuppressionAdd,
            "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"reason\":\"they replied stop\"}");
    }

    [Fact]
    public async Task Add_without_a_value_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("suppression", "add", "--channel", "email");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--value", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_without_a_reason_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("suppression", "remove", "--channel", "email", "--value", "ada@example.com");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--reason", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_sends_the_entry_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "suppression",
            "remove",
            "--channel",
            "email",
            "--value",
            "ada@example.com",
            "--reason",
            "they asked to hear from us again",
            "--actor",
            "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.SuppressionRemove,
            "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"reason\":\"they asked to hear from us again\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task List_passes_the_filters()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("suppression", "list", "--channel", "email", "--value", "ada@example.com", "--limit", "5", "--cursor", "c3VwX0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.SuppressionList,
            "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"limit\":5,\"cursor\":\"c3VwX0E\"}");
    }

    [Fact]
    public async Task Human_mode_renders_one_entry()
    {
        var suppression = JsonSerializer.Serialize(
            new SuppressionDto("sup_A", "email", "ada@example.com", "they replied stop", Moment),
            JasonJson.Options);
        using var cli = new CliRun(suppression);

        var exit = await cli.RunAsync("suppression", "add", "--channel", "email", "--value", "ada@example.com", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("sup_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("they replied stop", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_whether_a_removal_found_anything()
    {
        var removed = JsonSerializer.Serialize(new SuppressionRemovedDto("email", "ada@example.com", false), JasonJson.Options);
        using var cli = new CliRun(removed);

        var exit = await cli.RunAsync("suppression", "remove", "--channel", "email", "--value", "ada@example.com", "--reason", "a mistake", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("not suppressed", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_listing()
    {
        var page = JsonSerializer.Serialize(
            new Page<SuppressionDto>([new SuppressionDto("sup_A", "email", "ada@example.com", null, Moment)], null),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("suppression", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("CHANNEL", cli.Text, StringComparison.Ordinal);
        Assert.Contains("sup_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("next cursor", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
