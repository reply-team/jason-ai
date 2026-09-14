using Jason.App;

namespace Jason.App.Tests;

public class RuntimeRunArgumentsTests
{
    [Theory]
    [InlineData(new[] { "runtime", "run" }, false)]
    [InlineData(new[] { "runtime", "run", "--detached" }, true)]
    [InlineData(new[] { "runtime", "run", "--something", "--detached" }, true)]
    [InlineData(new[] { "runtime", "run", "--something" }, false)]
    public void Detached_is_read_from_the_arguments_after_the_verb(string[] args, bool expected) =>
        Assert.Equal(expected, RuntimeRunArguments.Parse(args).Detached);

    [Fact]
    public void The_verbs_themselves_are_never_read_as_flags() =>
        Assert.False(RuntimeRunArguments.Parse(["runtime", "--detached"]).Detached);

    [Fact]
    public void The_flag_is_matched_exactly() =>
        Assert.False(RuntimeRunArguments.Parse(["runtime", "run", "--Detached"]).Detached);

    [Fact]
    public void Parsing_nothing_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => RuntimeRunArguments.Parse(null!));
}
