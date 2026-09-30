using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Jason.App.Tests.Skills;

namespace Jason.App.Tests.Hosting;

/// <summary>
/// The executable's application manifest: what it declares, and the check that reads it out of the packaged
/// program before a release archives it.
/// </summary>
public class ApplicationManifestTests
{
    private const string Policy = "consoleAllocationPolicy";

    /// <summary>
    /// The first half of the proof: the manifest reaches the compiled program. Read out of <c>jason.dll</c>, the
    /// image the SDK copies an executable's resources from, and not out of the project. So a property that stopped
    /// pointing at the file, or a file that stopped saying what it says, is red on every platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A custom manifest replaces the compiler's default one rather than adding to it, and the default carried
    /// <c>asInvoker</c>. Measured on this product's own executable before this change: its only manifest was that
    /// default. Both halves are asserted, because losing either is a regression nobody would see in a terminal.
    /// </para>
    /// <para>
    /// The count is asserted with what the image holds, so a failure on a platform whose compiler wrote no
    /// resource says so, and a reader that cannot read throws with its own stack instead: two different findings.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_compiled_program_takes_no_console_it_did_not_inherit_and_runs_as_its_invoker()
    {
        var image = Path.Combine(AppContext.BaseDirectory, "jason.dll");
        var manifests = Win32Resources.Manifests(image);
        Assert.True(manifests.Count == 1, $"jason.dll carries {manifests.Count} RT_MANIFEST resources; {Win32Resources.Describe(image)}");
        var manifest = XDocument.Parse(manifests[0]);

        XNamespace settings = "http://schemas.microsoft.com/SMI/2024/WindowsSettings";
        XNamespace privileges = "urn:schemas-microsoft-com:asm.v3";
        Assert.Equal("detached", Assert.Single(manifest.Descendants(settings + Policy)).Value);
        var level = Assert.Single(manifest.Descendants(privileges + "requestedExecutionLevel"));
        Assert.Equal("asInvoker", level.Attribute("level")?.Value);
        Assert.Equal("false", level.Attribute("uiAccess")?.Value);
    }

    /// <summary>
    /// The second half: the check the package action runs on the executable it is about to archive, shown to pass
    /// this program and to refuse one without the policy. The refusal is what shows the check can refuse; a check
    /// only ever seen passing proves nothing. Windows only, because it reads the file the way Windows does.
    /// </summary>
    /// <remarks>
    /// Read from <c>jason.exe</c>, the executable Windows loads, which carries the resources the SDK copied into it
    /// from <c>jason.dll</c>. The test project's own image carries no policy — the very program the check exists
    /// to stop.
    /// </remarks>
    [Fact]
    public void The_packaged_executable_check_passes_this_program_and_refuses_one_without_the_policy()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The check reads a resource the way Windows loads it, with kernel32.");

        var passed = Check("-Executable", Path.Combine(AppContext.BaseDirectory, "jason.exe"));
        Assert.True(passed.Exit == 0, passed.Said);
        Assert.Contains("declares consoleAllocationPolicy detached and runs asInvoker", passed.Said, StringComparison.Ordinal);

        var refused = Check("-Executable", Path.Combine(AppContext.BaseDirectory, "Jason.App.Tests.dll"));
        Assert.True(refused.Exit != 0, $"A program without the policy was let through: {refused.Said}");
        Assert.Contains("does not declare consoleAllocationPolicy detached", refused.Said, StringComparison.Ordinal);
    }

    /// <summary>
    /// And each thing the check reads, shown refusing on its own: the level, its namespace and <c>uiAccess</c>, and
    /// the policy's value, case and namespace. Windows reads each of them exactly, so the check does too. A manifest
    /// is handed to the check as a file here, which needs no Windows, so this runs on every leg.
    /// </summary>
    [Theory]
    [InlineData("level=\"requireAdministrator\" uiAccess=\"false\"", "urn:schemas-microsoft-com:asm.v3", "detached", Settings, "does not run asInvoker")]
    [InlineData("level=\"AsInvoker\" uiAccess=\"false\"", "urn:schemas-microsoft-com:asm.v3", "detached", Settings, "does not run asInvoker")]
    [InlineData("level=\"asInvoker\" uiAccess=\"true\"", "urn:schemas-microsoft-com:asm.v3", "detached", Settings, "does not run asInvoker")]
    [InlineData("level=\"asInvoker\" uiAccess=\"false\"", "urn:schemas-microsoft-com:asm.v1", "detached", Settings, "does not run asInvoker")]
    [InlineData("level=\"asInvoker\" uiAccess=\"false\"", "urn:schemas-microsoft-com:asm.v3", "Detached", Settings, "does not declare consoleAllocationPolicy detached")]
    [InlineData("level=\"asInvoker\" uiAccess=\"false\"", "urn:schemas-microsoft-com:asm.v3", "detached", "http://schemas.microsoft.com/SMI/2016/WindowsSettings", "does not declare consoleAllocationPolicy detached")]
    public void The_check_refuses_each_thing_it_reads_on_its_own(string level, string privileges, string policy, string settings, string refusal)
    {
        var refused = CheckManifest(Manifest(level, privileges, policy, settings));

        Assert.True(refused.Exit != 0, $"A manifest the check should refuse was let through: {refused.Said}");
        Assert.Contains(refusal, refused.Said, StringComparison.Ordinal);
    }

    /// <summary>The same check passes the manifest this repository ships, read as a file: the control for the rows above.</summary>
    [Fact]
    public void The_check_passes_the_manifest_this_repository_ships()
    {
        var passed = CheckManifest(File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "runtime", "src", "Jason.App", "jason.manifest")));

        Assert.True(passed.Exit == 0, passed.Said);
        Assert.Contains("declares consoleAllocationPolicy detached and runs asInvoker", passed.Said, StringComparison.Ordinal);
    }

    private const string Settings = "http://schemas.microsoft.com/SMI/2024/WindowsSettings";

    private static string Manifest(string level, string privileges, string policy, string settings) => $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
          <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
            <security>
              <requestedPrivileges xmlns="{{privileges}}">
                <requestedExecutionLevel {{level}}/>
              </requestedPrivileges>
            </security>
          </trustInfo>
          <application>
            <windowsSettings>
              <consoleAllocationPolicy xmlns="{{settings}}">{{policy}}</consoleAllocationPolicy>
            </windowsSettings>
          </application>
        </assembly>
        """;

    private static (int Exit, string Said) CheckManifest(string manifest)
    {
        var file = Path.Combine(Path.GetTempPath(), "jason-manifest-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(file, manifest);
        try
        {
            return Check("-ManifestFile", file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static (int Exit, string Said) Check(string what, string path)
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it.");

        var start = new ProcessStartInfo(pwsh!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // PowerShell reports telemetry over the network unless told not to, and no test here reaches the network.
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(SkillPack.RepositoryRoot(), ".github", "scripts", "Assert-ApplicationManifest.ps1"),
            what, path,
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The manifest check did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
