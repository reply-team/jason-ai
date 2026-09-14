using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

public class OptionsHotReloadTests
{
    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hand_edit_of_the_user_settings_reaches_the_running_runtime()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":7}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>();
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":9}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentValue.TickSeconds != 9 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal(9, monitor.CurrentValue.TickSeconds);
    }

    [Fact]
    public async Task A_setting_the_runtime_cannot_work_with_stops_the_start_and_leaves_the_lock_free()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":0}}""");

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => RuntimeHost.StartAsync(dir.Paths, Quiet, Ct));

        Assert.Contains("Dispatcher:TickSeconds", failure.Message, StringComparison.Ordinal);

        // The failed start gave everything back: the next runtime acquires the instance lock as usual.
        using var free = InstanceLock.Acquire(dir.Paths);
        Assert.NotNull(free);
    }
}
