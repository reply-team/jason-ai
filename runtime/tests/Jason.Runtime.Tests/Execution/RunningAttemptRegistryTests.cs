using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Execution;

public class RunningAttemptRegistryTests
{
    [Fact]
    public void A_registered_attempt_is_counted_until_its_registration_is_disposed()
    {
        var registry = new RunningAttemptRegistry();
        Assert.Equal(0, registry.Count);

        var registration = registry.Register("att_A", () => { });

        Assert.Equal(1, registry.Count);
        Assert.Equal(["att_A"], registry.AttemptIds);

        registration.Dispose();

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.AttemptIds);
    }

    [Fact]
    public void A_kill_runs_once_however_often_it_is_asked_for()
    {
        var registry = new RunningAttemptRegistry();
        var kills = 0;
        using var registration = registry.Register("att_A", () => kills++);

        Assert.True(registry.TryKill("att_A"));
        Assert.True(registry.TryKill("att_A"));

        Assert.Equal(1, kills);
    }

    [Fact]
    public void An_attempt_nobody_here_is_running_cannot_be_killed()
    {
        var registry = new RunningAttemptRegistry();
        var kills = 0;
        var registration = registry.Register("att_A", () => kills++);

        Assert.False(registry.TryKill("att_OTHER"));

        registration.Dispose();

        Assert.False(registry.TryKill("att_A"));
        Assert.Equal(0, kills);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var registry = new RunningAttemptRegistry();
        var registration = registry.Register("att_A", () => { });
        registration.Dispose();

        using var other = registry.Register("att_B", () => { });
        registration.Dispose();

        Assert.Equal(["att_B"], registry.AttemptIds);
    }

    [Fact]
    public async Task Many_threads_registering_and_killing_leave_a_consistent_count()
    {
        var registry = new RunningAttemptRegistry();
        var kills = 0;
        var registrations = new IDisposable[64];

        await Parallel.ForAsync(0, 64, TestContext.Current.CancellationToken, (index, _) =>
        {
            registrations[index] = registry.Register($"att_{index}", () => Interlocked.Increment(ref kills));
            return ValueTask.CompletedTask;
        });

        Assert.Equal(64, registry.Count);
        Assert.All(registry.AttemptIds, id => Assert.True(registry.TryKill(id)));
        Assert.Equal(64, kills);

        foreach (var registration in registrations)
        {
            registration.Dispose();
        }

        Assert.Equal(0, registry.Count);
    }
}
