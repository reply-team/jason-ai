using System.Text.Json.Nodes;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Approvals;

/// <summary>
/// What a person is being asked to approve, as a document, and what that document is called. The claim writes
/// one when it parks work and compares one before it runs it, so the guarantee the gate makes — nothing runs but
/// what was approved — is a comparison of two strings rather than a judgement about two situations.
/// </summary>
/// <param name="Document">The subject itself, which <c>approval.get</c> answers with verbatim.</param>
/// <param name="Hash">Its canonical <c>sha256:</c>, spelled the way a binding's identity and a package digest are.</param>
public sealed record ApprovalSubject(JsonObject Document, string Hash)
{
    /// <summary>
    /// The subject of one parked item: the operation and the version of its contract, the item itself, the whole
    /// composed input the plugin would receive, the plugin that would perform it, and the account it would act
    /// through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is something a change to which makes the decision a different decision, with one exception the
    /// comparison makes rather than the document: a pin added where the approved subject held none (<see cref="Less"/>). A
    /// different argument reaches a different person; a different plugin or binding acts against a different account.
    /// </para>
    /// <para>
    /// The plugin's version and the package digest are deliberately absent. They are provenance — recorded on the
    /// attempt that eventually runs — and including them would supersede every pending decision the moment an
    /// operator reloaded a package, which changes nothing about what is done to whom.
    /// </para>
    /// </remarks>
    public static ApprovalSubject Of(ProviderOpPlan plan, WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(item);

        var document = new JsonObject
        {
            ["binding_identity"] = plan.BindingIdentity,
            ["input"] = plan.Input.DeepClone(),
            ["operation"] = plan.Contract.Id,
            ["operation_version"] = plan.Contract.Version,
            ["plugin"] = plan.Plugin.Manifest.Id,
            ["work_item"] = item.PublicId,
        };

        return new ApprovalSubject(document, CanonicalJson.Hash(document));
    }

    /// <summary>
    /// This subject less every pin it carries, for the person or for the campaign, of a kind <paramref name="approved"/>
    /// does not carry at the same place: this subject as it would be had no pin been added since that one was written, by
    /// whichever attempt learned it; every other change since stays in it. A pin added changes what reaches the plugin,
    /// which then addresses a record the provider already holds rather than making one; it moves nothing the person read,
    /// since such a pin is either not shown to them or bound to an argument they read. A kind both carry stays, with its
    /// value now, so another value or a pin gone still makes another document. This subject itself where nothing was
    /// added.
    /// </summary>
    /// <remarks>
    /// One member more is compared the same way: the earlier letter a message follows (<see cref="FollowsDecision"/>). It is
    /// left out where <paramref name="approved"/> was written by a runtime that composed none and this subject's says the
    /// message is a letter of its own, which is what such a runtime sent; and where both say it is a letter of its own, the
    /// approved one stands in for this one, since the two can differ only in why, and the reason reaches nobody: a package
    /// that came to take follows, or a root taken back, changes nothing the person decided about. Where this subject's says
    /// the message follows a letter it stays, so the person is asked again about a reply in a thread they never saw, and so
    /// does any change where the approved one follows a letter: a reply turned letter of its own is another letter too.
    /// </remarks>
    public ApprovalSubject Less(JsonObject? approved)
    {
        if (Document["input"] is not JsonObject input)
        {
            return this;
        }

        var earlier = approved?["input"] as JsonObject;
        var added = CanonicalInput.Added(input, earlier);
        var letter = NewLetterAdded(input, earlier);
        var reason = AnotherReason(input, earlier);
        if (added.Contact.Count == 0 && added.Campaign.Count == 0 && !letter && reason is null)
        {
            return this;
        }

        var less = CanonicalInput.Without(input, added);
        if (letter)
        {
            less.Remove(FollowsDecision.Key);
        }
        else if (reason is not null)
        {
            less[FollowsDecision.Key] = reason;
        }

        var document = Document.DeepClone().AsObject();
        document["input"] = less;
        return new ApprovalSubject(document, CanonicalJson.Hash(document));
    }

    /// <summary>
    /// Whether <paramref name="input"/> carries the decision that the message is a letter of its own, at the root, where
    /// <paramref name="earlier"/> carries no decision at all: written by a runtime that composed none.
    /// </summary>
    private static bool NewLetterAdded(JsonObject input, JsonObject? earlier) =>
        OwnLetter(input[FollowsDecision.Key]) && earlier?.ContainsKey(FollowsDecision.Key) != true;

    /// <summary>
    /// The decision <paramref name="earlier"/> carries, where it and <paramref name="input"/> both say the message is a
    /// letter of its own and say it differently, which can only be why; null otherwise.
    /// </summary>
    private static JsonObject? AnotherReason(JsonObject input, JsonObject? earlier) =>
        OwnLetter(input[FollowsDecision.Key])
        && earlier?[FollowsDecision.Key] is JsonObject approved
        && OwnLetter(approved)
        && !JsonNode.DeepEquals(input[FollowsDecision.Key], approved)
            ? approved.DeepClone().AsObject()
            : null;

    /// <summary>Whether a composed decision says the message is a letter of its own: it names no work item it follows.</summary>
    private static bool OwnLetter(JsonNode? decided) => decided is JsonObject follows && follows["work_item"] is null;
}
