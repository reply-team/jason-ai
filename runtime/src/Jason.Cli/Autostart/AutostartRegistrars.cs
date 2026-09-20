using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Autostart;

/// <summary>What a machine answered when it was asked whether anything is registered.</summary>
/// <param name="Registered">Whether this account has a registration.</param>
/// <param name="Document">What the machine handed back, where the machine holds the document itself.</param>
public sealed record AutostartAnswer(bool Registered, string Document);

/// <summary>
/// The registrars themselves: the only code in this product that starts a process or writes a file to register
/// something at logon. <b>No test constructs one.</b> What a test can reach is the pure half — the composition
/// in <see cref="AutostartArtifacts"/> and <see cref="Interpret"/> here, which is where a tool's exit code
/// becomes an answer and where "nothing is registered" is told apart from "the tool refused".
/// </summary>
public static class AutostartRegistrars
{
    /// <summary>The way this machine registers things at logon, or the one that refuses.</summary>
    public static IAutostartRegistrar ForThisMachine()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRegistrar();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new LaunchAgentRegistrar();
        }

        return OperatingSystem.IsLinux() ? new SystemdRegistrar() : Unsupported;
    }

    /// <summary>
    /// The registrar for a machine this product cannot register anything on — and the answer an environment
    /// that names no registrar gets, which is the fail-closed half of this seam.
    /// </summary>
    public static IAutostartRegistrar Unsupported { get; } = new NoRegistrar();

    /// <summary>
    /// What a query said, read as an answer rather than as an exit code. This is where "nothing is registered"
    /// is told apart from "the tool refused to say", and it is pure so that the first real run meets no shape
    /// this code has never seen.
    /// </summary>
    /// <remarks>
    /// <para><c>schtasks /Query</c> on a task that does not exist exits 1 and says so on standard error. That
    /// is an answer, not a failure — but so is "access is denied", with the same exit code, and the two must
    /// not be confused: one means "register it", the other means "this account may not look".</para>
    /// <para><c>systemctl --user is-enabled</c> exits 0 for enabled, 1 for disabled, and 4 when there is no
    /// such unit. Anything else is the manager itself refusing — most often because there is no user manager
    /// running at all, which is a thing to say out loud rather than to report as "not registered".</para>
    /// <para><c>launchctl list</c> exits non-zero when the label is unknown, and prints nothing useful.</para>
    /// </remarks>
    public static AutostartAnswer Interpret(AutostartPlatform platform, int exit, string output, string error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return platform switch
        {
            AutostartPlatform.Windows => exit == 0
                ? new AutostartAnswer(true, output)
                : Missing(error)
                    ? new AutostartAnswer(false, string.Empty)
                    : throw Refused("schtasks", error, output),
            AutostartPlatform.Linux => exit switch
            {
                0 => new AutostartAnswer(true, string.Empty),
                1 or 4 => new AutostartAnswer(false, string.Empty),
                _ => throw Refused("systemctl --user", error, output),
            },
            AutostartPlatform.MacOs => new AutostartAnswer(exit == 0, string.Empty),
            _ => new AutostartAnswer(false, string.Empty),
        };
    }

    /// <summary>The Task Scheduler's way of saying there is no such task, which is an answer and not a failure.</summary>
    private static bool Missing(string error) =>
        error.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase)
        || error.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private static AutostartException Refused(string tool, string error, string output)
    {
        var said = FirstLine(error) is { Length: > 0 } line ? line : FirstLine(output);
        return new AutostartException(
            AutostartCodes.Refused,
            said.Length == 0 ? $"{tool} refused, and said nothing about why." : $"{tool} refused: {said}");
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;

    /// <summary>The account's own home directory, which is where two of the three keep their document.</summary>
    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Runs one command and says what it said. Nothing here is a shell, and nothing opens a window.</summary>
    internal static (int Exit, string Output, string Error) Run(IReadOnlyList<string> command)
    {
        var start = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = OperatingSystemProcess.Start(start)
                ?? throw new AutostartException(AutostartCodes.Refused, $"'{command[0]}' could not be started.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output, error);
        }
        catch (Exception problem) when (problem is Win32Exception or FileNotFoundException)
        {
            throw new AutostartException(
                AutostartCodes.Refused,
                $"'{command[0]}' could not be run on this machine: {problem.Message}",
                problem);
        }
    }

    /// <summary>Runs one command that has to work, and says what the tool said when it did not.</summary>
    internal static void Must(string tool, IReadOnlyList<string> command)
    {
        var (exit, output, error) = Run(command);
        if (exit != 0)
        {
            throw Refused(tool, error, output);
        }
    }

    /// <summary>Writes the document, making the directory it belongs in.</summary>
    internal static void Write(string path, string artifact, Encoding encoding)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, artifact, encoding);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            throw new AutostartException(AutostartCodes.Refused, $"'{path}' could not be written: {problem.Message}", problem);
        }
    }

    /// <summary>Deletes the document, and says nothing about one that was not there.</summary>
    internal static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // The registration itself is gone; a leftover document registers nothing and the next `enable`
            // writes over it.
        }
    }

    /// <summary>Reads a document this account holds, or nothing where it holds none.</summary>
    internal static string? Held(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            throw new AutostartException(AutostartCodes.Refused, $"'{path}' could not be read: {problem.Message}", problem);
        }
    }
}

/// <summary>A machine with no way to start anything at logon that this product knows about.</summary>
internal sealed class NoRegistrar : IAutostartRegistrar
{
    public AutostartPlatform Platform => AutostartPlatform.Unsupported;

    public AutostartState Read() => new(false, [], null);

    public void Apply(AutostartRegistration registration) => throw Refuse();

    public void Remove(AutostartRegistration registration) => throw Refuse();

    private static AutostartException Refuse() =>
        new(
            AutostartCodes.Unsupported,
            "This machine has no way to start the runtime at logon that Jason knows about: it registers a logon task on Windows, "
            + "a LaunchAgent on macOS and a systemd user unit on Linux. Start the runtime with `jason runtime start` instead.");
}

/// <summary>
/// A Windows logon task, registered from the document rather than from a command line — which is how it can
/// say that it logs on without an interactive session, and therefore without a console window.
/// </summary>
internal sealed class WindowsRegistrar : IAutostartRegistrar
{
    public AutostartPlatform Platform => AutostartPlatform.Windows;

    public AutostartState Read()
    {
        var query = AutostartArtifacts.Compose(
            AutostartPlatform.Windows,
            ["jason"],
            Path.Combine(AutostartRegistrars.Home, ".jason"),
            AutostartRegistrars.Home,
            "S-1-0-0").Query;

        var (exit, output, error) = AutostartRegistrars.Run(query);
        var answer = AutostartRegistrars.Interpret(AutostartPlatform.Windows, exit, output, error);
        return answer.Registered
            ? new AutostartState(true, AutostartArtifacts.Read(AutostartPlatform.Windows, answer.Document), null)
            : new AutostartState(false, [], null);
    }

    public void Apply(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        // UTF-16: what `schtasks /Create /XML` reads. Handed a UTF-8 file it answers that the XML contains a
        // value which is incorrectly formatted, which is a sentence nobody could act on.
        AutostartRegistrars.Write(registration.ArtifactPath, registration.Artifact, Encoding.Unicode);
        foreach (var command in registration.Apply)
        {
            AutostartRegistrars.Must("schtasks", command);
        }
    }

    public void Remove(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        // Removing what is not there is not an error, so the query decides whether anything is run at all.
        if (Read().Registered)
        {
            foreach (var command in registration.Remove)
            {
                AutostartRegistrars.Must("schtasks", command);
            }
        }

        AutostartRegistrars.Discard(registration.ArtifactPath);
    }
}

/// <summary>A macOS LaunchAgent: a file under the account's own Library, loaded at login.</summary>
internal sealed class LaunchAgentRegistrar : IAutostartRegistrar
{
    public AutostartPlatform Platform => AutostartPlatform.MacOs;

    public AutostartState Read()
    {
        var path = AutostartArtifacts.ArtifactPath(AutostartPlatform.MacOs, AutostartRegistrars.Home, string.Empty);
        var held = AutostartRegistrars.Held(path);
        return held is null
            ? new AutostartState(false, [], null)
            : new AutostartState(true, AutostartArtifacts.Read(AutostartPlatform.MacOs, held), path);
    }

    public void Apply(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        // Unloaded first where something is already loaded: launchd refuses to load a label twice, and an
        // `enable` that follows an `enable` is the ordinary case rather than a mistake.
        if (Read().Registered)
        {
            foreach (var command in registration.Remove)
            {
                AutostartRegistrars.Run(command);
            }
        }

        AutostartRegistrars.Write(registration.ArtifactPath, registration.Artifact, new UTF8Encoding(false));
        foreach (var command in registration.Apply)
        {
            AutostartRegistrars.Must("launchctl", command);
        }
    }

    public void Remove(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (Read().Registered)
        {
            foreach (var command in registration.Remove)
            {
                AutostartRegistrars.Run(command);
            }
        }

        AutostartRegistrars.Discard(registration.ArtifactPath);
    }
}

/// <summary>A systemd user unit, enabled into the account's own default target.</summary>
internal sealed class SystemdRegistrar : IAutostartRegistrar
{
    public AutostartPlatform Platform => AutostartPlatform.Linux;

    public AutostartState Read()
    {
        var path = AutostartArtifacts.ArtifactPath(AutostartPlatform.Linux, AutostartRegistrars.Home, string.Empty);
        var held = AutostartRegistrars.Held(path);
        if (held is null)
        {
            return new AutostartState(false, [], null);
        }

        // The file is not the registration: a unit that is there and not enabled starts nothing at login. The
        // manager is asked, and a manager that will not answer is said out loud rather than read as "no".
        var query = AutostartArtifacts.Compose(AutostartPlatform.Linux, ["jason"], "/", AutostartRegistrars.Home, "unused").Query;
        var (exit, output, error) = AutostartRegistrars.Run(query);
        var answer = AutostartRegistrars.Interpret(AutostartPlatform.Linux, exit, output, error);

        return new AutostartState(answer.Registered, AutostartArtifacts.Read(AutostartPlatform.Linux, held), path);
    }

    public void Apply(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        AutostartRegistrars.Write(registration.ArtifactPath, registration.Artifact, new UTF8Encoding(false));
        foreach (var command in registration.Apply)
        {
            AutostartRegistrars.Must("systemctl --user", command);
        }
    }

    public void Remove(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (AutostartRegistrars.Held(registration.ArtifactPath) is not null)
        {
            AutostartRegistrars.Run(registration.Remove[0]);
            AutostartRegistrars.Discard(registration.ArtifactPath);
            AutostartRegistrars.Run(registration.Remove[1]);
        }
    }
}
