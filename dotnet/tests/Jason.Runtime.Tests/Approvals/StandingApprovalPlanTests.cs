using System.Globalization;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// Each read a standing approval makes of the chronicle names the index it is answered from, and SQLite answers it from
/// that index, bounded as the reason in <see cref="StandingApprovals"/> says: by the kind, and for a count or a later
/// verdict by the kind and the line's own place in the chronicle. Its one read of the approvals is bounded by the
/// campaign. The plans are read from the very queries the claim and the verbs run.
/// </summary>
public class StandingApprovalPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("the standing approval in force", "ix_journal_kind (kind=?)")]
    [InlineData("the campaign's standing approvals", "ix_journal_kind (kind=?)")]
    [InlineData("the releases since the set", "ix_journal_kind (kind=? AND rowid>?)")]
    [InlineData("a later verdict on the same reply", "ix_journal_kind (kind=? AND rowid>?)")]
    [InlineData("a change to the draft's context", "ix_journal_work_item_id_public_id (work_item_id=?)")]
    public async Task StandingApprovals_EachRead_UsesTheIndexItNames(string read, string search)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var line = new JournalEntry { Id = 41, PublicId = "jrn_01K52JR0000000000000000041", CampaignId = 3, Key = "interested", Kind = "standing_approval_set" };
        IQueryable query = read switch
        {
            "the standing approval in force" => StandingApprovals.Newest(db, 3, "interested").Take(1),
            "the campaign's standing approvals" => StandingApprovals.Lines(db, 3),
            "the releases since the set" => StandingApprovals.Released(db, line),
            "a later verdict on the same reply" => StandingApprovals.Later(db, line),
            "a change to the draft's context" => StandingApprovals.ContextChanges(db, "wi_01K52JR0000000000000000007"),
            _ => throw new ArgumentOutOfRangeException(nameof(read), read, "no such read"),
        };

        var plan = await PlanAsync(db, query.ToQueryString());

        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX " + search, StringComparison.Ordinal)),
            $"The plan of {read} is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether anybody was asked about the work is read through the campaign's approvals, never by walking every approval
    /// the installation holds: the table only grows, and the claim asks this of each draft it may release.
    /// </summary>
    [Fact]
    public async Task StandingApprovals_AskedBefore_ReadsTheCampaignsApprovals()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await PlanAsync(db, StandingApprovals.Asked(db, 3, 7).ToQueryString());

        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH approvals USING INDEX ix_approvals_campaign_id (campaign_id=?)", StringComparison.Ordinal)),
            $"The plan is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN approvals", StringComparison.Ordinal));
    }

    /// <summary>The query as EF Core writes it, its parameters bound as it binds them, asked for its plan.</summary>
    private static async Task<List<string>> PlanAsync(JasonDbContext db, string written)
    {
        await using var connection = new SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        var sql = new List<string>();
        foreach (var text in written.Split('\n'))
        {
            var row = text.TrimEnd('\r');
            if (row.StartsWith(".param set ", StringComparison.Ordinal))
            {
                var parts = row[".param set ".Length..].Split(' ', 2);
                var value = parts[1];
                command.Parameters.AddWithValue(
                    parts[0],
                    value.StartsWith('\'')
                        ? value[1..^1].Replace("''", "'", StringComparison.Ordinal)
                        : long.Parse(value, CultureInfo.InvariantCulture));
            }
            else
            {
                sql.Add(row);
            }
        }

        command.CommandText = "EXPLAIN QUERY PLAN " + string.Join('\n', sql).Trim();
        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            plan.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return plan;
    }
}
