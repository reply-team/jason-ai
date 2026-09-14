using System.Diagnostics;
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
    /// Starts this same executable as <c>runtime run --detached</c> against the given data directory. All three
    /// standard streams are redirected, so the child inherits no console handle; the child then puts its own
    /// descriptors on the null device and the pipes below are never written to again.
    /// </summary>
    IProcessHandle Launch(JasonPaths paths);

    bool IsRunning(int pid);
}

/// <inheritdoc />
public sealed class RuntimeProcessControl : IRuntimeProcessControl
{
    private const string DotnetHost = "dotnet";

    public static RuntimeProcessControl Instance { get; } = new();

    /// <summary>
    /// What to run to get another copy of this program. A published executable runs itself; a build started as
    /// <c>dotnet jason.dll</c> has <c>dotnet</c> for its process path, and the entry assembly has to be named
    /// again for the child to be this same build rather than whatever <c>jason</c> is on the PATH.
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Arguments) ResolveSelf()
    {
        var executable = Environment.ProcessPath;
        if (executable is null || string.Equals(Path.GetFileNameWithoutExtension(executable), DotnetHost, StringComparison.OrdinalIgnoreCase))
        {
            return (DotnetHost, [Environment.GetCommandLineArgs()[0], "runtime", "run", "--detached"]);
        }

        return (executable, ["runtime", "run", "--detached"]);
    }

    public IProcessHandle Launch(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var (fileName, arguments) = ResolveSelf();
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
    }

    private static void Discard(object sender, DataReceivedEventArgs e)
    {
        // The point is the reading, not the data.
    }

    private sealed class ProcessHandle(OperatingSystemProcess process) : IProcessHandle
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public int ExitCode => process.ExitCode;

        public void Dispose() => process.Dispose();
    }
}
