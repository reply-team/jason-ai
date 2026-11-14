using System.Runtime.CompilerServices;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Replies;

/// <summary>
/// Whether a contact stands opted out: the second lock beside the do-not-contact register, which the claim asks right
/// after the register, under the same gate. The register is by value; this is by the person, so a value the contact gains
/// after the verdict — a new address, a profile they did not have — is refused too, and so is a contact who had no value
/// at all when they asked. It is installation-wide: a person's request to be left alone is not one campaign's.
/// </summary>
/// <remarks>
/// <para>
/// STANDING: a <c>reply_classified</c> line, in any campaign, whose nature is <c>opt_out</c> and whose body names the
/// contact, with no later verdict under its key in its campaign that lifts it. Only a person's verdict lifts it: one whose
/// body carries <c>answered_by</c>, which <see cref="ReplyService"/> writes only for a decision of that campaign a named
/// person answered, checked there; or one written by a <c>human</c> actor outside a run, whose line therefore names no
/// attempt. A human named from inside a run lifts nothing, since such a line carries the run's attempt; a role's or an
/// attempt's own verdict never lifts it. A verdict on another reply, whatever it says, lifts nothing, and taking a value
/// off the register lifts only that entry.
/// </para>
/// <para>
/// The read is bounded by the contact: the contact's own lines through the contact index, the body held to the same
/// contact, and the verdict that would lift one through the verdicts' thread index by its campaign, its thread and its
/// key. The journal's contact column of a verdict is the contact its body names (<see cref="ReplyService"/> writes it so),
/// so the person a verdict names is the person whose lines hold it. It runs inside the claim's write transaction, and a
/// test pins its plan.
/// </para>
/// </remarks>
public static class OptOuts
{
    /// <summary>The id of the earliest opt-out verdict that stands for the contact, by its internal key; null where none does.</summary>
    public static Task<string?> StandingAsync(JasonDbContext db, int contactId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return Standing(db, contactId).FirstOrDefaultAsync(ct)!;
    }

    internal static IQueryable<string> Standing(JasonDbContext db, int contactId) =>
        db.Database.SqlQuery<string>(FormattableStringFactory.Create(
            """
            SELECT v.public_id AS "Value" FROM journal AS v INDEXED BY ix_journal_contact_id_public_id
            WHERE v.contact_id = {0} AND v.kind = 'reply_classified'
              AND json_extract(v.new_json, '$.nature') = 'opt_out'
              AND json_extract(v.new_json, '$.contact_id') = (SELECT public_id FROM contacts WHERE id = {0})
              AND NOT EXISTS (SELECT 1 FROM journal AS lift INDEXED BY ix_journal_campaign_verdict_thread
                  WHERE lift.kind = 'reply_classified' AND lift.campaign_id = v.campaign_id
                    AND json_extract(lift.new_json, '$.thread') = json_extract(v.new_json, '$.thread')
                    AND lift.key = v.key AND lift.id > v.id
                    AND (json_extract(lift.new_json, '$.answered_by') IS NOT NULL
                      OR (lift.actor_type = 'human' AND lift.attempt_id IS NULL)))
            ORDER BY v.public_id
            LIMIT 1
            """,
            contactId));
}
