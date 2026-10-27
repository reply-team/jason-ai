using Jason.Contracts.Discovery;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// An update that stopped part-way, as the verbs that are not the update read it: <c>jason status</c>, which names it
/// with the apply that finishes it, and <c>jason runtime start</c> and <c>restart</c>, which start nothing around it once
/// it has touched the install path.
/// </summary>
/// <remarks>
/// <para>
/// An apply that fails after the swap leaves the runtime stopped and the new build at the install path. The two verbs a
/// person reaches for next read no ledger, so status offered <c>jason runtime start</c>, and that ran the new build
/// outside the update: the health check, the skills, the UI and the ledger's own end were never done, and nothing said
/// so. The ledger is the one record of what is in flight; a completed one is the record of the last update and is not.
/// </para>
/// <para>
/// Before <see cref="UpdateStep.Kept"/> nothing has been replaced: the build at the install path is the one installed
/// before, and an apply that could not download, or met an asset that failed its digest, stops there. Starting that build
/// is what a start is for, and the apply resumes the update past a running runtime, so only from <c>kept</c> on is a
/// start refused. A ledger that cannot be read says neither, and is refused: whether the install path still holds the
/// build that was there cannot be told.
/// </para>
/// </remarks>
/// <param name="Path">Where the ledger is.</param>
/// <param name="Ledger">The ledger of the update in flight, or null where it could not be read.</param>
/// <param name="Unreadable">Why the file at <paramref name="Path"/> could not be read as a ledger, or null where it was.</param>
internal sealed record UnfinishedUpdate(string Path, UpdateLedger? Ledger, string? Unreadable)
{
    /// <summary>
    /// The update in flight, an unreadable ledger, or null where there is no ledger or the last update completed. Never
    /// throws: a file the account cannot open is an unreadable ledger, said by whoever asked, and not an error that ends
    /// <c>jason status</c> with no body.
    /// </summary>
    public static UnfinishedUpdate? Read(JasonPaths paths)
    {
        var path = new UpdatePaths(paths).Ledger;
        try
        {
            return UpdateLedger.ReadFile(path) is { Step: not UpdateStep.Complete } ledger ? new UnfinishedUpdate(path, ledger, null) : null;
        }
        catch (UpdateLedgerException invalid)
        {
            return new UnfinishedUpdate(path, null, invalid.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new UnfinishedUpdate(path, null, $"The update ledger at '{path}' could not be read: {error.Message}");
        }
    }

    /// <summary>
    /// Whether the install path may no longer hold the build installed before: from <see cref="UpdateStep.Kept"/> on, or
    /// where the ledger cannot say.
    /// </summary>
    public bool Replacing => Ledger is not { Step: < UpdateStep.Kept };

    /// <summary>The sentence the verbs open with: which update, and the step it stopped at; or why the ledger is unreadable.</summary>
    public string Said() => Ledger is { } ledger
        ? $"An update from {ledger.FromVersion} to {ledger.ToVersion} stopped at '{ledger.Step.ToString().ToLowerInvariant()}' and is not finished"
        : $"{Unreadable} Whether an update is in flight cannot be told from it, so nothing is started around it; if no update is under way, removing it is the way on";

    /// <summary>The refusal of a start or a restart, as one envelope: the code and the message.</summary>
    public (string Code, string Message) Refusal(string nothing) => Ledger is { } ledger
        ? (UpdateCodes.InProgress,
            $"{Said()}. `jason update apply` finishes it and starts the runtime as part of it, and `jason update rollback` goes back to {ledger.FromVersion}. {nothing}")
        : (UpdateLedgerException.Invalid, $"{Said()}. {nothing}");

    /// <summary>What a start says before the swap, beside the runtime it starts.</summary>
    public string Waiting() =>
        $"{Said()}; nothing has been replaced, so the installed {Ledger!.FromVersion} is started. `jason update apply` resumes the update.";
}
