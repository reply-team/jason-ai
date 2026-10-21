using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>Tests that count what the whole process holds, run while nothing else in it starts a runtime.</summary>
[CollectionDefinition(nameof(SettingsWatchCollection), DisableParallelization = true)]
public sealed class SettingsWatchCollection;

/// <summary>
/// The runtime watches its settings file so that edits apply live, and on Linux that watch is an inotify instance from a
/// per-user budget every process of the account shares. It is given back when the runtime is done with it, never left to
/// a garbage collection: the configuration disposes the file provider it watches through, the runtime disposes its
/// configuration when it stops — even when disposing the host fails — and the web host starts no watch of its own.
/// </summary>
/// <remarks>
/// A disposed file provider refuses to watch again, which is how a test sees on every platform that the watch was given
/// back. The count of inotify instances is Linux's own word for it, and the only one for the web host's watch, which no
/// object the runtime keeps can be asked about.
/// </remarks>
[Collection(nameof(SettingsWatchCollection))]
public sealed class SettingsWatchReleaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Disposing_the_configuration_gives_back_the_settings_file_watch()
    {
        using var data = new TempDataDir();
        var configuration = JasonConfiguration.Build(data.Paths, shippedSettingsDirectory: null);
        var watched = SettingsFileProvider(configuration, data.Paths);
        _ = watched.Provider.Watch(watched.Path);

        configuration.Dispose();

        Assert.Throws<ObjectDisposedException>(() => watched.Provider.Watch(watched.Path));
    }

    [Fact]
    public async Task A_stopped_runtime_has_given_back_its_settings_file_watch()
    {
        using var data = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);
        var watched = SettingsFileProvider(runtime.Configuration, data.Paths);

        await runtime.StopAsync();

        Assert.Throws<ObjectDisposedException>(() => watched.Provider.Watch(watched.Path));
    }

    /// <summary>
    /// Disposing the host can fail — a service of its own throwing as it is disposed — and the configuration and the log
    /// are given back all the same: the rest of the stop does not depend on the host going quietly.
    /// </summary>
    [Fact]
    public async Task A_stop_whose_host_fails_to_dispose_still_gives_back_the_watch_and_the_log()
    {
        using var data = new TempDataDir();
        var options = TestRuntimeOptions.Quiet with
        {
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                services.AddHostedService<RefusesToBeDisposed>();
            },
        };
        var runtime = await RuntimeHost.StartAsync(data.Paths, options, Ct);
        var watched = SettingsFileProvider(runtime.Configuration, data.Paths);

        await Assert.ThrowsAnyAsync<Exception>(runtime.StopAsync);

        Assert.Throws<ObjectDisposedException>(() => watched.Provider.Watch(watched.Path));
        var logs = Directory.GetFiles(data.Paths.LogsDirectory);
        Assert.Contains(logs, file => Integration.FakeHostRuntime.ReadShared(file).Contains("\"@mt\":\"Runtime {InstanceId} stopped\"", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
        {
            // The log writer has let go of its file: nobody else may hold it open while it is opened for itself alone.
            foreach (var file in logs)
            {
                using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
    }

    [Fact]
    public async Task On_Linux_a_runtime_holds_one_inotify_instance_while_it_runs_and_gives_it_back_when_it_stops()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "inotify instances are Linux's; elsewhere the watch is held otherwise, and the tests above see it given back.");

        using var data = new TempDataDir();

        // Whatever an earlier test left to the finalizer is finalized now, so the count is this process's settled one.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = InotifyInstances();

        var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);
        var running = InotifyInstances();
        await runtime.StopAsync();
        var after = InotifyInstances();

        // The stop removes the watch at once, and the instance's descriptor is closed a moment later, off the stop's own
        // path: an instance still open and watching nothing just after the stop is on its way back, so its release is
        // waited for, a bounded while. That it is given back at all is what the row holds.
        var released = after;
        for (var look = 0; look < ReleaseLooks && released.Count != before.Count; look++)
        {
            await Task.Delay(ReleaseLookSpacing, Ct);
            released = InotifyInstances();
        }

        // A count that is off says which instance it is and whether it went on its own: every descriptor at each moment,
        // and what the ones opened since the start watch.
        var inventory = Inventory(data.Paths.Root, before, running, after, released);
        Assert.True(running.Count == before.Count + 1, inventory);
        Assert.True(released.Count == before.Count, inventory);
    }

    /// <summary>How many times the count is read again after a stop, a tenth of a second apart: five seconds for the instance to be back.</summary>
    private const int ReleaseLooks = 50;

    private static readonly TimeSpan ReleaseLookSpacing = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// A start refused before the host is built gives the settings file's watch back: a test process refuses starts many
    /// times over, and a watch left behind by each would be spent from the account's budget until the process ends.
    /// </summary>
    [Theory]
    [InlineData("another runtime holds the data directory")]
    [InlineData("a setting is the wrong type")]
    public async Task A_start_refused_before_the_host_is_built_gives_back_the_settings_file_watch(string refusal)
    {
        using var data = new TempDataDir();
        RunningRuntime? first = null;
        if (refusal == "another runtime holds the data directory")
        {
            first = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(data.Paths.UserSettingsFile)!);
            await File.WriteAllTextAsync(data.Paths.UserSettingsFile, """{"Dispatcher":{"MaxParallel":"many"}}""", Ct);
        }

        try
        {
            IConfigurationRoot? built = null;
            var options = TestRuntimeOptions.Quiet with { ConfigurationBuilt = configuration => built = configuration };

            await Assert.ThrowsAnyAsync<Exception>(() => RuntimeHost.StartAsync(data.Paths, options, Ct));

            var watched = SettingsFileProvider(Assert.IsAssignableFrom<IConfigurationRoot>(built), data.Paths);
            Assert.Throws<ObjectDisposedException>(() => watched.Provider.Watch(watched.Path));
        }
        finally
        {
            if (first is not null)
            {
                await first.StopAsync();
            }
        }
    }

    /// <summary>And so does one refused once the host is built: here, by a service of its own that will not start.</summary>
    [Fact]
    public async Task A_start_refused_by_the_host_gives_back_the_settings_file_watch()
    {
        using var data = new TempDataDir();
        IConfigurationRoot? built = null;
        var options = TestRuntimeOptions.Quiet with
        {
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                services.AddHostedService<RefusesToStart>();
            },
            ConfigurationBuilt = configuration => built = configuration,
        };

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => RuntimeHost.StartAsync(data.Paths, options, Ct));
        Assert.Equal("this service refuses to start", refused.Message);

        var watched = SettingsFileProvider(Assert.IsAssignableFrom<IConfigurationRoot>(built), data.Paths);
        Assert.Throws<ObjectDisposedException>(() => watched.Provider.Watch(watched.Path));
    }

    /// <summary>The settings file's provider and the path it watches the file by, relative to its root.</summary>
    private static (PhysicalFileProvider Provider, string Path) SettingsFileProvider(IConfigurationRoot configuration, JasonPaths paths)
    {
        var provider = Assert.Single(configuration.Providers.OfType<JsonConfigurationProvider>());
        var files = Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider);
        Assert.Equal(Path.GetFullPath(paths.UserSettingsFile), Path.GetFullPath(Path.Combine(files.Root, provider.Source.Path!)));
        return (files, provider.Source.Path!);
    }

    /// <summary>
    /// The inotify instances this process holds open, as its file descriptors name them, each with the watches its
    /// fdinfo lists while it is open.
    /// </summary>
    private static SortedDictionary<int, IReadOnlyList<string>> InotifyInstances()
    {
        var instances = new SortedDictionary<int, IReadOnlyList<string>>();
        foreach (var link in new DirectoryInfo("/proc/self/fd").EnumerateFileSystemInfos())
        {
            try
            {
                if (link.LinkTarget == "anon_inode:inotify" && int.TryParse(link.Name, CultureInfo.InvariantCulture, out var descriptor))
                {
                    instances[descriptor] = [.. File.ReadLines($"/proc/self/fdinfo/{descriptor}").Where(line => line.StartsWith("inotify ", StringComparison.Ordinal))];
                }
            }
            catch (IOException)
            {
                // A descriptor closed between the listing and the reading of its link or its fdinfo.
            }
        }

        return instances;
    }

    /// <summary>
    /// The instances at each moment by descriptor, and for each one opened or changed since the start what it watches at
    /// each moment after: the kernel's inode in hexadecimal, resolved to its path when it is under the test's data
    /// directory.
    /// </summary>
    private static string Inventory(
        string root,
        SortedDictionary<int, IReadOnlyList<string>> before,
        SortedDictionary<int, IReadOnlyList<string>> running,
        SortedDictionary<int, IReadOnlyList<string>> after,
        SortedDictionary<int, IReadOnlyList<string>> released)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"inotify instances: {before.Count} before the start, {running.Count} while it ran, {after.Count} after it stopped, {released.Count} once its release was waited for.");
        foreach (var (moment, instances) in new[] { ("before", before), ("running", running), ("after", after), ("released", released) })
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{moment}: {string.Join(", ", instances.Keys)}");
        }

        var since = new[] { ("running", running), ("after", after), ("released", released) };
        var changed = since
            .SelectMany(moment => moment.Item2)
            .Where(instance => !before.TryGetValue(instance.Key, out var was) || !was.SequenceEqual(instance.Value))
            .Select(instance => instance.Key)
            .Distinct()
            .Order();
        var paths = Inodes(root);
        foreach (var descriptor in changed)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"descriptor {descriptor}, opened or changed since the start:");
            foreach (var (moment, instances) in since)
            {
                if (!instances.TryGetValue(descriptor, out var watches))
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  {moment}: closed");
                    continue;
                }

                if (watches.Count == 0)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  {moment}: open, watching nothing");
                }

                foreach (var watch in watches)
                {
                    var inode = Regex.Match(watch, @"\bino:([0-9a-f]+)");
                    var path = inode.Success && paths.TryGetValue(Convert.ToUInt64(inode.Groups[1].Value, 16), out var found) ? found : "not under the data directory";
                    text.AppendLine(CultureInfo.InvariantCulture, $"  {moment}: {watch} -> {path}");
                }
            }
        }

        return text.ToString();
    }

    /// <summary>Every inode under the directory, with its path, as find reads them.</summary>
    private static Dictionary<ulong, string> Inodes(string root)
    {
        var start = new System.Diagnostics.ProcessStartInfo("find") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in new[] { root, "-xdev", "-printf", "%i %p\n" })
        {
            start.ArgumentList.Add(argument);
        }

        using var find = System.Diagnostics.Process.Start(start)!;
        var listed = find.StandardOutput.ReadToEnd();
        find.WaitForExit();
        var inodes = new Dictionary<ulong, string>();
        foreach (var line in listed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0 && ulong.TryParse(line.AsSpan(0, space), CultureInfo.InvariantCulture, out var inode))
            {
                inodes.TryAdd(inode, line[(space + 1)..]);
            }
        }

        return inodes;
    }

    /// <summary>A service of the host's that throws when the host starts it.</summary>
    private sealed class RefusesToStart : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("this service refuses to start");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A service of the host's that throws when the host disposes it.</summary>
    private sealed class RefusesToBeDisposed : IHostedService, IAsyncDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => throw new InvalidOperationException("this service refuses to be disposed");
    }
}
