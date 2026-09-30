using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.Processes;

/// <summary>
/// No child this product starts gets a console window of its own: the compiled product, read.
/// </summary>
/// <remarks>
/// <para>
/// What that covers is what <c>CreateNoWindow</c> covers: a console program started without it gets a console
/// window. A graphical program opens its own windows whatever it was started with, and what a child starts in turn
/// is that child's business; neither is claimed here.
/// </para>
/// <para>
/// A source scan was defeated once in this repository by a spelling it did not expect, so this reads what the
/// compiler wrote instead. Every construction, property write and call is a metadata token in some method's IL,
/// whatever the source looked like, and <see cref="IlReferences"/> resolves each one. Reflection, an expression
/// tree and <c>new T()</c> reach a constructor without naming it in IL, and are beyond what this reads.
/// </para>
/// </remarks>
public class NoChildGetsAWindowTests
{
    private static readonly Assembly[] Product =
    [
        typeof(ModeRouter).Assembly,
        typeof(Jason.Cli.CliApp).Assembly,
        typeof(Jason.Runtime.Hosting.RuntimeHost).Assembly,
        typeof(Jason.PluginHost.PluginHostMode).Assembly,
        typeof(ChildProcess).Assembly,
    ];

    /// <summary>
    /// No child gets a window, as a property of what ships. The runtime a logon task starts has no console, so a
    /// child it starts without <c>CreateNoWindow</c> puts a window on the person's desktop for as long as it runs.
    /// Every child therefore comes out of <see cref="ChildProcess.StartInfo"/>, and nothing afterwards turns the
    /// window back on.
    /// </summary>
    [Fact]
    public void Only_the_door_makes_a_start_and_nothing_undoes_what_it_sets()
    {
        var door = typeof(ChildProcess).GetMethod(nameof(ChildProcess.StartInfo))!;
        var offenders = Product
            .SelectMany(IlReferences.Methods)
            .Where(method => method != door)
            .SelectMany(method => Forbidden(method).Select(reason => $"{Name(method)} {reason}"))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "a child could get a window: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// The scan reaches the places the door is really used, which is what makes "found nothing" worth anything. A
    /// scan that read only public types, say, would miss every async method and every lambda — the compiler puts
    /// both in private nested types — and the guard above would stay green over code it never read. So the door's
    /// own callers are found first: in each of the three assemblies that start children, and inside at least one
    /// compiler-written state machine.
    /// </summary>
    [Fact]
    public void The_scan_reaches_every_place_the_door_is_used()
    {
        var callers = Product
            .SelectMany(IlReferences.Methods)
            .Where(method => IlReferences.Called(method).Any(IsTheDoor))
            .ToList();

        Assert.Contains(callers, method => method.Module.Assembly == typeof(Jason.Cli.CliApp).Assembly);
        Assert.Contains(callers, method => method.Module.Assembly == typeof(Jason.Runtime.Hosting.RuntimeHost).Assembly);
        Assert.Contains(callers, method => method.Module.Assembly == typeof(Jason.PluginHost.PluginHostMode).Assembly);
        Assert.Contains(callers, method => method.DeclaringType is { } type && typeof(IAsyncStateMachine).IsAssignableFrom(type));
    }

    [Theory]
    [InlineData(nameof(Offenders.MakesItsOwn), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.MakesItsOwnTargetTyped), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.TurnsTheWindowBackOn), "sets CreateNoWindow")]
    [InlineData(nameof(Offenders.AsksForTheShell), "sets UseShellExecute")]
    [InlineData(nameof(Offenders.StartsAFileName), "starts a process from a file name")]
    [InlineData(nameof(Offenders.ReachesIntoAProcess), "reads a process's StartInfo")]
    [InlineData(nameof(Offenders.InALambda), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.InAnAsyncMethod), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.InAMethodGroup), "starts a process from a file name")]
    [InlineData(nameof(Offenders.InAStaticInitializer), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.InAnIterator), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.InALocalFunction), "makes a ProcessStartInfo")]
    [InlineData(nameof(Offenders.InAGenericMethod), "makes a ProcessStartInfo")]
    public void The_guard_sees_every_way_around_the_door(string method, string reason)
    {
        // A lambda's body, an async method's, an iterator's and a local function's are compiled into methods whose
        // names carry the source method's name (<InALambda>b__…, <InAnAsyncMethod>d__….MoveNext,
        // <InALocalFunction>g__…), and a static initializer is its type's .cctor, which is how a finding is traced
        // back to it. A method group is not a call at all: it is an ldftn, which is why the reader takes every
        // method token rather than only the call instructions.
        var found = IlReferences.Methods(typeof(Offenders))
            .Where(candidate => Name(candidate).Contains(method, StringComparison.Ordinal))
            .SelectMany(Forbidden)
            .ToList();

        Assert.Contains(reason, found);
    }

    private static IEnumerable<string> Forbidden(MethodBase method)
    {
        foreach (var called in IlReferences.Called(method))
        {
            if (called.DeclaringType == typeof(ProcessStartInfo))
            {
                if (called is ConstructorInfo)
                {
                    yield return "makes a ProcessStartInfo";
                }
                else if (called.Name == "set_CreateNoWindow")
                {
                    yield return "sets CreateNoWindow";
                }
                else if (called.Name == "set_UseShellExecute")
                {
                    yield return "sets UseShellExecute";
                }
            }
            else if (called.DeclaringType == typeof(Process))
            {
                if (called.Name == nameof(Process.Start) && called.IsStatic
                    && called.GetParameters() is [{ ParameterType: var first }, ..] && first == typeof(string))
                {
                    yield return "starts a process from a file name";
                }
                else if (called.Name == "get_StartInfo")
                {
                    yield return "reads a process's StartInfo";
                }
            }
        }
    }

    private static bool IsTheDoor(MethodBase called) =>
        called.DeclaringType == typeof(ChildProcess) && called.Name == nameof(ChildProcess.StartInfo);

    private static string Name(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";
}
