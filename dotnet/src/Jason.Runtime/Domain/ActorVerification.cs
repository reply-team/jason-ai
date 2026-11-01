using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Domain;

/// <summary>
/// The one actor claim the runtime can check. A human or a role is taken at its word — the runtime holds one
/// capability token and no caller identity — but an attempt is a row in this database, so a claim to be one is
/// either true or a mistake worth telling the caller about.
/// </summary>
public static class ActorVerification
{
    public static async Task VerifyAsync(JasonDbContext db, ActorRef actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Type != ActorType.Attempt)
        {
            return;
        }

        var id = actor.Id ?? string.Empty;
        var known = await db.Attempts.AsNoTracking().AnyAsync(a => a.PublicId == id, cancellationToken).ConfigureAwait(false);
        if (!known)
        {
            throw new ValidationException([new ErrorDetail("actor.id", "unknown", "actor.id must name an attempt this runtime knows.")]);
        }
    }
}
