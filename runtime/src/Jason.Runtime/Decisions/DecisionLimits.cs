namespace Jason.Runtime.Decisions;

/// <summary>
/// What a question and its answer may be. One set of numbers, named once: the schema is built from them, the
/// service refuses by them and the contract page prints them, so a bound that moves in one place and not the
/// others cannot be shipped.
/// </summary>
public static class DecisionLimits
{
    /// <summary>
    /// The same 2000 characters every other free-text field in this runtime is held to — and a listing carries
    /// the question whole, because "what am I being asked?" is the only reason to open one.
    /// </summary>
    public const int MaxQuestionLength = 2000;

    public const int MaxAnswerLength = 2000;

    public const int MaxOptionLabelLength = 200;

    public const int MaxOptionDetailLength = 1000;

    /// <summary>Ten named answers. A question with more than that is a question nobody can answer at a glance.</summary>
    public const int MaxOptions = 10;

    /// <summary>What to read before deciding, by id. Fifty is a reading list; more is a database dump.</summary>
    public const int MaxReferences = 50;

    /// <summary>A public id is at most this long anywhere in this runtime, so a reference that is longer is not one.</summary>
    public const int MaxReferenceIdLength = 40;
}
