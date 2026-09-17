using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests.Plugins.Reply;

namespace Jason.App.Tests.EndToEnd;

/// <summary>One CLI step: what the shipped program was asked, and everything it said about it.</summary>
internal sealed record CliResult(int ExitCode, string Output, string Error, string Command);

/// <summary>
/// The settings one golden-path test wants, as the few things that actually differ between them. Everything
/// else is the shipped default on purpose: a proof that ran under a configuration nobody ships would be a proof
/// about that configuration.
/// </summary>
/// <param name="Routed">Whether a global default route to the official package is in the file at all.</param>
/// <param name="TickSeconds">One second, so a test waits for work rather than for a clock.</param>
/// <param name="ProviderHeartbeatSeconds">
/// Absent by default, which is what a shipped runtime does: a provider attempt reports nothing and is only ever
/// lost to its lease. A test that needs an in-flight attempt to be given up on in seconds rather than in ten
/// minutes sets the smallest interval the validator accepts.
/// </param>
/// <param name="MaxAttempts">The shipped three, so a retriable end is retried the way it would be in the field.</param>
internal sealed record SettingsShape(
    bool Routed = true,
    int TickSeconds = 1,
    int? ProviderHeartbeatSeconds = null,
    int MaxAttempts = 3);

/// <summary>
/// One installation of Jason, as a test owns it: its own data directory, its own Reply account, and the
/// processes it has learned about. Disposing it leaves nothing running and nothing on disk.
/// </summary>
internal sealed class Installation : IDisposable
{
    private readonly HashSet<int> _children = [];

    internal Installation(string root, ReplyAccount account)
    {
        Root = root;
        Paths = new JasonPaths(root);
        Account = account;
    }

    public string Root { get; }

    public JasonPaths Paths { get; }

    /// <summary>The provider at the far end: a directory of JSON files and a log of everything it was asked.</summary>
    public ReplyAccount Account { get; }

    /// <summary>The runtime process the descriptor last named, which is what a kill and a stop are aimed at.</summary>
    public int? Pid { get; internal set; }

    /// <summary>
    /// A child this installation started that a test may have to wait for. A runtime killed without its tree
    /// leaves the plugin host and the vendor CLI alive; on Windows a live child holding files under the data
    /// directory makes the cleanup fail, so what is known is recorded and settled rather than hoped about.
    /// </summary>
    public void Track(int pid) => _children.Add(pid);

    public IReadOnlyCollection<int> Children => _children;

    public void Dispose()
    {
        foreach (var pid in _children)
        {
            GoldenPath.Kill(pid);
        }

        if (Pid is { } runtime)
        {
            GoldenPath.Kill(runtime);
        }

        Account.Dispose();
        GoldenPath.Delete(Root);
    }
}

/// <summary>
/// The process boundary, shared by every golden-path test: the shipped program started as a real process
/// against a real vendor CLI, driven the way the walkthrough drives it. Nothing here knows what any single
/// proof is about — it starts, stops, kills, asks and reads.
/// </summary>
/// <remarks>
/// The program is started as <c>dotnet jason.dll</c>, the repository's own documented development command and
/// the way every other end-to-end test here starts it. The search path put in front of a child is this test
/// tree first, because the stand-in vendor CLI is installed beside these tests rather than onto the machine —
/// and because a developer's workstation can hold a real <c>reply</c> signed into a real account, every test
/// that loads the official package asserts the program it resolved lies inside this tree before it invokes
/// anything.
/// </remarks>
internal static class GoldenPath
{
    /// <summary>The package this milestone is named after, by the id that is also its directory name.</summary>
    public const string PluginId = "reply";

    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    public static TimeSpan Step { get; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a claim, a child process and an answer may take between them.</summary>
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(90);

    /// <summary>Where the tests and everything referenced by them live: the one tree a program may come from.</summary>
    public static string TestTree => AppContext.BaseDirectory;

    public static string Assembly => Path.Combine(TestTree, "jason.dll");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A fresh installation: an empty data directory and an account nobody has called yet.</summary>
    public static Installation Create(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-" + name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return new Installation(root, new ReplyAccount());
    }

    /// <summary>The settings file this installation runs under, written where the runtime reads it.</summary>
    public static async Task WriteSettingsAsync(Installation it, SettingsShape shape)
    {
        ArgumentNullException.ThrowIfNull(it);
        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, Settings(it, shape), Ct);
    }

    public static string Settings(Installation it, SettingsShape shape)
    {
        ArgumentNullException.ThrowIfNull(it);
        ArgumentNullException.ThrowIfNull(shape);

        var providerOp = new JsonObject { ["MaxAttempts"] = shape.MaxAttempts };
        if (shape.ProviderHeartbeatSeconds is { } heartbeat)
        {
            providerOp["HeartbeatSeconds"] = heartbeat;
        }

        var settings = new JsonObject
        {
            ["Dispatcher"] = new JsonObject
            {
                ["TickSeconds"] = shape.TickSeconds,
                ["RetryDelaySeconds"] = 0,
                ["ProviderOp"] = providerOp,
            },
            ["Plugins"] = new JsonObject
            {
                // Declaration is not permission: the package asks for one program by name, and this is the
                // grant that answers it. Nothing wider is granted, because nothing wider is asked for.
                ["Grants"] = new JsonObject
                {
                    [PluginId] = new JsonObject { ["Exec"] = new JsonArray(PluginId) },
                },
            },
        };

        if (shape.Routed)
        {
            settings["Routes"] = new JsonObject
            {
                ["Default"] = new JsonObject
                {
                    ["Plugin"] = PluginId,
                    ["Binding"] = new JsonObject { ["profile"] = it.Account.Profile },
                },
            };
        }

        return settings.ToJsonString(JasonJson.Options);
    }

    /// <summary>Installing a plugin is copying its directory. There is no install verb, and none is needed.</summary>
    public static void InstallReplyPackage(Installation it)
    {
        ArgumentNullException.ThrowIfNull(it);
        Copy(Path.Combine(TestTree, "Fixtures", "plugins", PluginId), it.Paths.PluginPackageDirectory(PluginId));
    }

    /// <summary>Starts the runtime the way the walkthrough starts it, and remembers which process that is.</summary>
    public static async Task<RuntimeDescriptor> StartAsync(Installation it)
    {
        ArgumentNullException.ThrowIfNull(it);
        AssertSuccess(await JasonAsync(it, "runtime", "start"));
        var descriptor = ReadDescriptor(it.Paths)
            ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
        it.Pid = descriptor.Pid;
        return descriptor;
    }

    /// <summary>Stops it the same way, and proves it is gone: a stop that left the process alive proves nothing.</summary>
    public static async Task StopAsync(Installation it)
    {
        ArgumentNullException.ThrowIfNull(it);
        AssertSuccess(await JasonAsync(it, "runtime", "stop"));
        Assert.False(File.Exists(it.Paths.DescriptorFile), "The descriptor outlived the runtime it described.");
        if (it.Pid is { } pid)
        {
            Assert.False(IsRunning(pid), "The runtime process was still alive after runtime stop returned.");
        }

        it.Pid = null;
    }

    /// <summary>
    /// A crash, not a shutdown: the runtime process alone, never its tree. What it started keeps running, which
    /// is exactly the state a real crash leaves behind and the only state in which a restart's answer means
    /// anything.
    /// </summary>
    public static void Kill(Installation it)
    {
        ArgumentNullException.ThrowIfNull(it);
        var pid = it.Pid ?? throw new InvalidOperationException("There is no runtime process to kill.");
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: false);
        process.WaitForExit();
    }

    /// <summary>
    /// The child one attempt launched, where the runtime has written it down. A provider attempt records its
    /// launch — command, pid and exit code — when the invocation ends, so an attempt that is still in flight
    /// has no pid to learn: what a test can observe about a running child is what the child itself says, which
    /// is why the hold writes a marker file rather than the runtime being asked.
    /// </summary>
    public static async Task<int?> ChildPidAsync(Installation it, string workItemId)
    {
        var item = Json(await Ok(JasonAsync(it, "workitem", "get", workItemId)));
        var pid = item["attempts"]?.AsArray()
            .Select(attempt => (int?)attempt?["launch"]?["pid"])
            .LastOrDefault(value => value is not null);

        if (pid is { } found)
        {
            it.Track(found);
        }

        return pid;
    }

    /// <summary>
    /// Waits for everything this installation started to go, and ends what has not. A bounded wait rather than
    /// a kill outright: a child that is finishing its call is doing the very thing the proof is about.
    /// </summary>
    public static async Task SettleAsync(Installation it, TimeSpan within)
    {
        ArgumentNullException.ThrowIfNull(it);
        var deadline = DateTime.UtcNow.Add(within);
        foreach (var pid in it.Children)
        {
            while (IsRunning(pid) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, Ct);
            }

            Kill(pid);
        }
    }

    /// <summary>Reads one work item until it looks the way the test expects, because real processes take time.</summary>
    public static async Task<JsonObject> PollAsync(
        Installation it,
        string workItemId,
        Func<JsonObject, bool> expected,
        bool snapshots = false)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var deadline = DateTime.UtcNow.Add(Patience);
        JsonObject item;
        do
        {
            item = Json(await Ok(snapshots
                ? JasonAsync(it, "workitem", "get", workItemId, "--snapshots")
                : JasonAsync(it, "workitem", "get", workItemId)));
            if (expected(item))
            {
                return item;
            }

            await Task.Delay(200, Ct);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Work item {workItemId} never got where it was going: {item.ToJsonString(JasonJson.Options)}");
        return item;
    }

    public static bool Finished(string? status) => status is "succeeded" or "failed" or "expired" or "cancelled";

    /// <summary>
    /// A work item that ran. The whole item is the failure message: an end-to-end run that went wrong somewhere
    /// in a child process says why in the attempt it left behind, and nowhere a reader of the test output could
    /// otherwise reach.
    /// </summary>
    public static void AssertSucceeded(JsonObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Assert.True(
            (string?)item["status"] == "succeeded",
            $"The work item did not succeed: {item.ToJsonString(JasonJson.Options)}");
    }

    /// <summary>
    /// Fails unless the program the package resolved to lies under the test tree. Every test that loads the
    /// official package asserts this before it invokes anything: a resolution that escaped would not be a wrong
    /// answer, it would be a call to somebody's real account.
    /// </summary>
    public static void AssertTheStandInAnswered(JsonObject plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var resolved = (string?)plugin["capabilities"]?["exec"]?["requested"]?.AsArray()[0]?["path"];
        Assert.False(string.IsNullOrEmpty(resolved), "The package's own program was not resolved at all.");
        Assert.StartsWith(
            Path.GetFullPath(TestTree).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            Path.GetFullPath(resolved!),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>One CLI step: the shipped program, this installation's data directory, and everything it said.</summary>
    public static async Task<CliResult> JasonAsync(Installation it, params string[] args)
    {
        ArgumentNullException.ThrowIfNull(it);
        ArgumentNullException.ThrowIfNull(args);

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        start.ArgumentList.Add(Assembly);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[JasonPaths.DataDirectoryVariable] = it.Root;

        // The package declares the stand-in vendor CLI by its own name, and that program is installed beside
        // these tests rather than onto the machine, so the runtime this starts has to be told where to look.
        start.Environment["PATH"] = TestTree + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        // And the account it works in: the platform's own configuration variable, pointed at this test's store.
        // No vendor-named variable is ever set — none of them may enter the runtime in the first place.
        foreach (var (name, value) in it.Account.Variables)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The jason executable could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);
        var command = string.Join(' ', args);

        await process.WaitForExitAsync(Ct).WaitAsync(Step, Ct);
        return new CliResult(
            process.ExitCode,
            (await output.WaitAsync(Step, Ct)).Trim(),
            (await error.WaitAsync(Step, Ct)).Trim(),
            command);
    }

    public static async Task<CliResult> Ok(Task<CliResult> step)
    {
        var result = await step;
        AssertSuccess(result);
        return result;
    }

    public static void AssertSuccess(CliResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Assert.True(
            result.ExitCode == ExitCodes.Success && result.Error.Length == 0,
            $"'jason {result.Command}' exited with {result.ExitCode}.{Environment.NewLine}stdout: {result.Output}{Environment.NewLine}stderr: {result.Error}");
    }

    public static JsonObject Json(CliResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonNode.Parse(result.Output)?.AsObject()
            ?? throw new InvalidOperationException($"'jason {result.Command}' answered with no JSON object: {result.Output}");
    }

    public static RuntimeDescriptor? ReadDescriptor(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
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

    public static bool IsRunning(int pid)
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

    public static void Kill(int pid)
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

    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Removes a temporary directory, waiting a little for whatever still holds a file in it. A runtime killed
    /// without its tree leaves a plugin host and a vendor CLI finishing their work, and on Windows a live child
    /// holding one file is enough to refuse the whole delete — so this retries rather than failing a proof over
    /// its own cleanup, and gives up quietly if something outlives the wait.
    /// </summary>
    public static void Delete(string directory)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    // A handle may outlive the process that held it; a temporary directory left behind is
                    // harmless, and failing a test over it would be a test about the operating system.
                    return;
                }

                Thread.Sleep(100);
            }
        }
    }
}
