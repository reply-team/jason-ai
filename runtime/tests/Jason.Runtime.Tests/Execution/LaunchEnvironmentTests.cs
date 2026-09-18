using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The environment a launched agent runs in. It is the runtime's own plus four values and not one more: an
/// agent host dumps its environment when it is unhappy, and everything that is in there is in the transcript
/// the user keeps.
/// </summary>
public class LaunchEnvironmentTests
{
    private const string DataDir = "/home/somebody/.jason";
    private const string AttemptId = "att_one";
    private const string WorkItemId = "wi_one";

    [Fact]
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
    /// The environment as a started process really gets it: a copy of this one, on Windows in a dictionary that
    /// does not care that the variable is spelled <c>Path</c> there and <c>PATH</c> everywhere else.
    /// </summary>
    private static Dictionary<string, string?> Existing() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/somebody",
    };
}
