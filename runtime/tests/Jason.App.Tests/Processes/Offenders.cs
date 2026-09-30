using System.Diagnostics;

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
}
