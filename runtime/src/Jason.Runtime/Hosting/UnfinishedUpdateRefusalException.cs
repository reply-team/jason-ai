using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Hosting;

/// <summary>
/// An update stopped part-way from the step that moves the installed build aside on, or its ledger cannot be read, and
/// this start is not the update's own, so this runtime has not started (<see cref="Contracts.Update.UnfinishedUpdate"/>).
/// It is said where the start was asked for as <c>jason runtime start</c> says it, in the same envelope with the same
/// code, so a person reads one refusal whichever of the two met the update.
/// </summary>
public sealed class UnfinishedUpdateRefusalException(string code, string message) : Exception(message)
{
    /// <summary><c>update_in_progress</c>, or <c>update_ledger_invalid</c>.</summary>
    public string Code { get; } = code;

    /// <summary>The refusal as one line of the envelope every refusal of this product carries.</summary>
    public string Envelope => JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(Code, Message, Retryable: false)), JasonJson.Options);
}
