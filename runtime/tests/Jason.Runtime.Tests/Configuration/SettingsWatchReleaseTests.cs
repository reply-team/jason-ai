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

        Assert.Equal(before + 1, running);
        Assert.Equal(before, after);
    }

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

    /// <summary>The inotify instances this process holds open, as its file descriptors name them.</summary>
    private static int InotifyInstances() =>
        new DirectoryInfo("/proc/self/fd").EnumerateFileSystemInfos().Count(link =>
        {
            try
            {
                return link.LinkTarget == "anon_inode:inotify";
            }
            catch (IOException)
            {
                // A descriptor closed between the listing and the reading of its link.
                return false;
            }
        });

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
