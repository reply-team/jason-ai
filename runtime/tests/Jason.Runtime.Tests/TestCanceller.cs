using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests;

/// <summary>
/// The shared cancellation routine wired up for a test: the real journal writer and the real attempt outcomes,
/// over a registry that knows about nothing unless the test registered something in it.
/// </summary>
public static class TestCanceller
{
    public static WorkItemCanceller New(TimeProvider clock, RunningAttemptRegistry? registry = null) =>
        new(
            new JournalWriter(clock),
            clock,
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Dispatcher()),
            registry ?? new RunningAttemptRegistry());
}
