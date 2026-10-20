using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.Processes;

/// <summary>
/// No child this product starts gets a console window of its own, or standard streams its caller chose rather than
/// the door: the compiled product, read.
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
    /// What only the door sets. The window and the shell, which no child is given. And the standard streams, which
    /// the door redirects and decodes by what the child is: UTF-8 for a child that speaks to Jason, the console's code
    /// page for a system tool. Left to itself, .NET reads and writes a redirected stream on Windows in the console's
    /// page, and a child started without a window of its own has a console on the system's legacy page; a caller that
    /// redirected a stream itself would be one place more to remember the encoding in, and one such place forgot it.
    /// </summary>
    private static readonly HashSet<string> TheDoorsOwn =
    [
        nameof(ProcessStartInfo.CreateNoWindow),
        nameof(ProcessStartInfo.UseShellExecute),
        nameof(ProcessStartInfo.RedirectStandardInput),
        nameof(ProcessStartInfo.RedirectStandardOutput),
        nameof(ProcessStartInfo.RedirectStandardError),
        nameof(ProcessStartInfo.StandardInputEncoding),
        nameof(ProcessStartInfo.StandardOutputEncoding),
        nameof(ProcessStartInfo.StandardErrorEncoding),
    ];

    /// <summary>
    /// No child gets a window, and none speaks in an encoding its caller chose, as a property of what ships. The
    /// runtime a logon task starts has no console, so a child it starts without <c>CreateNoWindow</c> puts a window
    /// on the person's desktop for as long as it runs. Every child therefore comes out of <see cref="ChildProcess"/>,
    /// and nothing afterwards turns the window back on or picks its streams again.
    /// </summary>
    [Fact]
    public void Only_the_door_makes_a_start_and_nothing_undoes_what_it_sets()
    {
        var offenders = Product
            .SelectMany(IlReferences.Methods)
            .Where(method => method.DeclaringType != typeof(ChildProcess))
            .SelectMany(method => Forbidden(method).Select(reason => $"{Name(method)} {reason}"))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "a child could get a window or streams the door did not choose: " + string.Join("; ", offenders));
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
    [InlineData(nameof(Offenders.RedirectsInput), "sets RedirectStandardInput")]
    [InlineData(nameof(Offenders.RedirectsOutput), "sets RedirectStandardOutput")]
    [InlineData(nameof(Offenders.RedirectsError), "sets RedirectStandardError")]
    [InlineData(nameof(Offenders.PicksAnInputEncoding), "sets StandardInputEncoding")]
    [InlineData(nameof(Offenders.PicksAnOutputEncoding), "sets StandardOutputEncoding")]
    [InlineData(nameof(Offenders.PicksAnErrorEncoding), "sets StandardErrorEncoding")]
    [InlineData(nameof(Offenders.RedirectsInALambda), "sets RedirectStandardOutput")]
    [InlineData(nameof(Offenders.RedirectsInAnAsyncMethod), "sets StandardOutputEncoding")]
    [InlineData(nameof(Offenders.RedirectsInAnIterator), "sets RedirectStandardInput")]
    [InlineData(nameof(Offenders.RedirectsInALocalFunction), "sets StandardInputEncoding")]
    [InlineData(nameof(Offenders.RedirectsInAGenericMethod), "sets RedirectStandardError")]
    [InlineData(nameof(Offenders.RedirectsInAStaticInitializer), "sets RedirectStandardOutput")]
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
                else if (called.Name.StartsWith("set_", StringComparison.Ordinal) && TheDoorsOwn.Contains(called.Name[4..]))
                {
                    yield return $"sets {called.Name[4..]}";
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
