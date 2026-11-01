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

    [Theory]
    [InlineData(new[] { "runtime", "run", "--data-dir", "/srv/jason" }, "/srv/jason")]
    [InlineData(new[] { "runtime", "run", "--detached", "--data-dir", "/srv/jason" }, "/srv/jason")]
    [InlineData(new[] { "runtime", "run" }, null)]
    [InlineData(new[] { "runtime", "run", "--detached" }, null)]
    public void The_data_directory_is_read_from_the_arguments_after_the_verb(string[] args, string? expected) =>
        Assert.Equal(expected, RuntimeRunArguments.Parse(args).DataDirectory);

    /// <summary>The verbs are never flags, and neither is anything before them.</summary>
    [Fact]
    public void A_data_dir_before_the_verbs_is_not_read() =>
        Assert.Null(RuntimeRunArguments.Parse(["runtime", "--data-dir", "/srv/jason"]).DataDirectory);

    [Fact]
    public void The_data_dir_flag_is_matched_exactly() =>
        Assert.Null(RuntimeRunArguments.Parse(["runtime", "run", "--Data-Dir", "/srv/jason"]).DataDirectory);

    /// <summary>
    /// A flag with nothing after it is refused rather than read as "the default". This is the flag a logon task
    /// carries, because a scheduled task has no environment to carry the variable in — so a silent default here
    /// is a background runtime on the wrong database, which is the whole thing the flag exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "--detached" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "   " } })]
    public void A_data_dir_with_no_path_after_it_is_a_usage_error(string[] args) =>
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(args));

    [Fact]
    public void Parsing_nothing_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => RuntimeRunArguments.Parse(null!));
}
