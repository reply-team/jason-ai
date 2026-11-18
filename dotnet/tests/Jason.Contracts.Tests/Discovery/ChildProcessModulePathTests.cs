using System.Diagnostics;
using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// A Windows PowerShell child starts without the module path this process inherited. A PowerShell 7 session puts its own
/// module folders first in PSModulePath, and every program it starts inherits that, jason among them; Windows PowerShell
/// 5.1 started with it finds some of its own commands in PowerShell 7's modules and cannot load them. These rows set this
/// process's own PSModulePath, so they run alone.
/// </summary>
[Collection(ModulePathEnvironmentCollection.Name)]
public class ChildProcessModulePathTests
{
    private const string Variable = "PSModulePath";

    /// <summary>What a PowerShell 7 session hands on: its own folders first, then Windows PowerShell's.</summary>
    private static readonly string PowerShell7Folders = string.Join(';', @"C:\Program Files\PowerShell\Modules", @"C:\Program Files\PowerShell\7\Modules");

    /// <summary>
    /// Windows PowerShell, by any spelling of its file name, is given no PSModulePath; PowerShell 7 and every other program
    /// keep the one this process has.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false)]
    [InlineData(@"C:\Windows\SysWOW64\WindowsPowerShell\v1.0\POWERSHELL.EXE", false)]
    [InlineData("powershell", false)]
    [InlineData("PowerShell.exe", false)]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", true)]
    [InlineData("pwsh", true)]
    [InlineData(@"C:\Windows\System32\conhost.exe", true)]
    [InlineData("program", true)]
    public void On_Windows_a_Windows_PowerShell_child_is_given_no_inherited_module_path_and_every_other_child_keeps_it(string program, bool keeps)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows PowerShell is Windows' own; elsewhere powershell may name PowerShell 7, which is left as it is.");
        WithModulePath(PowerShell7Folders, () =>
        {
            foreach (var kind in new ChildKind?[] { null, ChildKind.SpeaksToJason, ChildKind.SystemTool, ChildKind.SystemToolWithInput })
            {
                var start = kind is { } known ? ChildProcess.StartInfo(program, known) : ChildProcess.StartInfo(program);

                Assert.Equal(keeps, start.Environment.ContainsKey(Variable));
                if (keeps)
                {
                    Assert.Equal(PowerShell7Folders, start.Environment[Variable]);
                }

                // Nothing else of the environment is touched.
                Assert.Equal(Environment.GetEnvironmentVariable("PATH"), start.Environment["PATH"]);
            }
        });
    }

    [Fact]
    public void Elsewhere_no_child_loses_the_module_path()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows' own rows are above.");
        WithModulePath(PowerShell7Folders, () =>
        {
            foreach (var program in new[] { "powershell", "pwsh", "program" })
            {
                Assert.Equal(PowerShell7Folders, ChildProcess.StartInfo(program).Environment[Variable]);
            }
        });
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", true)]
    [InlineData("powershell", true)]
    [InlineData("POWERSHELL.EXE", true)]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", false)]
    [InlineData("pwsh.exe", false)]
    [InlineData(@"C:\Tools\powershell-helper.exe", false)]
    [InlineData(@"C:\powershell\program.exe", false)]
    public void Windows_PowerShell_is_known_by_its_file_name_alone(string program, bool windowsPowerShell)
    {
        Assert.Equal(windowsPowerShell, ChildProcess.IsWindowsPowerShell(program));
    }

    /// <summary>
    /// The real child, with this process holding PowerShell 7's own module path where PowerShell 7 is on the machine: the
    /// template's own test of PowerShell's policy - what electron-builder's installer runs to decide whether PowerShell is
    /// there - succeeds in a child the door made, and fails in one started with the inherited path, which is what makes the
    /// path a hazard; and the child finds its modules in its own folder.
    /// </summary>
    [Fact]
    public void On_Windows_a_Windows_PowerShell_child_started_with_PowerShell_7s_module_path_inherited_loads_its_own_modules()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows PowerShell is Windows' own.");
        var inherited = PowerShell7ModulePath();
        Assert.SkipWhen(inherited is null, "PowerShell 7 is not on this machine, so there is no module path of its to inherit.");
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        // The path is written first, and the policy test is the last statement, so its failure is the exit code, as it is where
        // the template runs it alone.
        const string Command = "[Console]::Out.Write($env:PSModulePath); if ((Get-ExecutionPolicy -Scope Process) -eq 'Restricted') { exit 1 } else { exit 0 }";

        WithModulePath(inherited!, () =>
        {
            // A child handed the inherited path: the hazard is real on this machine, or the row says nothing.
            var handed = ChildProcess.StartInfo(powershell, ChildKind.SystemToolWithInput);
            handed.Environment[Variable] = inherited;
            var (refused, _) = Run(handed, Command);
            Assert.SkipWhen(refused == 0, "Windows PowerShell loads its own modules with PowerShell 7's path on this machine, so the path is no hazard here.");

            var (exit, path) = Run(ChildProcess.StartInfo(powershell, ChildKind.SystemToolWithInput), Command);
            Assert.True(exit == 0, $"The policy test failed in a child the door made; it saw the module path '{path}'.");
            Assert.Contains(Path.Combine(Path.GetDirectoryName(powershell)!, "Modules"), path.Split(';'), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(path.Split(';'), entry => entry.Contains(@"\PowerShell\7", StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Removed, not emptied: Windows PowerShell started with an empty PSModulePath keeps it empty, with not even its own
    /// folder in it, where one started without the variable builds its default, its own folder included.
    /// </summary>
    [Fact]
    public void On_Windows_removing_the_module_path_and_emptying_it_are_not_the_same_for_Windows_PowerShell()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows PowerShell is Windows' own.");
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        const string Report = "[Console]::Out.Write($env:PSModulePath)";

        var (_, removed) = Run(ChildProcess.StartInfo(powershell, ChildKind.SystemToolWithInput), Report);
        var emptied = ChildProcess.StartInfo(powershell, ChildKind.SystemToolWithInput);
        emptied.Environment[Variable] = string.Empty;
        var (_, empty) = Run(emptied, Report);

        Assert.Contains(Path.Combine(Path.GetDirectoryName(powershell)!, "Modules"), removed.Split(';'), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, empty);
    }

    private static (int Exit, string Output) Run(ProcessStartInfo start, string command)
    {
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = ChildProcess.Start(start)!;
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Windows PowerShell did not end within a minute.");
        _ = error.Result;
        return (process.ExitCode, output);
    }

    /// <summary>The module path PowerShell 7 hands the programs it starts, asked of PowerShell 7 itself, or null.</summary>
    private static string? PowerShell7ModulePath()
    {
        var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (!File.Exists(pwsh))
        {
            return null;
        }

        var start = ChildProcess.StartInfo(pwsh, ChildKind.SystemToolWithInput);
        var (exit, output) = Run(start, "[Console]::Out.Write($env:PSModulePath)");
        return exit == 0 && output.Length > 0 ? output : null;
    }

    private static void WithModulePath(string value, Action act)
    {
        var original = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, value);
        try
        {
            act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, original);
        }
    }
}

/// <summary>The tests that set this process's own PSModulePath, kept away from everything else while they run.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModulePathEnvironmentCollection
{
    public const string Name = "module path environment";
}
