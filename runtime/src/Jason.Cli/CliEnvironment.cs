using Jason.Cli.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli;

/// <summary>Everything the CLI touches outside itself, so tests can substitute all of it.</summary>
/// <param name="InstallPath">
/// The file this Jason is installed as. Null asks the operating system, which is the answer everywhere but a
/// test: <c>jason update apply</c> replaces the file it is running as, and a test that let it work that out for
/// itself would replace the test host.
/// </param>
public sealed record CliEnvironment(TextWriter Out, TextWriter Error, JasonPaths Paths, HttpMessageHandler? HttpHandler = null, TextReader? In = null, IRuntimeProcessControl? Processes = null, string? InstallPath = null)
{
    public static CliEnvironment Default() => new(Console.Out, Console.Error, JasonPaths.FromEnvironment(), null, Console.In, RuntimeProcessControl.Instance);
}
