using Jason.Cli.Autostart;
using Jason.Cli.Process;
using Jason.Cli.Skills;
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
/// <param name="Programs">
/// How a program that is not a Jason runtime is run — and the third. One of the verbs those guards will type
/// fetches a skills source with <c>git</c>; a seam that defaulted to the real runner would make a
/// documentation guard the first test in this repository to open a socket to the internet.
/// </param>
public sealed record CliEnvironment(TextWriter Out, TextWriter Error, JasonPaths Paths, HttpMessageHandler? HttpHandler = null, TextReader? In = null, IRuntimeProcessControl? Processes = null, string? InstallPath = null, IAutostartRegistrar? Autostart = null, IHarnessLocator? Harnesses = null, IProgramRunner? Programs = null)
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
            ProgramRunners.ForThisMachine());
}
