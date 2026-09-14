using Jason.Contracts.Discovery;

namespace Jason.Cli;

/// <summary>Everything the CLI touches outside itself, so tests can substitute all of it.</summary>
public sealed record CliEnvironment(TextWriter Out, TextWriter Error, JasonPaths Paths, HttpMessageHandler? HttpHandler = null, TextReader? In = null)
{
    public static CliEnvironment Default() => new(Console.Out, Console.Error, JasonPaths.FromEnvironment(), null, Console.In);
}
