namespace Jason.Cli.Update;

/// <summary>
/// What <c>jason update apply --finish</c> answers on a jason Jason Desktop carries with no update in flight: what belongs to
/// the installed version brought up to it, part by part (<c>docs/release-and-update.md</c> §7, "The finishing step").
/// </summary>
/// <param name="Version">The installed version, this jason's own.</param>
/// <param name="DataDir">The data directory the parts belong to.</param>
/// <param name="DataDirFound">
/// Whether that directory exists. Where it does not, nothing is done and nothing is made under it, and every part says so.
/// </param>
/// <param name="InstallationFound">
/// Whether an installation is there: false where no data directory exists or it holds nothing but Jason Desktop's own
/// (<see cref="Commands.UpdateCommands.InstallationAt"/>), and then nothing was done, no release read and nothing made, so
/// nothing is left to finish; true in every other answer. The one definition of "an installation is here" the Desktop reads.
/// </param>
/// <param name="Complete">Whether every part is <c>done</c> or <c>already_there</c>.</param>
/// <param name="Parts"><c>plugins</c>, <c>skills</c> and <c>ui</c>, in that order.</param>
public sealed record UpdateFinishResponse(string Version, string DataDir, bool DataDirFound, bool InstallationFound, bool Complete, IReadOnlyList<UpdateFinishPart> Parts);

/// <summary>One part of the finishing step.</summary>
/// <param name="Part"><c>plugins</c>, <c>skills</c> or <c>ui</c>.</param>
/// <param name="State"><c>done</c>, <c>already_there</c> or <c>not_done</c> (<see cref="UpdateFinishStates"/>).</param>
/// <param name="Reason">At <c>not_done</c>, why, in one sentence; null otherwise.</param>
/// <param name="Lines">What the part did and left, one line each, as an update's step lines say them.</param>
public sealed record UpdateFinishPart(string Part, string State, string? Reason, IReadOnlyList<string> Lines);

/// <summary>The three states a part of the finishing step ends in.</summary>
public static class UpdateFinishStates
{
    /// <summary>The part changed something, and it is now the installed version's.</summary>
    public const string Done = "done";

    /// <summary>Nothing needed changing: the part was the installed version's already, or nothing of it is installed.</summary>
    public const string AlreadyThere = "already_there";

    /// <summary>The part could not be brought up; the reason says why, and nothing of the part was left half changed.</summary>
    public const string NotDone = "not_done";
}
