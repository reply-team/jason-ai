using System.ComponentModel;
using Jason.Cli.Process;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Tests.Process;

/// <summary>
/// "Is that pid still alive" asked about a process this prompt may not open. It is not a corner: a runtime
/// registered to start at logon runs in another logon session, and from an ordinary prompt .NET answers the
/// question with an exception rather than with a bool.
/// </summary>
/// <remarks>
/// Found by the hand check. `jason runtime stop` against a runtime the logon task had started shut it down
/// and then reported <c>Access is denied.</c> with exit 1: the process really was gone, and the verb said it
/// had failed. Measured on that machine: <c>GetProcessById</c> returns for such a pid — so the pid is in the
/// process table — and <c>HasExited</c> throws <see cref="Win32Exception"/>, which
/// <see cref="RuntimeProcessControl.IsRunning"/> did not catch, so it left the verb altogether.
/// </remarks>
public class RuntimeLivenessTests
{
    /// <summary>A pid that is certainly not in the process table reads as gone, which is the ordinary answer.</summary>
    [Fact]
    public void A_pid_that_is_not_there_is_not_running() =>
        Assert.False(RuntimeProcessControl.Instance.IsRunning(Nowhere()));

    /// <summary>And this very process, which can be opened, reads as running.</summary>
    [Fact]
    public void A_process_that_can_be_opened_reads_as_running() =>
        Assert.True(RuntimeProcessControl.Instance.IsRunning(Environment.ProcessId));

    /// <summary>
    /// The one this is about. A process that is there and cannot be opened is <b>running</b>: the pid is in
    /// the table, which is what <c>GetProcessById</c> returning says, and that is the answer even though the
    /// question could not be put to the process itself.
    /// </summary>
    /// <remarks>
    /// Answering "gone" instead would be worse than throwing: `stop` would report success for a runtime that
    /// is still serving, and a script would start the next one on top of it.
    /// </remarks>
    [Fact]
    public void A_process_this_prompt_may_not_open_is_running_rather_than_gone()
    {
        var closed = Unopenable();
        Assert.SkipWhen(closed is null, "Every process on this machine can be opened by this test, so there is nothing here that could refuse.");

        Assert.True(RuntimeProcessControl.Instance.IsRunning(closed!.Value));
    }

    /// <summary>
    /// A pid in the process table whose <c>HasExited</c> refuses, found rather than assumed. On Windows the
    /// System process is the reliable one — pid 4, which nothing may open — but it is looked for instead of
    /// being named, so this test proves what it says on a machine where that is not true.
    /// </summary>
    private static int? Unopenable()
    {
        foreach (var process in OperatingSystemProcess.GetProcesses())
        {
            using (process)
            {
                try
                {
                    _ = process.HasExited;
                }
                catch (Win32Exception)
                {
                    return process.Id;
                }
                catch (InvalidOperationException)
                {
                    // It left between the listing and the question, which is not what this test is looking for.
                }
            }
        }

        return null;
    }

    /// <summary>A pid nothing is using, found by asking rather than by picking a big number and hoping.</summary>
    private static int Nowhere()
    {
        var taken = OperatingSystemProcess.GetProcesses().Select(process => process.Id).ToHashSet();
        for (var candidate = 999_999; candidate > 1; candidate--)
        {
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Every pid on this machine is taken, which cannot be.");
    }
}
