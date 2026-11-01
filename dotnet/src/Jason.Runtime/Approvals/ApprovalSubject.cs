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
    /// Everything here is something a change to which makes the decision a different decision. A different
    /// argument reaches a different person; a different plugin or binding acts against a different account.
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
}
