using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Jason.Cli.Tests;

/// <summary>
/// The programs the rows build as stand-ins for real ones, and the guard that keeps a stand-in a script starts from opening
/// a console window on the machine the rows run on.
/// </summary>
/// <remarks>
/// <para>
/// A script that starts a program with Start-Process, as install.ps1 starts the setup and the uninstall's waiter starts the
/// uninstaller, gives a console program a console window of its own, which Windows shows and puts in front of whatever the
/// person at the keyboard is typing into. The real setup and uninstaller are windowed programs and get none, so a stand-in
/// for either is built as a windowed program too (the C# compiler's <c>/target:winexe</c>), and a row of its class reads its
/// subsystem from the built program's header. A stand-in for jason, which the scripts run inside their own console as the
/// real console program is, stays a console program and opens no window of its own.
/// </para>
/// <para>
/// Every place in the rows that builds a program names its target here, with the reason, so that a new stand-in cannot be
/// built as a console program for a windowed part unnoticed: <see cref="Builders"/> is held to the sources.
/// </para>
/// </remarks>
public sealed class StandInPrograms
{
    /// <summary>The subsystem a built program declares in its header.</summary>
    public static Subsystem SubsystemOf(byte[] program)
    {
        using var stream = new MemoryStream(program, writable: false);
        return new PEHeaders(stream).PEHeader!.Subsystem;
    }

    /// <summary>
    /// Each source file under <c>dotnet/tests</c> that builds a program with the C# compiler, and the targets it may name:
    /// a target given as a word, or <c>"/target:" + target</c> where the file's own row holds each build's subsystem.
    /// </summary>
    private static readonly Dictionary<string, string[]> Builders = new(StringComparer.Ordinal)
    {
        // The setup and the window as windowed programs, the carried jason as a console program, chosen per build and held
        // by Windows_the_setup_and_the_window_the_script_starts_are_windowed_programs_and_the_carried_jason_a_console_one.
        ["Jason.Cli.Tests/Documentation/InstallDesktopScriptTests.cs"] = ["\"/target:\" + target"],
        // jason.exe, which the scripts run inside their own console and read the output of, as the real console program.
        ["Jason.Cli.Tests/Documentation/InstallNoticesTests.cs"] = ["\"/target:exe\""],
        // An uninstaller run the proof's wait for an uninstaller is handed, started by the row's harness with Start-Process:
        // its subsystem held by The_Windows_proof_helpers_after_an_uninstall_run_as_the_proof_runs_them.
        ["Jason.Cli.Tests/Documentation/InstallScriptTests.cs"] = ["\"/target:winexe\""],
        // The uninstaller, which the waiter starts with Start-Process: held by The_uninstaller_stand_in_the_waiter_starts_is_a_windowed_program.
        ["Jason.Cli.Tests/Uninstall/UninstallCarriedTests.cs"] = ["\"/target:winexe\""],
    };

    [Fact]
    public void Every_program_the_rows_build_is_named_with_its_target()
    {
        var tests = Path.Combine(RepositoryRoot(), "dotnet", "tests");
        var found = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(tests, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal) || relative == "Jason.Cli.Tests/StandInPrograms.cs")
            {
                continue;
            }

            var targets = Regex.Matches(File.ReadAllText(file), @"""/target:(?:[a-z]+""|""\s*\+\s*target)").Select(match => match.Value).ToArray();
            if (targets.Length > 0)
            {
                found[relative] = targets;
            }
        }

        Assert.Equal(Builders.Keys.Order(StringComparer.Ordinal), found.Keys.Order(StringComparer.Ordinal));
        foreach (var (file, targets) in found)
        {
            Assert.All(targets, target => Assert.Contains(target, Builders[file]));
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
