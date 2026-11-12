using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The environment a launched agent runs in. It is the runtime's own, less the runtime's configuration, plus four values
/// and the constants a host's own row declares, and not one more: an agent host dumps its environment when it is unhappy,
/// and everything that is in there is in the transcript the user keeps.
/// </summary>
public class LaunchEnvironmentTests
{
    private const string DataDir = "/home/somebody/.jason";
    private const string AttemptId = "att_one";
    private const string WorkItemId = "wi_one";

    [Fact]
    [Critical(Family.W7, "the child's environment")]
    public void The_child_is_told_its_attempt_id_its_work_item_and_where_the_cli_is()
    {
        var environment = Existing();

        LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason");

        Assert.Equal(DataDir, environment[JasonPaths.DataDirectoryVariable]);

        // Both are non-secret values the envelope already carries. They are here so that an agent reporting
        // through the CLI is its attempt without having to remember to say so.
        Assert.Equal(AttemptId, environment[ExecutionEnvironment.AttemptIdVariable]);
        Assert.Equal(WorkItemId, environment[ExecutionEnvironment.WorkItemIdVariable]);

        // And the bare word the allow rule was built from has to resolve to this build of Jason, so the
        // directory that build lives in comes first on the search path.
        Assert.StartsWith("/opt/jason" + Path.PathSeparator, environment["PATH"], StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.B1, "no stray variable reaches a child")]
    public void Nothing_else_is_added_and_nothing_is_taken_away()
    {
        var before = Existing();
        var after = Existing();

        LaunchEnvironment.Apply(after, DataDir, AttemptId, WorkItemId, "/opt/jason");

        var added = after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] expected = [ExecutionEnvironment.AttemptIdVariable, JasonPaths.DataDirectoryVariable, ExecutionEnvironment.WorkItemIdVariable];
        Assert.Equal(expected.Order(StringComparer.Ordinal), added.Order(StringComparer.Ordinal));
        Assert.Empty(before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase));

        // Of what was already there, only the search path was touched.
        var changed = before.Keys.Where(key => before[key] != after[key]).ToArray();
        Assert.Equal(["PATH"], changed);
    }

    /// <summary>
    /// A host's constants are added after the four and nothing else changes: they are a fact of the host class, never a
    /// value taken from a person, and never one of the runtime's own names.
    /// </summary>
    [Fact]
    [Critical(Family.B1, "no stray variable reaches a child")]
    public void A_hosts_constants_are_added_after_the_four_and_nothing_else_is_touched()
    {
        var before = Existing();
        var after = Existing();

        LaunchEnvironment.Apply(after, DataDir, AttemptId, WorkItemId, "/opt/jason", new Dictionary<string, string> { ["NO_BROWSER"] = "true" });

        var added = after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal);
        Assert.Equal(
            new[] { ExecutionEnvironment.AttemptIdVariable, JasonPaths.DataDirectoryVariable, "NO_BROWSER", ExecutionEnvironment.WorkItemIdVariable }.Order(StringComparer.Ordinal),
            added);
        Assert.Equal("true", after["NO_BROWSER"]);
        Assert.Equal(["PATH"], before.Keys.Where(key => before[key] != after[key]));
    }

    [Theory]
    [InlineData("PATH")]
    [InlineData("path")]
    [InlineData("JASON_DATA_DIR")]
    [InlineData("JASON_ANYTHING")]
    [InlineData("jason_lower")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_host_cannot_name_one_of_the_runtimes_own_variables_or_any_of_the_prefix_it_keeps(string name)
    {
        var environment = Existing();

        Assert.Throws<ArgumentException>(
            () => LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason", new Dictionary<string, string> { [name] = "x" }));

        // Refused before anything is written: not one of the runtime's four, and no search path prefix, is there.
        Assert.Equal(Existing().OrderBy(pair => pair.Key, StringComparer.Ordinal), environment.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void A_refused_name_among_good_ones_leaves_the_environment_as_it_was()
    {
        var environment = Existing();

        Assert.Throws<ArgumentException>(
            () => LaunchEnvironment.Apply(
                environment,
                DataDir,
                AttemptId,
                WorkItemId,
                "/opt/jason",
                new Dictionary<string, string> { ["NO_BROWSER"] = "true", ["JASON_SNEAKY"] = "x" }));

        Assert.Equal(Existing().OrderBy(pair => pair.Key, StringComparer.Ordinal), environment.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void A_host_cannot_name_the_attempt_or_the_work_item_variable()
    {
        Assert.All(
            [ExecutionEnvironment.AttemptIdVariable, ExecutionEnvironment.WorkItemIdVariable, JasonPaths.DataDirectoryVariable],
            name => Assert.Throws<ArgumentException>(
                () => LaunchEnvironment.Apply(Existing(), DataDir, AttemptId, WorkItemId, "/opt/jason", new Dictionary<string, string> { [name] = "x" })));
    }

    [Fact]
    public void The_search_path_that_was_there_is_kept_behind_the_runtimes_own_directory()
    {
        var environment = Existing();

        LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason");

        Assert.Equal("/opt/jason" + Path.PathSeparator + "/usr/bin", environment["PATH"]);
    }

    [Fact]
    public void A_runtime_that_publishes_no_directory_of_its_own_leaves_the_search_path_alone()
    {
        var environment = Existing();

        LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, cliDirectory: null);

        Assert.Equal("/usr/bin", environment["PATH"]);
    }

    [Fact]
    public void A_child_of_a_process_with_no_search_path_at_all_still_finds_the_cli()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason");

        Assert.Equal("/opt/jason", environment["PATH"]);
    }

    /// <summary>
    /// A setting of the runtime given as a variable, a plugin's secret under <c>Plugins:Config</c> among them, is the
    /// runtime's configuration and never the child's: every name that starts with <c>JASON_</c> and holds the double
    /// underscore between a section and a key is taken out, in any case, and in a dictionary that compares names exactly,
    /// as a started process's does outside Windows.
    /// </summary>
    [Theory]
    [InlineData("JASON_Dispatcher__MaxParallel")]
    [InlineData("JASON_Plugins__Config__acme-provider__api_key")]
    [InlineData("jason_runtime__port")]
    [InlineData("Jason_Harness__Root")]
    public void A_configuration_override_of_the_runtime_never_reaches_the_child(string name)
    {
        foreach (var environment in new[] { Existing(), ExistingExactly() })
        {
            environment[name] = "made-up-value";

            LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason");

            Assert.DoesNotContain(name, environment.Keys);
            Assert.DoesNotContain("made-up-value", environment.Values);
        }
    }

    [Fact]
    public void Taking_the_runtimes_configuration_out_leaves_the_four_and_everything_else_it_holds()
    {
        var environment = ExistingExactly();
        environment["JASON_Plugins__Config__acme-provider__api_key"] = "made-up-secret";
        environment["JASON_Dispatcher__MaxParallel"] = "3";
        environment["JASON_SINGLE_WORD"] = "kept";
        environment["ORDINARY__WITH_UNDERSCORES"] = "kept";

        LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason");

        string[] expected =
        [
            "HOME",
            ExecutionEnvironment.AttemptIdVariable,
            JasonPaths.DataDirectoryVariable,
            "JASON_SINGLE_WORD",
            ExecutionEnvironment.WorkItemIdVariable,
            "ORDINARY__WITH_UNDERSCORES",
            "PATH",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), environment.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("/home/somebody", environment["HOME"]);
        Assert.Equal("/opt/jason" + Path.PathSeparator + "/usr/bin", environment["PATH"]);
    }

    /// <summary>
    /// A refused host constant still leaves the environment as it was: nothing is taken out before every name is known to
    /// be one the host may set.
    /// </summary>
    [Fact]
    public void A_refused_host_constant_takes_nothing_out_either()
    {
        var environment = Existing();
        environment["JASON_Dispatcher__MaxParallel"] = "3";

        Assert.Throws<ArgumentException>(
            () => LaunchEnvironment.Apply(environment, DataDir, AttemptId, WorkItemId, "/opt/jason", new Dictionary<string, string> { ["JASON_SNEAKY"] = "x" }));

        Assert.Equal("3", environment["JASON_Dispatcher__MaxParallel"]);
    }

    /// <summary>
    /// The environment as a started process really gets it: a copy of this one, on Windows in a dictionary that
    /// does not care that the variable is spelled <c>Path</c> there and <c>PATH</c> everywhere else.
    /// </summary>
    private static Dictionary<string, string?> Existing() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/somebody",
    };

    /// <summary>The same, as a started process gets it outside Windows: names are compared exactly.</summary>
    private static Dictionary<string, string?> ExistingExactly() => new(StringComparer.Ordinal)
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/somebody",
    };
}
