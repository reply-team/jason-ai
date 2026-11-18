using Jason.Contracts.Programs;

namespace Jason.Runtime.Tests;

/// <summary>
/// The .NET muxer by its full path, for a test that starts a build's dll through it: every child is started by its full
/// path, so a bare <c>dotnet</c> is refused at the door. Found on the search path's full entries, else under
/// <c>DOTNET_ROOT</c>.
/// </summary>
public static class Muxer
{
    public static string Path { get; } =
        ProgramLookup.FullPathOf("dotnet", new ProgramSearchPath(Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT")))
        ?? (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
            ? System.IO.Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
            : throw new InvalidOperationException("The tests need 'dotnet' on PATH or DOTNET_ROOT."));
}
