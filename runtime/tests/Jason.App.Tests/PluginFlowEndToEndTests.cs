using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.App.Tests;

/// <summary>
/// A plugin through the shipped executable and nothing else: a package copied into the data directory, a
/// runtime started in the background, the package listed with its digest, a reload that refuses a broken
/// manifest and leaves the running snapshot exactly as it was, the repaired package activated, and one
/// invocation that actually runs.
/// </summary>
/// <remarks>
/// Every step is a child <c>jason</c> process except the invocation itself, which the test performs by building
/// the runtime's own invoker over the same data directory. That is deliberate: no production surface may invoke
/// a plugin directly — there is no <c>plugin.invoke</c> operation, because an unmanaged side-effect path outside
/// routing, pinning and approvals is exactly what this design refuses. The child under test is still the real
/// thing: the same <c>jason</c> executable, in plugin-host mode, started by the runtime's own invoker.
/// </remarks>
public class PluginFlowEndToEndTests
{
    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The dispatcher stays off — nothing here is work — and the grants are what a user writes by hand after
    /// reading a plugin's manifest: declaration is not permission.
    /// </summary>
    private const string Settings = """
        {"Dispatcher":{"Enabled":false},"Plugins":{"Grants":{"fake-provider":{"Exec":["*"],"Env":["FAKE_TOKEN"]}}}}
        """;

    private const string PluginId = "fake-provider";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_package_is_installed_listed_refused_repaired_and_invoked_through_the_shipped_executable()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-plugins", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var paths = new JasonPaths(root);
        await File.WriteAllTextAsync(paths.UserSettingsFile, Settings, Ct);

        // Installing a plugin is copying its directory. There is no install verb, and none is needed.
        var package = paths.PluginPackageDirectory(PluginId);
        Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", PluginId), package);

        var pids = new HashSet<int>();
        try
        {
            AssertSuccess(await JasonAsync(root, "runtime", "start"));
            var descriptor = ReadDescriptor(paths) ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
            pids.Add(descriptor.Pid);

            // The package was on disk before the runtime was, so the startup load is what is active.
            var listed = Json(await Ok(JasonAsync(root, "plugin", "list")));
            var plugin = Assert.Single(listed["plugins"]!.AsArray())!;
            Assert.Equal(PluginId, (string?)plugin["id"]);
            Assert.Equal("valid", (string?)plugin["status"]);
            Assert.Empty(plugin["problems"]!.AsArray());
            var digest = (string)plugin["digest"]!;
            Assert.StartsWith("sha256:", digest, StringComparison.Ordinal);
            Assert.Equal("dotnet", (string?)Assert.Single(plugin["capabilities"]!["exec"]!["granted"]!.AsArray()));
            var startupSnapshot = (string)listed["snapshot"]!["id"]!;

            // A manifest edited into nonsense. A reload refuses the whole set rather than activate part of it.
            var manifest = Path.Combine(package, "plugin.yaml");
            var original = await File.ReadAllTextAsync(manifest, Ct);
            await File.WriteAllTextAsync(manifest, original.Replace("kind: provider", "kind: bogus", StringComparison.Ordinal), Ct);

            var refused = await JasonAsync(root, "plugin", "reload");
            Assert.Equal(ExitCodes.ApiError, refused.ExitCode);
            var error = Json(refused)["error"]!;
            Assert.Equal("plugin_reload_rejected", (string?)error["code"]);
            var detail = Assert.Single(error["details"]!.AsArray())!;
            Assert.Equal("fake-provider/plugin.yaml#kind", (string?)detail["field"]);
            Assert.Equal("kind_invalid", (string?)detail["code"]);

            var unchanged = Json(await Ok(JasonAsync(root, "plugin", "list")));
            Assert.Equal(startupSnapshot, (string?)unchanged["snapshot"]!["id"]);
            Assert.Equal("valid", (string?)Assert.Single(unchanged["plugins"]!.AsArray())!["status"]);
            Assert.False((bool)unchanged["last_reload"]!["activated"]!);
            var candidate = Assert.Single(unchanged["last_reload"]!["candidates"]!.AsArray())!;
            Assert.Equal("kind_invalid", (string?)Assert.Single(candidate["problems"]!.AsArray())!["code"]);

            await File.WriteAllTextAsync(manifest, original, Ct);
            var activated = Json(await Ok(JasonAsync(root, "plugin", "reload", "--reason", "repaired the manifest")));
            var reloadedSnapshot = (string)activated["snapshot"]!["id"]!;
            Assert.True((bool)activated["activated"]!);
            Assert.NotEqual(startupSnapshot, reloadedSnapshot);
            Assert.Equal(digest, (string?)Assert.Single(activated["plugins"]!.AsArray())!["digest"]);

            // The roundtrip: the runtime's own invoker, over the same data directory, driving the shipped
            // executable in plugin-host mode.
            var result = await RoundtripAsync(paths);
            if (result.Launch?.Pid is { } child)
            {
                pids.Add(child);
            }

            var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
            Assert.Equal("world", succeeded.Result!["echo"]!["hello"]!.GetValue<string>());
            Assert.Equal(digest, result.Provenance.Digest);
            Assert.Equal("dotnet", result.Launch!.Command[0]);
            Assert.Equal("jason.dll", Path.GetFileName(result.Launch.Command[1]));
            Assert.Equal(0, result.Launch.ExitCode);

            var status = await Ok(JasonAsync(root, "runtime", "status", "--human"));
            Assert.Contains("Plugins:    1 active", status.Output, StringComparison.Ordinal);
            Assert.Contains(reloadedSnapshot, status.Output, StringComparison.Ordinal);

            AssertSuccess(await JasonAsync(root, "runtime", "stop"));
            Assert.False(File.Exists(paths.DescriptorFile));
            Assert.False(IsRunning(descriptor.Pid), "The runtime process was still alive after runtime stop returned.");
        }
        finally
        {
            // Whatever went wrong, nothing this test started is left running.
            if (ReadDescriptor(paths) is { } surviving)
            {
                pids.Add(surviving.Pid);
            }

            foreach (var pid in pids)
            {
                Kill(pid);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A handle may outlive the process that held it; a temporary directory left behind is harmless.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The runtime's invoker, assembled the way the runtime assembles it: the registry fed by one load over the
    /// data directory the background runtime is using, the options bound from the same settings file, the real
    /// search path, and the shipped executable as the child.
    /// </summary>
    private static async Task<PluginInvocationResult> RoundtripAsync(JasonPaths paths)
    {
        var configuration = JasonConfiguration.Build(paths, null);
        var settings = new PluginsOptions();
        configuration.GetSection(PluginsOptions.Section).Bind(settings);
        (configuration as IDisposable)?.Dispose();

        var options = new FixedOptions<PluginsOptions>(settings);
        var loader = new PluginLoader(
            paths,
            new ExecutableResolver(new EnvironmentSearchPath()),
            options,
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance);

        var load = await loader.LoadAsync(SnapshotSource.Reload, Ct);
        Assert.NotNull(load.Snapshot);
        var registry = new PluginRegistry(TimeProvider.System);
        registry.Replace(load.Snapshot, load.Report);

        var invoker = new PluginInvoker(
            registry,
            new JasonDllLocator(),
            options,
            paths,
            RuntimeInfo.Create(),
            TimeProvider.System,
            NullLogger<PluginInvoker>.Instance);

        return await invoker.InvokeAsync(
            new PluginInvocationRequest(PluginId, "echo.run", new JsonObject { ["hello"] = "world" }, null, "att_01K0E2E"),
            Ct);
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task<CliResult> Ok(Task<CliResult> step)
    {
        var result = await step;
        AssertSuccess(result);
        return result;
    }

    private static void AssertSuccess(CliResult result) =>
        Assert.True(
            result.ExitCode == ExitCodes.Success && result.Error.Length == 0,
            $"'jason {result.Command}' exited with {result.ExitCode}.{Environment.NewLine}stdout: {result.Output}{Environment.NewLine}stderr: {result.Error}");

    private static JsonObject Json(CliResult result) => JsonNode.Parse(result.Output)!.AsObject();

    /// <summary>One CLI step: the shipped executable, the isolated data directory, and everything it said.</summary>
    private static async Task<CliResult> JasonAsync(string root, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "jason.dll"));
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[JasonPaths.DataDirectoryVariable] = root;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The jason executable could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);
        var command = string.Join(' ', args);

        await process.WaitForExitAsync(Ct).WaitAsync(StepTimeout, Ct);
        return new CliResult(
            process.ExitCode,
            (await output.WaitAsync(StepTimeout, Ct)).Trim(),
            (await error.WaitAsync(StepTimeout, Ct)).Trim(),
            command);
    }

    private static RuntimeDescriptor? ReadDescriptor(JasonPaths paths)
    {
        try
        {
            return JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllBytes(paths.DescriptorFile), JasonJson.Options);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // Already gone, which is the state this is trying to reach.
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error, string Command);

    /// <summary>
    /// The shipped executable as this test can start it: the test host is not <c>jason</c>, so the invoker's own
    /// locator would name the test runner. The child is still the real executable, copied next to the tests.
    /// </summary>
    private sealed class JasonDllLocator : IPluginHostLocator
    {
        public IReadOnlyList<string> Command { get; } = ["dotnet", Path.Combine(AppContext.BaseDirectory, "jason.dll")];
    }

    /// <summary>The options a running runtime would monitor, held still: this test changes no setting mid-flight.</summary>
    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
