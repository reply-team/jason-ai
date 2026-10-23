using System.Diagnostics;
using System.Text;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.Processes;

/// <summary>
/// Every way around the door this guard knows of, compiled, so the guard is shown to see each one. Nothing calls
/// these: they exist to be read.
/// </summary>
internal static class Offenders
{
    public static ProcessStartInfo MakesItsOwn() => new ProcessStartInfo("x");

    public static ProcessStartInfo MakesItsOwnTargetTyped()
    {
        ProcessStartInfo start = new() { FileName = "x" };
        return start;
    }

    public static void TurnsTheWindowBackOn(ProcessStartInfo start) => start.CreateNoWindow = false;

    public static void AsksForTheShell(ProcessStartInfo start) => start.UseShellExecute = true;

    public static Process? StartsAFileName() => Process.Start("x");

    // A start the door made, started somewhere else: whatever the door decides a start is made under would not hold.
    public static Process? StartsAStartOfItsOwn(ProcessStartInfo start) => Process.Start(start);

    public static bool StartsAProcessOfItsOwn(Process process) => process.Start();

    public static Func<ProcessStartInfo, Process?> StartsThroughAMethodGroup() => Process.Start;

    public static ProcessStartInfo ReachesIntoAProcess(Process process) => process.StartInfo;

    public static Func<ProcessStartInfo> InALambda() => () => new ProcessStartInfo("x");

    public static async Task<ProcessStartInfo> InAnAsyncMethod()
    {
        await Task.Yield();
        return new ProcessStartInfo("x");
    }

    public static Func<string, Process?> InAMethodGroup() => Process.Start;

    public static IEnumerable<ProcessStartInfo> InAnIterator()
    {
        yield return new ProcessStartInfo("x");
    }

    public static ProcessStartInfo InALocalFunction()
    {
        return Make();

        static ProcessStartInfo Make() => new("x");
    }

    public static ProcessStartInfo InAGenericMethod<T>(T name) => new(name?.ToString() ?? "x");

    public static class InAStaticInitializer
    {
        public static readonly ProcessStartInfo Start = new("x");
    }

    // The streams. Each of these takes a start from the door and then chooses a redirect or an encoding itself, which
    // is the choice the door makes by what the child is.
    public static void RedirectsInput(ProcessStartInfo start) => start.RedirectStandardInput = true;

    public static void RedirectsOutput(ProcessStartInfo start) => start.RedirectStandardOutput = true;

    public static void RedirectsError(ProcessStartInfo start) => start.RedirectStandardError = true;

    public static void PicksAnInputEncoding(ProcessStartInfo start) => start.StandardInputEncoding = Encoding.UTF8;

    public static void PicksAnOutputEncoding(ProcessStartInfo start) => start.StandardOutputEncoding = Encoding.UTF8;

    public static void PicksAnErrorEncoding(ProcessStartInfo start) => start.StandardErrorEncoding = Encoding.UTF8;

    public static Action<ProcessStartInfo> RedirectsInALambda() => start => start.RedirectStandardOutput = true;

    public static async Task RedirectsInAnAsyncMethod(ProcessStartInfo start)
    {
        await Task.Yield();
        start.StandardOutputEncoding = Encoding.UTF8;
    }

    public static IEnumerable<ProcessStartInfo> RedirectsInAnIterator()
    {
        var start = ChildProcess.StartInfo("x");
        start.RedirectStandardInput = true;
        yield return start;
    }

    public static ProcessStartInfo RedirectsInALocalFunction()
    {
        return Redirect(ChildProcess.StartInfo("x"));

        static ProcessStartInfo Redirect(ProcessStartInfo start)
        {
            start.StandardInputEncoding = Encoding.UTF8;
            return start;
        }
    }

    public static void RedirectsInAGenericMethod<T>(T name) => ChildProcess.StartInfo(name?.ToString() ?? "x").RedirectStandardError = true;

    public static class RedirectsInAStaticInitializer
    {
        public static readonly bool Redirected = ChildProcess.StartInfo("x").RedirectStandardOutput = true;
    }
}
