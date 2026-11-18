using System.Reflection;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.Processes;

/// <summary>
/// Every Windows PowerShell this product starts is started without the PSModulePath it inherited: the compiled product,
/// read. The door removes the variable for a child whose program is Windows PowerShell (<see cref="ChildProcess"/>), and
/// <see cref="NoChildGetsAWindowTests"/> holds every start to the door; what remains is a Windows PowerShell that another
/// program of ours starts, the console host a session opens. So every method that names Windows PowerShell's program in a
/// literal is listed here, each with what keeps it from the inherited variable, and a method that names it anew fails
/// this row until it is read and listed.
/// </summary>
public class WindowsPowerShellChildrenTests
{
    private static readonly Assembly[] Product =
    [
        typeof(ModeRouter).Assembly,
        typeof(Jason.Cli.CliApp).Assembly,
        typeof(Jason.Runtime.Hosting.RuntimeHost).Assembly,
        typeof(Jason.PluginHost.PluginHostMode).Assembly,
        typeof(ChildProcess).Assembly,
    ];

    /// <summary>How each method that names Windows PowerShell keeps it from the inherited module path.</summary>
    private enum Kept
    {
        /// <summary>The rule itself: the door's test of a program's name.</summary>
        TheRule,

        /// <summary>Started at the door, with Windows PowerShell as the program, so the door removes the variable.</summary>
        AtTheDoor,

        /// <summary>Handed to the console host as the host it starts, so the console host's start has no variable to hand on.</summary>
        HostedByTheConsoleHost,

        /// <summary>No program: a name an agent host gives its own shell tool.</summary>
        NotAProgram,
    }

    private static readonly Dictionary<string, Kept> Named = new(StringComparer.Ordinal)
    {
        ["Jason.Contracts.Discovery.ChildProcess.IsWindowsPowerShell"] = Kept.TheRule,
        ["Jason.Cli.Uninstall.InstallationRemovers+MachineRemover.StartCleanup"] = Kept.AtTheDoor,
        ["Jason.Cli.Uninstall.InstallationRemovers+MachineRemover.HandToUninstaller"] = Kept.AtTheDoor,
        ["Jason.Cli.Session.TerminalPlan.Shell"] = Kept.HostedByTheConsoleHost,
        ["Jason.Runtime.Execution.Hosts.CopilotCliHost.Compose"] = Kept.NotAProgram,
    };

    [Fact]
    public void Every_method_that_names_Windows_PowerShell_keeps_it_from_the_inherited_module_path()
    {
        var naming = Product
            .SelectMany(IlReferences.Methods)
            .Where(method => IlReferences.Strings(method).Any(NamesWindowsPowerShell))
            .ToList();

        Assert.Equal(string.Join(Environment.NewLine, Named.Keys.Order(StringComparer.Ordinal)), string.Join(Environment.NewLine, naming.Select(Name).Distinct().Order(StringComparer.Ordinal)));
        foreach (var method in naming)
        {
            var calls = IlReferences.Called(method);
            var door = calls.Any(called => called.DeclaringType == typeof(ChildProcess) && called.Name == nameof(ChildProcess.StartInfo));
            switch (Named[Name(method)])
            {
                case Kept.AtTheDoor:
                    Assert.True(door, $"{Name(method)} names Windows PowerShell and does not start it at the door.");
                    break;
                case Kept.HostedByTheConsoleHost:
                case Kept.NotAProgram:
                case Kept.TheRule:
                    Assert.False(door, $"{Name(method)} starts a child at the door, so it is no {Named[Name(method)]}.");
                    break;
            }
        }
    }

    /// <summary>Windows PowerShell's program, its file name with or without the extension, or its folder's name.</summary>
    private static bool NamesWindowsPowerShell(string literal) =>
        literal.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
        || literal.Equals("powershell", StringComparison.OrdinalIgnoreCase)
        || literal.Equals("WindowsPowerShell", StringComparison.OrdinalIgnoreCase)
        || literal.Contains(@"\powershell.exe", StringComparison.OrdinalIgnoreCase)
        || literal.Contains("/powershell.exe", StringComparison.OrdinalIgnoreCase);

    private static string Name(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";
}
