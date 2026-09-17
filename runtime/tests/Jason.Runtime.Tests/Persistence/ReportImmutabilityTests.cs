using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// An admitted report is somebody's word about something that already happened. There is nothing in it for the
/// runtime to correct later, and a runtime able to rewrite one would be able to make a reporter say what it
/// wanted. Two layers refuse that here — the triggers and the interceptor — and each test goes around the other
/// layer, so neither is quietly proving the other's work. The third layer, that no verb writes one, is a
/// question about the API and is asserted where the verbs live.
/// </summary>
public class ReportImmutabilityTests
{
    [Fact]
    public void Raw_update_and_delete_of_a_report_are_refused_by_the_triggers()
    {
        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            db.Reports.Add(Admitted("rpt_A"));
            db.SaveChanges();
        }

        // Straight at the file: no DbContext, so the interceptor is not in this path at all and what answers is
        // the database itself.
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE reports SET summary = 'rewritten' WHERE public_id = 'rpt_A'";
        var refused = Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
        Assert.Contains("immutable", refused.Message, StringComparison.Ordinal);

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM reports WHERE public_id = 'rpt_A'";
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT summary FROM reports WHERE public_id = 'rpt_A'";
        Assert.Equal("what the reporter said happened", read.ExecuteScalar());
    }

    [Fact]
    public void Ef_core_refuses_to_modify_or_remove_a_report_before_it_reaches_the_database()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var report = Admitted("rpt_B");
        db.Reports.Add(report);
        db.SaveChanges();

        report.Summary = "rewritten";
        var modified = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("immutable", modified.Message, StringComparison.Ordinal);

        db.Entry(report).State = EntityState.Deleted;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        // Nothing reached the file, which is what makes this a layer of its own rather than a second reading of
        // the triggers: a fresh context still sees the row the reporter submitted.
        db.ChangeTracker.Clear();
        Assert.Equal("what the reporter said happened", db.Reports.AsNoTracking().Single(r => r.PublicId == "rpt_B").Summary);
    }

    private static Report Admitted(string publicId) => new()
    {
        PublicId = publicId,
        ReporterType = ActorType.Human,
        ReporterId = "person-1",
        Effect = "email_sent",
        Tool = "some-other-cli",
        Summary = "what the reporter said happened",
        Assertion = new JsonObject { ["effect"] = "email_sent", ["tool"] = "some-other-cli" },
        AssertionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        ReceivedAt = DateTime.UtcNow,
    };
}
