using Jason.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Tests.Hosting;

public class RunningRuntimeTests
{
    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stopping_tears_down_even_when_a_hosted_service_refuses_to_stop()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff, Ct);
        var options = Quiet with { ConfigureServices = services => services.AddHostedService<RefusesToStop>() };

        var runtime = await RuntimeHost.StartAsync(dir.Paths, options, Ct);
        Assert.True(File.Exists(dir.Paths.DescriptorFile));

        // The failure is reported to the caller, not swallowed...
        await Assert.ThrowsAnyAsync<Exception>(runtime.StopAsync);

        // ...and the teardown still happened: nothing advertises the dead instance, and the lock is free, so the
        // next runtime takes the same data directory without a fight.
        Assert.False(File.Exists(dir.Paths.DescriptorFile));
        await using var next = await RuntimeHost.StartAsync(dir.Paths, Quiet, Ct);
        Assert.NotEqual(runtime.Descriptor.InstanceId, next.Descriptor.InstanceId);

        // A second stop of the failed runtime is a no-op rather than a second failure.
        await runtime.StopAsync();
    }

    private sealed class RefusesToStop : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("This service will not stop.");
    }
}
