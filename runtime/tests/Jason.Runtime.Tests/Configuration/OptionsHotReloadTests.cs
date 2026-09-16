using System.Text.Json.Nodes;
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
    public async Task An_edited_grant_is_seen_by_the_runtime_that_will_resolve_it_at_the_next_reload()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Grants":{"fake":{"Env":["A"]}}}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<PluginsOptions>>();
        Assert.Equal(["A"], monitor.CurrentValue.Grants["fake"].Env);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Grants":{"fake":{"Env":["A","B"]}}}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentValue.Grants["fake"].Env.Count != 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal(["A", "B"], monitor.CurrentValue.Grants["fake"].Env);
    }

    /// <summary>
    /// Routes are read rather than bound, so the change token is registered by hand; this is what says the hand
    /// did it. A route still only takes effect when a reload freezes it — what must reach a running runtime is
    /// the edited file, so that the reload has something new to freeze.
    /// </summary>
    [Fact]
    public async Task An_edited_route_is_seen_by_the_runtime_that_will_freeze_it_at_the_next_reload()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Routes":{"Default":{"Plugin":"fake-provider","Binding":{"workspace":"west"}}}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<RoutesOptions>>();
        Assert.Equal("fake-provider", monitor.CurrentValue.Default!.Plugin);
        Assert.Equal("west", (string?)Assert.IsType<JsonObject>(monitor.CurrentValue.Default.Binding)["workspace"]);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Routes":{"Default":{"Plugin":"other-provider"}}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentValue.Default!.Plugin != "other-provider" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal("other-provider", monitor.CurrentValue.Default!.Plugin);
        Assert.Null(monitor.CurrentValue.Default.Binding);
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
