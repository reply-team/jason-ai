using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Tests.Dispatch;
using static Jason.Runtime.Tests.Plugins.Subscriptions.SubscriptionsBed;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The scan's subscription step, last after the notifier: it reads, and offers the runner each subscribed kind that has a
/// line after its position and no chain in flight, the kind whose next line is oldest first, while the runner has a free
/// child. It starts nothing while the dispatcher drains or is stopped.
/// </summary>
public class SubscriptionScanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_kinds_with_a_due_line_are_offered_oldest_line_first_while_the_runner_has_a_free_child()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed, Requested, Created));
        Lines(harness, Requested, Failed, Requested, Created);
        await CursorAsync(harness, Failed);
        await CursorAsync(harness, Requested);
        await CursorAsync(harness, Created);

        Assert.Equal(2, await harness.SubscriptionScanAsync(Ct));

        Assert.Equal([Requested, Failed], harness.SubscriptionRunner.InFlight);
    }

    [Fact]
    public async Task A_kind_at_the_head_or_with_a_chain_in_flight_is_not_offered()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.MaxParallel = 4;
        Load(harness, Subscriber("watcher", Failed, Requested));
        var lines = Lines(harness, Failed, Requested);
        await CursorAsync(harness, Failed, after: lines[0]);
        await CursorAsync(harness, Requested);

        Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));

        Assert.Equal([Requested], harness.SubscriptionRunner.InFlight);
    }

    /// <summary>A position whose kind no chain of the plugins in force holds is the load's to remove, never the scan's to run.</summary>
    [Fact]
    public async Task A_kind_no_chain_holds_is_not_offered()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Requested, Failed);
        await CursorAsync(harness, Requested);
        await CursorAsync(harness, Failed);

        Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));

        Assert.Equal([Failed], harness.SubscriptionRunner.InFlight);
    }

    /// <summary>
    /// A chain starts only when every member can run on this machine: the kind waits at its line, nothing is spent, and
    /// the wait is said once for each load, by the kind, the line and the member, and not again at the next scan under
    /// the same load.
    /// </summary>
    [Fact]
    public async Task A_kind_whose_chain_has_an_unavailable_member_is_not_offered_and_its_wait_is_said_once_for_each_load()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("alpha", Failed), Subscriber("beta", PluginStatus.Unavailable, Failed));
        var id = await PublicIdOfAsync(harness, Lines(harness, Failed)[0]);
        await CursorAsync(harness, Failed);
        var said = $"Subscription kind {Failed} waits at line {id} on unavailable beta";

        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));
        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));

        Assert.Empty(harness.SubscriptionRunner.InFlight);
        Assert.Empty(harness.Handlers.Requests);
        Assert.True(harness.SubscriptionRunner.HasFreeChild);
        Assert.Single(harness.Logs.All, message => message == said);

        Load(harness, Subscriber("alpha", Failed), Subscriber("beta", PluginStatus.Unavailable, Failed));
        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));
        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));

        Assert.Equal(2, harness.Logs.All.Count(message => message == said));
    }

    [Theory]
    [InlineData(DispatcherState.Draining)]
    [InlineData(DispatcherState.Stopped)]
    public async Task A_dispatcher_that_is_not_running_offers_nothing(DispatcherState state)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        harness.Status.State = state;

        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));

        Assert.Empty(harness.SubscriptionRunner.InFlight);
    }

    /// <summary>A lowered number of children is read at the next scan: nothing more is offered until enough chains end.</summary>
    [Fact]
    public async Task The_number_of_children_is_read_at_each_scan()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed, Requested, Created));
        Lines(harness, Failed, Requested, Created);
        await CursorAsync(harness, Failed);
        await CursorAsync(harness, Requested);
        await CursorAsync(harness, Created);
        harness.Subscriptions.CurrentValue.MaxParallel = 1;

        Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
        harness.Subscriptions.CurrentValue.MaxParallel = 3;
        Assert.Equal(2, await harness.SubscriptionScanAsync(Ct));

        Assert.Equal([Failed, Requested, Created], harness.SubscriptionRunner.InFlight);
    }

    /// <summary>The scan's report counts the kinds the step offered, and the step runs after the notifier.</summary>
    [Fact]
    public async Task The_scan_reports_the_kinds_it_offered()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Subscribed);
        Assert.Equal([Failed], harness.SubscriptionRunner.InFlight);
    }
}
