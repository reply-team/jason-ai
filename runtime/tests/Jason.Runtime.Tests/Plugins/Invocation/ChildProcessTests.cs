using System.Diagnostics;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The wait that follows a kill. A kill is a request rather than a guarantee — a platform that cannot end a
/// whole tree, or a process the operating system will not let go of, leaves something running — so the wait for
/// a child to be gone is given the same grace as the kill itself and answers what it found.
/// </summary>
public class ChildProcessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_child_nothing_managed_to_end_is_waited_for_no_longer_than_its_grace()
    {
        using var process = Start("hold", "30000");
        try
        {
            var wait = ChildProcess.EndedWithinAsync(process, 500);
            var answered = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(20), Ct)) == wait;

            Assert.True(answered, "the wait for a child that nothing ended never came back");
            Assert.False(await wait);
            Assert.False(process.HasExited);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(30_000);
        }
    }

    [Fact]
    public async Task A_child_that_is_gone_is_waited_for_and_no_longer()
    {
        using var process = Start("hold", "0");

        Assert.True(await ChildProcess.EndedWithinAsync(process, 30_000));
        Assert.True(process.HasExited);
    }

    /// <summary>The stand-in vendor program, holding the handles it was given and writing nothing at all.</summary>
    private static Process Start(params string[] behaviour)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(FakeProviderCli.Dll);
        foreach (var argument in behaviour)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)!;
    }
}
