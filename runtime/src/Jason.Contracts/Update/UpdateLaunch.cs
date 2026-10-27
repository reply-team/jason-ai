namespace Jason.Contracts.Update;

/// <summary>
/// The mark an update puts on its own launch of the runtime: one environment variable, set in that launch alone, that
/// tells <c>jason runtime run</c> the start is the update's and not one made around it.
/// </summary>
/// <remarks>
/// <para>
/// A runtime registered to start at sign-in runs <c>jason runtime run</c>, and so does the apply's own start and the
/// rollback's. From <see cref="UpdateStep.Kept"/> on a run that is not the update's is refused, since the install path
/// may hold a build whose last steps were never taken; the update's own run is the one that takes them, so it has to be
/// told apart, and the command line it shares with every registration cannot tell it.
/// </para>
/// <para>
/// Its value is the step the launch was made at. It says whose start this is and is not a secret: anyone who can set a variable
/// for that program can start it by hand anyway, and what it guards against is a start nobody meant, at a sign-in. It
/// is never put on the command line, which a registration copies, and the runtime takes it out of its own environment
/// as it starts, so nothing the runtime starts carries it on.
/// </para>
/// </remarks>
public static class UpdateLaunch
{
    /// <summary>The variable's name.</summary>
    public const string Variable = "JASON_UPDATE_LAUNCH";

    /// <summary>The value an update's launch made at <paramref name="step"/> carries: the step's own word.</summary>
    public static string At(UpdateStep step) => step.ToString().ToLowerInvariant();

    /// <summary>Whether <paramref name="value"/>, read from the variable, marks a launch as the update's own.</summary>
    public static bool Marks(string? value) => !string.IsNullOrWhiteSpace(value);
}
