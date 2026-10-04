using System.Text.RegularExpressions;

namespace Jason.Contracts.Operations;

/// <summary>
/// How a compiled schema pattern is matched against a value. This is a test seam and nothing else: the runtime always
/// uses <see cref="Default"/>, which matches for real under the dialect's own timeout,
/// <see cref="SchemaValidator.PatternTimeoutMilliseconds"/>, and there is no setting that changes either.
/// </summary>
/// <remarks>
/// It is handed to each call rather than kept anywhere a call could find it, because a timing that lived in a static
/// or followed the async flow would let one test's timeouts leak into another's requests. The runtime registers the
/// default as a service and every site that validates takes it from there, so a test replaces it for one host and
/// nowhere else — typically with a timing whose first matches throw <see cref="RegexMatchTimeoutException"/>, which is
/// how a test shows a timeout without making anything slow.
/// </remarks>
public class PatternTiming
{
    /// <summary>The real match, under the timeout each pattern was compiled with: what every runtime site is given.</summary>
    public static PatternTiming Default { get; } = new();

    /// <summary>
    /// How many patterns this process has compiled for the dialect. It is here for one claim a test has to be able to
    /// make: that a process which never matches a value — the command-line client — compiles none, whatever of this
    /// assembly it loads.
    /// </summary>
    public static long PatternsCompiled => SchemaValidator.Compiled;

    /// <summary>
    /// Whether <paramref name="value"/> matches <paramref name="pattern"/>. A match that runs past the pattern's own
    /// timeout throws <see cref="RegexMatchTimeoutException"/>, which the validator reads as no answer at all.
    /// </summary>
    public virtual bool IsMatch(Regex pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);

        return pattern.IsMatch(value);
    }
}
