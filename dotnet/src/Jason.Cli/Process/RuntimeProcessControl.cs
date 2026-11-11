using System.ComponentModel;
using System.Diagnostics;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Update;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Process;

/// <summary>A child process, as much of one as the CLI ever needs to know.</summary>
public interface IProcessHandle : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int ExitCode { get; }

    /// <summary>
    /// What the child wrote to its standard error before it let go of it, bounded — for a runtime that will not
    /// start, the reason. Empty for a child that said nothing.
    /// </summary>
    string Said { get; }
}

/// <summary>What a command run to its end did: its exit code, and everything it wrote on standard output.</summary>
public sealed record FinishedCommand(int ExitCode, string StandardOutput);

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
    /// it rather than the seam composing it, because the callers do not want the same program — an ordinary
    /// <c>jason runtime start</c> wants another copy of itself, and an update wants the executable it has just
    /// installed, which is not this process's own file when a person resumed the update from the copy of the
    /// applier under the data directory. A seam that resolved this on its own would have that update start the old
    /// build, whereupon the health check would read the old version and fail an update that had in fact succeeded.
    /// </param>
    /// <param name="updateLaunch">
    /// The update's mark, for a launch the update makes itself, and null for every other launch. From the step that moves
    /// the installed build aside on, a runtime starts only under the mark, so the one an update starts must carry it and
    /// one a person or a logon starts must not (<see cref="UpdateLaunch"/>).
    /// </param>
    IProcessHandle Launch(JasonPaths paths, IReadOnlyList<string> executable, string? updateLaunch);

    /// <summary>
    /// Runs a command of this product to its end against the given data directory and hands back its exit code and what
    /// it wrote on standard output. What it writes on standard error is passed to <paramref name="error"/> as it comes.
    /// </summary>
    /// <remarks>
    /// How an update hands the rest of itself to the executable it has just put in place. A cancellation ends the child
    /// and nothing it started: what it started is a runtime, which is meant to outlive it.
    /// </remarks>
    Task<FinishedCommand> RunAsync(JasonPaths paths, IReadOnlyList<string> command, TextWriter error, CancellationToken cancellationToken);

    bool IsRunning(int pid);

    /// <summary>
    /// When the process with that id started, or null where this account may not ask — or it has gone.
    /// </summary>
    /// <remarks>
    /// A pid names a process only while that process lives. After a restart the id a descriptor recorded belongs
    /// to whatever the machine started next under it, and "is that pid running" answers yes about a stranger; the
    /// start time is what tells the two apart.
    /// </remarks>
    DateTimeOffset? StartTime(int pid);
}

/// <inheritdoc />
public sealed class RuntimeProcessControl : IRuntimeProcessControl
{
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

    /// <summary>
    /// What <see cref="Launch"/> hands the operating system: the command, the data directory, and the update's mark only
    /// where it is the update's launch.
    /// </summary>
    public static ProcessStartInfo LaunchStart(JasonPaths paths, IReadOnlyList<string> executable, string? updateLaunch)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var (fileName, arguments) = Resolve(executable);
        // The runtime speaks to Jason: the one sentence it may say before it lets go of its streams is written as UTF-8,
        // and read as UTF-8 here whatever this console's code page.
        var startInfo = ChildProcess.StartInfo(fileName, ChildKind.SpeaksToJason);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The child must read the same data directory this CLI is talking about, whether it came from the
        // environment or from a default.
        startInfo.Environment[JasonPaths.DataDirectoryVariable] = paths.Root;

        // The mark is set here and nowhere else, in the environment and never on the command line; a launch that is not
        // the update's carries none, even where this process was handed one.
        if (UpdateLaunch.Marks(updateLaunch))
        {
            startInfo.Environment[UpdateLaunch.Variable] = updateLaunch;
        }
        else
        {
            startInfo.Environment.Remove(UpdateLaunch.Variable);
        }

        // The confinement is the call's that started this runtime, never the runtime's: a runtime lives on past that call.
        startInfo.Environment.Remove(Confinement.Variable);

        return startInfo;
    }

    public IProcessHandle Launch(JasonPaths paths, IReadOnlyList<string> executable, string? updateLaunch)
    {
        var startInfo = LaunchStart(paths, executable, updateLaunch);
        var fileName = startInfo.FileName;

        StandardStreams.KeepOutOfChildren();

        var process = ChildProcess.Start(startInfo)
            ?? throw new IOException($"The runtime executable '{fileName}' could not be started.");

        // Standard input is closed rather than left open: a service process has nothing to read from anyone.
        process.StandardInput.Close();

        // The child redirects itself to the null device within milliseconds, but a runtime that fails before
        // that point still writes to these pipes, and a full pipe would block it forever. So both are read to
        // the end -- and what arrives on standard error is kept, bounded: a runtime that refuses to start says
        // why there, and that sentence was read and thrown away while `jason runtime start` pointed at an
        // empty log directory.
        var said = new StandardErrorLines();
        process.OutputDataReceived += Discard;
        process.ErrorDataReceived += (_, line) => said.Add(line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new ProcessHandle(process, said);
    }

    /// <summary>
    /// What <see cref="RunAsync"/> hands the operating system: the command and the data directory, and never the update's
    /// mark or the confinement of the call that runs it, since the command starts a runtime.
    /// </summary>
    public static ProcessStartInfo RunStart(JasonPaths paths, IReadOnlyList<string> command)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
        {
            throw new ArgumentException("A command cannot be run from nothing.", nameof(command));
        }

        // The CLI writes what any console program writes, in the console's page, so it is read as a system tool is read.
        // Its standard input is its own and closed at once: it asks nobody anything.
        var startInfo = ChildProcess.StartInfo(command[0], ChildKind.SystemToolWithInput);
        foreach (var argument in command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[JasonPaths.DataDirectoryVariable] = paths.Root;
        startInfo.Environment.Remove(UpdateLaunch.Variable);
        startInfo.Environment.Remove(Confinement.Variable);
        return startInfo;
    }

    public async Task<FinishedCommand> RunAsync(JasonPaths paths, IReadOnlyList<string> command, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        var startInfo = RunStart(paths, command);

        // The child starts a runtime, which outlives it: neither may hold the streams of whoever is reading this process.
        StandardStreams.KeepOutOfChildren();

        var output = new System.Text.StringBuilder();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new OperatingSystemProcess { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(line.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null)
            {
                lock (error)
                {
                    error.WriteLine(line.Data);
                }
            }
        };

        ChildProcess.Start(process);
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It has gone already, which is all the kill was for.
            }

            throw;
        }

        // The lines still in flight to the reader are waited for, and not for long: a grandchild holding the pipe would
        // otherwise hold this answer too.
        try
        {
            await Task.Run(process.WaitForExit, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // What arrived is what there is.
        }

        lock (output)
        {
            return new FinishedCommand(process.ExitCode, output.ToString());
        }
    }

    /// <summary>
    /// Whether that pid is still in the process table. Three answers are possible from the operating system
    /// and only two of them are a bool, which is what the last clause is about.
    /// </summary>
    /// <remarks>
    /// A runtime <b>a 0.1.0 installation</b> registered to start at logon runs in <b>another logon session</b> —
    /// session 0, with an S4U logon on Windows — and an ordinary prompt may not open such a process to ask it
    /// anything. <c>GetProcessById</c> still returns, because finding a pid in the table needs no handle;
    /// <c>HasExited</c> then throws <see cref="Win32Exception"/> rather than answering. Measured on a real
    /// machine, and before it was caught it left `jason runtime stop` altogether: the runtime shut down, the
    /// verb printed <c>Access is denied.</c> and exited 1, and the shutdown it had just completed looked like
    /// a failure. A runtime this version registers runs in the account's own session and answers like any
    /// other; the clause stays for the registrations 0.1.0 left behind.
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

    public DateTimeOffset? StartTime(int pid)
    {
        try
        {
            using var process = OperatingSystemProcess.GetProcessById(pid);

            // Through universal time, which keeps the hidden mark a local time carries in the hour a clock goes back
            // and so says the one instant it was.
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Gone, or in a session this prompt may not open -- a runtime a 0.1.0 logon registration started, on Windows.
            return null;
        }
    }

    private static void Discard(object sender, DataReceivedEventArgs e)
    {
        // The point is the reading, not the data.
    }

    private sealed class ProcessHandle(OperatingSystemProcess process, StandardErrorLines said) : IProcessHandle
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public int ExitCode => process.ExitCode;

        public string Said
        {
            get
            {
                // A child that has exited may still have lines in flight to the reader; they are waited for, and
                // not for long, because a grandchild holding the pipe would otherwise hold this answer too.
                if (process.HasExited)
                {
                    Task.Run(process.WaitForExit).Wait(TimeSpan.FromSeconds(2));
                }

                return said.Text;
            }
        }

        public void Dispose() => process.Dispose();
    }

    /// <summary>Lines from a child's standard error, kept up to a bound and no further.</summary>
    private sealed class StandardErrorLines
    {
        private const int Bound = 4096;

        private readonly System.Text.StringBuilder _text = new();

        private readonly Lock _gate = new();

        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return _text.ToString().Trim();
                }
            }
        }

        public void Add(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                if (_text.Length < Bound)
                {
                    _text.Append(line.Length > Bound - _text.Length ? line[..(Bound - _text.Length)] : line).Append('\n');
                }
            }
        }
    }
}
