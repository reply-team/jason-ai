using Jason.App;

namespace Jason.App.Tests;

public class RuntimeRunArgumentsTests
{
    [Theory]
    [InlineData(new[] { "runtime", "run" }, false)]
    [InlineData(new[] { "runtime", "run", "--detached" }, true)]
    [InlineData(new[] { "runtime", "run", "--detached", "--help" }, true)]
    public void Detached_is_read_from_the_arguments_after_the_verb(string[] args, bool expected) =>
        Assert.Equal(expected, RuntimeRunArguments.Parse(args).Detached);

    [Fact]
    public void The_verbs_themselves_are_never_read_as_flags() =>
        Assert.False(RuntimeRunArguments.Parse(["runtime", "--detached"]).Detached);

    [Fact]
    public void The_flag_is_matched_exactly() =>
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(["runtime", "run", "--Detached"]));

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void A_help_flag_asks_for_the_usage_alone_or_beside_another_flag(string help)
    {
        Assert.True(RuntimeRunArguments.Parse(["runtime", "run", help]).Help);
        Assert.True(RuntimeRunArguments.Parse(["runtime", "run", "--detached", help]).Help);
        Assert.True(RuntimeRunArguments.Parse(["runtime", "run", help, "--data-dir", "/srv/jason"]).Help);
    }

    [Fact]
    public void Without_a_help_flag_the_usage_is_not_asked_for() =>
        Assert.False(RuntimeRunArguments.Parse(["runtime", "run", "--detached"]).Help);

    [Theory]
    [InlineData("--something")]
    [InlineData("--Help")]
    [InlineData("-x")]
    [InlineData("detached")]
    [InlineData("/srv/jason")]
    public void A_flag_it_does_not_know_is_refused_by_name(string flag)
    {
        var error = Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(["runtime", "run", flag]));
        Assert.Contains(flag, error.Message, StringComparison.Ordinal);
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(["runtime", "run", "--detached", flag]));
    }

    [Theory]
    [InlineData("/a", "/b")]
    [InlineData("/a", "/a")]
    public void A_second_data_dir_is_a_usage_error_whatever_its_value(string first, string second)
    {
        var error = Assert.Throws<RuntimeRunUsageException>(
            () => RuntimeRunArguments.Parse(["runtime", "run", "--data-dir", first, "--data-dir", second]));
        Assert.Contains("--data-dir", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A usage error wins over help, wherever the unknown flag stands.</summary>
    [Theory]
    [InlineData(new object[] { new[] { "runtime", "run", "--help", "--bogus" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--bogus", "--help" } })]
    public void A_flag_it_does_not_know_wins_over_help(string[] args) =>
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(args));

    [Fact]
    public void A_path_after_the_data_dir_flag_is_its_value_and_not_an_unknown_argument() =>
        Assert.Equal("/srv/jason", RuntimeRunArguments.Parse(["runtime", "run", "--data-dir", "/srv/jason", "--detached"]).DataDirectory);

    [Theory]
    [InlineData(new[] { "runtime", "run", "--data-dir", "/srv/jason" }, "/srv/jason")]
    [InlineData(new[] { "runtime", "run", "--detached", "--data-dir", "/srv/jason" }, "/srv/jason")]
    [InlineData(new[] { "runtime", "run" }, null)]
    [InlineData(new[] { "runtime", "run", "--detached" }, null)]
    public void The_data_directory_is_read_from_the_arguments_after_the_verb(string[] args, string? expected) =>
        Assert.Equal(expected, RuntimeRunArguments.Parse(args).DataDirectory);

    /// <summary>The verbs are never flags, and neither is anything before them: what stands where the flags begin is an argument the verb does not know.</summary>
    [Fact]
    public void A_data_dir_before_the_verbs_is_refused() =>
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(["runtime", "--data-dir", "/srv/jason"]));

    [Fact]
    public void The_data_dir_flag_is_matched_exactly() =>
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(["runtime", "run", "--Data-Dir", "/srv/jason"]));

    /// <summary>
    /// A flag with nothing after it is refused rather than read as "the default". This is the flag a logon task
    /// carries, because a scheduled task has no environment to carry the variable in — so a silent default here
    /// is a background runtime on the wrong database, which is the whole thing the flag exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "--detached" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "   " } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "--help" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "-h" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "-?" } })]
    [InlineData(new object[] { new[] { "runtime", "run", "--data-dir", "-x" } })]
    public void A_data_dir_with_no_path_after_it_is_a_usage_error(string[] args) =>
        Assert.Throws<RuntimeRunUsageException>(() => RuntimeRunArguments.Parse(args));

    [Fact]
    public void Parsing_nothing_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => RuntimeRunArguments.Parse(null!));
}
