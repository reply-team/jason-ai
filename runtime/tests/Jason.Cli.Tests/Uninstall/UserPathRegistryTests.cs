using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Jason.Cli.Uninstall;
using Jason.Cli.Tests.Documentation;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// This account's Path, read, edited and written back the way the registry holds it — run for real, against a
/// key of the test's own.
/// </summary>
/// <remarks>
/// <para>
/// A fresh Windows account's <c>Path</c> is <c>REG_EXPAND_SZ</c> and holds <c>%USERPROFILE%\…</c> entries.
/// The installer, the removal and the <c>path</c> repair all read it through <c>GetEnvironmentVariable</c> and
/// wrote it through the matching setter, which expand on the way in and write <c>REG_SZ</c> on the way out:
/// predicted in writing before a fresh account installed and removed Jason, and measured by that run, where one
/// install turned the whole value into fixed strings and the uninstall left it that way. Every test here that
/// could have caught it read text; these run it.
/// </para>
/// <para>
/// Never against <c>HKCU\Environment</c>. That value belongs to whoever runs the suite, so each test makes a key
/// of its own under <c>HKCU\Software\jason-tests</c>, points the code at it and deletes it. The broadcast the
/// code sends afterwards is the one every environment edit sends, and changes nothing. Every test here skips
/// itself off Windows, which is what the attribute on the class says to the analyzer.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public class UserPathRegistryTests
{
    private const string Directory = @"C:\Users\a\AppData\Local\Programs\jason";

    // --- the removal ----------------------------------------------------------------------------------------

    [Fact]
    public void The_removal_keeps_the_kind_and_the_variables_of_everything_it_does_not_take()
    {
        using var key = ScratchKey.Create();
        var directory = key.Installed();
        key.SetPath(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + directory + @";%USERPROFILE%\.dotnet\tools", RegistryValueKind.ExpandString);
        var remover = InstallationRemovers.ForThisMachine(key.SubKey);

        var plan = remover.ReadPathEntry(directory);
        var outcome = remover.RemovePathEntry(plan);

        Assert.True(plan.Ours);
        Assert.Contains("%USERPROFILE%", plan.RegistryValue, StringComparison.Ordinal);
        Assert.True(outcome.Removed);
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;%USERPROFILE%\.dotnet\tools", RegistryValueKind.ExpandString), key.ReadPath());
    }

    /// <summary>A value somebody had already flattened stays flat: the kind is the value's own, not a preference.</summary>
    [Fact]
    public void A_plain_string_stays_a_plain_string()
    {
        using var key = ScratchKey.Create();
        var directory = key.Installed();
        key.SetPath(@"C:\tools;" + directory, RegistryValueKind.String);
        var remover = InstallationRemovers.ForThisMachine(key.SubKey);

        remover.RemovePathEntry(remover.ReadPathEntry(directory));

        Assert.Equal((@"C:\tools", RegistryValueKind.String), key.ReadPath());
    }

    /// <summary>
    /// A Path holding nothing but the directory goes away with it: it is what the account had before the
    /// installer created one.
    /// </summary>
    [Fact]
    public void A_path_holding_only_that_directory_is_removed_rather_than_left_empty()
    {
        using var key = ScratchKey.Create();
        var directory = key.Installed();
        key.SetPath(directory, RegistryValueKind.ExpandString);
        var remover = InstallationRemovers.ForThisMachine(key.SubKey);

        remover.RemovePathEntry(remover.ReadPathEntry(directory));

        Assert.Null(key.ReadPath());
    }

    /// <summary>
    /// An entry for a directory that holds more than this installer writes is not Jason's to take off, whoever
    /// put Jason in it. A build copied "somewhere on your PATH" lands where everything else is on the Path through
    /// the same entry — this account's <c>WindowsApps</c>, on every account's default Path — and
    /// <c>install.ps1 -InstallDir</c> into a directory already on the Path writes nothing there. Taking the entry
    /// off took everything else in the directory off with Jason.
    /// </summary>
    [Fact]
    public void An_entry_for_a_directory_that_holds_more_than_jason_is_left_on_the_path()
    {
        using var key = ScratchKey.Create();
        var shared = key.Installed();
        File.WriteAllText(Path.Combine(shared, "winget.exe"), "somebody else's");
        var value = @"%USERPROFILE%\.dotnet\tools;" + shared;
        key.SetPath(value, RegistryValueKind.ExpandString);
        var remover = InstallationRemovers.ForThisMachine(key.SubKey);

        var plan = remover.ReadPathEntry(shared);
        var outcome = remover.RemovePathEntry(plan);

        Assert.False(plan.Ours);
        Assert.NotNull(plan.Persisted);
        Assert.False(outcome.Removed);
        Assert.Contains("holds more than this installer writes there", outcome.Note, StringComparison.Ordinal);
        Assert.Equal((value, RegistryValueKind.ExpandString), key.ReadPath());
    }

    /// <summary>
    /// And the executable an upgrade replaced while it was running is the installer's own file: it does not make
    /// the directory anybody else's.
    /// </summary>
    [Fact]
    public void The_executable_an_upgrade_replaced_does_not_make_the_directory_somebody_elses()
    {
        using var key = ScratchKey.Create();
        var directory = key.Installed();
        File.WriteAllText(Path.Combine(directory, "jason.previous.exe"), "the one before");
        key.SetPath(directory, RegistryValueKind.ExpandString);

        Assert.True(InstallationRemovers.ForThisMachine(key.SubKey).ReadPathEntry(directory).Ours);
    }

    // --- the installer's line and the repair's, which are one text -------------------------------------------

    /// <summary>
    /// The line <c>jason status</c> prints on Windows keeps the kind, keeps the variables and appends the
    /// directory once — and typed a second time, it changes nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shells))]
    public void The_repair_appends_once_and_keeps_the_kind_and_the_variables(string shell)
    {
        using var key = ScratchKey.Create();
        key.SetPath(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;%USERPROFILE%\.local\bin", RegistryValueKind.ExpandString);
        var directory = key.NewDirectory();
        var command = PathEntry.RegistryCommand(directory, key.SubKey);

        var said = Run(shell, $"{command}; {command}; @($env:Path -split ';' | Where-Object {{ $_ -ieq '{directory}' }}).Count");

        Assert.Equal(
            (@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;%USERPROFILE%\.local\bin;" + directory, RegistryValueKind.ExpandString),
            key.ReadPath());
        Assert.Equal("1", said.Trim());
    }

    /// <summary>An account with no Path at all gets one, of the kind Windows itself would have created.</summary>
    [Theory]
    [MemberData(nameof(Shells))]
    public void The_repair_creates_a_missing_path_as_an_expandable_one(string shell)
    {
        using var key = ScratchKey.Create();
        var directory = key.NewDirectory();

        Run(shell, PathEntry.RegistryCommand(directory, key.SubKey));

        Assert.Equal((directory, RegistryValueKind.ExpandString), key.ReadPath());
    }

    /// <summary>
    /// A directory the Path already names through a variable is the same directory, so nothing is appended:
    /// comparing the stored text would have added it a second time.
    /// </summary>
    /// <remarks>
    /// Through a variable this test sets itself. It was <c>%TEMP%</c>, on the assumption that the variable
    /// expands to exactly the prefix <see cref="Path.GetTempPath"/> returns; on the Windows CI runner the two
    /// spell that directory differently, so the entry and the directory were different text and the repair
    /// appended it — correctly, by its own rule, which compares what Windows reads rather than resolving
    /// one spelling of a path into another.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Shells))]
    public void The_repair_finds_the_directory_behind_a_variable(string shell)
    {
        using var key = ScratchKey.Create();
        var directory = key.NewDirectory();
        var spelled = $"%{SpelledRoot}%\\{Path.GetFileName(directory)}";
        key.SetPath(spelled, RegistryValueKind.ExpandString);

        Run(shell, PathEntry.RegistryCommand(directory, key.SubKey), new() { [SpelledRoot] = Path.GetDirectoryName(directory)! });

        Assert.Equal((spelled, RegistryValueKind.ExpandString), key.ReadPath());
    }

    /// <summary>The variable that test spells its directory with, set for the shell it runs and nowhere else.</summary>
    private const string SpelledRoot = "JASON_TESTS_SPELLED_ROOT";

    public static TheoryData<string> Shells()
    {
        var shells = new TheoryData<string>();
        if (!OperatingSystem.IsWindows())
        {
            // Registered anyway so the theory is not empty; each case skips itself off Windows.
            shells.Add("pwsh");
            return shells;
        }

        shells.Add("pwsh");
        shells.Add(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
        return shells;
    }

    private static string Run(string shell, string command, Dictionary<string, string>? environment = null)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The repair edits the Windows registry, so it runs where there is one.");
        var executable = shell == "pwsh" ? OnPath("pwsh.exe") : shell;
        Assert.SkipWhen(executable is null || !File.Exists(executable), $"{shell} is not on this machine.");

        var start = new ProcessStartInfo(executable!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? [])
        {
            start.Environment[name] = value;
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{shell} did not finish in two minutes.");
        }

        Assert.True(process.ExitCode == 0 && stderr.Result.Length == 0, $"{shell} exited {process.ExitCode}: {stderr.Result}");
        return stdout.Result;
    }

    private static string? OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>A key of one test's own under <c>HKCU\Software\jason-tests</c>, and a directory to name in it.</summary>
    private sealed class ScratchKey : IDisposable
    {
        private readonly TempTree _tree = new();

        private const string Parent = @"Software\jason-tests";

        private ScratchKey(string subKey) => SubKey = subKey;

        public string SubKey { get; }

        public static ScratchKey Create()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "An account's Path is a registry value only on Windows.");
            var subKey = $@"{Parent}\{Guid.NewGuid():N}";
            Registry.CurrentUser.CreateSubKey(subKey).Dispose();
            return new ScratchKey(subKey);
        }

        public string NewDirectory() => _tree.NewDirectory("jason");

        /// <summary>A directory of its own holding what the installer writes there, and nothing else.</summary>
        public string Installed()
        {
            var directory = NewDirectory();
            File.WriteAllText(Path.Combine(directory, "jason.exe"), "not really a program");
            return directory;
        }

        public void SetPath(string raw, RegistryValueKind kind)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue("Path", raw, kind);
        }

        public (string Raw, RegistryValueKind Kind)? ReadPath()
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string raw
                ? (raw, key.GetValueKind("Path"))
                : null;
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(SubKey, throwOnMissingSubKey: false);

            // And the parent, once no test is using it: an empty key left in somebody's registry is still
            // something a test suite left there.
            try
            {
                Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
            }
            catch (InvalidOperationException)
            {
                // Another test's key is still in it; the last one out removes it.
            }

            _tree.Dispose();
        }
    }
}
