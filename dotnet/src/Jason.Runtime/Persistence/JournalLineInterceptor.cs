using Jason.Contracts.Journal;
using Jason.Contracts.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

/// <summary>
/// Holds every journal line of a kind the runtime reserves to that kind's document as it is written: the body against
/// the document's schema, and each entity the document says is always on a line's own column. It sits beside
/// <see cref="AppendOnlyInterceptor"/> rather than in the writer so a line written any other way is held as well. A kind
/// with no document — one a person or an agent appends under a name of its own — is not checked. What happens to a line
/// off its document is <see cref="JournalLines"/>'s to say.
/// </summary>
public sealed class JournalLineInterceptor(JournalLines lines, PatternTiming timing) : SaveChangesInterceptor
{
    /// <summary>The interceptor every context is given: the body's patterns matched as every runtime site matches them.</summary>
    public JournalLineInterceptor(JournalLines lines)
        : this(lines, PatternTiming.Default)
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State != EntityState.Added || JournalKindCatalog.Find(entry.Entity.Kind) is not { } contract)
            {
                continue;
            }

            var line = entry.Entity;
            // A pattern that did not finish in time is no verdict on the value: it neither refuses a line nor warns of one.
            var pointers = SchemaValidator.Verdicts(contract.CheckBody(line.Key, line.Old, line.New, timing))
                .Select(problem => problem.Pointer)
                .Concat(MissingColumns(contract, line))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (pointers.Count > 0)
            {
                lines.Report(line.Kind, pointers);
            }
        }
    }

    /// <summary>The columns the document says every line of the kind names and this line leaves empty.</summary>
    internal static IEnumerable<string> MissingColumns(JournalKindContract contract, JournalEntry line)
    {
        foreach (var entity in contract.Entities.Values)
        {
            if (entity.Presence != JournalEntityPresence.Always)
            {
                continue;
            }

            var named = entity.At switch
            {
                "/campaign_id" => line.CampaignId is not null || line.Campaign is not null,
                "/contact_id" => line.ContactId is not null || line.Contact is not null,
                "/work_item_id" => line.WorkItemId is not null,
                "/attempt_id" => line.AttemptId is not null,
                _ => true,
            };

            if (!named)
            {
                yield return entity.At;
            }
        }
    }
}
