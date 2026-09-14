using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

public class InstanceLockTests
{
    [Fact]
    public void Only_one_holder_at_a_time()
    {
        using var dir = new TempDataDir();
        using var first = InstanceLock.Acquire(dir.Paths);

        var error = Assert.Throws<RuntimeAlreadyRunningException>(() => InstanceLock.Acquire(dir.Paths));

        Assert.Contains(dir.Paths.LockFile, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Releasing_lets_the_next_runtime_start()
    {
        using var dir = new TempDataDir();
        var first = InstanceLock.Acquire(dir.Paths);
        first.Dispose();

        using var second = InstanceLock.Acquire(dir.Paths);

        Assert.NotNull(second);
    }
}
