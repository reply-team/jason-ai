using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Jason.Contracts.Discovery;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Autostart;

/// <summary>What a machine answered when it was asked whether anything is registered.</summary>
/// <param name="Registered">Whether this account has a registration.</param>
/// <param name="Document">What the machine handed back, where the machine holds the document itself.</param>
public sealed record AutostartAnswer(bool Registered, string Document);

/// <summary>
/// The registrars themselves: the only code in this product that starts a process or writes a file to register
/// something at logon. <b>No test constructs one.</b> What a test can reach is the pure half — the composition
/// in <see cref="AutostartArtifacts"/>, <see cref="Interpret"/> here, and <see cref="WindowsTasks"/>, which is
/// where a tool's exit code becomes an answer and where "nothing is registered" is told apart from "the tool
/// refused".
/// </summary>
public static class AutostartRegistrars
{
    /// <summary>The way this machine registers things at logon, or the one that refuses.</summary>
    public static IAutostartRegistrar ForThisMachine()
    {
        if (OperatingSystem.IsWindows())
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new WindowsRegistrar(new WindowsAccount(ThisAccount(), identity.Name));
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

    /// <summary>ERROR_FILE_NOT_FOUND as an HRESULT: the Task Scheduler's "there is no such task".</summary>
    private const int TaskNotFound = unchecked((int)0x80070002);

    /// <summary>
    /// What a query said, read as an answer rather than as noise. This is where "nothing is registered" is told
    /// apart from "the tool would not say", and it is pure so that the first real run meets no shape this code
    /// has never seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neither platform is read by its prose.</b> <c>schtasks /Query</c> exits 1 both for a task that is not
    /// there and for a task this account may not read, and the sentence that separates them is printed in the
    /// language Windows was installed in — a Russian Windows says
    /// <c>ОШИБКА: Не удается найти указанный файл.</c>, and an English matcher reads that as a refusal: a clean machine
    /// would answer <c>status</c> with a failure, <c>disable</c> on nothing with a failure, and <c>enable</c>
    /// would register the task and then fail reading it back. <c>/HRESULT</c> makes the exit code carry the
    /// answer instead, in every language.
    /// </para>
    /// <para>
    /// <c>systemctl --user is-enabled</c> exits 1 for <c>disabled</c> — and exits 1 with nothing on standard
    /// output when it cannot reach a user manager at all (measured: <c>Failed to connect to user scope bus</c>).
    /// Read by the exit code, a machine with no user bus reports every unit as disabled, which for a unit that
    /// is registered is a lie in the safe-sounding direction. So the word it prints decides, and a word it does
    /// not print — including none at all — is the manager refusing to answer.
    /// </para>
    /// <para><c>launchctl list</c> exits non-zero when the label is unknown, and prints nothing useful.</para>
    /// </remarks>
    public static AutostartAnswer Interpret(AutostartPlatform platform, int exit, string output, string error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return platform switch
        {
            AutostartPlatform.Windows => exit switch
            {
                0 => new AutostartAnswer(true, output),
                TaskNotFound => new AutostartAnswer(false, string.Empty),
                _ => throw Refused("schtasks", error, output),
            },
            AutostartPlatform.Linux => Enabled(output, error),
            AutostartPlatform.MacOs => new AutostartAnswer(exit == 0, string.Empty),
            _ => new AutostartAnswer(false, string.Empty),
        };
    }

    /// <summary>The words <c>is-enabled</c> answers with that mean something will start at login.</summary>
    private static readonly string[] WillStart =
        ["enabled", "enabled-runtime", "static", "indirect", "generated", "transient", "alias", "linked", "linked-runtime"];

    /// <summary>And the ones that mean nothing will.</summary>
    private static readonly string[] WillNot = ["disabled", "masked", "masked-runtime", "not-found", "bad-setting"];

    /// <summary>
    /// What <c>is-enabled</c> said, by the word it said. A word this does not know is not guessed at from an
    /// exit code: it is the one thing a registrar must never get wrong quietly.
    /// </summary>
    private static AutostartAnswer Enabled(string output, string error)
    {
        var said = FirstLine(output);
        if (WillStart.Contains(said, StringComparer.Ordinal))
        {
            return new AutostartAnswer(true, string.Empty);
        }

        return WillNot.Contains(said, StringComparer.Ordinal)
            ? new AutostartAnswer(false, string.Empty)
            : throw Refused("systemctl --user", error, output);
    }

    /// <summary>The tool refusing, with its own first line where it said one.</summary>
    internal static AutostartException Refused(string tool, string error, string output)
    {
        var said = Said(error, output);
        return new AutostartException(
            AutostartCodes.Refused,
            said.Length == 0 ? $"{tool} refused, and said nothing about why." : $"{tool} refused: {said}");
    }

    /// <summary>The one line of a tool's own words worth repeating: its first, wherever it put it.</summary>
    internal static string Said(string error, string output) =>
        FirstLine(error) is { Length: > 0 } line ? line : FirstLine(output);

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;

    /// <summary>
    /// What a machine that registers nothing says, in one place: the registrar raises it when something is
    /// applied to such a machine, and the verb raises it before composing anything, because there is nothing to
    /// compose for a platform that registers nothing.
    /// </summary>
    public static AutostartException CannotRegister() =>
        new(
            AutostartCodes.Unsupported,
            "This machine has no way to start the runtime at logon that Jason knows about: it registers a logon task on Windows, "
            + "a LaunchAgent on macOS and a systemd user unit on Linux. Start the runtime with `jason runtime start` instead.");

    /// <summary>
    /// Who a registration runs as, and on Windows whose task it is. There that is this account's SID: a Microsoft
    /// account's display name is not what the Task Scheduler stores, and a name with a domain in front of it is not
    /// what it compares. Everywhere else a document under the account's own home directory says whose it is by
    /// being there, and the name is only carried.
    /// </summary>
    internal static string ThisAccount() =>
        OperatingSystem.IsWindows()
            ? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName
            : Environment.UserName;

    /// <summary>The account's own home directory, which is where two of the three keep their document.</summary>
    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Runs one command and says what it said. Nothing here is a shell, and nothing opens a window.</summary>
    internal static (int Exit, string Output, string Error) Run(IReadOnlyList<string> command)
    {
        var start = ChildProcess.StartInfo(command[0], ChildKind.SystemTool);

        foreach (var argument in command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = OperatingSystemProcess.Start(start)
                ?? throw new AutostartException(AutostartCodes.Refused, $"'{command[0]}' could not be started.");

            // Both pipes are drained at once, and only then is the exit waited for. Reading one to its end
            // first is the deadlock every program that shells out learns about eventually: a tool that fills
            // the other pipe blocks writing to it, and never gets to the end of the one being read.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
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

    /// <summary>
    /// Writes the document a tool is about to be handed, asks the tool, and puts the directory back the way it
    /// was if the tool refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every platform writes before it asks — the Task Scheduler is handed a file, and so is systemd — so a
    /// refusal that walked away left a document behind on a machine with nothing registered. A person reading
    /// the data directory finds a file named after a registration that does not exist; the hand check found it
    /// after an `enable` from an unelevated prompt was refused.
    /// </para>
    /// <para>
    /// A document that was already there is put back <b>byte for byte</b> rather than deleted. On two of the
    /// three platforms the document <i>is</i> the registration, at a fixed path in the account's own home, so
    /// deleting it would take somebody's working registration away as the parting act of a command that
    /// changed nothing, and leaving the new one would quietly swap one command line for another.
    /// </para>
    /// </remarks>
    public static void WriteThen(string path, string artifact, Encoding encoding, Action ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        var directory = Path.GetDirectoryName(path);
        var hadDirectory = string.IsNullOrEmpty(directory) || Directory.Exists(directory);
        var before = Existing(path);

        Write(path, artifact, encoding);
        try
        {
            ask();
        }
        catch
        {
            PutBack(path, before, directory, hadDirectory);
            throw;
        }
    }

    /// <summary>What is at the path already, or nothing where there is nothing.</summary>
    private static byte[]? Existing(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            throw new AutostartException(AutostartCodes.Refused, $"'{path}' could not be read: {problem.Message}", problem);
        }
    }

    /// <summary>The directory as it was: what was there back, what was not gone, and nothing else touched.</summary>
    private static void PutBack(string path, byte[]? before, string? directory, bool hadDirectory)
    {
        try
        {
            if (before is not null)
            {
                File.WriteAllBytes(path, before);
                return;
            }

            File.Delete(path);
            if (!hadDirectory
                && directory is { Length: > 0 }
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // The refusal on its way out is the thing worth reporting, and a document nothing registered is
            // harmless where it lies: the next `enable` writes over it and `disable` deletes it.
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

    public void Apply(AutostartRegistration registration) => throw AutostartRegistrars.CannotRegister();

    public AutostartRemoval Remove(AutostartRegistration registration) => throw AutostartRegistrars.CannotRegister();
}

/// <summary>
/// A Windows logon task, registered from the document rather than from a command line, which is how it can say
/// whose logon it starts at and what kind of logon that is.
/// </summary>
internal sealed class WindowsRegistrar(WindowsAccount account) : IAutostartRegistrar
{
    public AutostartPlatform Platform => AutostartPlatform.Windows;

    public AutostartState Read()
    {
        // Asked without composing anything: what is registered may have been registered by another
        // installation, under a data directory this one knows nothing about, which is the answer worth having.
        // Only this account's task is asked after; another account's is not this account's to read.
        var (exit, output, error) = AutostartRegistrars.Run(AutostartArtifacts.QueryFor(AutostartPlatform.Windows, account.Sid));
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
        WindowsTasks.Apply(
            AutostartRegistrars.Run,
            registration,
            account,
            register => AutostartRegistrars.WriteThen(registration.ArtifactPath, registration.Artifact, Encoding.Unicode, register));
    }

    public AutostartRemoval Remove(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var removed = WindowsTasks.Remove(AutostartRegistrars.Run, registration, account);
        AutostartRegistrars.Discard(registration.ArtifactPath);
        return removed;
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

        AutostartRegistrars.WriteThen(
            registration.ArtifactPath,
            registration.Artifact,
            new UTF8Encoding(false),
            () =>
            {
                foreach (var command in registration.Apply)
                {
                    AutostartRegistrars.Must("launchctl", command);
                }
            });
    }

    public AutostartRemoval Remove(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var registered = Read().Registered;
        if (registered)
        {
            foreach (var command in registration.Remove)
            {
                AutostartRegistrars.Run(command);
            }
        }

        AutostartRegistrars.Discard(registration.ArtifactPath);
        return new AutostartRemoval(registered, Legacy: false);
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
        var (exit, output, error) = AutostartRegistrars.Run(AutostartArtifacts.QueryFor(AutostartPlatform.Linux, string.Empty));
        var answer = AutostartRegistrars.Interpret(AutostartPlatform.Linux, exit, output, error);

        return new AutostartState(answer.Registered, AutostartArtifacts.Read(AutostartPlatform.Linux, held), path);
    }

    public void Apply(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        AutostartRegistrars.WriteThen(
            registration.ArtifactPath,
            registration.Artifact,
            new UTF8Encoding(false),
            () =>
            {
                foreach (var command in registration.Apply)
                {
                    AutostartRegistrars.Must("systemctl --user", command);
                }
            });
    }

    public AutostartRemoval Remove(AutostartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var held = AutostartRegistrars.Held(registration.ArtifactPath) is not null;
        if (held)
        {
            AutostartRegistrars.Run(registration.Remove[0]);
            AutostartRegistrars.Discard(registration.ArtifactPath);
            AutostartRegistrars.Run(registration.Remove[1]);
        }

        return new AutostartRemoval(held, Legacy: false);
    }
}
