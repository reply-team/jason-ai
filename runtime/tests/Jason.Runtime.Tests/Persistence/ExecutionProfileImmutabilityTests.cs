using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// An attempt names the revision it ran under for ever, so a revision that could be edited would let a profile
/// change what an attempt says it did. Two layers refuse that here — the triggers and the interceptor — and each
/// test goes around the other layer, so neither is quietly proving the other's work. The third layer, that no
/// verb rewrites one, is a question about the API and is asserted where the verbs live.
/// <para>
/// The profile row itself is not frozen: it carries which revision is current and whether the profile is
/// disabled, and both are meant to change. The last test here is the guard against freezing too much.
/// </para>
/// </summary>
public class ExecutionProfileImmutabilityTests
{
    [Fact]
    public void Raw_update_and_delete_of_a_revision_are_refused_by_the_triggers()
    {
        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
            db.SaveChanges();
        }

        // Straight at the file: no DbContext, so the interceptor is not in this path at all and what answers is
        // the database itself.
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE execution_profile_revisions SET program = 'somewhere-else'";
        var refused = Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
        Assert.Contains("immutable", refused.Message, StringComparison.Ordinal);

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM execution_profile_revisions";
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT program FROM execution_profile_revisions";
        Assert.Equal("claude", read.ExecuteScalar());
    }

    [Fact]
    public void Ef_core_refuses_to_modify_or_remove_a_revision_before_it_reaches_the_database()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var profile = Profile("prf_B", "second");
        db.ExecutionProfiles.Add(profile);
        db.SaveChanges();

        var revision = profile.Revisions[0];
        revision.Program = "somewhere-else";
        var modified = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("immutable", modified.Message, StringComparison.Ordinal);

        db.Entry(revision).State = EntityState.Deleted;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        // Nothing reached the file, which is what makes this a layer of its own rather than a second reading of
        // the triggers: a fresh context still sees the revision as it was written.
        db.ChangeTracker.Clear();
        Assert.Equal("claude", db.ExecutionProfileRevisions.AsNoTracking().Single(r => r.Profile!.PublicId == "prf_B").Program);
    }

    [Fact]
    public void The_profile_row_still_moves_because_a_revision_and_a_profile_are_not_the_same_thing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var profile = Profile("prf_C", "third");
        db.ExecutionProfiles.Add(profile);
        db.SaveChanges();

        profile.CurrentRevision = 2;
        profile.DisabledAt = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        db.SaveChanges();

        db.ChangeTracker.Clear();
        var reread = db.ExecutionProfiles.AsNoTracking().Single(p => p.PublicId == "prf_C");
        Assert.Equal(2, reread.CurrentRevision);
        Assert.NotNull(reread.DisabledAt);
    }

    private static ExecutionProfile Profile(string publicId, string name)
    {
        var now = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);
        var profile = new ExecutionProfile
        {
            PublicId = publicId,
            Name = name,
            CurrentRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = 1,
            Host = AgentHostKind.ClaudeCode,
            Program = "claude",
            Args = [],
            Deny = ["Write"],
            CreatedByType = ActorType.Human,
            CreatedAt = now,
        });

        return profile;
    }
}
