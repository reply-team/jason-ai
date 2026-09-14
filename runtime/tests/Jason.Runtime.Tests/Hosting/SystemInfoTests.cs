using Jason.Contracts.Api;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Hosting;

public class SystemInfoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fixture_the_test_wrote_no_settings_for_runs_without_a_dispatcher()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        // Nothing scans behind the test's back: work seeded here stays where the test put it.
        Assert.Equal(DispatcherState.Disabled, info.Dispatcher.State);
        Assert.Equal(0, info.Dispatcher.Scans);
    }

    [Fact]
    public async Task System_info_reports_the_dispatcher_even_when_it_is_turned_off()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false}}"""));

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.Equal(DispatcherState.Disabled, info.Dispatcher.State);
        Assert.Equal(10, info.Dispatcher.TickSeconds);
        Assert.Equal(4, info.Dispatcher.MaxParallel);
        Assert.Equal(0, info.Dispatcher.RunningAttempts);
        Assert.Null(info.Dispatcher.LastScanAt);
        Assert.Equal(0, info.Dispatcher.Scans);
    }

    [Fact]
    public async Task The_dispatcher_section_is_read_live_from_the_status_and_the_settings()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false,"TickSeconds":3}}"""));

        var status = fixture.Resolve<DispatcherStatus>();
        status.State = DispatcherState.Running;
        status.MaxParallel = 7;
        status.Scans = 11;
        status.LastScanAt = DateTimeOffset.UnixEpoch;
        using var attempt = fixture.Resolve<RunningAttemptRegistry>().Register("att_A", () => { });

        var (_, body) = await fixture.PostAsync(Operations.SystemInfo, null, Ct);

        Assert.Contains("\"state\":\"running\"", body, StringComparison.Ordinal);
        Assert.Contains("\"tick_seconds\":3", body, StringComparison.Ordinal);
        Assert.Contains("\"max_parallel\":7", body, StringComparison.Ordinal);
        Assert.Contains("\"running_attempts\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"scans\":11", body, StringComparison.Ordinal);
    }
}
