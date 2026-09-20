using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Jason.Contracts.Discovery;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Process;

/// <summary>A child process, as much of one as the CLI ever needs to know.</summary>
public interface IProcessHandle : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int ExitCode { get; }
}

/// <summary>
/// The only place the CLI touches the operating system's process table. Behind an interface because the tests
/// must never spawn a runtime, and because "is that pid still alive" is the one question whose answer differs
/// per platform.
/// </summary>
public interface IRuntimeProcessControl
{
    /// <summary>
    /// Starts an executable as <c>runtime run --detached</c> against the given data directory. All three
    /// standard streams are redirected, so the child inherits no console handle; the child then puts its own
    /// descriptors on the null device and the pipes below are never written to again.
    /// </summary>
    /// <param name="executable">
    /// What to run, as a command: the program and whatever has to come before the mode word. The caller names
    /// it rather than the seam composing it, because the two callers do not want the same program — an ordinary
    /// <c>jason runtime start</c> wants another copy of itself, and an applier running from a copy of itself
    /// under the data directory wants the executable it has just installed. A seam that resolved this on its own
    /// would have the applier start the old build from the wrong path, whereupon the health check would read the
    /// old version and roll back an update that had in fact succeeded.
    /// </param>
    IProcessHandle Launch(JasonPaths paths, IReadOnlyList<string> executable);

    bool IsRunning(int pid);
}

/// <inheritdoc />
public sealed class RuntimeProcessControl : IRuntimeProcessControl
{
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private const int HandleFlagInherit = 0x00000001;

    private static readonly IntPtr InvalidHandle = new(-1);

    public static RuntimeProcessControl Instance { get; } = new();

    /// <summary>
    /// What to run to get another copy of this program, in the detached runtime mode. Which program that is —
    /// the published executable, or the muxer with this build's entry assembly named again — is
    /// <see cref="SelfExecutable"/>'s question, and the plugin host is started from the same answer.
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Arguments) ResolveSelf() => Resolve(SelfExecutable.Command);

    /// <summary>The same composition for any executable: the program, then what it needs, then the mode word.</summary>
    public static (string FileName, IReadOnlyList<string> Arguments) Resolve(IReadOnlyList<string> executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (executable.Count == 0)
        {
            throw new ArgumentException("A runtime cannot be started from an empty command.", nameof(executable));
        }

        return (executable[0], [.. executable.Skip(1), "runtime", "run", "--detached"]);
    }

    public IProcessHandle Launch(JasonPaths paths, IReadOnlyList<string> executable)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var (fileName, arguments) = Resolve(executable);
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The child must read the same data directory this CLI is talking about, whether it came from the
        // environment or from a default.
        startInfo.Environment[JasonPaths.DataDirectoryVariable] = paths.Root;

        KeepOwnStandardStreamsOutOfTheChild();

        var process = OperatingSystemProcess.Start(startInfo)
            ?? throw new IOException($"The runtime executable '{fileName}' could not be started.");

        // Standard input is closed rather than left open: a service process has nothing to read from anyone.
        process.StandardInput.Close();

        // The child redirects itself to the null device within milliseconds, but a runtime that fails before
        // that point still writes to these pipes, and a full pipe would block it forever.
        process.OutputDataReceived += Discard;
        process.ErrorDataReceived += Discard;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new ProcessHandle(process);
    }

    /// <summary>
    /// Whether that pid is still in the process table. Three answers are possible from the operating system
    /// and only two of them are a bool, which is what the last clause is about.
    /// </summary>
    /// <remarks>
    /// A runtime registered to start at logon runs in <b>another logon session</b> — session 0, with an S4U
    /// logon on Windows — and an ordinary prompt may not open such a process to ask it anything.
    /// <c>GetProcessById</c> still returns, because finding a pid in the table needs no handle;
    /// <c>HasExited</c> then throws <see cref="Win32Exception"/> rather than answering. Measured on a real
    /// machine, and before it was caught it left `jason runtime stop` altogether: the runtime shut down, the
    /// verb printed <c>Access is denied.</c> and exited 1, and the shutdown it had just completed looked like
    /// a failure.
    /// <para>
    /// The answer in that case is <b>running</b>, and not because it is the safer-sounding one: the pid is in
    /// the table, which is precisely what <c>GetProcessById</c> returning says. Saying "gone" would let `stop`
    /// report success over a runtime that is still serving, and a script would start the next one on top of it.
    /// When the process really does leave, <c>GetProcessById</c> says so by throwing, and the clause above
    /// answers.
    /// </para>
    /// </remarks>
    public bool IsRunning(int pid)
    {
        try
        {
            using var process = OperatingSystemProcess.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No process with that id: on every platform this is how "it is gone" reads.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // It is there, and this prompt may not ask it anything. See the remarks above.
            return true;
        }
    }

    private static void Discard(object sender, DataReceivedEventArgs e)
    {
        // The point is the reading, not the data.
    }

    /// <summary>
    /// A new process on Windows is handed every handle its parent left inheritable, whether or not it is told
    /// to use it. When the CLI itself runs under a redirect — a shell capturing its output, a build step, a
    /// test — its own standard streams are such handles, so a runtime meant to outlive the CLI would hold them
    /// open for as long as it runs and the caller would wait for an end of file that never comes. The CLI has
    /// no child that should ever speak through its streams, so they stop being inheritable before one starts.
    /// Unix needs none of this: the child's descriptors 0, 1 and 2 are replaced outright and everything else
    /// the runtime might have inherited is closed on exec.
    /// </summary>
    private static void KeepOwnStandardStreamsOutOfTheChild()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var standardStream in new[] { StandardInputHandle, StandardOutputHandle, StandardErrorHandle })
        {
            var handle = GetStdHandle(standardStream);
            if (handle != IntPtr.Zero && handle != InvalidHandle)
            {
                // A failure here costs the caller nothing but the wait this avoids; there is nothing to report
                // it to, and the launch itself is unaffected.
                SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, int mask, int flags);

    private sealed class ProcessHandle(OperatingSystemProcess process) : IProcessHandle
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public int ExitCode => process.ExitCode;

        public void Dispose() => process.Dispose();
    }
}
