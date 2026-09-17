using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Approvals;

/// <summary>
/// What a person reads before deciding: the operation's own sentence about itself, the dangerous reading of what
/// it reaches, undoes and costs, who it would act on and where they would be reached, which account it would act
/// through, and the exact document it would send.
/// </summary>
/// <remarks>
/// <para>
/// It is assembled at the claim, where all of that is already in hand, and stored beside the subject — so what a
/// person reads is what the claim saw. Assembling it again at read time would mean answering from rows that may
/// have moved since, which is the one thing a preview must not do.
/// </para>
/// <para>
/// Nothing here is a judgement. Every property is copied from the published contract, condition and all: a
/// conditional block's <c>value</c> is always the dangerous reading, and the detail is what says when it is less
/// than that. Resolving the condition here would be the runtime deciding what to tell a person about risk.
/// </para>
/// </remarks>
public static class ApprovalPreview
{
    public static JsonObject Of(
        OperationContract contract,
        ApprovalSubject subject,
        Campaign campaign,
        Contact? contact,
        string? channel)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(campaign);

        return new JsonObject
        {
            ["intent"] = contract.Intent,
            ["reach"] = Property(contract.Reach),
            ["reversibility"] = Property(contract.Reversibility),
            ["cost"] = Property(contract.Cost),
            ["campaign"] = new JsonObject
            {
                ["id"] = campaign.PublicId,
                ["name"] = campaign.Name,
            },
            ["contact"] = Person(contact, channel),
            ["plugin"] = (string?)subject.Document["plugin"],
            ["binding_identity"] = (string?)subject.Document["binding_identity"],
            ["subject"] = subject.Document.DeepClone(),
        };
    }

    private static JsonObject Property(ContractProperty property) => new()
    {
        ["value"] = property.Value,
        ["conditional"] = property.Conditional,
        ["detail"] = property.Detail,
    };

    /// <summary>
    /// The person and the one value they would be reached at — not the whole contact, because a preview is about
    /// this effect and a decision is not the place to publish everything Jason knows about somebody.
    /// </summary>
    private static JsonObject? Person(Contact? contact, string? channel)
    {
        if (contact is null)
        {
            return null;
        }

        var name = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return new JsonObject
        {
            ["id"] = contact.PublicId,
            ["name"] = name.Length == 0 ? null : name,
            ["channel"] = channel,
            ["value"] = channel is null ? null : CanonicalInput.Reachable(contact, channel)?.Value,
        };
    }
}
