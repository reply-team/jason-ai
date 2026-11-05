using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Contracts.Subscriptions;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Plugins.Subscriptions.FakeSubscriptionInvoker;
using static Jason.Runtime.Tests.Plugins.Subscriptions.SubscriptionsBed;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The runner: one turn of a kind takes its due lines one at a time, and each line's chain hands the line to every
/// subscriber in the order the load froze, each handler given the envelope the one before returned. A handler that fails
/// in a final class ends the chain for that line with one <c>subscription_failed</c> line; a transient one is tried again
/// after a backoff that holds no child; a stop writes nothing, and the next start takes the line again from the first
/// handler. The position moves only where it still stands as the line's start read it.
/// </summary>
public class SubscriptionRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_handler_of_the_chain_is_handed_the_line_and_the_envelope_the_one_before_returned_in_the_order_the_load_froze()
    {
        using var harness = new DispatchHarness(Noon);
        var snapshot = Load(harness, new Dictionary<string, string[]> { [Failed] = ["beta", "alpha"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        var line = Line(harness, Failed);
        var id = await PublicIdOfAsync(harness, line);
        await CursorAsync(harness, Failed);

        var turn = await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Head), turn);
        Assert.Equal(["beta", "alpha"], harness.Handlers.Invoked);
        var (first, second) = (harness.Handlers.Requests[0], harness.Handlers.Requests[1]);
        Assert.Equal("{}", first.Input["envelope"]!.ToJsonString());
        Assert.Equal($$"""{"beta":"{{id}}#1"}""", second.Input["envelope"]!.ToJsonString());
        foreach (var request in harness.Handlers.Requests)
        {
            Assert.Equal(Failed, request.Operation);
            Assert.Equal(id, request.CorrelationId);
            Assert.Equal(id, request.Door!.LineId);
            Assert.Equal(InvocationPurpose.Subscription, request.Purpose);
            Assert.Equal(snapshot.Id, request.Pinned!.SnapshotId);
            Assert.Equal(request.PluginId, request.Pinned.Plugin.Manifest.Id);
            Assert.Equal(1, request.Input["attempt"]!.GetValue<int>());
            Assert.Equal(id, request.Input["line"]!["id"]!.GetValue<string>());
            Assert.Equal(Failed, request.Input["line"]!["kind"]!.GetValue<string>());
            Assert.Equal("runtime", request.Input["line"]!["actor"]!["id"]!.GetValue<string>());
            Assert.Empty(SubscriptionCatalog.Find("subscription.handle")!.CheckInput(request.Input));
        }

        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
        Assert.Equal((line, id), (run.LineId, run.LinePublicId));
        Assert.Equal($$"""{"beta":"{{id}}#1","alpha":"{{id}}#1"}""", run.Envelope!.ToJsonString());
        Assert.Equal(["beta|succeeded", "alpha|succeeded"], Handled(run));
        Assert.Equal(Noon, run.EndedAt);
        Assert.Null(run.RetryAt);
        Assert.Equal([$"{Failed}|{line}|1"], await PositionsAsync(harness));
        Assert.Empty(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
    }

    [Fact]
    public async Task A_turn_takes_at_most_fifty_lines_and_the_next_takes_the_rest()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        var lines = Lines(harness, [.. Enumerable.Repeat(Failed, SubscriptionRunner.LinesPerTurn + 2)]);
        await CursorAsync(harness, Failed);

        Assert.Equal(new SubscriptionTurn(Failed, 50, 0, SubscriptionTurn.Limit), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        Assert.Equal([$"{Failed}|{lines[49]}|50"], await PositionsAsync(harness));

        Assert.Equal(new SubscriptionTurn(Failed, 2, 0, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        Assert.Equal([$"{Failed}|{lines[^1]}|52"], await PositionsAsync(harness));
        Assert.Equal(52, harness.Handlers.Requests.Count);
    }

    [Theory]
    [InlineData(FailureClass.Permanent, "permanent")]
    [InlineData(FailureClass.Validation, "validation")]
    [InlineData(FailureClass.Blocked, "blocked")]
    [InlineData(FailureClass.Ambiguous, "ambiguous")]
    public async Task A_handler_that_fails_in_a_final_class_ends_the_chain_for_that_line_once_and_the_kind_moves_on(FailureClass failure, string word)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta", "gamma"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed), Subscriber("gamma", Failed));
        var lines = Lines(harness, Failed, Failed);
        var id = await PublicIdOfAsync(harness, lines[0]);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("beta", Failure("watcher_refused", failure));

        Assert.Equal(new SubscriptionTurn(Failed, 2, 0, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Assert.Equal(["alpha", "beta", "alpha", "beta", "gamma"], harness.Handlers.Invoked);
        var runs = await RunsAsync(harness);
        Assert.Equal([SubscriptionRunStatus.Failed, SubscriptionRunStatus.Succeeded], runs.Select(run => run.Status));
        Assert.Equal(("beta", word, "watcher_refused"), (runs[0].FailedPluginId, runs[0].FailureClass, runs[0].FailureCode));
        Assert.Null(runs[0].Envelope);
        Assert.Equal(["alpha|succeeded", $"beta|{word}"], Handled(runs[0]));
        Assert.Equal([$"{Failed}|{lines[1]}|2"], await PositionsAsync(harness));

        var said = Assert.Single(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
        Assert.Equal((ActorType.System, "dispatcher", (int?)null, Failed), (said.ActorType, said.ActorId, said.CampaignId, said.Key));
        Assert.Equal($$"""{"kind":"{{Failed}}","line":"{{id}}","plugin_id":"beta","class":"{{word}}","code":"watcher_refused","attempts":1}""", said.New!.ToJsonString());
        Assert.DoesNotContain(Sentinel, said.New!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, runs[0].Handlers.ToJsonString(), StringComparison.Ordinal);
        Assert.False(harness.Logs.Mentions(Sentinel));
        Assert.Contains(harness.Logs.All, message => message == $"Subscription chain for {Failed} at line {id} failed at beta ({word}, watcher_refused)");
    }

    /// <summary>
    /// Each line's failure is said by a line of its own: two lines of one kind that each end in a final class leave two
    /// <c>subscription_failed</c> lines, keyed by the kind and each naming its own line, the second never refused or
    /// folded into the first.
    /// </summary>
    [Fact]
    public async Task Two_lines_of_one_kind_that_each_fail_are_said_by_two_lines_each_naming_its_own()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("alpha", Failed));
        var lines = Lines(harness, Failed, Failed);
        var first = await PublicIdOfAsync(harness, lines[0]);
        var second = await PublicIdOfAsync(harness, lines[1]);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("alpha", Failure("watcher_refused", FailureClass.Permanent), Failure("watcher_mismatch", FailureClass.Validation));

        Assert.Equal(new SubscriptionTurn(Failed, 2, 0, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Assert.Equal([SubscriptionRunStatus.Failed, SubscriptionRunStatus.Failed], (await RunsAsync(harness)).Select(run => run.Status));
        var said = await JournalAsync(harness, JournalKinds.SubscriptionFailed);
        Assert.Equal([Failed, Failed], said.Select(entry => entry.Key));
        Assert.Equal(
            [
                $$"""{"kind":"{{Failed}}","line":"{{first}}","plugin_id":"alpha","class":"permanent","code":"watcher_refused","attempts":1}""",
                $$"""{"kind":"{{Failed}}","line":"{{second}}","plugin_id":"alpha","class":"validation","code":"watcher_mismatch","attempts":1}""",
            ],
            said.Select(entry => entry.New!.ToJsonString()));
        Assert.Equal([$"{Failed}|{lines[1]}|2"], await PositionsAsync(harness));
    }

    /// <summary>A protocol failure is classed as every invocation's is: a timeout may have acted, so it is ambiguous and final.</summary>
    [Fact]
    public async Task A_protocol_failure_ends_the_chain_in_the_class_the_protocol_gives_it()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("watcher", Protocol(ProtocolCodes.PluginTimeout));

        await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal((SubscriptionRunStatus.Failed, "ambiguous", ProtocolCodes.PluginTimeout), (run.Status, run.FailureClass, run.FailureCode));
        Assert.Single(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
    }

    public static TheoryData<string, InvocationOutcome> AnswersOutsideTheContract => new()
    {
        { "no envelope", Result(new JsonObject { ["seen"] = true }) },
        { "an envelope that is no object", Result(new JsonObject { ["envelope"] = new JsonArray(1, 2) }) },
        { "a member beside the envelope", Result(new JsonObject { ["envelope"] = new JsonObject(), ["more"] = 1 }) },
        { "an identifier pinned", Result(new JsonObject { ["envelope"] = new JsonObject() }, new JsonObject { ["contact"] = "c-1" }) },
    };

    [Theory]
    [MemberData(nameof(AnswersOutsideTheContract))]
    public async Task An_answer_outside_the_contract_ends_the_chain_as_the_runtime_s_validation_code(string what, InvocationOutcome answer)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("alpha", answer);

        await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        Assert.Equal(["alpha"], harness.Handlers.Invoked);
        var run = Assert.Single(await RunsAsync(harness));
        Assert.True(
            (run.Status, run.FailedPluginId, run.FailureClass, run.FailureCode) == (SubscriptionRunStatus.Failed, "alpha", "validation", SubscriptionContract.AnswerInvalid),
            what);
    }

    /// <summary>The bound is the one in force when the line's chain starts, and it is charged to the handler that returned the envelope.</summary>
    [Fact]
    public async Task An_envelope_over_the_bound_ends_the_chain_at_the_handler_that_returned_it_and_the_bound_is_read_at_each_line()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.EnvelopeBytes = 1024;
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        Lines(harness, Failed, Failed);
        await CursorAsync(harness, Failed);
        var large = new JsonObject { ["notes"] = new string('x', 1100) };
        harness.Handlers.Answer("alpha", (_, _) => Envelope((JsonObject)large.DeepClone()));
        harness.Handlers.Answer("alpha", (_, _) =>
        {
            harness.Subscriptions.CurrentValue.EnvelopeBytes = 4096;
            return Envelope((JsonObject)large.DeepClone());
        });

        await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        Assert.Equal(["alpha", "alpha"], harness.Handlers.Invoked);
        var runs = await RunsAsync(harness);
        Assert.Equal(
            [("alpha", "validation", SubscriptionContract.EnvelopeTooLarge), ("alpha", "validation", SubscriptionContract.EnvelopeTooLarge)],
            runs.Select(run => (run.FailedPluginId, run.FailureClass, run.FailureCode)));

        Lines(harness, Failed);
        harness.Handlers.Answer("alpha", (_, _) => Envelope((JsonObject)large.DeepClone()));
        await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        Assert.Equal(SubscriptionRunStatus.Succeeded, (await RunsAsync(harness))[^1].Status);
        Assert.Equal(["alpha", "alpha", "alpha", "beta"], harness.Handlers.Invoked);
    }

    /// <summary>
    /// A transient handler is run again after the backoff, with the envelope as it stood before it: the handlers before it
    /// are not run again, and the wait holds no child.
    /// </summary>
    [Fact]
    public async Task A_transient_handler_is_run_again_after_its_backoff_with_the_envelope_as_it_stood_before_it()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        var line = Line(harness, Failed);
        var id = await PublicIdOfAsync(harness, line);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("beta", Failure("watcher_busy", FailureClass.Transient));

        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Backoff), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        var waiting = Assert.Single(await RunsAsync(harness));
        Assert.Equal((SubscriptionRunStatus.Running, Noon.AddSeconds(30)), (waiting.Status, waiting.RetryAt));
        Assert.Equal(["alpha|succeeded", "beta|transient"], Handled(waiting));
        Assert.Equal([$"{Failed}|0|0"], await PositionsAsync(harness));
        Assert.Equal([Failed], harness.SubscriptionRunner.InFlight);
        Assert.True(harness.SubscriptionRunner.HasFreeChild);

        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Assert.Equal(["alpha", "beta", "beta"], harness.Handlers.Invoked);
        var retry = harness.Handlers.Requests[^1];
        Assert.Equal($$"""{"alpha":"{{id}}#1"}""", retry.Input["envelope"]!.ToJsonString());
        Assert.Equal(2, retry.Input["attempt"]!.GetValue<int>());
        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal((SubscriptionRunStatus.Succeeded, (DateTime?)null), (run.Status, run.RetryAt));
        Assert.Equal($$"""{"alpha":"{{id}}#1","beta":"{{id}}#2"}""", run.Envelope!.ToJsonString());
        Assert.Empty(harness.SubscriptionRunner.InFlight);
    }

    [Fact]
    public async Task Each_backoff_doubles_the_one_before_and_none_is_longer_than_an_hour()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.TransientRetries = 10;
        harness.Subscriptions.CurrentValue.RetryBackoffSeconds = 1000;
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("watcher", Failure("watcher_busy", FailureClass.Transient), Failure("watcher_busy", FailureClass.Transient), Failure("watcher_busy", FailureClass.Transient));

        var waits = new List<double>();
        for (var turn = 0; turn < 3; turn++)
        {
            var start = harness.Clock.Now.UtcDateTime;
            Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
            var due = Assert.Single(await RunsAsync(harness)).RetryAt!.Value;
            waits.Add((due - start).TotalSeconds);
            harness.Clock.Now = new DateTimeOffset(due, TimeSpan.Zero);
        }

        Assert.Equal([1000, 2000, 3600], waits);
    }

    /// <summary>A start that failed this time is transient like any other: retried, and once the retries are spent the line says so.</summary>
    [Fact]
    public async Task A_transient_handler_whose_retries_are_spent_ends_the_chain_in_the_class_transient()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.TransientRetries = 1;
        Load(harness, Subscriber("watcher", Failed));
        var line = Line(harness, Failed);
        var id = await PublicIdOfAsync(harness, line);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("watcher", Protocol(ProtocolCodes.PluginLaunchFailed), Protocol(ProtocolCodes.PluginLaunchFailed));

        Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(SubscriptionTurn.Head, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);

        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal((SubscriptionRunStatus.Failed, "transient", ProtocolCodes.PluginLaunchFailed), (run.Status, run.FailureClass, run.FailureCode));
        var said = Assert.Single(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
        Assert.Equal($$"""{"kind":"{{Failed}}","line":"{{id}}","plugin_id":"watcher","class":"transient","code":"plugin_launch_failed","attempts":2}""", said.New!.ToJsonString());
        Assert.Equal([$"{Failed}|{line}|1"], await PositionsAsync(harness));
    }

    /// <summary>
    /// The number of children caps the handlers running, never the chains: a kind waiting out a backoff holds none, so
    /// another kind's chain runs to its end meanwhile, and the waiting one resumes at the same handler once it is due.
    /// </summary>
    [Fact]
    public async Task A_kind_in_backoff_holds_no_child_and_resumes_at_the_same_handler_while_another_kind_runs_meanwhile()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.MaxParallel = 1;
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta"] }, Subscriber("alpha", Failed, Requested), Subscriber("beta", Failed));
        var failed = Line(harness, Failed);
        var requested = Line(harness, Requested);
        var id = await PublicIdOfAsync(harness, failed);
        await CursorAsync(harness, Failed);
        await CursorAsync(harness, Requested);
        harness.Handlers.Answer("beta", Failure("watcher_busy", FailureClass.Transient));

        Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
        Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);

        Assert.True(harness.SubscriptionRunner.HasFreeChild);
        Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
        Assert.Equal([Failed, Requested], harness.SubscriptionRunner.InFlight);
        Assert.Equal(SubscriptionTurn.Head, (await harness.SubscriptionRunner.RunNextAsync(Requested, Ct))!.Ended);
        Assert.Equal([$"{Requested}|{requested}|1", $"{Failed}|0|0"], await PositionsAsync(harness));

        // Once due, the waiting chain has the first claim on the child: nothing new is offered ahead of it.
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        Lines(harness, Requested);
        Assert.False(harness.SubscriptionRunner.HasFreeChild);
        Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));

        Assert.Equal(SubscriptionTurn.Head, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
        Assert.Equal(["alpha", "beta", "alpha", "beta"], harness.Handlers.Invoked);
        Assert.Equal($$"""{"alpha":"{{id}}#1"}""", harness.Handlers.Requests[^1].Input["envelope"]!.ToJsonString());
        Assert.Equal(Failed, harness.Handlers.Requests[^1].Operation);
        Assert.Equal([SubscriptionRunStatus.Succeeded, SubscriptionRunStatus.Succeeded], (await RunsAsync(harness)).Select(run => run.Status));
    }

    /// <summary>A stop during a backoff writes nothing; the next start takes the record up and runs the line again from the first handler.</summary>
    [Fact]
    public async Task A_start_after_a_stop_during_a_backoff_takes_the_line_up_again_from_the_first_handler()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        var line = Line(harness, Failed);
        var id = await PublicIdOfAsync(harness, line);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("beta", Failure("watcher_busy", FailureClass.Transient));
        Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
        var before = Assert.Single(await RunsAsync(harness));

        var restarted = NewRunner(harness);
        Assert.Equal(SubscriptionTurn.Head, (await restarted.RunNextAsync(Failed, Ct))!.Ended);

        Assert.Equal(["alpha", "beta", "alpha", "beta"], harness.Handlers.Invoked);
        Assert.Equal("{}", harness.Handlers.Requests[2].Input["envelope"]!.ToJsonString());
        Assert.Equal([1, 1, 2, 2], harness.Handlers.Requests.Select(request => request.Input["attempt"]!.GetValue<int>()));
        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal((before.PublicId, SubscriptionRunStatus.Succeeded, (DateTime?)null), (run.PublicId, run.Status, run.RetryAt));
        Assert.Equal(["alpha|succeeded", "beta|transient", "alpha|succeeded", "beta|succeeded"], Handled(run));
        Assert.Equal($$"""{"alpha":"{{id}}#2","beta":"{{id}}#2"}""", run.Envelope!.ToJsonString());
    }

    /// <summary>
    /// A stop while a handler runs writes nothing more: the record keeps the invocation that may have acted, with no
    /// outcome, and the position stays. The next start runs the line again, and every handler is told how many times it
    /// has been handed it.
    /// </summary>
    [Fact]
    public async Task A_stop_during_a_handler_writes_nothing_and_the_next_start_hands_each_handler_its_count()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, new Dictionary<string, string[]> { [Failed] = ["alpha", "beta"] }, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        harness.Handlers.Answer("beta", (_, _) =>
        {
            stop.Cancel();
            return Protocol(ProtocolCodes.PluginKilled);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.SubscriptionRunner.RunNextAsync(Failed, stop.Token));

        var stopped = Assert.Single(await RunsAsync(harness));
        Assert.Equal(SubscriptionRunStatus.Running, stopped.Status);
        Assert.Equal(["alpha|succeeded", "beta|"], Handled(stopped));
        Assert.Equal([$"{Failed}|0|0"], await PositionsAsync(harness));
        Assert.Empty(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
        Assert.Contains(harness.Logs.All, message => message.StartsWith($"Subscription handler alpha for {Failed} ended succeeded", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.All, message => message.StartsWith($"Subscription handler beta for {Failed} ended", StringComparison.Ordinal));

        Assert.Equal(SubscriptionTurn.Head, (await NewRunner(harness).RunNextAsync(Failed, Ct))!.Ended);

        Assert.Equal([1, 1, 2, 2], harness.Handlers.Requests.Select(request => request.Input["attempt"]!.GetValue<int>()));
        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
        Assert.Equal(["alpha|succeeded", "beta|", "alpha|succeeded", "beta|succeeded"], Handled(run));
    }

    /// <summary>The retries spent are read from the record, so neither a backoff nor a restart gives a handler more.</summary>
    [Fact]
    public async Task A_restart_gives_a_transient_handler_no_more_retries_than_it_had_left()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            harness.Handlers.Answer("watcher", Failure("watcher_busy", FailureClass.Transient));
        }

        Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(SubscriptionTurn.Backoff, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);

        Assert.Equal(SubscriptionTurn.Head, (await NewRunner(harness).RunNextAsync(Failed, Ct))!.Ended);

        Assert.Equal([1, 2, 3, 4], harness.Handlers.Requests.Select(request => request.Input["attempt"]!.GetValue<int>()));
        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal((SubscriptionRunStatus.Failed, "transient"), (run.Status, run.FailureClass));
        Assert.Equal(4, (await JournalAsync(harness, JournalKinds.SubscriptionFailed)).Single().New!["attempts"]!.GetValue<int>());
    }

    /// <summary>A member that cannot run on this machine is no outcome: the kind waits at its line, said once, and nothing is spent.</summary>
    [Fact]
    public async Task A_kind_whose_chain_has_an_unavailable_member_waits_at_its_line_until_a_load_makes_it_available()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("alpha", Failed), Subscriber("beta", PluginStatus.Unavailable, Failed));
        var line = Line(harness, Failed);
        var id = await PublicIdOfAsync(harness, line);
        await CursorAsync(harness, Failed);

        Assert.Equal(new SubscriptionTurn(Failed, 0, 0, SubscriptionTurn.Waiting), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        Assert.Equal(new SubscriptionTurn(Failed, 0, 0, SubscriptionTurn.Waiting), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Assert.Empty(harness.Handlers.Requests);
        Assert.Empty(await RunsAsync(harness));
        Assert.Equal([$"{Failed}|0|0"], await PositionsAsync(harness));
        Assert.Single(harness.Logs.All, message => message == $"Subscription kind {Failed} waits at line {id} on unavailable beta");

        Load(harness, Subscriber("alpha", Failed), Subscriber("beta", Failed));
        Assert.Equal(SubscriptionTurn.Head, (await harness.SubscriptionRunner.RunNextAsync(Failed, Ct))!.Ended);
        Assert.Equal(["alpha", "beta"], harness.Handlers.Invoked);
    }

    /// <summary>
    /// No cascades: a line a plugin wrote through its door, and a line about a work item a plugin created, are passed over
    /// with no chain and no record, the position moving past them. A line about work a role created is handed out as any.
    /// </summary>
    [Fact]
    public async Task A_line_a_plugin_wrote_or_about_work_a_plugin_created_is_passed_over_and_one_about_a_role_s_work_is_not()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        var byPlugin = Line(harness, Failed, actorType: "plugin", actorId: "watcher");
        var aboutPluginWork = Line(harness, Failed, workItem: await WorkItemAsync(harness, ActorType.Plugin));
        var aboutRoleWork = Line(harness, Failed, workItem: await WorkItemAsync(harness, ActorType.Role));
        var afterIt = Line(harness, Failed, actorType: "plugin", actorId: "watcher");
        await CursorAsync(harness, Failed);

        Assert.Equal(new SubscriptionTurn(Failed, 1, 3, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        var handled = Assert.Single(harness.Handlers.Requests);
        Assert.Equal(await PublicIdOfAsync(harness, aboutRoleWork), handled.CorrelationId);
        Assert.Equal([aboutRoleWork], (await RunsAsync(harness)).Select(run => run.LineId));
        Assert.Equal([$"{Failed}|{afterIt}|3"], await PositionsAsync(harness));
        Assert.True(byPlugin < aboutPluginWork);
    }

    /// <summary>Ended records beyond the bound are deleted when the kind's next record is made; the one in flight never is.</summary>
    [Fact]
    public async Task The_oldest_ended_records_beyond_the_bound_are_deleted_when_the_next_starts()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.RunRecordsPerKind = 2;
        Load(harness, Subscriber("watcher", Failed, Requested));
        var lines = Lines(harness, Failed, Failed, Failed, Failed);
        var other = Line(harness, Requested);
        await CursorAsync(harness, Failed);
        await CursorAsync(harness, Requested);
        await harness.SubscriptionRunner.RunNextAsync(Requested, Ct);
        harness.Handlers.Answer("watcher", Failure("watcher_refused", FailureClass.Permanent));

        await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        Assert.Equal([lines[1], lines[2], lines[3], other], (await RunsAsync(harness)).Select(run => run.LineId).Order());

        // While the next line's chain runs, its record is in flight beside the two newest ended ones of its kind.
        List<int>? during = null;
        harness.Handlers.Answer("watcher", (_, _) =>
        {
            during = [.. RunsAsync(harness).GetAwaiter().GetResult().Select(run => run.LineId).Order()];
            return Envelope(new JsonObject());
        });
        var next = Line(harness, Failed);
        await harness.SubscriptionRunner.RunNextAsync(Failed, Ct);

        Assert.Equal([lines[2], lines[3], other, next], during);
    }

    public static TheoryData<string> MovesUnderAChain => ["rewound", "removed", "removed and placed again"];

    /// <summary>
    /// The position a line's chain started from is the one its end moves: one moved, removed, or removed and placed again
    /// under it — the same revision, a new row — leaves the record superseded, writes no failure line, and ends the turn.
    /// </summary>
    [Theory]
    [MemberData(nameof(MovesUnderAChain))]
    public async Task A_position_that_moved_under_a_chain_supersedes_its_record_and_ends_the_turn(string move)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed, Failed);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("watcher", (_, _) =>
        {
            using var db = harness.Open();
            switch (move)
            {
                case "rewound":
                    db.Database.ExecuteSqlRaw("UPDATE subscription_cursors SET after_line = 0, revision = revision + 1");
                    break;
                case "removed":
                    db.Database.ExecuteSqlRaw("DELETE FROM subscription_cursors");
                    break;
                default:
                    db.Database.ExecuteSqlRaw("DELETE FROM subscription_cursors");
                    db.Database.ExecuteSqlRaw($"INSERT INTO subscription_cursors (kind, after_line, revision, created_at, updated_at) VALUES ('{Failed}', 0, 0, '2026-11-04 12:00:00', '2026-11-04 12:00:00')");
                    break;
            }

            return Failure("watcher_refused", FailureClass.Permanent);
        });

        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Superseded), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal((SubscriptionRunStatus.Superseded, Noon), (run.Status, run.EndedAt));
        Assert.Null(run.FailedPluginId);
        Assert.Empty(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
        Assert.Equal(move == "removed" ? [] : [$"{Failed}|0|{(move == "rewound" ? 1 : 0)}"], await PositionsAsync(harness));
    }

    /// <summary>
    /// What a start finds in flight from the runtime before: a record at its kind's next line is taken up and its retry
    /// time cleared; any other — a kind with no position, a line behind the position — is ended superseded.
    /// </summary>
    [Fact]
    public async Task A_start_takes_up_the_record_at_its_kind_s_next_line_and_ends_every_other_left_running()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed, Requested));
        var failed = Lines(harness, Failed, Failed);
        Line(harness, Requested);
        await CursorAsync(harness, Failed);
        await using (var db = harness.Open())
        {
            db.SubscriptionRuns.AddRange(
                Running(Failed, failed[0], retryAt: Noon.AddMinutes(5)),
                Running(Requested, 99),
                Running(Created, 98));
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(new SubscriptionTurn(Requested, 0, 0, SubscriptionTurn.Gone), await NewRunner(harness).RunNextAsync(Requested, Ct));

        var runs = await RunsAsync(harness);
        Assert.Equal(
            [(Failed, SubscriptionRunStatus.Running, (DateTime?)null, (DateTime?)null), (Requested, SubscriptionRunStatus.Superseded, Noon, null), (Created, SubscriptionRunStatus.Superseded, Noon, null)],
            runs.Select(run => (run.Kind, run.Status, run.EndedAt, run.RetryAt)));
        Assert.Empty(harness.Handlers.Requests);
    }

    /// <summary>
    /// The runner's own loop runs what the scan offers, resumes a backoff when its time comes, and a stop leaves the
    /// chain waiting to the next start, said by the count in flight.
    /// </summary>
    [Fact]
    public async Task The_runner_s_own_loop_runs_what_the_scan_offers_and_resumes_a_backoff_when_it_is_due()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed, Requested));
        Lines(harness, Failed, Requested);
        await CursorAsync(harness, Failed);
        await CursorAsync(harness, Requested);
        harness.Handlers.Answer("watcher", Failure("watcher_busy", FailureClass.Transient));
        var runner = harness.SubscriptionRunner;
        await runner.StartAsync(Ct);
        try
        {
            // Both kinds run at once; whichever is handed the one transient answer waits out its backoff.
            Assert.Equal(2, await harness.SubscriptionScanAsync(Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => harness.Handlers.Requests.Count == 2, Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => RunsAsync(harness).GetAwaiter().GetResult().Count(run => run.Status == SubscriptionRunStatus.Succeeded) == 1, Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => harness.Clock.Armed > 0, Ct));
            Assert.Equal(1, (await RunsAsync(harness)).Count(run => run.RetryAt is not null));

            harness.Clock.Advance(TimeSpan.FromSeconds(30));
            Assert.True(await DispatchHarness.EventuallyAsync(() => harness.Handlers.Requests.Count == 3, Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => runner.InFlight.Count == 0, Ct));
            Assert.Equal(
                [SubscriptionRunStatus.Succeeded, SubscriptionRunStatus.Succeeded],
                (await RunsAsync(harness)).Select(run => run.Status));

            Lines(harness, Failed);
            harness.Handlers.Answer("watcher", Failure("watcher_busy", FailureClass.Transient));
            Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => RunsAsync(harness).GetAwaiter().GetResult().Any(run => run.RetryAt is not null), Ct));
        }
        finally
        {
            await runner.StopAsync(Ct);
        }

        Assert.Equal(SubscriptionRunStatus.Running, (await RunsAsync(harness))[^1].Status);
        Assert.Contains(harness.Logs.All, message => message == "Subscription runner stopped with 1 chains in flight");
    }

    /// <summary>
    /// A turn that gives out is said by its kind and the failure's type, never its message, and the kind rests: it holds
    /// no child, the scan does not offer it again until its wait has passed, and the line it never reached is handed out
    /// then.
    /// </summary>
    [Fact]
    public async Task A_kind_that_gave_out_is_not_offered_again_until_its_wait_has_passed()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);
        var runner = harness.SubscriptionRunner;
        await runner.StartAsync(Ct);
        try
        {
            // The scan reads no work item and the turn's first read of its lines does: that read is the one that fails.
            harness.Commands.FailNextWorkItemRead();
            Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
            var said = $"Subscription chain for {Failed} could not finish (InvalidOperationException); it is offered again in 30 s";
            Assert.True(await DispatchHarness.EventuallyAsync(() => harness.Logs.Count(said) == 1, Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => runner.InFlight.Count == 0, Ct));
            Assert.Empty(harness.Handlers.Requests);
            Assert.True(runner.HasFreeChild);

            Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));
            harness.Clock.Advance(TimeSpan.FromSeconds(29));
            Assert.Equal(0, await harness.SubscriptionScanAsync(Ct));
            Assert.Empty(harness.Handlers.Requests);

            harness.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
            Assert.True(await DispatchHarness.EventuallyAsync(() => RunsAsync(harness).GetAwaiter().GetResult().Any(run => run.Status == SubscriptionRunStatus.Succeeded), Ct));
            Assert.Equal(1, harness.Logs.Count(said));
        }
        finally
        {
            await runner.StopAsync(Ct);
        }
    }

    /// <summary>
    /// The wait after a give-out doubles with each one of the same streak, from <c>Subscriptions:RetryBackoffSeconds</c>
    /// read at the give-out, and none is longer than an hour; the first give-out is the kind's first turn, offered at once.
    /// </summary>
    [Fact]
    public async Task Each_give_out_of_a_streak_doubles_the_wait_before_the_next_offer_and_none_is_longer_than_an_hour()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.RetryBackoffSeconds = 1000;
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);

        var waits = new List<int>();
        for (var giveOut = 0; giveOut < 5; giveOut++)
        {
            waits.Add(await GiveOutAsync(harness, Failed));
        }

        Assert.Equal([0, 1000, 2000, 3600, 3600], waits);
        Assert.Empty(await RunsAsync(harness));
        Assert.Equal([$"{Failed}|0|0"], await PositionsAsync(harness));
    }

    /// <summary>
    /// A streak is said once, at Warning, while its failure stays the same; a give-out of another failure in the same
    /// streak is said again, with the wait it begins. Neither line carries the failure's message.
    /// </summary>
    [Fact]
    public async Task A_streak_of_one_failure_is_said_once_and_a_different_failure_is_said_again()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);

        var waits = new List<int>();
        for (var giveOut = 0; giveOut < 3; giveOut++)
        {
            waits.Add(await GiveOutAsync(harness, Failed));
        }

        Assert.Equal([0, 30, 60], waits);

        // A fault of another type, met after the handler's start was recorded: the invoker itself throws.
        harness.Clock.Advance(TimeSpan.FromSeconds(120));
        harness.Handlers.Answer("watcher", (_, _) => throw new TimeoutException(Sentinel));
        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.GaveOut), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Assert.Equal(
            [
                $"Subscription chain for {Failed} could not finish (InvalidOperationException); it is offered again in 30 s",
                $"Subscription chain for {Failed} could not finish (TimeoutException); it is offered again in 240 s",
            ],
            harness.Logs.Warnings.Where(message => message.StartsWith($"Subscription chain for {Failed} could not finish", StringComparison.Ordinal)));
        Assert.DoesNotContain(harness.Logs.All, message => message.Contains(Sentinel, StringComparison.Ordinal));
    }

    /// <summary>
    /// A kind at rest holds no child: with one child in all, the scan offers another kind while the first rests, though
    /// the resting kind's line is the older one, and the resting kind is still refused when it is asked for.
    /// </summary>
    [Fact]
    public async Task A_kind_at_rest_holds_no_child_and_another_kind_is_offered_and_runs_meanwhile()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Subscriptions.CurrentValue.MaxParallel = 1;
        Load(harness, Subscriber("watcher", Failed, Requested));
        Line(harness, Failed);
        var requested = Line(harness, Requested);
        await CursorAsync(harness, Failed);
        await CursorAsync(harness, Requested);

        Assert.Equal(0, await GiveOutAsync(harness, Failed));

        Assert.True(harness.SubscriptionRunner.HasFreeChild);
        Assert.Equal(1, await harness.SubscriptionScanAsync(Ct));
        Assert.Equal([Requested], harness.SubscriptionRunner.InFlight);
        Assert.Equal(SubscriptionTurn.Head, (await harness.SubscriptionRunner.RunNextAsync(Requested, Ct))!.Ended);
        Assert.Equal([$"{Requested}|{requested}|1", $"{Failed}|0|0"], await PositionsAsync(harness));
        Assert.Null(await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        Assert.Equal([Requested], harness.Handlers.Requests.Select(request => request.Operation));
    }

    /// <summary>
    /// Any turn of the kind that ends without a give-out ends the streak: the next give-out waits one
    /// <c>Subscriptions:RetryBackoffSeconds</c> again, and is said again as the first of a new streak.
    /// </summary>
    [Fact]
    public async Task A_turn_that_ends_without_a_give_out_clears_the_streak()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);

        Assert.Equal(0, await GiveOutAsync(harness, Failed));
        Assert.Equal(30, await GiveOutAsync(harness, Failed));
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Lines(harness, Failed);
        Assert.Equal(0, await GiveOutAsync(harness, Failed));
        Assert.Equal(30, await GiveOutAsync(harness, Failed));
        Assert.Equal(
            2,
            harness.Logs.Count($"Subscription chain for {Failed} could not finish (InvalidOperationException); it is offered again in 30 s"));
    }

    /// <summary>
    /// A give-out after a handler's start was recorded leaves the record at that line, so the kind's next turn hands the
    /// line to the handler again: not before the wait has passed, and then once, told by <c>attempt</c> it is the second.
    /// </summary>
    [Fact]
    public async Task A_handler_that_ran_before_a_give_out_is_not_handed_the_line_again_before_the_wait_has_passed()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        var line = Line(harness, Failed);
        await CursorAsync(harness, Failed);
        harness.Handlers.Answer("watcher", (_, _) => throw new InvalidOperationException(Sentinel));

        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.GaveOut), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        Assert.Null(await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));
        Assert.Single(harness.Handlers.Requests);
        Assert.Equal(["watcher|"], Handled(Assert.Single(await RunsAsync(harness))));

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Head), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        Assert.Equal([1, 2], harness.Handlers.Requests.Select(request => request.Input["attempt"]!.GetValue<int>()));
        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
        Assert.Equal(["watcher|", "watcher|succeeded"], Handled(run));
        Assert.Equal([$"{Failed}|{line}|1"], await PositionsAsync(harness));
    }

    /// <summary>
    /// A streak lives in the runner's memory: a reload keeps it, and a runtime that starts again over the same database
    /// starts afresh, its first give-out after the start waiting one <c>Subscriptions:RetryBackoffSeconds</c>.
    /// </summary>
    [Fact]
    public async Task A_reload_keeps_a_streak_and_a_restart_starts_afresh()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        Lines(harness, Failed);
        await CursorAsync(harness, Failed);

        Assert.Equal(0, await GiveOutAsync(harness, Failed));
        Assert.Equal(30, await GiveOutAsync(harness, Failed));
        Load(harness, Subscriber("watcher", Failed));
        Assert.Equal(60, await GiveOutAsync(harness, Failed));

        var restarted = NewRunner(harness);
        Assert.Equal(0, await GiveOutAsync(harness, Failed, restarted));
        Assert.Equal(30, await GiveOutAsync(harness, Failed, restarted));
    }

    /// <summary>
    /// Asks for the kind's next turn a second at a time, with the turn's first read made to fail, until one runs: the
    /// seconds that passed before the kind was offered. The turn that runs is a give-out that reached no line.
    /// </summary>
    private static async Task<int> GiveOutAsync(DispatchHarness harness, string kind, SubscriptionRunner? runner = null)
    {
        runner ??= harness.SubscriptionRunner;
        harness.Commands.FailNextWorkItemRead();
        for (var waited = 0; waited <= 2 * SubscriptionRunner.MaximumBackoffSeconds; waited++)
        {
            if (await runner.RunNextAsync(kind, Ct) is { } turn)
            {
                Assert.Equal(new SubscriptionTurn(kind, 0, 0, SubscriptionTurn.GaveOut), turn);
                return waited;
            }

            harness.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Fail($"{kind} was not offered within two hours.");
        return -1;
    }

    private static SubscriptionRun Running(string kind, int line, DateTime? retryAt = null) => new()
    {
        PublicId = Jason.Contracts.Ids.PublicId.New("sbr"),
        Kind = kind,
        LineId = line,
        LinePublicId = "jrn_01K0left",
        Status = SubscriptionRunStatus.Running,
        StartedAt = Noon,
        RetryAt = retryAt,
    };

    /// <summary>Each handler entry of a record as <c>plugin|outcome</c>, the outcome empty for an invocation that may have acted.</summary>
    private static List<string> Handled(SubscriptionRun run) =>
        [.. run.Handlers.Select(entry => $"{entry!["plugin_id"]!.GetValue<string>()}|{entry["outcome"]?.GetValue<string>()}")];
}
