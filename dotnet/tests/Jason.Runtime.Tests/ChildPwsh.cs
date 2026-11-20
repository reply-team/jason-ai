using System.Diagnostics;

namespace Jason.Runtime.Tests;

/// <summary>
/// The one start of a test's PowerShell 7: PowerShell 7 from this machine's PATH, with a start cache of its own inside a
/// directory the row owns.
/// </summary>
/// <remarks>
/// <para>
/// PowerShell 7 reads and writes a cache as it starts, in <c>$XDG_CACHE_HOME/powershell</c> on Linux and macOS, else
/// <c>~/.cache/powershell</c>: the startup profile the .NET runtime writes for the next start
/// (<c>StartupProfileData-NonInteractive</c>), the module analysis cache, which <c>PSModuleAnalysisCachePath</c> moves on
/// every system, and the telemetry identifier, which telemetry turned off never writes. Children started in parallel
/// that share the runner's folder can leave the startup profile broken, and every later child then ends with a stack
/// overflow, exit 134 on macOS and 139 on Linux, which two CI shards saw. On Windows the folder is the account's known
/// Local AppData folder, which no variable moves; the module analysis cache is moved there all the same.
/// </para>
/// <para>
/// A start carries the program and the environment only: the arguments, the redirections and the rest are the caller's.
/// <see cref="WithOwnCache"/> gives the same cache to a start made elsewhere, the product's child-process door, which
/// a test of the door starts PowerShell 7 through. This file is linked into every test project that starts PowerShell 7,
/// and a guard over the test sources refuses a start of PowerShell 7 made anywhere else.
/// </para>
/// </remarks>
internal static class ChildPwsh
{
    /// <summary>The variables that move PowerShell 7's start cache, each pointed at a folder of the start's own.</summary>
    internal static readonly string[] CacheVariables = ["XDG_CACHE_HOME", "PSModuleAnalysisCachePath"];

    /// <summary>
    /// PowerShell 7 from this machine's PATH, with a start cache of its own in a new folder inside
    /// <paramref name="directory"/>; the row is skipped where PowerShell 7 is not on the PATH.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string directory)
    {
        var program = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(folder => new[] { Path.Combine(folder, "pwsh.exe"), Path.Combine(folder, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(program is null, "PowerShell 7 is not on this machine's PATH; every CI runner has it.");

        return WithOwnCache(new ProcessStartInfo(program!) { UseShellExecute = false, CreateNoWindow = true }, directory);
    }

    /// <summary>
    /// <paramref name="start"/>, a start of PowerShell 7 made elsewhere, given a start cache of its own in a new folder
    /// inside <paramref name="directory"/>, and telemetry, which keeps its identifier there and reports over the network,
    /// turned off.
    /// </summary>
    internal static ProcessStartInfo WithOwnCache(ProcessStartInfo start, string directory)
    {
        var own = Path.Combine(Path.GetFullPath(directory), "pwsh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(own);
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(own, "cache");
        start.Environment["PSModuleAnalysisCachePath"] = Path.Combine(own, "ModuleAnalysisCache");
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        return start;
    }
}
