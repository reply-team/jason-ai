using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;

namespace Jason.Runtime.Tests;

/// <summary>
/// A pattern timing whose first matches are reported as having run past the timeout, whatever the pattern, and whose
/// matches after that are answered. It is how a test makes a match time out a set number of times without making
/// anything slow: nothing here waits, the exception is the one a slow match throws, and every answer is the default
/// match's.
/// </summary>
/// <remarks>
/// <para>
/// Every match is performed for real, the ones reported as timed out included, and only then is the timeout thrown.
/// That is what a slow real match leaves behind — an engine that has built its automaton and compiled its code — so the
/// warm retry a test exercises meets a warm engine, exactly as it would on a busy machine, rather than a cold first
/// match with no retry left behind it. A real timeout where one was scheduled counts as the scheduled one.
/// </para>
/// <para>
/// The count is shared by every pattern the timing is handed, because a test states how many timeouts a request meets
/// rather than which pattern meets them; a request whose first pattern times out twice has met two. A test that has to
/// leave everything else in a runtime alone — its warm-up, its plugin load — names the one value, or the one pattern,
/// whose matches may time out, and every other match is answered and uncounted.
/// </para>
/// <para>
/// A pattern is known here by the text of the compiled expression a match is handed, because that is all a match
/// carries: it is what a real timeout names too, and it is the same on every path, since a warm-up, a load-time probe
/// and a validation all compile the pattern as written into the one cached expression. That text is the pattern as
/// the engine runs it, with every `$` that anchors written `\z` so it ends the value and nothing else, and every `.`
/// outside a class written as a class of everything but the four line terminators; so
/// <see cref="Matched"/> lists that form, <see cref="OnlyPattern"/> names it, and a test that starts from the pattern
/// a document writes reads it through <see cref="AsCompiled"/>. The pattern as written is what problems and messages
/// report, and it never passes through a timing.
/// </para>
/// </remarks>
/// <param name="times">How many matches time out before the rest are answered.</param>
/// <param name="only">The values whose matches may time out; null for every value.</param>
public sealed class TimingOut(int times, Func<string, bool>? only = null) : PatternTiming
{
    private readonly ConcurrentQueue<string> _matched = new();
    private int _remaining = times;
    private int _calls;
    private int _timedOut;

    /// <summary>A timing under which no pattern ever finishes for the values it names: every match is a timeout, the warm one included.</summary>
    public static TimingOut Always(Func<string, bool>? only = null) => new(int.MaxValue, only);

    /// <summary>A timing under which every match is answered: what a test that only records is given.</summary>
    public static TimingOut Never() => new(0);

    /// <summary>
    /// The one pattern whose matches may time out, as the engine runs it; null for every pattern. For a test whose pattern
    /// is matched against the same probe the runtime warms its own patterns with, where the value alone cannot tell the
    /// two apart.
    /// </summary>
    public string? OnlyPattern { get; init; }

    /// <summary>How many matches were asked for, timed out or not.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>How many matches were reported as timed out.</summary>
    public int TimedOut => Volatile.Read(ref _timedOut);

    /// <summary>The patterns a match was answered for, as the engine runs them, in the order they were answered.</summary>
    public IReadOnlyCollection<string> Matched => _matched;

    /// <summary>
    /// The text a timing knows <paramref name="pattern"/> by: the expression the validator compiles from it, read from
    /// the validator's own rewrite rather than a copy of it. Nothing is compiled and nothing is matched: a match here runs
    /// inside the real timeout, often in a test's field initializer, where a cold engine that timed out would fail every
    /// test of the class with nothing to say why — and it would warm the very cache the test is about.
    /// </summary>
    public static string AsCompiled(string pattern)
    {
        var form = SchemaValidator.EcmaForm(pattern, out var refused);
        return refused is null
            ? form
            : throw new ArgumentException($"`{pattern}` is not a pattern the validator compiles: it writes `{refused}`.", nameof(pattern));
    }

    /// <summary>From now on the next <paramref name="count"/> matches time out: for a test that lets a runtime start first.</summary>
    public void TimeOutNext(int count) => Interlocked.Exchange(ref _remaining, count);

    public override bool IsMatch(Regex pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);

        Interlocked.Increment(ref _calls);
        var due = (only is null || only(value))
            && (OnlyPattern is null || string.Equals(OnlyPattern, pattern.ToString(), StringComparison.Ordinal))
            && Interlocked.Decrement(ref _remaining) >= 0;

        bool matched;
        try
        {
            matched = base.IsMatch(pattern, value);
        }
        catch (RegexMatchTimeoutException) when (due)
        {
            // A real timeout where one was scheduled is that one: a cold first match on a busy machine must not spend
            // the scheduled timeout and leave the warm retry to meet a second, invented one.
            Interlocked.Increment(ref _timedOut);
            throw;
        }

        if (due)
        {
            Interlocked.Increment(ref _timedOut);
            throw new RegexMatchTimeoutException(value, pattern.ToString(), pattern.MatchTimeout);
        }

        // Kept from going further below zero, so a timing told "never" stays "never" however long a host runs.
        if (Volatile.Read(ref _remaining) < 0)
        {
            Interlocked.Exchange(ref _remaining, -1);
        }

        _matched.Enqueue(pattern.ToString());
        return matched;
    }
}
