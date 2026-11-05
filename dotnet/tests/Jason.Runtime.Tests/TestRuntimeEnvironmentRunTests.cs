using System.Diagnostics;

namespace Jason.Runtime.Tests;

/// <summary>
/// A suite run from inside a Jason attempt hands every child it starts the attempt's two variables, and the executable
/// started with them is that attempt: its CLI names it on every request, and a person's verb is refused. A test's outcome
/// may not depend on where the suite runs, so the environment a spawned executable is started with carries neither
/// unless the test sets one itself, after this helper, as the test of a launched actor does.
/// </summary>
[Collection(AttemptEnvironmentCollection.Name)]
public class TestRuntimeEnvironmentRunTests
{
    [Fact]
    public void A_start_made_while_this_process_runs_inside_an_attempt_names_no_attempt_and_no_work_item()
    {
        var previous = (Attempt: Environment.GetEnvironmentVariable("JASON_ATTEMPT_ID"), Item: Environment.GetEnvironmentVariable("JASON_WORK_ITEM_ID"));
        try
        {
            Environment.SetEnvironmentVariable("JASON_ATTEMPT_ID", "att_01K7QN0000000000000000000R");
            Environment.SetEnvironmentVariable("JASON_WORK_ITEM_ID", "wi_01K7QN0000000000000000000R");
            var start = new ProcessStartInfo("jason");

            // The start inherits them, as every start made in this process would.
            Assert.Equal("att_01K7QN0000000000000000000R", start.Environment["JASON_ATTEMPT_ID"]);
            Assert.Equal("wi_01K7QN0000000000000000000R", start.Environment["JASON_WORK_ITEM_ID"]);

            TestRuntimeEnvironment.Offline(start.Environment, Path.Combine(Path.GetTempPath(), "jason-tests", "somewhere"));

            Assert.False(start.Environment.ContainsKey("JASON_ATTEMPT_ID"), "The child would run as the attempt this suite runs inside.");
            Assert.False(start.Environment.ContainsKey("JASON_WORK_ITEM_ID"), "The child would run as the work item this suite runs inside.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JASON_ATTEMPT_ID", previous.Attempt);
            Environment.SetEnvironmentVariable("JASON_WORK_ITEM_ID", previous.Item);
        }
    }
}

/// <summary>The tests that put an attempt in this process's own environment, kept away from everything else while they run.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AttemptEnvironmentCollection
{
    public const string Name = "attempt environment";
}
