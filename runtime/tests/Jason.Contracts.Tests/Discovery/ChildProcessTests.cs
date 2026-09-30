using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// The one door every child process comes out of. What it sets is the whole of its job, and
/// <c>NoChildGetsAWindowTests</c> in the application's tests holds the rest of the product to it.
/// </summary>
public class ChildProcessTests
{
    [Fact]
    public void A_child_is_started_with_no_window_and_without_the_shell()
    {
        var start = ChildProcess.StartInfo("program");

        Assert.Equal("program", start.FileName);
        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
    }
}
