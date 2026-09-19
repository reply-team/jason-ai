using System.Net;
using System.Text;
using Jason.Contracts.Update;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Update;

/// <summary>
/// The unattended check: when it asks, what it keeps, and what it does when the answer is no good. Time is the
/// suite's clock with its timer, so "five minutes" and "a day" are moves of the clock and not waits; the feed is
/// a handler the test writes, installed where a real runtime would send, so nothing here opens a socket.
/// </summary>
public class UpdateCheckerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);


    /// <summary>Long enough that a wait on a completed check never expires by accident under a full run.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nothing_is_asked_before_the_initial_delay_and_then_it_is_asked_on_the_interval()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);

        // The dispatcher's tick is on this clock as well, and a day of ticks is not what is under test.
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var clock = new FixedClock(Noon);
        var feed = new StubFeed(_ => Ok(Manifest("0.2.0")));
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, Checking(clock, feed), Ct);
        var armed = await ArmedAsync(clock, atLeast: 1);

        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.Equal(0, feed.Calls);

        clock.Advance(TimeSpan.FromSeconds(1));
        await feed.Asked(1).WaitAsync(Patience, Ct);
        Assert.Equal(1, feed.Calls);

        // The next wait is armed only once the check is over, so the clock is not moved until it is.
        await ArmedAsync(clock, atLeast: armed);
        clock.Advance(TimeSpan.FromHours(24));
        await feed.Asked(2).WaitAsync(Patience, Ct);
        Assert.Equal(2, feed.Calls);

        var current = runtime.Services.GetRequiredService<UpdateAdvertisement>().Current;
        Assert.NotNull(current);
        Assert.True(current.Available);
        Assert.Equal("0.2.0", current.Version);
        Assert.Equal(Noon.AddMinutes(5).AddHours(24), current.CheckedAt);
    }

    /// <summary>
    /// The second half of the offline guarantee for a runtime that is composed in-process: with nothing in the
    /// settings about updates, the shipped delay stands between the start and the first request, and no test
    /// runtime lives that long. The handler installed here is where a real runtime would send, and it fails the
    /// test if anything reaches it.
    /// </summary>
    [Fact]
    public async Task A_runtime_with_the_shipped_defaults_asks_nobody_anything()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var clock = new FixedClock(Noon);
        var feed = new StubFeed(_ => throw new InvalidOperationException("nothing may ask the feed here"));
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, Checking(clock, feed), Ct);
        var advertisement = runtime.Services.GetRequiredService<UpdateAdvertisement>();
        await ArmedAsync(clock, atLeast: 1);

        // One second short of the shipped delay: longer than any test runtime lives, by a great deal.
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await runtime.StopAsync();

        Assert.Equal(0, feed.Calls);
        Assert.Null(advertisement.Current);
    }

    /// <summary>
    /// The first half of that guarantee, for the runtimes the fixture starts: whatever settings a test writes,
    /// the fixture's runtime never checks. Written as an override rather than into the settings file, because
    /// the file is the test's to write and a fixture that edited it would be editing the thing under test.
    /// </summary>
    [Fact]
    public async Task A_fixture_never_checks_for_updates_whatever_settings_the_test_wrote()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Update":{"CheckEnabled":true}}"""));

        Assert.False(fixture.Resolve<IOptionsMonitor<UpdateOptions>>().CurrentValue.CheckEnabled);
        Assert.False(fixture.Resolve<LiveSettings<UpdateOptions>>().Current.CheckEnabled);
    }

    /// <summary>
    /// The same schedule on the checker alone, and the two things an operator may change while it runs: the
    /// interval, which applies at the next wait, and the switch, which stops the asking without stopping the
    /// service — so turning it back on needs no restart either.
    /// </summary>
    [Fact]
    public async Task A_checker_reads_its_settings_before_every_wait()
    {
        var clock = new FixedClock(Noon);
        var feed = new StubFeed(_ => Ok(Manifest("0.2.0")));
        var options = new TestOptionsMonitor<UpdateOptions>(new UpdateOptions());
        using var checker = Checker(clock, feed, new UpdateAdvertisement(), options: options);
        await checker.StartAsync(Ct);
        await ArmedAsync(clock, atLeast: 1);

        clock.Advance(TimeSpan.FromMinutes(5));
        await feed.Asked(1).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => clock.Armed == 1, Ct));

        options.CurrentValue = new UpdateOptions { IntervalHours = 1 };
        clock.Advance(TimeSpan.FromHours(24));
        await feed.Asked(2).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => clock.Armed == 1, Ct));

        // The edit applied at the wait after the one it was made during: the next check came an hour later.
        clock.Advance(TimeSpan.FromHours(1));
        await feed.Asked(3).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => clock.Armed == 1, Ct));

        options.CurrentValue = new UpdateOptions { IntervalHours = 1, CheckEnabled = false };
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(await DispatchHarness.EventuallyAsync(() => clock.Armed == 1, Ct));
        Assert.Equal(3, feed.Calls);

        options.CurrentValue = new UpdateOptions { IntervalHours = 1 };
        clock.Advance(TimeSpan.FromHours(1));
        await feed.Asked(4).WaitAsync(Patience, Ct);

        await checker.StopAsync(Ct);
    }

    [Fact]
    public async Task A_feed_that_answers_badly_leaves_the_advertisement_alone_and_logs_once_per_failed_check()
    {
        var clock = new FixedClock(Noon);
        var logs = new RecordingLogs();
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => Ok(Manifest("0.2.0")),
            () => Ok("this is not a manifest"),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => throw new HttpRequestException("connection refused"),
        ]);
        var feed = new StubFeed(_ => answers.Dequeue()());
        var advertisement = new UpdateAdvertisement();
        using var checker = Checker(clock, feed, advertisement, logs);
        await checker.StartAsync(Ct);
        await ArmedAsync(clock, atLeast: 1);

        clock.Advance(TimeSpan.FromMinutes(5));
        await feed.Asked(1).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => advertisement.Current is not null && clock.Armed == 1, Ct));
        var good = advertisement.Current!;
        Assert.Equal("0.2.0", good.Version);

        clock.Advance(TimeSpan.FromHours(24));
        await feed.Asked(2).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => logs.Warnings.Count == 1 && clock.Armed == 1, Ct));

        clock.Advance(TimeSpan.FromHours(24));
        await feed.Asked(3).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => logs.Warnings.Count == 2 && clock.Armed == 1, Ct));

        clock.Advance(TimeSpan.FromHours(24));
        await feed.Asked(4).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => logs.Warnings.Count == 3 && clock.Armed == 1, Ct));

        // The same object, not an equal one: nothing about a failed check touches what the last good one said.
        Assert.Same(good, advertisement.Current);

        // Once per failed check and with the code, so a feed that is down for a week is visible daily and no
        // louder — and a person reading the log learns which of the three things went wrong.
        Assert.Contains(UpdateFeedException.Invalid, logs.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(UpdateFeedException.Unreachable, logs.Warnings[1], StringComparison.Ordinal);
        Assert.Contains(UpdateFeedException.Unreachable, logs.Warnings[2], StringComparison.Ordinal);

        await checker.StopAsync(Ct);
    }

    [Theory]
    [InlineData("older")]
    [InlineData("equal")]
    public async Task An_equal_or_older_version_is_recorded_as_not_available(string which)
    {
        var version = which == "equal" ? SemanticVersion.Current.ToString() : "0.0.1";
        var clock = new FixedClock(Noon);
        var feed = new StubFeed(_ => Ok(Manifest(version)));
        var advertisement = new UpdateAdvertisement();
        using var checker = Checker(clock, feed, advertisement);
        await checker.StartAsync(Ct);
        await ArmedAsync(clock, atLeast: 1);

        clock.Advance(TimeSpan.FromMinutes(5));
        await feed.Asked(1).WaitAsync(Patience, Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => advertisement.Current is not null, Ct));

        // Recorded, not dropped: "checked, and there is nothing newer" is what a status line shows, and a
        // runtime that had checked and kept nothing would look like one that had never checked.
        var current = advertisement.Current!;
        Assert.False(current.Available);
        Assert.Equal(version, current.Version);
        Assert.Equal(Noon.AddMinutes(5), current.CheckedAt);
        Assert.Equal("https://example.test/notes", current.ReleaseNotesUrl);

        await checker.StopAsync(Ct);
    }

    /// <summary>
    /// Waits until the checker has armed its wait, and says how many timers are armed then. A hosted service's
    /// first synchronous steps run after <c>StartAsync</c> has returned, not inside it, so a test that moved
    /// the clock straight after starting could move it past a wait that was not yet on the clock — and would
    /// then be proving nothing at all, or only what a slow start happened to let it prove.
    /// </summary>
    /// <summary>
    /// The shared options with the check deliberately left on and a stub where the network would be. Every
    /// other test in this suite starts from options that turn it off; these are the tests it is for, so they
    /// say so in one place rather than each quietly dropping the hook that turns it off.
    /// </summary>
    private static RuntimeHostOptions Checking(FixedClock clock, HttpMessageHandler feed) =>
        TestRuntimeOptions.Quiet with { Clock = clock, FeedHandler = feed, ConfigureServices = null };

    private static async Task<int> ArmedAsync(FixedClock clock, int atLeast)
    {
        Assert.True(
            await DispatchHarness.EventuallyAsync(() => clock.Armed >= atLeast, Ct),
            $"the checker never armed its wait: {clock.Armed} timers armed, {atLeast} expected");
        return clock.Armed;
    }

    private static UpdateChecker Checker(
        FixedClock clock,
        StubFeed feed,
        UpdateAdvertisement advertisement,
        RecordingLogs? logs = null,
        TestOptionsMonitor<UpdateOptions>? options = null)
    {
        var settings = new LiveSettings<UpdateOptions>(
            options ?? new TestOptionsMonitor<UpdateOptions>(new UpdateOptions()),
            NullLogger<LiveSettings<UpdateOptions>>.Instance,
            UpdateOptions.Section);
        // Not disposed here: the checker keeps logging through it for as long as the test runs it.
        var factory = LoggerFactory.Create(logging => logging.AddProvider(logs ?? new RecordingLogs()));
        return new UpdateChecker(clock, settings, new UpdateFeed(new HttpClient(feed, disposeHandler: false)), advertisement, factory.CreateLogger<UpdateChecker>());
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Manifest(string version) =>
        $$$"""
        {"schema":1,"version":"{{{version}}}","published_at":"2026-09-19T08:00:00Z",
         "artifacts":{"win-x64":{"asset":"jason-win-x64.zip","sha256":"{{{new string('a', 64)}}}","size":1}},
         "release_notes_url":"https://example.test/notes"}
        """;

    /// <summary>The feed as a test writes it: answers what it is told, counts the asks, and lets a test wait for the n-th.</summary>
    private sealed class StubFeed(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, TaskCompletionSource> _waiters = [];
        private int _calls;

        public int Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls;
                }
            }
        }

        /// <summary>Completes once the feed has been asked this many times; already complete if it has been.</summary>
        public Task Asked(int times)
        {
            lock (_gate)
            {
                if (_calls >= times)
                {
                    return Task.CompletedTask;
                }

                if (!_waiters.TryGetValue(times, out var waiter))
                {
                    waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters[times] = waiter;
                }

                return waiter.Task;
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(answer(request));
            }
            finally
            {
                // Counted whether the answer was a response or an exception: an ask is an ask.
                lock (_gate)
                {
                    _calls++;
                    if (_waiters.Remove(_calls, out var waiter))
                    {
                        waiter.TrySetResult();
                    }
                }
            }
        }
    }
}
