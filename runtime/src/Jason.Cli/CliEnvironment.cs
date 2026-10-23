using Jason.Cli.Autostart;
using Jason.Cli.Doctor;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts.Discovery;

namespace Jason.Cli;

/// <summary>Everything the CLI touches outside itself, so tests can substitute all of it.</summary>
/// <param name="InstallPath">
/// The file this Jason is installed as. Null asks the operating system, which is the answer everywhere but a
/// test: <c>jason update apply</c> replaces the file it is running as, and a test that let it work that out for
/// itself would replace the test host.
/// </param>
/// <param name="Autostart">
/// How this machine registers something to start at logon — and the one seam here whose default is the refusal
/// rather than the real thing. The three above are safe to leave null in a test because a real handler, process
/// table or install path acts on a temporary directory or an unreachable host; a real registrar acts on the
/// machine whatever the data directory says, and two guards in this repository type documented command lines
/// for real. So <see cref="Default"/> names this machine's registrar and nothing else does: null is
/// <c>autostart_unsupported</c>, which is the wrong answer to get by accident and a harmless one to get.
/// </param>
/// <param name="Harnesses">
/// Where the person's own agent harnesses keep skills — and the second seam whose default is the refusal. The
/// same reasoning as <paramref name="Autostart"/>, with a different directory at the end of it: a verb that
/// detected for itself would write into the home directory of whoever ran the suite, and into three CI
/// runners'. Null refuses, which is the wrong answer to get by accident and a harmless one to get.
/// </param>
/// <param name="Removes">
/// How this machine has things taken off it — and the fourth seam here whose default is the refusal. The same
/// reasoning as <paramref name="Autostart"/> and <paramref name="Harnesses"/>, with the sharpest consequence:
/// the file <c>jason uninstall</c> removes is decided by <paramref name="InstallPath"/>, whose null means "ask
/// the operating system", which under a test is the test host executable — and the PATH it edits belongs to
/// whoever ran the suite. Null refuses before anything at all is removed.
/// </param>
/// <param name="SearchPath">
/// The directories a bare command name is looked up in — <c>PATH</c>. Null asks the operating system, which is
/// the answer everywhere but a test, for the same reason <paramref name="InstallPath"/> gives: whether
/// <c>jason</c> can be typed by name is a fact about the machine, and a test that read the real one would
/// report the developer's machine rather than the installation under test.
/// </param>
/// <param name="Programs">
/// How a program that is not a Jason runtime is run — and the third. One of the verbs those guards will type
/// fetches a skills source with <c>git</c>; a seam that defaulted to the real runner would make a
/// documentation guard the first test in this repository to open a socket to the internet.
/// </param>
/// <param name="Identity">
/// The names this installation goes by — the home directory, the account, the machine, its domain and the account's
/// security identifier — which a diagnostic bundle rewrites before anything is written. Null reads them from this
/// machine, the answer everywhere but a test, and unlike the seams above a harmless one to leave null: reading a name
/// acts on nothing. But a test that checks what a bundle says about a name would be checking the developer's own, so
/// it hands in names of its own, and no fixture ever holds the real ones.
/// </param>
/// <param name="Variables">
/// This process's environment variables, of which a diagnostic bundle describes the <c>JASON_*</c> ones. Null reads the
/// process's own, the answer everywhere but a test — and the same kind of seam as <paramref name="Identity"/>: reading a
/// variable acts on nothing, but a test that read the real environment would describe whatever the person running the
/// suite happens to have set, so it hands in variables of its own.
/// </param>
/// <param name="BuildVersion">
/// The version this build goes by, which decides the ref its skills are pinned to and which <c>jason status</c> compares
/// with the version the running runtime reports. Null is this build's own stamp, the answer everywhere but a test, and
/// <see cref="Default"/> never names one: a release compares its skills and its runtime with itself. A test names one
/// because the suite runs at whatever stamp CI gave it, a development one or a release, and a fact about what a release
/// does with an older deployment, or about a runtime that is not this build, has to hold at both.
/// </param>
/// <param name="Opener">
/// How a link reaches the person's browser — the fifth seam whose default is the refusal, for the reason
/// <paramref name="Autostart"/> gives: a verb that opened a browser for itself would open one on whoever runs the
/// suite. Null opens nothing and says the link is the person's to open; <see cref="Default"/> names this platform's.
/// </param>
/// <param name="Managers">
/// What the package managers recorded about what they installed, which decides whether this installation is Jason's own
/// or a manager's. Null is "no manager recorded anything", which leaves every installation Jason's own — and the
/// default for the reason <paramref name="Removes"/> gives: winget's record is in this account's registry, and a test
/// that read it would read the registry of whoever ran the suite. <see cref="Default"/> names this machine's.
/// </param>
/// <param name="WorkingDirectory">
/// The folder the command was typed in, from which a verb that takes a project and was given none finds it by the nearest
/// marker of the harness (<see cref="Folders.WorkingFolder"/>). Null finds nothing, which is the answer everywhere but
/// <see cref="Default"/>, and the default for the reason <paramref name="Harnesses"/> gives: a test that walked up from
/// its own folder would read whatever folders the person running the suite has above it.
/// </param>
public sealed record CliEnvironment(TextWriter Out, TextWriter Error, JasonPaths Paths, HttpMessageHandler? HttpHandler = null, TextReader? In = null, IRuntimeProcessControl? Processes = null, string? InstallPath = null, IAutostartRegistrar? Autostart = null, IHarnessLocator? Harnesses = null, IProgramRunner? Programs = null, string? SearchPath = null, IInstallationRemover? Removes = null, IdentityFacts? Identity = null, IReadOnlyDictionary<string, string>? Variables = null, string? BuildVersion = null, IManagerRecords? Managers = null, IUrlOpener? Opener = null, string? WorkingDirectory = null)
{
    public static CliEnvironment Default() =>
        new(
            Console.Out,
            Console.Error,
            JasonPaths.FromEnvironment(),
            null,
            Console.In,
            RuntimeProcessControl.Instance,
            null,
            AutostartRegistrars.ForThisMachine(),
            HarnessLocators.ForThisMachine(),
            ProgramRunners.ForThisMachine(),
            null,
            InstallationRemovers.ForThisMachine(),
            Managers: ManagerRecords.ForThisMachine(),
            Opener: new PlatformUrlOpener(),
            WorkingDirectory: WorkingDirectoryOf(() => Environment.CurrentDirectory));

    /// <summary>
    /// The folder the command was typed in, or null where the platform cannot name it — on Linux and macOS, a folder
    /// deleted under the shell standing in it — which finds nothing, as everywhere a verb is given no folder: a verb that
    /// does not need one, <c>jason status</c> above all, is never ended by it before it starts.
    /// </summary>
    internal static string? WorkingDirectoryOf(Func<string> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            return read();
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
