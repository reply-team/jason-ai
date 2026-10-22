using System.Text;
using System.Text.RegularExpressions;
using Jason.Runtime.Campaigns;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// Every count and list of the Campaigns screen reads one campaign's rows through an index: SQLite's plan for each never
/// scans a table of the installation, never reads every journal line of one kind, and never finds approvals or questions
/// by their status alone — each of those grows with the installation rather than with the campaigns asked.
/// </summary>
public partial class CampaignPeoplePlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_campaign_is_counted_without_reading_the_whole_installation()
    {
        using var database = new TestDatabase();
        await using var connection = new SqliteConnection($"Data Source={database.File}");
        await connection.OpenAsync(Ct);
        var scans = new StringBuilder();

        string[] counts = [CampaignPeople.SummaryCountsSql, CampaignPeople.ActivityCountsSql];
        string[] why = [CampaignWhy.FirstReplies, CampaignWhy.Additions, CampaignWhy.Removals, CampaignWhy.OptOuts];
        foreach (var sql in CampaignPeople.Summary.Values.Concat(CampaignPeople.Activity.Values).Concat(counts).Concat(why))
        {
            // {0} is the campaigns asked (or the one campaign), {1} and {2} the period, or {1} the page's people.
            var one = sql.Replace("{0}", "'[1]'", StringComparison.Ordinal)
                .Replace("IN (SELECT value FROM json_each({1}))", "IN (SELECT value FROM json_each('[1]'))", StringComparison.Ordinal)
                .Replace("{1}", "'2026-10-09 00:00:00'", StringComparison.Ordinal)
                .Replace("{2}", "'2026-10-16 09:00:00'", StringComparison.Ordinal);
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + one;
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                var step = reader.GetString(reader.GetOrdinal("detail"));
                if (TableScan().IsMatch(step))
                {
                    scans.AppendLine(step);
                }
            }
        }

        Assert.True(scans.Length == 0, scans.ToString());
    }

    [GeneratedRegex("^SCAN (w|j|cc|m|a|d|ch|s|p)( |$)|USING (COVERING )?INDEX ix_journal_kind \\(kind=\\?\\)|USING (COVERING )?INDEX ix_(approvals|decisions)_status")]
    private static partial Regex TableScan();
}
