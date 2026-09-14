using System.Text.Json.Nodes;
using Jason.Cli;

namespace Jason.Cli.Tests;

public class ActorOptionTests
{
    [Fact]
    public void The_option_is_named_actor_and_applies_to_every_subcommand()
    {
        var option = ActorOption.Create();

        Assert.Equal("--actor", option.Name);
        Assert.True(option.Recursive);
    }

    [Fact]
    public void An_absent_actor_parses_as_nothing()
    {
        Assert.Null(ActorOption.Parse(null));
        Assert.Null(ActorOption.Parse("   "));
    }

    [Fact]
    public void A_bare_type_parses_without_an_id() =>
        Assert.Equal("{\"type\":\"human\"}", Serialize(ActorOption.Parse("human")));

    [Fact]
    public void A_human_with_an_id_parses() =>
        Assert.Equal("{\"type\":\"human\",\"id\":\"ada\"}", Serialize(ActorOption.Parse("human:ada")));

    [Fact]
    public void A_role_with_an_id_parses() =>
        Assert.Equal("{\"type\":\"role\",\"id\":\"planner\"}", Serialize(ActorOption.Parse("role:planner")));

    [Fact]
    public void A_running_attempt_may_claim_the_work_it_creates() =>
        Assert.Equal(
            "{\"type\":\"attempt\",\"id\":\"att_01K52JR0000000000000000001\"}",
            Serialize(ActorOption.Parse("attempt:att_01K52JR0000000000000000001")));

    [Fact]
    public void An_attempt_without_an_id_is_a_usage_error()
    {
        var failure = Assert.Throws<UsageException>(() => ActorOption.Parse("attempt"));

        Assert.Contains("--actor", failure.Message, StringComparison.Ordinal);
        Assert.Contains("attempt:", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("system:runtime")]
    public void System_is_reserved_for_the_runtime(string text)
    {
        var failure = Assert.Throws<UsageException>(() => ActorOption.Parse(text));

        Assert.Contains("--actor", failure.Message, StringComparison.Ordinal);
        Assert.Contains("reserved", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("robot")]
    [InlineData("Human")]
    [InlineData("human:")]
    [InlineData("attempt:")]
    public void Anything_else_is_a_usage_error(string text)
    {
        var failure = Assert.Throws<UsageException>(() => ActorOption.Parse(text));

        Assert.Contains("--actor", failure.Message, StringComparison.Ordinal);
    }

    private static string Serialize(JsonObject? actor) => RequestBody.Serialize(actor!);
}
