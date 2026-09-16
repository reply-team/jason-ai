using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The machine as a Reply test needs to see it. The stand-in vendor CLI is named <c>reply</c>, so the official
/// package's own manifest resolves to it unchanged — and that is exactly why the search path may not end in the
/// machine's own <c>PATH</c>: a developer's workstation can have a real <c>reply</c> on it, installed by npm and
/// signed into a real account, and a test that forgot to put the stand-in first would then reach it. So the path
/// here is built rather than inherited, and <see cref="AssertInsideTheTestTree"/> is what every Reply test uses
/// to prove, before it runs anything, that what was resolved is the stand-in and nothing else.
/// </summary>
public static class ReplyPlugins
{
    /// <summary>The program name the package declares, which is also this stand-in's assembly name.</summary>
    public const string ExecutableName = "reply";

    /// <summary>The official package's id, which is also its directory name under <c>plugins/</c>.</summary>
    public const string PluginId = "reply";

    /// <summary>
    /// The marketplace package as MSBuild copies it next to the tests. It is linked in rather than found by
    /// walking up from the working directory, so no test depends on where it happens to be run from.
    /// </summary>
    public static string PackageSource => Path.Combine(TestTree, "Fixtures", "plugins", PluginId);

    /// <summary>The package's error table, which is data and is read as data by the tests that hold it to the contracts.</summary>
    public static string ErrorTableModule => Path.Combine(PackageSource, "modules", "errors.js");

    /// <summary>Copies the official package into an installation's plugin directory and answers with its root.</summary>
    public static string Install(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var root = paths.PluginPackageDirectory(PluginId);
        TestPlugins.Copy(PackageSource, root);
        return root;
    }

    /// <summary>
    /// A runtime with the official package installed, granted the one program it declares, and resolving
    /// executables on this class's own search path. Before it answers, the resolved program is held to the test
    /// tree: a resolution that escaped would not be a wrong answer, it would be a call to somebody's real
    /// account, so it fails here rather than in whatever the test went on to do.
    /// </summary>
    public static async Task<RuntimeApiFixture> StartAsync(CancellationToken cancellationToken, Action<JasonPaths>? prepare = null)
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            cancellationToken,
            prepare: paths =>
            {
                Install(paths);
                TestPlugins.Grant(paths, PluginId, exec: [ExecutableName]);
                prepare?.Invoke(paths);
            },
            configureServices: services =>
            {
                services.AddSingleton<ISearchPath>(SearchPath);

                // The shipped executable in plugin-host mode, as the tests can reach it: an invocation of this
                // package runs a real child process, the same one an installation would run.
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
            });

        try
        {
            var registry = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, cancellationToken);
            var plugin = Assert.Single(registry.Plugins, listed => listed.Id == PluginId);
            AssertInsideTheTestTree(Assert.Single(plugin.Capabilities.Exec!.Requested).Path);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>Where the tests and everything referenced by them live: the one tree an executable may come from.</summary>
    public static string TestTree => AppContext.BaseDirectory;

    public static string ExecutablePath =>
        Path.Combine(TestTree, ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    /// <summary>
    /// The stand-in's own directory, and then only what a .NET program needs to start at all. The machine's
    /// <c>PATH</c> is never part of it, so no program the operator installed can answer for a name a Reply test
    /// resolves.
    /// </summary>
    public static ISearchPath SearchPath { get; } = new TestSearchPath
    {
        Path = string.Join(Path.PathSeparator, Directories()),
    };

    /// <summary>Whether a resolved program is one of ours rather than one the machine happens to hold.</summary>
    public static bool IsInsideTheTestTree(string? path) =>
        path is not null
        && Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(TestTree).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Fails the test unless the program that was resolved lies under the test tree. A Reply test asserts this
    /// before it invokes anything: a resolution that escaped would not be a wrong answer, it would be a call to
    /// somebody's real account.
    /// </summary>
    public static void AssertInsideTheTestTree(string? path) =>
        Assert.True(
            IsInsideTheTestTree(path),
            $"'{path ?? "<nothing>"}' is not inside the test tree '{TestTree}'; a Reply test may only ever start the stand-in.");

    private static IEnumerable<string> Directories()
    {
        yield return TestTree;

        // Whatever a .NET apphost may need to find its own runtime, derived rather than read off the machine's
        // search path: the muxer this run was started through, the root the operator pointed at, and the root
        // above the shared framework this very process loaded.
        var host = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var roots = new List<string>();

        var process = Environment.ProcessPath;
        if (process is not null && Path.GetFileName(process).Equals(host, StringComparison.OrdinalIgnoreCase))
        {
            roots.Add(Path.GetDirectoryName(process)!);
        }

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } configured)
        {
            roots.Add(configured);
        }

        if (Path.GetDirectoryName(typeof(object).Assembly.Location) is { Length: > 0 } framework)
        {
            roots.Add(Path.GetFullPath(Path.Combine(framework, "..", "..", "..")));
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (File.Exists(Path.Combine(root, host)) && seen.Add(Path.GetFullPath(root)))
            {
                yield return Path.GetFullPath(root);
            }
        }
    }
}
