using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>What one work item records about the run that caused it, in the four values the row keeps.</summary>
public readonly record struct LineageRecord(LineageState State, string? ProfileName, int? ProfileRevision, string? FromAttemptId)
{
    /// <summary>Work nobody's run caused. It legitimately resolves to the global default.</summary>
    public static LineageRecord Root { get; } = new(LineageState.Root, null, null, null);

    /// <summary>A run caused this and nothing about its profile can be read, so the claim will refuse it.</summary>
    public static LineageRecord Unresolved { get; } = new(LineageState.Unresolved, null, null, null);

    /// <summary>The record an item already holds, to be handed on exactly as it stands.</summary>
    public static LineageRecord Of(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new LineageRecord(item.LineageState, item.LineageProfileName, item.LineageProfileRevision, item.LineageFromAttemptId);
    }
}

/// <summary>
/// Causal lineage: work inherits the execution profile of the run that caused it. It is read once, when the item
/// is created, and written onto the row — never recomputed, because a walk back through the ancestry would
/// answer with whatever that history has since been edited into, and the question is what caused <em>this</em>.
/// <para>
/// The read is one hop and no more. An attempt that pinned a profile is the answer; an attempt that pinned none
/// hands on its own item's record unchanged, which is how a chain crosses deterministic work: a provider
/// operation created by an agent attempt carries a record it will never use itself and gives it to whatever its
/// own attempt creates.
/// </para>
/// </summary>
public static class Lineage
{
    /// <summary>What the creating actor hands down to the item it is about to create.</summary>
    public static async Task<LineageRecord> ForCreationAsync(JasonDbContext db, ActorRef actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);

        // A person, a role or the runtime itself is root work. A role deliberately so: it is an interactive
        // agent session that its user owns, the runtime never launched it, and there is no profile anywhere to
        // read for it — so calling that unresolvable ancestry would block the ordinary interactive path over a
        // run that never had a profile to change in the first place.
        if (actor.Type != ActorType.Attempt || string.IsNullOrWhiteSpace(actor.Id))
        {
            return LineageRecord.Root;
        }

        var id = actor.Id.Trim();
        var ancestor = await db.Attempts
            .AsNoTracking()
            .Include(a => a.WorkItem)
            .FirstOrDefaultAsync(a => a.PublicId == id, cancellationToken)
            .ConfigureAwait(false);

        // The actor was verified before this was read, so an attempt that cannot be read here is not a caller's
        // mistake — it is ancestry nothing can be said about, which is what unresolved means.
        if (ancestor?.WorkItem is not { } causedBy)
        {
            return LineageRecord.Unresolved;
        }

        // The profile that attempt actually ran under, and the launch record is what "actually" means. An agent
        // record without a name resolved no profile at all — the role's own entry command ran it — so there is
        // nothing to pin; its item's record carries on instead.
        //
        // A claim refused before any child existed keeps its provenance too, deliberately, so that an operator
        // can read which profile was chosen. That is a fact about a choice and not a chain: inheriting it handed
        // work created because of the refusal the very profile that could not start — and the manager loop's
        // review of such a failure was then refused for the same reason, which is the one review that had to
        // run. Nothing launched, so nothing is handed down.
        if (ancestor.Launch is not null && ancestor.Provenance?.Agent is { ProfileName: { } profile } agent)
        {
            return new LineageRecord(LineageState.Inherited, profile, agent.ProfileRevision, ancestor.PublicId);
        }

        // One hop: whatever the causing item holds is handed on exactly as it stands. Inherited carries forward
        // with the attempt it was pinned at, root stays root, and unresolved stays unresolved.
        return LineageRecord.Of(causedBy);
    }
}
