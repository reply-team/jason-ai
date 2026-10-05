using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The settings a runtime is starting with cannot be used: the settings file is not one it can read, or a section holds
/// a value of the wrong type or out of range. The message is everything that is known — the file and where the parser
/// stopped, or the setting's path and the type it has to be — and nothing of what was typed.
/// </summary>
/// <remarks>
/// <para>
/// Its own type for the reason a data directory that cannot be prepared has one: the host has to be able to pick it out
/// from every other exception to say it where the person who started the runtime will hear it. A runtime started in the
/// background has put its standard error on the null device by then, and a refusal that went only to the log sent that
/// person to read files rather than telling them what to fix. Nothing is kept underneath it, since the framework's own
/// exceptions quote the file.
/// </para>
/// <para>
/// It is raised wherever in the start the refusal is found: building the configuration, reading the sections the host
/// is built from, and validating every other section as the host starts (<see cref="FromStart"/>).
/// </para>
/// </remarks>
public sealed class InvalidSettingsException : InvalidOperationException
{
    internal InvalidSettingsException(string failure)
        : base("Invalid Jason configuration: " + failure)
    {
        Failure = failure;
    }

    /// <summary>What was refused, without the words that introduce it: one sentence for each setting.</summary>
    public string Failure { get; }

    /// <summary>
    /// The settings refusal behind what the host raised while it started, or null where it raised something else. The
    /// options system refuses a section validated on start with an <see cref="OptionsValidationException"/>, and several
    /// at once with an <see cref="AggregateException"/> of them; their sentences are the validators' own, which name a
    /// setting and quote nothing that was typed.
    /// </summary>
    /// <remarks>
    /// A section's refusal can arrive twice: a section that reads another through its seam refuses with that section's
    /// own refusal when there is nothing to fall back to. Each section's is said once, and every sentence of it, so that
    /// settings refused in the same words — keys written as placeholders say the same thing — are each said.
    /// </remarks>
    internal static InvalidSettingsException? FromStart(Exception exception) => exception switch
    {
        OptionsValidationException refused => new(string.Join(" ", refused.Failures)),
        AggregateException several when several.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(one => one is OptionsValidationException)
            => new(string.Join(
                " ",
                inner.Cast<OptionsValidationException>().DistinctBy(one => (one.OptionsType, one.OptionsName)).SelectMany(one => one.Failures))),
        _ => null,
    };
}
