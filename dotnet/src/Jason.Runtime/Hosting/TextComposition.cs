using System.Text;

namespace Jason.Runtime.Hosting;

/// <summary>
/// Whether this process composes text, which the runtime checks before it does anything else. The key of a LinkedIn
/// profile link composes the profile's name, so that an accent written as one letter and the same accent written as a
/// letter and a combining mark are one profile and one stop. .NET in globalization invariant mode composes nothing, and
/// there the two would be two keys, which the rule forbids: a runtime that cannot keep that promise refuses to start rather
/// than keep it silently some of the time.
/// </summary>
public static class TextComposition
{
    /// <summary>A letter and a combining acute accent, which a process that composes text makes the one letter U+00E1.</summary>
    public const string LetterAndMark = "a\u0301";

    private const string Composed = "\u00e1";

    private const string Refused =
        "The runtime did not start: this process does not compose text, so an accented letter written as one character and "
        + "the same letter written as a letter and a combining mark would be two LinkedIn keys for one profile, and a stop "
        + "recorded under one would miss the other. It runs in .NET's globalization invariant mode, the mode for running "
        + "without the ICU libraries, which DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 selects whether or not they are "
        + "installed. Install libicu and take that variable out of the runtime's environment, then start the runtime again. "
        + "Nothing was written, and no log file exists to read, because this happens before logging starts.";

    /// <summary>
    /// What a runtime that made <paramref name="composed"/> of <see cref="LetterAndMark"/> says as it refuses to start, or
    /// null where it made the one letter of it and may start.
    /// </summary>
    public static string? Refusal(string composed) => composed == Composed ? null : Refused;

    /// <summary>
    /// Refuses the start, in the sentence <see cref="Refusal"/> gives, where this process does not compose the letter and its
    /// mark into one.
    /// </summary>
    public static void EnsureThisProcessComposes()
    {
        if (Refusal(LetterAndMark.Normalize(NormalizationForm.FormC)) is { } refusal)
        {
            throw new TextCompositionUnavailableException(refusal);
        }
    }
}
