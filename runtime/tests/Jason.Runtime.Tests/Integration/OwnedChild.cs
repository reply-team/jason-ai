using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The rule for ending a process from a test: a test ends only a child it can prove is its own. A child the test
/// started itself is ended through the <see cref="Process"/> it got from the start. A child somebody else started —
/// the runtime under test, or a program that runtime started — is opened by the pid that was written down, kept as
/// a <see cref="Held"/> with the start time it had when it was opened, and ended through that, only if the process
/// that answers to the pid is the one that was written down.
/// </summary>
/// <remarks>
/// <para>
/// What proves it is the start time. A later holder of a pid always started later than the child that had it,
/// because the pid was the child's for as long as the child lived. Where the record is a moment taken around the
/// start, the process must have started no later than that moment plus <see cref="StartAllowance"/> (a symmetric
/// window would refuse the real child, whose record is taken after it began). Where the child said when it started
/// itself, the two readings are one moment read twice, so the process must have started within
/// <see cref="StartAllowance"/> of it either way: on Linux a start time is derived from the boot time at one-second
/// precision, which is why that cannot be equality. A name is a belt and never the proof: it says only that a
/// process is a .NET program.
/// </para>
/// <para>
/// A held process is read again before it is ended, and ended only if it still started when it did at the opening.
/// On Windows an open handle keeps the pid from being given to somebody else; on Linux and macOS a process taken
/// by its number pins nothing, and a kill is a signal to the number, so the second reading is what covers a number
/// reused while the handle was held. A process that cannot be read — this account may not open it — is treated as
/// gone or not ours. Nothing here ever looks a process up by name alone.
/// </para>
/// </remarks>
internal static class OwnedChild
{
    /// <summary>How far apart two readings of one start may be, or how far after a record a process may be read to have started.</summary>
    public static readonly TimeSpan StartAllowance = TimeSpan.FromSeconds(2);

    /// <summary>The most characters of a process name that every platform keeps: Linux keeps fifteen.</summary>
    private const int NameKept = 15;

    /// <summary>A process opened by its pid, with the start time it had then, to be ended only while it still has it.</summary>
    public sealed class Held : IDisposable
    {
        public Held(Process process, DateTimeOffset started)
        {
            ArgumentNullException.ThrowIfNull(process);
            Process = process;
            Started = started;
        }

        public Process Process { get; }

        /// <summary>The start time the process had when it was opened.</summary>
        public DateTimeOffset Started { get; }

        public int Id => Process.Id;

        public void Dispose() => Process.Dispose();
    }

    /// <summary>
    /// The process with that id if it is proven to be the child recorded, and null if it is gone or is somebody
    /// else's. A running process that is refused is reported, so a leak is never silent.
    /// </summary>
    /// <param name="pid">The id that was written down.</param>
    /// <param name="recordedStart">A moment, taken around the child's start, that the child had started by.</param>
    /// <param name="names">The process names the child may have; empty when the name proves nothing.</param>
    public static Held? Open(int pid, DateTimeOffset recordedStart, params string[] names) =>
        OpenCore(pid, recordedStart, bothWays: false, names);

    /// <summary>
    /// Like <see cref="Open"/> for a child that said when it started itself: the process must have started within
    /// <see cref="StartAllowance"/> of that moment, before it or after it.
    /// </summary>
    public static Held? OpenKnown(int pid, DateTimeOffset childStart, params string[] names) =>
        OpenCore(pid, childStart, bothWays: true, names);

    /// <summary>Whether a process name read from the system is the one that was written down; Linux keeps only the first fifteen characters.</summary>
    public static bool NameFits(string seen, string name)
    {
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(name);
        return seen.Length >= Math.Min(name.Length, NameKept) && name.StartsWith(seen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ends a child the test started itself, through the <see cref="Process"/> it got from the start.</summary>
    public static void End(Process process, bool tree = true)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: tree);
            }
        }
        catch (InvalidOperationException)
        {
            // Gone already, which is the state this is trying to reach.
        }
        catch (Win32Exception)
        {
            // Gone in the meantime, or not a process this account can end.
        }
    }

    /// <summary>Ends a held process if it still started when it did at the opening, and leaves it alone if it changed hands.</summary>
    public static void End(Held held, bool tree = true)
    {
        ArgumentNullException.ThrowIfNull(held);
        var process = held.Process;
        try
        {
            if (process.HasExited)
            {
                return;
            }

            // A fresh reading by the number: the held object may have kept the time it read, and a number reused
            // while it was held is what this exists to see.
            using var now = Process.GetProcessById(process.Id);
            var started = StartOf(now);
            if ((started - held.Started).Duration() > StartAllowance)
            {
                Say(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Left alone: process {process.Id} changed hands; it started {started:O} and was opened when it had started {held.Started:O} (allowance {StartAllowance.TotalSeconds:0} s)."));
                return;
            }

            process.Kill(entireProcessTree: tree);
        }
        catch (ArgumentException)
        {
            // Gone already, which is the state this is trying to reach.
        }
        catch (InvalidOperationException)
        {
            // Gone already, which is the state this is trying to reach.
        }
        catch (Win32Exception)
        {
            // Gone in the meantime, or not a process this account can end.
        }
    }

    /// <summary>Ends the process with that id if, and only if, it is proven to be the child recorded.</summary>
    public static void End(int pid, DateTimeOffset recordedStart, params string[] names)
    {
        using var held = Open(pid, recordedStart, names);
        if (held is not null)
        {
            End(held);
        }
    }

    /// <summary>Ends the process with that id if, and only if, it started when the child said it did.</summary>
    public static void EndKnown(int pid, DateTimeOffset childStart, params string[] names)
    {
        using var held = OpenKnown(pid, childStart, names);
        if (held is not null)
        {
            End(held);
        }
    }

    /// <summary>
    /// Whether the process with that id is running. A process this account cannot read is not ours to ask about,
    /// so it reads as not running: a caller waiting for a child to be gone is answered "gone" for one it cannot see.
    /// </summary>
    public static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static Held? OpenCore(int pid, DateTimeOffset recorded, bool bothWays, string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }

        DateTimeOffset started;
        string seen;
        try
        {
            if (process.HasExited)
            {
                process.Dispose();
                return null;
            }

            started = StartOf(process);
            seen = process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            process.Dispose();
            return null;
        }
        catch (Win32Exception ex)
        {
            Say($"Left alone: process {pid} could not be read ({ex.Message}); recorded start {recorded:O}.");
            process.Dispose();
            return null;
        }

        var nameFits = names.Length == 0 || names.Any(name => NameFits(seen, name));
        var timeFits = bothWays ? (started - recorded).Duration() <= StartAllowance : started <= recorded + StartAllowance;
        if (timeFits && nameFits)
        {
            return new Held(process, started);
        }

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"Left alone: process {pid} is named '{seen}' and started {started:O}, but the child recorded was one of [{string.Join(", ", names)}] and started {(bothWays ? "at" : "by")} {recorded:O} (allowance {StartAllowance.TotalSeconds:0} s)."));
        process.Dispose();
        return null;
    }

    private static DateTimeOffset StartOf(Process process) => new(process.StartTime.ToUniversalTime(), TimeSpan.Zero);

    private static void Say(string line)
    {
        if (TestContext.Current.TestOutputHelper is { } output)
        {
            output.WriteLine(line);
        }
        else
        {
            Console.Error.WriteLine(line);
        }
    }
}
