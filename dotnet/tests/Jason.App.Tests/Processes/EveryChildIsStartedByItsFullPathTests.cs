using System.Text.RegularExpressions;
using Jason.App.Tests.Skills;

namespace Jason.App.Tests.Processes;

/// <summary>
/// Every child Jason starts is started by its full path, through either of the door's ways in and under every kind, and
/// the door refuses anything else. A name the operating system looked up
/// would be looked up in places no caller chose: the working folder, and on Windows the folder of the program starting it.
/// </summary>
/// <remarks>
/// The door's refusal is a throw, so what keeps a start from being refused is each caller. This reads the sources for
/// every call of the door and holds them to a table of callers, and each caller to the row that proves the path it passes
/// is a full one. A new call of the door, or a call that passes something else, is a change to this table, and so a
/// change to a row.
/// </remarks>
public partial class EveryChildIsStartedByItsFullPathTests
{
    /// <summary>
    /// The callers of the door: the file under <c>dotnet/src</c>, what the call hands the door as written, and the row that
    /// proves the path it hands is a full one.
    /// </summary>
    private static readonly (string File, string Arguments, string Proof)[] Callers =
    [
        ("Jason.Cli/Autostart/AutostartRegistrars.cs", "command[0], ChildKind.SystemTool",
            "Jason.Cli.Tests/Autostart/AutostartArtifactsTests.cs:Every_command_a_registration_holds_names_its_program_by_its_full_path"),
        ("Jason.Cli/Process/IUrlOpener.cs", "opener",
            "Jason.Cli.Tests/Process/PlatformUrlOpenerTests.cs:The_opener_is_named_by_its_full_path"),
        ("Jason.Cli/Process/ProgramRunner.cs", "fullPath, ChildKind.SystemTool",
            "Jason.Cli.Tests/Process/ProgramRunnerTests.cs:A_bare_name_is_started_by_the_full_path_the_search_path_gives"),
        ("Jason.Cli/Process/RuntimeProcessControl.cs", "fileName, ChildKind.SpeaksToJason",
            "Jason.Cli.Tests/Process/RuntimeProcessControlTests.cs:The_runtime_and_a_command_of_this_product_are_started_by_a_full_path"),
        ("Jason.Cli/Process/RuntimeProcessControl.cs", "command[0], ChildKind.SystemToolWithInput",
            "Jason.Cli.Tests/Process/RuntimeProcessControlTests.cs:The_runtime_and_a_command_of_this_product_are_started_by_a_full_path"),
        ("Jason.Cli/Session/ITerminalStarter.cs", "argv[0]",
            "Jason.Cli.Tests/Session/TerminalPlanTests.cs:Every_terminal_found_is_named_by_its_full_path"),
        ("Jason.Cli/Session/ITerminalStarter.cs", "argv[0], ChildKind.SystemToolWithInput",
            "Jason.Cli.Tests/Session/TerminalPlanTests.cs:Every_terminal_found_is_named_by_its_full_path"),
        ("Jason.Cli/Uninstall/IInstallationRemover.cs", "powershell, ChildKind.SystemToolWithInput",
            "Jason.App.Tests/Processes/EveryChildIsStartedByItsFullPathTests.cs:Windows_PowerShell_is_named_by_its_place_in_the_system_folder"),
        ("Jason.Cli/Uninstall/IInstallationRemover.cs", "powershell, ChildKind.SystemToolWithInput",
            "Jason.App.Tests/Processes/EveryChildIsStartedByItsFullPathTests.cs:Windows_PowerShell_is_named_by_its_place_in_the_system_folder"),
        ("Jason.Cli/Update/AppImageCopy.cs", "appImage, ChildKind.SystemToolWithInput",
            "Jason.Cli.Tests/Update/UpdateCarriedLinuxTests.cs:The_appimage_is_named_by_its_full_path_whatever_its_record_says"),
        ("Jason.Cli/Update/MoveSystem.cs", "setup, ChildKind.SystemToolWithInput",
            "Jason.Cli.Tests/Update/MoveSystemToolsTests.cs:Every_program_a_move_starts_is_named_by_its_full_path"),
        ("Jason.Cli/Update/MoveSystem.cs", "program, ChildKind.SystemToolWithInput",
            "Jason.Cli.Tests/Update/MoveSystemToolsTests.cs:Every_program_a_move_starts_is_named_by_its_full_path"),
        ("Jason.PluginHost/Sdk/ExecService.cs", "executable.Path, ChildKind.SpeaksToJason",
            "Jason.Runtime.Tests/Plugins/ExecutableResolverTests.cs:A_declared_program_is_resolved_to_its_full_path_and_never_from_an_entry_that_is_not_one"),
        ("Jason.Runtime/Execution/AiRoleCommand.cs", "program, ChildKind.SpeaksToJason",
            "Jason.Runtime.Tests/Execution/AiRoleCommandTests.cs:A_role_s_entry_command_is_started_by_its_full_path"),
        ("Jason.Runtime/Notifications/Desktop/ToastLauncher.cs", "launch.Command[0], ChildKind.SpeaksToJason",
            "Jason.Runtime.Tests/Notifications/ToastCommandsTests.cs:Every_toast_is_started_by_the_full_path_of_its_program"),
        ("Jason.Runtime/Plugins/Invocation/PluginInvoker.cs", "command[0], ChildKind.SpeaksToJason",
            "Jason.Runtime.Tests/Plugins/Invocation/ProcessPathLocatorTests.cs:A_process_with_no_path_is_started_through_the_muxer_the_search_path_gives"),
        ("Jason.Runtime/Plugins/Registry/ExecutableResolver.cs", "resolved.Path, ChildKind.SystemToolWithInput",
            "Jason.Runtime.Tests/Plugins/ExecutableResolverTests.cs:A_declared_program_is_resolved_to_its_full_path_and_never_from_an_entry_that_is_not_one"),
    ];

    /// <summary>
    /// Every call of the door in the product's sources is a caller in the table, and every caller in the table is a call
    /// there, with what it hands the door as written: seventeen calls in thirteen files. Comments are not calls, and a
    /// qualified name is read as the short one.
    /// </summary>
    [Fact]
    public void Every_call_of_the_door_is_a_caller_in_the_table()
    {
        var sources = Path.Combine(SkillPack.RepositoryRoot(), "dotnet", "src");
        var found = Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(sources, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(file => DoorCalls(File.ReadAllText(file)).Select(call => (Path.GetRelativePath(sources, file).Replace('\\', '/'), call)))
            .Order()
            .ToList();

        Assert.Equal(Callers.Select(caller => (caller.File, caller.Arguments)).Order(), found);
        Assert.Equal((17, 13), (Callers.Length, Callers.Select(caller => caller.File).Distinct().Count()));
    }

    /// <summary>Each caller's row is there to be run: a method of that name in that test file.</summary>
    [Fact]
    public void Every_caller_has_a_row_that_proves_its_path_is_a_full_one()
    {
        var tests = Path.Combine(SkillPack.RepositoryRoot(), "dotnet", "tests");
        var missing = Callers
            .Select(caller => caller.Proof.Split(':'))
            .Where(proof => !File.Exists(Path.Combine(tests, proof[0]))
                || !Regex.IsMatch(File.ReadAllText(Path.Combine(tests, proof[0])), $@"public (?:async Task|void) {Regex.Escape(proof[1])}\("))
            .Select(proof => string.Join(':', proof))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "a caller of the door has no row proving its path: " + string.Join("; ", missing));
    }

    /// <summary>
    /// The two Windows PowerShell children of an uninstall are named by the program's place in the system folder, which on
    /// Windows, the one system they run on, is a full path.
    /// </summary>
    [Fact]
    public void Windows_PowerShell_is_named_by_its_place_in_the_system_folder()
    {
        const string Named = """var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");""";
        var remover = File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "dotnet", "src", "Jason.Cli", "Uninstall", "IInstallationRemover.cs"));

        var named = remover.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("var powershell =", StringComparison.Ordinal)).ToList();
        Assert.Equal([Named, Named], named);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(Path.IsPathFullyQualified(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")));
        }
    }

    /// <summary>What each of the door's calls in one source file hands it, as written, outside comments.</summary>
    private static IEnumerable<string> DoorCalls(string source)
    {
        foreach (var line in source.Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith("//", StringComparison.Ordinal)))
        {
            foreach (Match match in DoorCall().Matches(line))
            {
                var depth = 0;
                for (var index = match.Index + match.Length - 1; index < line.Length; index++)
                {
                    depth += line[index] switch { '(' => 1, ')' => -1, _ => 0 };
                    if (depth == 0)
                    {
                        yield return line[(match.Index + match.Length)..index].Replace("Jason.Contracts.Discovery.", string.Empty, StringComparison.Ordinal);
                        break;
                    }
                }
            }
        }
    }

    [GeneratedRegex(@"(?:Jason\.Contracts\.Discovery\.)?ChildProcess\.StartInfo\(")]
    private static partial Regex DoorCall();
}
