using System.Diagnostics;
using System.Text;

namespace Jason.Runtime.Tests;

/// <summary>
/// Every PowerShell 7 a test starts has a start cache of its own, inside the directory the row that starts it owns.
/// </summary>
/// <remarks>
/// <para>
/// PowerShell 7 reads and writes a cache as it starts: the module analysis cache, which <c>PSModuleAnalysisCachePath</c>
/// names, and, beside it in <c>$XDG_CACHE_HOME/powershell</c> on Linux and macOS, the startup profile the .NET runtime
/// writes for its next start (<c>StartupProfileData-NonInteractive</c>) and the telemetry identifier. Children that share
/// the runner's home share those files; a shard twice saw every later child end with a stack overflow, exit 134 on macOS
/// and 139 on Linux, from one moment on, while the same head was green elsewhere. So <see cref="ChildPwsh"/> is the one
/// start of a test's PowerShell 7, and every start it makes points each of those variables into a folder of its own.
/// </para>
/// <para>
/// On Windows the cache folder is the account's known Local AppData folder, which no variable moves; the module analysis
/// cache is still moved by its own variable there.
/// </para>
/// </remarks>
public sealed class ChildPwshTests
{
    /// <summary>The variables that move PowerShell 7's start cache, each of which every start points somewhere of its own.</summary>
    private static readonly string[] CacheVariables = ["XDG_CACHE_HOME", "PSModuleAnalysisCachePath"];

    [Fact]
    public void Every_start_points_the_start_cache_into_a_folder_of_its_own_inside_the_row_s_directory()
    {
        using var tree = new TempTree();
        var root = Path.GetFullPath(tree.Root) + Path.DirectorySeparatorChar;

        var first = ChildPwsh.StartInfo(tree.Root);
        var second = ChildPwsh.StartInfo(tree.Root);

        foreach (var variable in CacheVariables)
        {
            Assert.True(first.Environment.TryGetValue(variable, out var one) && !string.IsNullOrEmpty(one), $"The start does not set {variable}.");
            Assert.True(second.Environment.TryGetValue(variable, out var two) && !string.IsNullOrEmpty(two), $"The start does not set {variable}.");
            Assert.True(Path.IsPathFullyQualified(one!), $"{variable} is {one}, which is not a full path.");
            Assert.StartsWith(root, Path.GetFullPath(one!), StringComparison.Ordinal);
            Assert.StartsWith(root, Path.GetFullPath(two!), StringComparison.Ordinal);
            Assert.NotEqual(Path.GetFullPath(one!), Path.GetFullPath(two!));
        }

        // Telemetry is reported over the network and keeps its identifier in the same cache folder.
        Assert.Equal("1", first.Environment["POWERSHELL_TELEMETRY_OPTOUT"]);

        // What the child itself reads is what the start says, and on Linux and macOS PowerShell takes the folder.
        foreach (var start in new[] { first, second })
        {
            var said = Run(start, "[Console]::Out.Write($env:XDG_CACHE_HOME + '|' + $env:PSModuleAnalysisCachePath)");
            Assert.Equal($"{start.Environment["XDG_CACHE_HOME"]}|{start.Environment["PSModuleAnalysisCachePath"]}", said);
            if (!OperatingSystem.IsWindows())
            {
                Assert.True(
                    Directory.Exists(Path.Combine(start.Environment["XDG_CACHE_HOME"]!, "powershell")),
                    "PowerShell did not make its cache folder under the XDG_CACHE_HOME it was given.");
            }
        }
    }

    private static string Run(ProcessStartInfo start, string command)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("PowerShell 7 did not finish in a minute.");
        }

        Assert.True(process.ExitCode == 0, $"PowerShell 7 ended with {process.ExitCode}: {stderr.Result}");
        return stdout.Result;
    }
}
