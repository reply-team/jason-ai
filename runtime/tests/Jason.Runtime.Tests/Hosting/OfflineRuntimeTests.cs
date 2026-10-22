using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// The suite's promise that nothing it starts talks to the internet, enforced over the source rather than
/// trusted. A runtime composed in a test has a real transport for the release feed unless it is given another
/// one, and the five-minute initial delay is the only other thing between it and a request — which holds until
/// somebody writes a test that keeps a runtime alive, or shortens the delay to exercise something.
/// </summary>
/// <remarks>
/// This is written the way <c>ArchitectureTests</c> is, and for the same reason: the rule is about every call
/// site, including the ones not written yet, and a rule like that cannot be asserted from inside one test.
/// </remarks>
public partial class OfflineRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// There is one declaration of the options a test runtime is started with. Ten identical ones were ten
    /// places to forget, which is how nineteen runtimes came to be composed with a live transport pointed at a
    /// public web page and nothing but a five-minute delay in the way.
    /// </summary>
    [Fact]
    [Critical(Family.B9, "one offline switch")]
    public void There_is_one_declaration_of_the_shared_options()
    {
        var declarations = Lines()
            .Where(line => line.Text.Contains("RuntimeHostOptions Quiet", StringComparison.Ordinal))
            .Select(Where)
            .ToList();

        Assert.True(declarations.Count == 1, $"the shared options are declared {declarations.Count} times: {string.Join(", ", declarations)}");
    }

    /// <summary>
    /// And nothing else builds host options at all — which is what makes the rule hold for call sites this test
    /// cannot read. Every options value under the test tree is <see cref="TestRuntimeOptions.Quiet"/> or a
    /// <c>with</c> of it, whether it is passed inline, held in a local or returned by a helper, so every test
    /// runtime carries the transport that refuses the release feed.
    /// </summary>
    [Fact]
    public void Nothing_else_builds_host_options()
    {
        var built = Lines()
            .Where(line => line.Text.Contains("new RuntimeHostOptions(", StringComparison.Ordinal))
            .Select(Where)
            .ToList();

        Assert.True(built.Count == 0, $"host options are built by hand at: {string.Join(", ", built)}");
    }

    /// <summary>
    /// And that transport is replaced in exactly one place: the tests whose subject the check is. Anywhere else
    /// it would be a runtime quietly given a way to the network, which is the thing all of this prevents.
    /// </summary>
    [Fact]
    public void The_feed_transport_is_replaced_only_where_the_check_itself_is_under_test()
    {
        var replaced = Lines()
            .Where(line => line.Text.Contains("FeedHandler =", StringComparison.Ordinal) || line.Text.Contains("FeedHandler:", StringComparison.Ordinal))
            .Where(line => !Path.GetFileName(line.File).Equals("UpdateCheckerTests.cs", StringComparison.Ordinal))
            .Where(line => !Path.GetFileName(line.File).Equals("TestRuntimeOptions.cs", StringComparison.Ordinal))
            .Select(Where)
            .ToList();

        Assert.True(replaced.Count == 0, $"the feed transport is replaced at: {string.Join(", ", replaced)}");
    }

    /// <summary>
    /// The shared options carry two layers, and this is the second one doing its job: a runtime started from
    /// them with the check deliberately left on reaches for the feed, and the transport refuses it.
    /// </summary>
    /// <remarks>
    /// The three guards above prove the value is declared once and used everywhere. They say nothing about what
    /// it carries — deleting the transport from it leaves every one of them green, which is a guard over a name
    /// rather than over a guarantee. This asserts the guarantee: the request is counted where it is refused, so
    /// no reading of a log line and no absence of one stands in for it.
    /// </remarks>
    [Fact]
    public async Task A_runtime_that_reaches_for_the_feed_is_refused_by_the_shared_transport()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        var before = TestRuntimeOptions.Refusals;

        // The hook is what turns the check off, so this test replaces it with the half that keeps notifications
        // off — and keeps the transport, which is the whole point: one `with` cannot take away the other layer.
        await using var runtime = await RuntimeHost.StartAsync(
            dir.Paths,
            TestRuntimeOptions.Quiet with { Clock = clock, ConfigureServices = TestRuntimeOptions.TurnNotificationsOff },
            Ct);

        Assert.True(
            runtime.Services.GetRequiredService<IOptionsMonitor<UpdateOptions>>().CurrentValue.CheckEnabled,
            "this test needs the check on; with it off there is nothing for the transport to refuse");

        Assert.True(await DispatchHarness.EventuallyAsync(() => clock.Armed >= 1, Ct), "the checker never armed its wait");
        clock.Advance(TimeSpan.FromMinutes(new UpdateOptions().InitialDelayMinutes));

        Assert.True(
            await DispatchHarness.EventuallyAsync(() => TestRuntimeOptions.Refusals > before, Ct),
            "the runtime's update check did not reach the transport, so nothing here proves it would have been refused");

        // And the check that was refused left nothing behind: no version was advertised on the strength of a
        // failure.
        Assert.Null(runtime.Services.GetRequiredService<UpdateAdvertisement>().Current);
    }

    /// <summary>
    /// And the first layer: a runtime started from the shared options as they stand has the check off, whatever
    /// its settings file says.
    /// </summary>
    /// <remarks>
    /// <c>RuntimeApiFixture</c> has its own test for this, which proves it for the fixture — most of the suite's
    /// runtimes are not started through the fixture.
    /// </remarks>
    [Fact]
    public async Task The_shared_options_turn_the_check_off_whatever_the_settings_say()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);

        // A settings file that asks for the check, and asks for it soon.
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Dispatcher":{"Enabled":false},"Update":{"CheckEnabled":true,"InitialDelayMinutes":1}}""");

        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        Assert.False(runtime.Services.GetRequiredService<IOptionsMonitor<UpdateOptions>>().CurrentValue.CheckEnabled);
    }

    /// <summary>
    /// And the same layer for notifications: a runtime started from the shared options notifies nobody, whatever
    /// its settings file says. No test shows a toast or sends anything; one that means to exercise the notifier
    /// turns it on itself, with channels of its own.
    /// </summary>
    [Fact]
    [Critical(Family.B9, "tests notify nobody")]
    public async Task The_shared_options_turn_notifications_off_whatever_the_settings_say()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Dispatcher":{"Enabled":false},"Notifications":{"Enabled":true,"Toast":true}}""");

        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        Assert.False(runtime.Services.GetRequiredService<IOptionsMonitor<NotificationsOptions>>().CurrentValue.Enabled);
    }

    /// <summary>
    /// The hook that keeps a test runtime from notifying anybody is replaced, never added to, wherever a test sets its
    /// own, so every replacement turns the outside off again before it does anything else. A replacement that did not
    /// would be a test runtime free to show a toast or send to a notification plugin.
    /// </summary>
    [Fact]
    public void Every_replacement_of_the_services_hook_keeps_notifications_off()
    {
        var unguarded = Unguarded(Sources()
            .Where(file => !Path.GetFileName(file).Equals("TestRuntimeOptions.cs", StringComparison.Ordinal))
            .Select(file => (file, File.ReadAllLines(file))))
            .ToList();
        Assert.True(unguarded.Count == 0, $"the services hook is replaced without turning notifications off at: {string.Join(", ", unguarded)}");

        // The rule's positive controls: a replacement that only adds a service is found, however it is spaced, across a
        // line break too, and a comparison is not a replacement.
        Assert.Equal(
            ["Control.cs(1)", "Tight.cs(1)", "Split.cs(2)"],
            Unguarded(
            [
                ("Control.cs", ["ConfigureServices = services =>", "{", "    // a comment first", "    services.AddSingleton<object>(new object());", "},"]),
                ("Tight.cs", ["ConfigureServices=services =>", "{", "    services.AddSingleton<object>(new object());", "},"]),
                ("Split.cs", ["var options = new TestRuntimeOptions {", "    ConfigureServices", "        = services =>", "    {", "        services.AddSingleton<object>(new object());", "    },", "};"]),
                ("Compared.cs", ["if (options.ConfigureServices == null)", "{", "    return;", "}"]),
            ]));
    }

    /// <summary>An assignment to the hook, however it is spaced, across a line break too; a comparison with it is not one.</summary>
    [GeneratedRegex(@"\bConfigureServices\s*=(?!=)")]
    private static partial Regex Replacement();

    /// <summary>
    /// Each <c>ConfigureServices =</c> — matched over the whole text, so a line break inside it hides nothing — whose own
    /// lines, or whose first statement after its opening brace and comments, is not a call that turns notifications
    /// off. Each is named by the line it starts on.
    /// </summary>
    private static IEnumerable<string> Unguarded(IEnumerable<(string File, string[] Lines)> sources)
    {
        static bool TurnsOff(string text) =>
            text.Contains("TurnTheOutsideOff", StringComparison.Ordinal) || text.Contains("TurnNotificationsOff", StringComparison.Ordinal);

        static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n');

        foreach (var (file, lines) in sources)
        {
            var text = string.Join('\n', lines);
            foreach (Match match in Replacement().Matches(text))
            {
                var first = LineOf(text, match.Index);
                var last = LineOf(text, match.Index + match.Length - 1);
                if (TurnsOff(string.Join('\n', lines[first..(last + 1)])))
                {
                    continue;
                }

                var next = last + 1;
                while (next < lines.Length && (lines[next].Trim() is "" or "{" || lines[next].TrimStart().StartsWith("//", StringComparison.Ordinal)))
                {
                    next++;
                }

                if (next >= lines.Length || !TurnsOff(lines[next]))
                {
                    yield return $"{Path.GetFileName(file)}({first + 1})";
                }
            }
        }
    }

    /// <summary>Every line of every test source but this one, with where it is.</summary>
    private static IEnumerable<(string File, int Line, string Text)> Lines() =>
        Sources().SelectMany(file => File.ReadLines(file).Select((text, index) => (File: file, Line: index + 1, Text: text)));

    private static string Where((string File, int Line, string Text) line) => $"{Path.GetFileName(line.File)}({line.Line})";

    /// <summary>
    /// Every test source but this one. A guard that reads source has its own source read back at it: the lines
    /// below name the very things they forbid, and counting them would be counting the rule as a breach of
    /// itself.
    /// </summary>
    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "runtime", "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).Equals("OfflineRuntimeTests.cs", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? ".";
    }
}
