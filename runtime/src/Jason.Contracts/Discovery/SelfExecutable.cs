namespace Jason.Contracts.Discovery;

/// <summary>
/// How to run this program again: the command, as far as the mode word that comes next. One executable serves
/// every mode, so every part of Jason that starts another copy of itself — the CLI starting a detached runtime,
/// the runtime starting a plugin host — is asking this same question and must get this same answer.
/// </summary>
/// <remarks>
/// A published executable runs itself. A build started as <c>dotnet jason.dll</c> has the muxer for its process
/// path and the assembly is not recoverable from it, so the entry assembly has to be named again — both because
/// the child would otherwise be <c>dotnet &lt;mode&gt;</c>, which is not a program, and because naming
/// <c>jason</c> alone would start whatever is on the PATH rather than this build.
/// </remarks>
public static class SelfExecutable
{
    private const string DotnetHost = "dotnet";

    /// <summary>What to run to get another copy of this very process.</summary>
    public static IReadOnlyList<string> Command { get; } =
        Resolve(Environment.ProcessPath, Environment.GetCommandLineArgs()[0]);

    /// <summary>
    /// The same decision over the two values it depends on: the path the operating system says this process is
    /// running, and the entry assembly it was handed. Public so that the answer can be tested without a process.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? processPath, string entryAssembly)
    {
        if (processPath is not null
            && !string.Equals(Path.GetFileNameWithoutExtension(processPath), DotnetHost, StringComparison.OrdinalIgnoreCase))
        {
            return [processPath];
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssembly);
        return [DotnetHost, entryAssembly];
    }
}
