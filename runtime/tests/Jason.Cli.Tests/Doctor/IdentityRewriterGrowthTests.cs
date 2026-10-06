using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Cli.Doctor;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// A long run of separators is a string like any other, and a home directory that starts with a separator — every
/// one on Linux and macOS, and a share — must not make it cost more than reading it. The longest string is a megabyte,
/// which is as long as a line of a log may be.
/// </summary>
/// <remarks>
/// <para>
/// What is held is how the time grows, not how long it is: a bound in seconds is a bound on the machine, and a runner
/// busy with other suites took nine seconds for what an idle one does in half of one. Sixteen times the text takes
/// about sixteen times as long when the rewrite reads each character a bounded number of times — measured from 15.9 to
/// 22.8 here, the percent-escaped separators the slowest — and about two hundred and fifty-six times as long when it
/// reads the rest of the run again from every character of it: from 254.5 to 262.0, measured at four and sixty-four
/// kibibytes, because at these sizes a quadratic rewrite of the megabyte does not come back inside the minute a timing
/// is allowed. The bound is ninety-six: over four times the slowest linear ratio, which a loaded runner has been seen to
/// double, and still under the quadratic one by more than two and a half times.
/// </para>
/// <para>
/// Each size is timed several times, the two sizes taking turns so that both meet the same load, and the fastest of
/// each is compared, because a pause the machine takes can only make a rewrite slower. The class runs on its own, after
/// the tests that run side by side, so the timings measure the rewrite rather than the suite around it. The minute
/// each timing is allowed is no measurement at all, only a guard against a rewrite that never comes back. A failure
/// says every timing it took, so a slow machine and a slow rewrite can be told apart from the message alone.
/// </para>
/// </remarks>
[Collection(TimingCollection.Name)]
public sealed class IdentityRewriterGrowthTests
{
    private const int Small = 1 << 16;
    private const int Large = 1 << 20;
    private const int Rounds = 5;
    private const double Bound = 96;

    [Theory]
    [InlineData("/home/ada", "/")]
    [InlineData("/home/ada", "\\")]
    [InlineData("/home/ada", "%5C")]
    [InlineData("/home/ada", "%2f")]
    [InlineData(@"\\fs01\home\ada", "\\")]
    public async Task A_long_run_of_separators_is_rewritten_in_time_that_grows_as_its_length_does(string home, string separator)
    {
        var tail = home.TrimStart('/', '\\').Replace("/", separator).Replace("\\", separator);
        var rewriter = new IdentityRewriter(new IdentityFacts(home, "ada", "WORKSTATION-7", null, null));

        // The expression, the engine that runs it and the code around it are warmed before anything is timed.
        await RewriteRunAsync(rewriter, separator, tail, 1 << 12);

        var smalls = new List<TimeSpan>();
        var larges = new List<TimeSpan>();
        for (var round = 0; round < Rounds; round++)
        {
            smalls.Add(await RewriteRunAsync(rewriter, separator, tail, Small));
            larges.Add(await RewriteRunAsync(rewriter, separator, tail, Large));
        }

        var ratio = larges.Min() / smalls.Min();
        Assert.True(
            ratio < Bound,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Sixteen times the run took {ratio:0.0} times as long, against a bound of {Bound}: the fastest of {Small} characters took {smalls.Min().TotalMilliseconds:0.0} ms and of {Large} took {larges.Min().TotalMilliseconds:0.0} ms. Every timing, in turn: {Small} characters {Milliseconds(smalls)}; {Large} characters {Milliseconds(larges)}."));
    }

    private static string Milliseconds(IEnumerable<TimeSpan> timings) =>
        string.Join(", ", timings.Select(timing => timing.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms"));

    /// <summary>
    /// How long it took to rewrite a run of <paramref name="length"/> characters twice: alone, when it names nobody and
    /// comes back as it was, and followed by the rest of the home directory, when it is the home directory and comes back
    /// as <c>~</c>.
    /// </summary>
    private static async Task<TimeSpan> RewriteRunAsync(IdentityRewriter rewriter, string separator, string tail, int length)
    {
        var run = string.Concat(Enumerable.Repeat(separator, length / separator.Length));
        var inHome = run + tail + separator + "x";

        // Off this thread, so that a rewrite that does not come back fails the test after a while instead of holding the suite.
        var (elapsed, untouched, rewritten) = await Task.Run(
            () =>
            {
                var started = Stopwatch.StartNew();
                var untouched = rewriter.Rewrite(JsonValue.Create(run)!).GetValue<string>();
                var rewritten = rewriter.Rewrite(JsonValue.Create(inHome)!).GetValue<string>();
                return (started.Elapsed, untouched, rewritten);
            },
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.Equal(run, untouched);
        Assert.Equal("~" + separator + "x", rewritten);
        return elapsed;
    }
}

/// <summary>
/// The tests that time something, kept away from everything else in this assembly while they run: a timing taken
/// beside a dozen other tests measures the dozen.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "timing";
}
