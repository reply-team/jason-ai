using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ProjectsAndPreferences = Jason.Runtime.Persistence.Migrations.ProjectsAndPreferences;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Projects arrive in an installation that already holds campaigns. A campaign's project is added where <c>campaigns</c>
/// stands — the column with its reference, every campaign put in the default, two triggers refusing a campaign without one
/// — and <c>role_notes</c> is rebuilt, so that a note may name no campaign: copied into a new table, the old one dropped
/// and the new one renamed, with the database's foreign keys off while it happens. Each case here seeds a database at the
/// migration before, as an installation upgraded from it holds one, with every kind of row that refers to a campaign, and
/// reads what the migration left.
/// </summary>
public class ProjectsAndPreferencesMigrationTests
{
    private const string PreviousMigration = "20261020172838_JournalByPerson";

    private const string BeforePreviousMigration = "20261020050039_LinkedInOneKey";

    private const string ThisMigration = "ProjectsAndPreferences";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every table whose rows refer to a campaign, to something a campaign's rows refer to, or to one another — and the
    /// two rebuilt tables themselves. Two of them, routes and external identifiers, are deleted with their campaign by
    /// the database: a rebuild that dropped <c>campaigns</c> with the foreign keys on would empty them without a word.
    /// </summary>
    private static readonly string[] Tables =
    [
        "approvals", "attempts", "campaign_contacts", "campaign_routes", "campaigns", "contact_channels", "contacts", "decisions",
        "event_attributions", "event_sources", "events", "external_ids", "journal", "reports", "role_notes", "roles",
        "suppressions", "work_items",
    ];

    /// <summary>
    /// A populated installation at the migration before: three campaigns — live, draft and archived — with members, work
    /// that finished, waits and waits for approval, attempts, an approval, a question, routes, provider identifiers for a
    /// campaign and for a person, a report, two role notes, an event told to a campaign, and journal lines about
    /// campaigns, a work item, a person and nothing in particular. The live campaign's outstanding read is a work item,
    /// so the rebuilt table also refers back to the table that refers to it.
    /// </summary>
    private const string Seed =
        """
        INSERT INTO contacts (public_id, first_name, custom_json, created_at, updated_at)
        VALUES ('cnt_marta', 'Marta', '{"tier":"a"}', '2026-09-01 00:00:00', '2026-09-01 00:00:00'),
               ('cnt_bo', 'Bo', '{}', '2026-09-01 00:00:00', '2026-09-01 00:00:00');
        INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES (1, 'email', 'marta@example.com', 1);
        INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, archived_at, idempotency_key,
                               idempotency_digest, manager_review_seconds, execution_profile)
        VALUES ('cmp_live', 'Autumn outreach', 'active', '{"icp":"founders"}', '2026-09-01 00:00:00', '2026-09-02 00:00:00', NULL,
                'launch-autumn', 'sha256:0000000000000000000000000000000000000000000000000000000000000001', 900, 'local-claude'),
               ('cmp_draft', 'Spring test', 'draft', '{}', '2026-09-01 00:00:00', '2026-09-01 00:00:00', NULL, NULL, NULL, NULL, NULL),
               ('cmp_gone', 'Old pilot', 'archived', '{}', '2026-08-01 00:00:00', '2026-08-02 00:00:00', '2026-08-02 00:00:00',
                NULL, NULL, NULL, NULL);
        INSERT INTO campaign_contacts (campaign_id, contact_id, state, added_at, updated_at)
        VALUES (1, 1, 'enrolled', '2026-09-01 00:00:00', '2026-09-01 00:00:00'),
               (2, 2, 'enrolled', '2026-09-01 00:00:00', '2026-09-01 00:00:00');
        INSERT INTO work_items (public_id, campaign_id, contact_id, kind, role, operation, status, priority, created_by_type,
                                context_json, attempt_count, created_at, updated_at, result_json, finished_at)
        VALUES ('wi_done', 1, 1, 'ai_role', 'researcher', NULL, 'succeeded', 0, 'human', '{"brief":"read the site"}', 1,
                '2026-09-01 00:00:00', '2026-09-01 01:00:00', '{"found":true}', '2026-09-01 01:00:00'),
               ('wi_read', 1, NULL, 'provider_op', NULL, 'conversation.list', 'created', 0, 'system', '{}', 0,
                '2026-09-02 00:00:00', '2026-09-02 00:00:00', NULL, NULL),
               ('wi_send', 2, 2, 'provider_op', NULL, 'message.send', 'awaiting_approval', 0, 'role', '{}', 1,
                '2026-09-02 00:00:00', '2026-09-02 00:00:00', NULL, NULL);
        UPDATE campaigns SET inbound_sync_item_id = 2, inbound_watermark = '2026-09-02 00:00:00', events_from = '2026-09-02 00:00:00',
                             manager_review_anchor = '2026-09-02 00:00:00', manager_event_watermark = 3
        WHERE id = 1;
        INSERT INTO attempts (public_id, work_item_id, number, command, status, context_snapshot_json, claimed_at, lock_until, finished_at)
        VALUES ('att_done', 1, 1, 'ai_role', 'succeeded', '{}', '2026-09-01 00:10:00', '2026-09-01 00:40:00', '2026-09-01 01:00:00'),
               ('att_send', 3, 1, 'provider_op', 'interrupted', '{}', '2026-09-02 00:10:00', '2026-09-02 00:40:00', '2026-09-02 00:11:00');
        INSERT INTO approvals (public_id, work_item_id, campaign_id, operation, operation_version, subject_json, subject_hash, preview_json,
                               plugin_id, route_scope, plugin_snapshot_id, routing_snapshot_id, status, reason, requested_at)
        VALUES ('apr_send', 3, 2, 'message.send', 1, '{"to":"cnt_bo"}', 'sha256:subject', '{"text":"Hello"}', 'example', 'campaign_default',
                'pls_1', 'rts_1', 'pending', 'operation_requires_approval', '2026-09-02 00:11:00');
        INSERT INTO decisions (public_id, campaign_id, work_item_id, attempt_id, question, status, raised_at)
        VALUES ('dec_ask', 1, 1, 1, 'Which segment first?', 'pending', '2026-09-01 00:30:00');
        INSERT INTO campaign_routes (campaign_id, operation, plugin_id, binding_json, updated_at)
        VALUES (1, NULL, 'example', NULL, '2026-09-01 00:00:00'),
               (1, 'message.send', 'example', '{"account":"sales"}', '2026-09-01 00:00:00'),
               (3, NULL, 'example', NULL, '2026-08-01 00:00:00');
        INSERT INTO external_ids (contact_id, campaign_id, plugin_id, kind, value, recorded_at, recorded_by_attempt_id)
        VALUES (NULL, 1, 'example', 'sequence', 'seq-1', '2026-09-01 01:00:00', 'att_done'),
               (1, NULL, 'example', 'contact', 'person-1', '2026-09-01 01:00:00', 'att_done');
        INSERT INTO reports (public_id, reporter_type, reporter_id, effect, tool, campaign_id, operation_known, summary, assertion_json,
                             assertion_hash, received_at)
        VALUES ('rpt_hand', 'human', 'marta', 'message_sent', 'mail client', 1, 0, 'Wrote by hand', '{}', 'sha256:assertion',
                '2026-09-02 00:00:00');
        INSERT INTO role_notes (campaign_id, role, note_json, note_hash, note_bytes, updated_by_type, created_at, updated_at)
        VALUES (1, 'researcher', '{"seen":["site"]}', 'sha256:note-1', 17, 'role', '2026-09-01 01:00:00', '2026-09-01 01:00:00'),
               (2, 'manager', '{}', 'sha256:note-2', 2, 'role', '2026-09-02 00:00:00', '2026-09-02 00:00:00');
        INSERT INTO suppressions (public_id, channel, value, created_at) VALUES ('sup_one', 'email', 'nobody@example.com', '2026-09-01 00:00:00');
        INSERT INTO event_sources (public_id, plugin_id, binding_identity, created_at)
        VALUES ('evs_one', 'example', 'sha256:binding', '2026-09-01 00:00:00');
        INSERT INTO events (public_id, source_id, plugin_id, kind, occurred_at, stored_at, key, contact_id, data_json)
        VALUES ('evt_one', 1, 'example', 'reply', '2026-09-02 00:00:00', '2026-09-02 00:01:00', 'sha256:event', 1, '{}');
        INSERT INTO event_attributions (event_id, campaign_id, rule) VALUES (1, 1, 'campaign_pin');
        INSERT INTO journal (public_id, ts, actor_type, kind, key, campaign_id, work_item_id, contact_id, new_json)
        VALUES ('jrn_1', '2026-09-01 00:00:00', 'human', 'campaign_created', 'name', 1, NULL, NULL, '"Autumn outreach"'),
               ('jrn_2', '2026-09-01 01:00:00', 'system', 'workitem_succeeded', NULL, 1, 'wi_done', 1, NULL),
               ('jrn_3', '2026-09-01 02:00:00', 'human', 'suppression_added', 'email', NULL, NULL, NULL, NULL),
               ('jrn_4', '2026-08-02 00:00:00', 'human', 'campaign_archived', 'status', 3, NULL, NULL, '"archived"');
        """;

    /// <summary>
    /// The whole start path, the way <c>jason runtime start</c> takes it: the backup first, then the migration. Every row of
    /// every table is there afterwards with every value it had, every campaign — the archived one too — is in the one
    /// default project, and nothing refers to a row that is not there. Every index and trigger of every table, every column
    /// and every reference is there after with the definition it had, but the two the migration means to change; and what
    /// it adds is exactly what it says.
    /// </summary>
    [Fact]
    public void An_upgraded_installation_keeps_every_row_and_every_campaign_lands_in_the_default_project()
    {
        using var dir = Seeded();
        var before = Snapshot(dir.Paths.DatabaseFile);

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Equal(AppliedFrom(dir, ThisMigration), report.NewlyApplied);
        using var after = Open(dir.Paths.DatabaseFile);
        foreach (var (table, rows) in before.Rows)
        {
            Assert.Equal(rows, Dump(after, table, before.Columns[table]));
        }

        var orphans = Rows(after, "PRAGMA foreign_key_check");
        Assert.True(orphans.Count == 0, $"Rows refer to rows that are not there: {string.Join("; ", orphans)}");

        var project = Assert.Single(Rows(after, "SELECT public_id, name, slug, is_default, folder_path IS NULL, created_at = updated_at FROM projects"));
        Assert.Matches("^prj_[0-9A-HJKMNP-TV-Z]{26}\\|Default\\|default\\|1\\|1\\|1$", project);
        Assert.Equal(
            ["cmp_draft|1|1", "cmp_gone|1|1", "cmp_live|1|1"],
            Rows(after, "SELECT c.public_id, p.is_default, c.folder_path IS NULL FROM campaigns c JOIN projects p ON p.id = c.project_id ORDER BY c.public_id"));

        var shape = ShapeOf(after);
        string[] changed = ["column role_notes.campaign_id", "index role_notes.ix_role_notes_one_per_campaign_role"];
        var lost = before.Shape.Where(line => !changed.Contains(KeyOf(line))).Except(shape).ToList();
        Assert.True(lost.Count == 0, $"The migration lost or changed:{Environment.NewLine}{string.Join(Environment.NewLine, lost)}");
        Assert.Equal(
            [
                "column campaigns.folder_path", "column campaigns.project_id", "column role_notes.project_id", "index campaigns.ix_campaigns_project",
                "index preferences.ix_preferences_key", "index projects.ix_projects_one_default", "index projects.ix_projects_public_id",
                "index projects.ix_projects_slug", "index role_notes.ix_role_notes_one_per_client_role",
                "index role_notes.ix_role_notes_one_per_project_role",
                "reference campaigns.project_id", "reference role_notes.project_id",
                "trigger campaigns.campaigns_project_required_insert", "trigger campaigns.campaigns_project_required_update",
            ],
            shape.Select(KeyOf).Except(before.Shape.Select(KeyOf))
                .Where(key => OwnTables.Contains(TableOf(key)))
                .Where(key => !key.StartsWith("column projects.", StringComparison.Ordinal) && !key.StartsWith("column preferences.", StringComparison.Ordinal))

                // Later migrations' columns, which the migrator applies in the same run.
                .Where(key => key is not ("column campaigns.responder_watermark" or "column campaigns.audience_threshold" or "column campaigns.audience_watermark")));
        foreach (var expected in new[]
        {
            "column campaigns.project_id | INTEGER notnull=0 default=none pk=0",
            "reference campaigns.project_id | projects(id) on delete RESTRICT on update NO ACTION",
            "column role_notes.campaign_id | INTEGER notnull=0 default=none pk=0",
            "column role_notes.project_id | INTEGER notnull=0 default=none pk=0",
            "reference role_notes.project_id | projects(id) on delete RESTRICT on update NO ACTION",
            "index role_notes.ix_role_notes_one_per_campaign_role | CREATE UNIQUE INDEX \"ix_role_notes_one_per_campaign_role\" ON \"role_notes\" (\"campaign_id\", \"role\") WHERE campaign_id IS NOT NULL",
            "index role_notes.ix_role_notes_one_per_client_role | CREATE UNIQUE INDEX \"ix_role_notes_one_per_client_role\" ON \"role_notes\" (\"role\") WHERE campaign_id IS NULL AND project_id IS NULL",
            "index role_notes.ix_role_notes_one_per_project_role | CREATE UNIQUE INDEX \"ix_role_notes_one_per_project_role\" ON \"role_notes\" (\"project_id\", \"role\") WHERE project_id IS NOT NULL",
        })
        {
            Assert.True(shape.Contains(expected), $"The migrated database has no '{expected}':{Environment.NewLine}{string.Join(Environment.NewLine, shape)}");
        }

        // The rebuilt table by name: every index it had, made again, and the ones the migration adds; and the rule that a
        // note is at one level, held by the table itself.
        var indexes = Rows(after, "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'role_notes' ORDER BY name");
        Assert.True(
            indexes.SequenceEqual(["ix_role_notes_one_per_campaign_role", "ix_role_notes_one_per_client_role", "ix_role_notes_one_per_project_role"]),
            $"The role notes' indexes after the migration are {string.Join(", ", indexes)}.");
        var definition = Assert.Single(Rows(after, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'role_notes'"));
        Assert.True(
            definition.Contains("CONSTRAINT \"ck_role_notes_one_level\" CHECK (campaign_id IS NULL OR project_id IS NULL)", StringComparison.Ordinal),
            $"The role notes' table does not hold that a note is at one level:{Environment.NewLine}{definition}");
    }

    /// <summary>
    /// <c>campaigns</c> is changed where it stands and never rebuilt: the script copies, drops and renames no campaign, and
    /// SQLite's sequence for campaigns — the promise never to give an id again — is what it was.
    /// </summary>
    [Fact]
    public void The_campaigns_are_changed_where_they_stand_and_never_rebuilt()
    {
        using var dir = Seeded();
        var script = Script(dir);
        foreach (var rebuild in new[] { "ef_temp_campaigns", "DROP TABLE \"campaigns\"", "RENAME TO \"campaigns\"" })
        {
            Assert.True(!script.Contains(rebuild, StringComparison.Ordinal), $"The script rebuilds campaigns ('{rebuild}'):{Environment.NewLine}{script}");
        }

        // Campaigns made and deleted since, as far as the sequence can tell.
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, "UPDATE sqlite_sequence SET seq = 10 WHERE name = 'campaigns'");
        }

        new DatabaseMigrator(dir.Paths).Migrate();

        using var after = Open(dir.Paths.DatabaseFile);
        Assert.Equal(["10"], Rows(after, "SELECT seq FROM sqlite_sequence WHERE name = 'campaigns'"));
    }

    /// <summary>
    /// A runtime started on the populated installation migrates it on its way up, after its backup, and then answers from
    /// every table the rebuild made again or that refers to one: the campaigns, their work, attempts, approvals, questions,
    /// notes, reports and chronicle, each with every row it had. The routes are rows like the rest; what the runtime answers
    /// about them is its routing snapshot, which names plugins this installation does not have.
    /// </summary>
    [Fact]
    public async Task A_runtime_started_on_an_upgraded_installation_answers_from_every_table_the_rebuild_touched()
    {
        State? before = null;
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            SeedInto(paths);
            before = Snapshot(paths.DatabaseFile);
        });

        Assert.Single(Directory.GetFiles(api.Paths.BackupsDirectory, $"jason-*-before-*_{ThisMigration}.db"));
        using (var connection = Open(api.Paths.DatabaseFile))
        {
            foreach (var (table, rows) in before!.Rows)
            {
                Assert.Equal(rows, Dump(connection, table, before.Columns[table]));
            }
        }

        var live = await OkAsync(api, Operations.CampaignGet, new { campaign_id = "cmp_live" });
        Assert.Equal("active", (string?)live["status"]);
        Assert.Equal("founders", (string?)live["context"]?["icp"]);
        Assert.Equal("seq-1", (string?)live["external_ids"]?[0]?["value"]);
        Assert.Equal("archived", (string?)(await OkAsync(api, Operations.CampaignGet, new { campaign_id = "cmp_gone" }))["status"]);
        Assert.Equal("succeeded", (string?)(await OkAsync(api, Operations.WorkItemGet, new { work_item_id = "wi_done" }))["status"]);
        Assert.Equal("awaiting_approval", (string?)(await OkAsync(api, Operations.WorkItemGet, new { work_item_id = "wi_send" }))["status"]);
        Assert.Equal("pending", (string?)(await OkAsync(api, Operations.ApprovalGet, new { approval_id = "apr_send" }))["status"]);
        Assert.Equal("Which segment first?", (string?)(await OkAsync(api, Operations.DecisionGet, new { decision_id = "dec_ask" }))["question"]);
        Assert.Equal("cmp_live", (string?)(await OkAsync(api, Operations.ReportGet, new { report_id = "rpt_hand" }))["correlation"]?["campaign_id"]);
        Assert.Equal(
            new JsonArray("site").ToJsonString(),
            (await OkAsync(api, Operations.RoleNoteGet, new { campaign_id = "cmp_live", role = "researcher" }))["note"]?["seen"]?.ToJsonString());
        Assert.Equal(
            ["jrn_1", "jrn_2"],
            (await OkAsync(api, Operations.JournalList, new { campaign_id = "cmp_live" }))["items"]!.AsArray().Select(line => (string?)line?["id"]).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The backup is taken before the migration starts — a copy of the installation as it stood, which the rebuild had not
    /// touched yet: no projects, no project column, every campaign and every route.
    /// </summary>
    [Fact]
    public void The_backup_is_the_installation_as_it_stood_before_the_rebuild()
    {
        using var dir = Seeded();
        var before = Snapshot(dir.Paths.DatabaseFile);

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.NotNull(report.BackupFile);
        Assert.EndsWith($"-before-{Applied(dir, ThisMigration)}.db", report.BackupFile, StringComparison.Ordinal);
        using var backup = Open(report.BackupFile);
        Assert.Empty(Rows(backup, "SELECT name FROM sqlite_master WHERE name IN ('projects', 'preferences', 'ef_temp_campaigns', 'ef_temp_role_notes')"));
        Assert.Empty(Rows(backup, "SELECT name FROM pragma_table_info('campaigns') WHERE name IN ('project_id', 'folder_path')"));
        foreach (var (table, rows) in before.Rows)
        {
            Assert.Equal(rows, Dump(backup, table, before.Columns[table]));
        }
    }

    /// <summary>
    /// The guards that live on the tables beside the rebuilt ones still refuse after it, tried rather than listed: a
    /// journal line cannot be changed or deleted, and a finished work item's result cannot be rewritten.
    /// </summary>
    [Fact]
    public void After_the_rebuild_the_journal_and_a_finished_result_still_refuse_to_change()
    {
        using var dir = Seeded();
        new DatabaseMigrator(dir.Paths).Migrate();
        using var connection = Open(dir.Paths.DatabaseFile);

        var rewritten = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE journal SET reason = 'rewritten' WHERE public_id = 'jrn_1'"));
        Assert.Contains("append-only", rewritten.Message, StringComparison.Ordinal);
        var deleted = Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM journal WHERE public_id = 'jrn_4'"));
        Assert.Contains("append-only", deleted.Message, StringComparison.Ordinal);
        var frozen = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE work_items SET result_json = '{\"found\":false}' WHERE public_id = 'wi_done'"));
        Assert.Contains("frozen", frozen.Message, StringComparison.Ordinal);

        Assert.Equal(
            ["jrn_1|", "jrn_2|", "jrn_3|", "jrn_4|"],
            Rows(connection, "SELECT public_id, coalesce(reason, '') FROM journal ORDER BY id"));
        Assert.Equal("{\"found\":true}", Assert.Single(Rows(connection, "SELECT result_json FROM work_items WHERE public_id = 'wi_done'")));
    }

    /// <summary>
    /// After the migration the database itself holds what the model says: a campaign needs a project that exists, and one
    /// written or changed without a project is refused by the triggers that say what NOT NULL would; a second default is
    /// refused; and a project a campaign is in cannot be deleted.
    /// </summary>
    [Fact]
    public void The_migrated_tables_refuse_what_the_model_refuses()
    {
        using var dir = Seeded();
        new DatabaseMigrator(dir.Paths).Migrate();
        using var connection = Open(dir.Paths.DatabaseFile);
        Execute(connection, "PRAGMA foreign_keys = 1");

        const string Columns = "(public_id, name, status, context_json, created_at, updated_at, project_id)";
        var unknown = Assert.Throws<SqliteException>(() => Execute(connection, $"INSERT INTO campaigns {Columns} VALUES ('cmp_x', 'x', 'draft', '{{}}', '2026-10-01', '2026-10-01', 99)"));
        Assert.Contains("FOREIGN KEY", unknown.Message, StringComparison.Ordinal);
        var none = Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at) VALUES ('cmp_y', 'y', 'draft', '{}', '2026-10-01', '2026-10-01')"));
        Assert.Contains("campaigns.project_id may not be null", none.Message, StringComparison.Ordinal);
        var named = Assert.Throws<SqliteException>(() => Execute(connection, $"INSERT INTO campaigns {Columns} VALUES ('cmp_n', 'n', 'draft', '{{}}', '2026-10-01', '2026-10-01', NULL)"));
        Assert.Contains("campaigns.project_id may not be null", named.Message, StringComparison.Ordinal);
        var taken = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE campaigns SET project_id = NULL WHERE public_id = 'cmp_live'"));
        Assert.Contains("campaigns.project_id may not be null", taken.Message, StringComparison.Ordinal);
        Assert.Equal(["0"], Rows(connection, "SELECT count(*) FROM campaigns WHERE project_id IS NULL OR public_id IN ('cmp_y', 'cmp_n')"));
        var second = Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO projects (public_id, name, slug, is_default, created_at, updated_at) VALUES ('prj_x', 'Other', 'other', 1, '2026-10-01', '2026-10-01')"));
        Assert.Contains("UNIQUE", second.Message, StringComparison.Ordinal);
        Execute(connection, "INSERT INTO projects (public_id, name, slug, is_default, created_at, updated_at) VALUES ('prj_y', 'Other', 'other', 0, '2026-10-01', '2026-10-01')");
        var slug = Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO projects (public_id, name, slug, is_default, created_at, updated_at) VALUES ('prj_z', 'Other again', 'other', 0, '2026-10-01', '2026-10-01')"));
        Assert.Contains("UNIQUE", slug.Message, StringComparison.Ordinal);
        var removed = Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM projects WHERE is_default = 1"));
        Assert.Contains("FOREIGN KEY", removed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A role's note is at one of three levels — the client's (no campaign, no project), a project's, or a campaign's — and
    /// there is one per role at each: a second for the same role and the same place is refused, while the same role may
    /// keep one at every level. A note that names both a campaign and a project is refused, because a campaign's project is
    /// the campaign's and is never stored twice; a note names a project that exists, and a project a note names cannot be
    /// deleted.
    /// </summary>
    [Fact]
    public void A_role_note_is_at_one_level_and_there_is_one_per_role_there()
    {
        using var dir = Seeded();
        new DatabaseMigrator(dir.Paths).Migrate();
        using var connection = Open(dir.Paths.DatabaseFile);
        Execute(connection, "PRAGMA foreign_keys = 1");
        Execute(connection, "INSERT INTO projects (public_id, name, slug, is_default, created_at, updated_at) VALUES ('prj_y', 'Other', 'other', 0, '2026-10-01', '2026-10-01')");
        void Note(string campaign, string project, string role) =>
            Execute(connection, $"INSERT INTO role_notes (campaign_id, project_id, role, note_json, note_hash, note_bytes, updated_by_type, created_at, updated_at) VALUES ({campaign}, {project}, '{role}', '{{}}', 'sha256:n', 2, 'role', '2026-10-01', '2026-10-01')");
        void Refused(string campaign, string project, string role, string because)
        {
            var refused = Assert.Throws<SqliteException>(() => Note(campaign, project, role));
            Assert.True(refused.Message.Contains(because, StringComparison.Ordinal), $"A note ({campaign}, {project}, {role}) was refused for another reason: {refused.Message}");
        }

        Refused("1", "1", "writer", "CHECK constraint failed: ck_role_notes_one_level");

        Note("NULL", "NULL", "researcher");
        Refused("NULL", "NULL", "researcher", "UNIQUE");

        Note("NULL", "1", "researcher");
        Refused("NULL", "1", "researcher", "UNIQUE");
        Note("NULL", "2", "researcher");
        Note("NULL", "1", "manager");
        Refused("NULL", "99", "manager", "FOREIGN KEY");

        Refused("1", "NULL", "researcher", "UNIQUE");
        Note("1", "NULL", "manager");

        Assert.Equal(
            ["-|-|researcher", "-|1|manager", "-|1|researcher", "-|2|researcher", "1|-|manager", "1|-|researcher", "2|-|manager"],
            Rows(connection, "SELECT coalesce(campaign_id, '-'), coalesce(project_id, '-'), role FROM role_notes ORDER BY coalesce(campaign_id, 0), coalesce(project_id, 0), role"));
        var named = Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM projects WHERE public_id = 'prj_y'"));
        Assert.Contains("FOREIGN KEY", named.Message, StringComparison.Ordinal);
        var both = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE role_notes SET project_id = 1 WHERE campaign_id = 1 AND role = 'manager'"));
        Assert.Contains("CHECK constraint failed: ck_role_notes_one_level", both.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A new installation gets its default project from the same migration, under an id of its own: the id is made when
    /// the migration runs, never written into it, so two installations never share one.
    /// </summary>
    [Fact]
    public void Every_installation_gets_a_default_project_of_its_own()
    {
        using var one = new TempDataDir();
        using var two = new TempDataDir();
        new DatabaseMigrator(one.Paths).Migrate();
        new DatabaseMigrator(two.Paths).Migrate();

        using var first = Open(one.Paths.DatabaseFile);
        using var second = Open(two.Paths.DatabaseFile);
        var a = Assert.Single(Rows(first, "SELECT public_id || '|' || name || '|' || slug || '|' || is_default FROM projects"));
        var b = Assert.Single(Rows(second, "SELECT public_id || '|' || name || '|' || slug || '|' || is_default FROM projects"));
        Assert.Matches("^prj_[0-9A-HJKMNP-TV-Z]{26}\\|Default\\|default\\|1$", a);
        Assert.Matches("^prj_[0-9A-HJKMNP-TV-Z]{26}\\|Default\\|default\\|1$", b);
        Assert.NotEqual(a, b);

        // Its moments are the database's own spelling of the moment the migration ran, which the runtime reads back.
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(one.Paths.DatabaseFile));
        var stored = db.Projects.AsNoTracking().Single();
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);

        // The first row of a table made by the same migration: the key tests seed campaigns under.
        Assert.Equal(DefaultProject.Id, stored.Id);
    }

    /// <summary>
    /// The backfill passes over the campaigns once and reads the default's id once, through the index that holds there is
    /// one: a scalar subquery that names no column of the row being updated is computed once, never for every row. It is
    /// planned on the database as it stands when the statement runs — the script EF Core runs, up to it — so the plan is the
    /// plan of this very text there; and it runs after the default exists and the column is there, before the triggers that
    /// refuse a campaign without a project.
    /// </summary>
    [Fact]
    public void The_backfill_passes_over_the_campaigns_once_and_reads_the_default_once()
    {
        using var dir = Seeded();
        var script = Script(dir);

        var at = script.IndexOf(ProjectsAndPreferences.Backfill, StringComparison.Ordinal);
        Assert.True(
            at >= 0 && script.IndexOf(ProjectsAndPreferences.Backfill, at + 1, StringComparison.Ordinal) < 0,
            $"The script does not run the backfill exactly once:{Environment.NewLine}{script}");
        foreach (var earlier in new[] { "INSERT INTO \"projects\"", "ALTER TABLE \"campaigns\" ADD \"project_id\"" })
        {
            var where = script.IndexOf(earlier, StringComparison.Ordinal);
            Assert.True(where >= 0 && where < at, $"'{earlier}' is at {where}, not before the backfill at {at}:{Environment.NewLine}{script}");
        }

        var held = script.IndexOf("CREATE TRIGGER campaigns_project_required_", StringComparison.Ordinal);
        Assert.True(held > at, $"The triggers are made at {held}, not after the backfill at {at}:{Environment.NewLine}{script}");

        using var connection = Open(dir.Paths.DatabaseFile);
        Execute(connection, script[..at]);
        var plan = PlanOf(connection, ProjectsAndPreferences.Backfill);
        var described = string.Join(Environment.NewLine, plan);

        Assert.True(
            plan.Where(step => Reads(step, "campaigns")).SequenceEqual(["SCAN campaigns"]),
            $"The backfill does not pass over the campaigns exactly once, by themselves:{Environment.NewLine}{described}");
        Assert.True(
            plan.Where(step => Reads(step, "projects")).SequenceEqual(["SEARCH projects USING COVERING INDEX ix_projects_one_default (is_default=?)"]),
            $"The backfill does not read the default through the index that holds there is one:{Environment.NewLine}{described}");
        Assert.True(
            !plan.Any(step => step.Contains("CORRELATED", StringComparison.Ordinal) || step.Contains("AUTOMATIC", StringComparison.Ordinal)),
            $"The backfill reads something once for every campaign, or builds an index for itself:{Environment.NewLine}{described}");
    }

    /// <summary>
    /// A start stopped after the migration's first commit — the default project made, every campaign put in it, the role
    /// notes copied into their new table and the old one about to be dropped — puts back the copy it took before it began,
    /// and says so. The next start migrates that file as though nothing had happened, and only then is the copy let go.
    /// </summary>
    [Fact]
    public void A_start_interrupted_after_the_first_commit_is_put_back_and_the_next_start_migrates_cleanly()
    {
        using var dir = Seeded();
        var before = Snapshot(dir.Paths.DatabaseFile);
        var interruption = new Interruption("DROP TABLE \"role_notes\"");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths) { Interceptors = [interruption] }.Migrate());

        Assert.True(
            interruption is { Commits: >= 1, ProjectsWhenInterrupted: 1 },
            $"The migration was interrupted after {interruption.Commits} commits, with {interruption.ProjectsWhenInterrupted} projects written.");
        Assert.Equal(Applied(dir, ThisMigration), failed.Migration);
        AssertAsItStood(dir, dir.Paths.DatabaseFile, before);
        AssertAsItStood(dir, failed.Backup, before);

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Equal(AppliedFrom(dir, ThisMigration), report.NewlyApplied);
        using var after = Open(dir.Paths.DatabaseFile);
        foreach (var (table, rows) in before.Rows)
        {
            Assert.Equal(rows, Dump(after, table, before.Columns[table]));
        }

        var orphans = Rows(after, "PRAGMA foreign_key_check");
        Assert.True(orphans.Count == 0, $"Rows refer to rows that are not there: {string.Join("; ", orphans)}");
        Assert.Equal(["cmp_draft|1", "cmp_gone|1", "cmp_live|1"], Rows(after, "SELECT c.public_id, p.is_default FROM campaigns c JOIN projects p ON p.id = c.project_id ORDER BY c.public_id"));
        var kept = Directory.GetFiles(dir.Paths.BackupsDirectory).Select(Path.GetFileName).ToList();
        Assert.True(kept.SequenceEqual([Path.GetFileName(report.BackupFile)]), $"The backups kept are {string.Join(", ", kept)}, not this start's alone.");
    }

    /// <summary>
    /// A start stopped part way by something no code survives — a power cut, a process ended — leaves the database part
    /// migrated, and nothing could put it back then. The next start fails on it, finds the record the stopped start wrote,
    /// and, the chronicle standing where it stood when that start took its copy, puts that copy back: the installation as
    /// it stood before the upgrade. Both copies are kept, the record is gone, and the start after migrates from there.
    /// </summary>
    [Fact]
    [Critical(Family.P2, "a stopped start is put back")]
    public void A_start_that_fails_on_what_a_stopped_start_left_puts_back_the_copy_that_start_took()
    {
        using var dir = Seeded();
        var (before, clean) = StoppedPartWay(dir);

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.True(failed.StoppedStart == StoppedStart.PutBack && failed.Restored, $"The stopped start's copy was not put back ({failed.StoppedStart}): {failed.Message}");
        Assert.Equal(clean, failed.Backup);
        foreach (var said in new[] { "did not start", "stopped while it migrated", Applied(dir, ThisMigration), "already exists", clean, "nothing has been recorded since", "Start the runtime again" })
        {
            Assert.True(failed.Message.Contains(said, StringComparison.Ordinal), $"The refusal does not say '{said}': {failed.Message}");
        }

        AssertAsItStood(dir, dir.Paths.DatabaseFile, before);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dir.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            Assert.Equal(["0"], Rows(connection, "SELECT count(*) FROM \"__EFMigrationsLock\""));
        }

        var kept = Directory.GetFiles(dir.Paths.BackupsDirectory).Order(StringComparer.Ordinal).ToList();
        Assert.True(kept.Count == 2 && kept.Contains(clean), $"The backups kept are {string.Join(", ", kept)}, not the stopped start's and this start's.");
        AssertAsItStood(dir, clean, before);
        Assert.False(File.Exists(MarkerOf(dir)), "The record of the stopped start is still there after its copy was put back.");

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Equal(AppliedFrom(dir, ThisMigration), report.NewlyApplied);
    }

    /// <summary>
    /// A line recorded after the stopped start took its copy is work that copy does not hold: the start does not put it
    /// back. The database is as this start found it, the record stays, and the refusal names the copy and how to put it
    /// back by hand, for a person who decides that losing the line is what they want.
    /// </summary>
    [Fact]
    [Critical(Family.P2, "a moved chronicle is never put back over")]
    public void A_stopped_start_is_not_put_back_once_the_chronicle_has_moved()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, "INSERT INTO journal (public_id, ts, actor_type, kind) VALUES ('jrn_after', '2026-10-21 01:00:00', 'human', 'suppression_added')");
        }

        AssertRefusedAndUntouched(dir, clean);
    }

    /// <summary>A line that cannot be read is not a line that stands still: the copy is not put back.</summary>
    [Fact]
    public void A_stopped_start_is_not_put_back_when_the_chronicle_cannot_be_read()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, "ALTER TABLE journal RENAME TO journal_elsewhere");
        }

        AssertRefusedAndUntouched(dir, clean);
    }

    /// <summary>
    /// Whatever reading the chronicle's line throws, the line is not known to stand still: the copy is not put back.
    /// </summary>
    [Fact]
    public void A_stopped_start_is_not_put_back_when_reading_the_chronicle_throws_anything()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);

        AssertRefusedAndUntouched(dir, clean, new LineUnreadable());
    }

    /// <summary>
    /// The copy the stopped start took is gone, so there is nothing from before the upgrade to put back: the database is as
    /// this start found it, the record stays, and the refusal says the copy is no longer there, that this runtime cannot put
    /// the installation back as it stood, and where the backups that are left are kept.
    /// </summary>
    [Fact]
    public void A_stopped_start_whose_copy_is_gone_is_not_put_back_and_says_where_the_backups_are()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);
        File.Delete(clean);
        var found = Everything(dir.Paths.DatabaseFile);
        var record = File.ReadAllText(MarkerOf(dir));

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.True(failed.StoppedStart == StoppedStart.BackupGone && failed.Restored, $"The stopped start's copy was {failed.StoppedStart}, restored {failed.Restored}: {failed.Message}");
        foreach (var said in new[] { "did not start", clean, "no longer there", "cannot put back the installation as it stood", dir.Paths.BackupsDirectory })
        {
            Assert.True(failed.Message.Contains(said, StringComparison.Ordinal), $"The refusal does not say '{said}': {failed.Message}");
        }

        var now = Everything(dir.Paths.DatabaseFile);
        Assert.True(
            now.SequenceEqual(found),
            $"The database is not as this start found it:{Environment.NewLine}lost: {string.Join("; ", found.Except(now))}{Environment.NewLine}gained: {string.Join("; ", now.Except(found))}");
        Assert.Equal(WithoutMoment(record), WithoutMoment(File.ReadAllText(MarkerOf(dir))));
    }

    /// <summary>
    /// A record whose copy is not in this installation's backups is not one this runtime wrote: it reads as unreadable, is
    /// left exactly as it is, and the file it names is never copied over the database.
    /// </summary>
    [Fact]
    public void A_record_naming_a_copy_outside_the_backups_is_never_put_back()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);
        var elsewhere = Path.Combine(dir.Paths.Root, "elsewhere.db");
        File.Copy(clean, elsewhere);
        var record = JsonNode.Parse(File.ReadAllText(MarkerOf(dir)))!.AsObject();
        record["clean_backup"] = elsewhere;
        File.WriteAllText(MarkerOf(dir), record.ToJsonString());
        var written = File.ReadAllText(MarkerOf(dir));
        var found = Everything(dir.Paths.DatabaseFile);

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.True(failed.StoppedStart == StoppedStart.None && failed.Backup != elsewhere, $"The copy outside the backups was {failed.StoppedStart}, put back from {failed.Backup}: {failed.Message}");
        Assert.True(Everything(dir.Paths.DatabaseFile).SequenceEqual(found), "The database is not as this start found it.");
        Assert.Equal(written, File.ReadAllText(MarkerOf(dir)));
    }

    /// <summary>
    /// A second start stopped while migrating must not take the place of the first one's copy, the only copy of the
    /// installation as it stood: the record it writes before it begins carries the first copy and the first line forward.
    /// </summary>
    [Fact]
    public void A_second_stopped_start_carries_the_first_copy_and_line_forward()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir, highWaterShift: 1000, lastPending: "20991231000000_FromALaterVersion");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.Equal(StoppedStart.ChronicleMoved, failed.StoppedStart);
        var record = JsonNode.Parse(File.ReadAllText(MarkerOf(dir)))!;
        Assert.Equal(clean, record["clean_backup"]!.GetValue<string>());
        Assert.Equal(HighWater(dir.Paths.DatabaseFile) + 1000, record["journal_high_water"]!.GetValue<long>());
        Assert.Equal("20991231000000_FromALaterVersion", record["last_pending"]!.GetValue<string>());
    }

    /// <summary>
    /// A start that applies two migrations and is stopped inside the second leaves the first in the history: the record it
    /// wrote names a migration that is applied, and the database is part migrated all the same. The next start fails on the
    /// second and puts back the copy taken before the first, not its own copy of what the stopped start left.
    /// </summary>
    [Fact]
    public void A_start_stopped_inside_its_second_migration_is_put_back_from_before_its_first()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(BeforePreviousMigration);
        }

        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, "INSERT INTO journal (public_id, ts, actor_type, kind) VALUES ('jrn_1', '2026-09-01 00:00:00', 'human', 'suppression_added')");
        }

        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        var clean = Path.Combine(dir.Paths.BackupsDirectory, $"jason-20261021T000000Z-before-{PreviousMigration}.db");
        using (var source = Open(dir.Paths.DatabaseFile))
        using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = clean, Pooling = false }.ToString()))
        {
            copy.Open();
            source.BackupDatabase(copy);
        }

        File.WriteAllText(
            MarkerOf(dir),
            new JsonObject
            {
                ["clean_backup"] = clean,
                ["first_pending"] = PreviousMigration,
                ["last_pending"] = LastOf(dir),
                ["journal_high_water"] = HighWater(dir.Paths.DatabaseFile),
                ["written_at"] = "2026-10-21T00:00:00Z",
            }.ToJsonString());

        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        var script = Script(dir);
        var commit = script.IndexOf("COMMIT;", StringComparison.Ordinal) + "COMMIT;".Length;
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, script[..commit]);
            Execute(connection, "INSERT INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") VALUES (1, '2026-10-21 00:00:00+00:00')");
        }

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.True(failed.StoppedStart == StoppedStart.PutBack && failed.Backup == clean, $"The copy from before the first migration was not put back ({failed.StoppedStart}, {failed.Backup}): {failed.Message}");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dir.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            Assert.Equal(["0"], Rows(connection, $"SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" IN ('{PreviousMigration}', '{Applied(dir, ThisMigration)}')"));
            Assert.Empty(Rows(connection, "SELECT name FROM sqlite_master WHERE name = 'projects'"));
        }

        Assert.False(File.Exists(MarkerOf(dir)), "The record is still there after its copy was put back.");
        var report = new DatabaseMigrator(dir.Paths).Migrate();
        Assert.Contains(PreviousMigration, report.NewlyApplied);
    }

    /// <summary>
    /// The log of a start whose migration failed on what a stopped start left names the migration and which copy was put
    /// back, and carries none of the failure's own words; the refusal is said where the start was asked for.
    /// </summary>
    [Fact]
    public async Task The_log_of_a_put_back_stopped_start_names_the_migration_and_never_the_cause()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);
        using var refusals = new StringWriter();

        // Bounded, because a runtime that migrated after all would start and run until it was stopped.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        var exit = await Jason.Runtime.Hosting.RuntimeHost.RunAsync(dir.Paths, TestRuntimeOptions.Quiet with { Refusals = refusals }, bound.Token);

        var said = refusals.ToString().Trim();
        Assert.True(exit == 1, $"The start answered {exit}: {said}");
        Assert.StartsWith("The runtime did not start:", said, StringComparison.Ordinal);
        Assert.Contains(clean, said, StringComparison.Ordinal);

        var logs = string.Concat(Directory.GetFiles(dir.Paths.LogsDirectory).Select(File.ReadAllText));
        Assert.True(logs.Contains($"\"Migration\":\"{Applied(dir, ThisMigration)}\"", StringComparison.Ordinal), $"The log does not name the migration:{Environment.NewLine}{logs}");
        Assert.True(logs.Contains("A start stopped while migrating was found; its backup {Backup} was put back", StringComparison.Ordinal), $"The log does not say the stopped start's copy was put back:{Environment.NewLine}{logs}");
        Assert.False(logs.Contains("already exists", StringComparison.Ordinal), $"The log carries the failure's own words:{Environment.NewLine}{logs}");
    }

    /// <summary>
    /// The put-back restores the database alone. An update's record is left exactly as it was, so a rollback after it still
    /// finds its record whole, and the chronicle line the record holds is the one the put-back database stands at: the two
    /// restore to the same line.
    /// </summary>
    [Fact]
    public void A_stopped_start_put_back_leaves_an_update_record_as_it_was_at_the_same_line()
    {
        using var dir = Seeded();
        var (_, clean) = StoppedPartWay(dir);
        var ledger = new Jason.Cli.Update.UpdatePaths(dir.Paths).Ledger;
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        new Jason.Contracts.Update.UpdateLedger(
            Jason.Contracts.Update.SemanticVersion.Parse("0.18.0"),
            Jason.Contracts.Update.SemanticVersion.Parse("0.19.0"),
            Jason.Contracts.Update.UpdateStep.Complete,
            new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero),
            Path.Combine(dir.Paths.Root, "bin", "jason"),
            Path.Combine(dir.Paths.Root, "update", "staged", "jason"),
            Path.Combine(dir.Paths.Root, "update", "previous", "jason"),
            BackupFile: clean,
            NewlyApplied: [Applied(dir, ThisMigration)],
            ChronicleId: NewestEntry(clean)).Write(ledger);
        var written = File.ReadAllBytes(ledger);

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.Equal(StoppedStart.PutBack, failed.StoppedStart);
        Assert.Equal(written, File.ReadAllBytes(ledger));
        Assert.Equal(Jason.Contracts.Update.UpdateLedger.ReadFile(ledger)!.ChronicleId, NewestEntry(dir.Paths.DatabaseFile));
    }

    /// <summary>
    /// What a start stopped after the migration's first commit leaves, by hand: the copy it took and the record it wrote
    /// before it began, then the migration run to its first commit with EF Core's lock still taken. The record's line is
    /// shifted by the number given, for a row that must tell the line written from the line read.
    /// </summary>
    private static (State Before, string Clean) StoppedPartWay(TempDataDir dir, long highWaterShift = 0, string? lastPending = null)
    {
        var before = Snapshot(dir.Paths.DatabaseFile);
        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        var clean = Path.Combine(dir.Paths.BackupsDirectory, $"jason-20261021T000000Z-before-{Applied(dir, ThisMigration)}.db");
        using (var source = Open(dir.Paths.DatabaseFile))
        using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = clean, Pooling = false }.ToString()))
        {
            copy.Open();
            source.BackupDatabase(copy);
        }

        File.WriteAllText(
            MarkerOf(dir),
            new JsonObject
            {
                ["clean_backup"] = clean,
                ["first_pending"] = Applied(dir, ThisMigration),
                ["last_pending"] = lastPending ?? LastOf(dir),
                ["journal_high_water"] = HighWater(dir.Paths.DatabaseFile) + highWaterShift,
                ["written_at"] = "2026-10-21T00:00:00Z",
            }.ToJsonString());

        var script = Script(dir);
        var commit = script.IndexOf("COMMIT;", StringComparison.Ordinal) + "COMMIT;".Length;
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, script[..commit]);
            Execute(connection, "INSERT INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") VALUES (1, '2026-10-21 00:00:00+00:00')");
        }

        return (before, clean);
    }

    /// <summary>Refused: the stopped start's copy not put back, the database exactly as this start found it, the record kept, the copy named.</summary>
    private static void AssertRefusedAndUntouched(TempDataDir dir, string clean, params IInterceptor[] interceptors)
    {
        var found = Everything(dir.Paths.DatabaseFile);
        var record = File.ReadAllText(MarkerOf(dir));

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths) { Interceptors = interceptors }.Migrate());

        Assert.True(failed.StoppedStart == StoppedStart.ChronicleMoved, $"The stopped start's copy was {failed.StoppedStart}: {failed.Message}");
        foreach (var said in new[] { "did not start", "stopped while it migrated", clean, "Nothing was put back", "chronicle has moved", dir.Paths.DatabaseFile + "-wal" })
        {
            Assert.True(failed.Message.Contains(said, StringComparison.Ordinal), $"The refusal does not say '{said}': {failed.Message}");
        }

        var now = Everything(dir.Paths.DatabaseFile);
        Assert.True(
            now.SequenceEqual(found),
            $"The database is not as this start found it:{Environment.NewLine}lost: {string.Join("; ", found.Except(now))}{Environment.NewLine}gained: {string.Join("; ", now.Except(found))}");
        Assert.True(File.Exists(clean), "The stopped start's copy is gone.");
        Assert.Equal(WithoutMoment(record), WithoutMoment(File.ReadAllText(MarkerOf(dir))));
    }

    /// <summary>Every table's every row and the shape, of a database in any state, a migration's own tables included.</summary>
    private static List<string> Everything(string file)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        var everything = Rows(connection, "SELECT type || ' ' || name || ' | ' || coalesce(sql, '') FROM sqlite_master ORDER BY type, name");
        foreach (var table in Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND substr(name, 1, 7) <> 'sqlite_' ORDER BY name"))
        {
            everything.AddRange(Dump(connection, table, Rows(connection, $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid")));
        }

        return everything;
    }

    /// <summary>A record with its moment taken out, since a start writes its own moment and carries the rest.</summary>
    private static string WithoutMoment(string record)
    {
        var node = JsonNode.Parse(record)!.AsObject();
        node.Remove("written_at");
        return node.ToJsonString();
    }

    private static string MarkerOf(TempDataDir dir) => new DatabaseMigrator(dir.Paths).MarkerFile;

    /// <summary>The last migration this build has.</summary>
    private static string LastOf(TempDataDir dir)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return db.Database.GetMigrations().Last();
    }

    /// <summary>Throws something other than SQLite's own failure where the chronicle's line is read.</summary>
    private sealed class LineUnreadable : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) =>
            command.CommandText.Contains("max(id)", StringComparison.Ordinal) && command.CommandText.Contains("journal", StringComparison.Ordinal)
                ? throw new InvalidOperationException("The line could not be read.")
                : result;
    }

    private static long HighWater(string file)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        return long.Parse(Assert.Single(Rows(connection, DatabaseMigrator.JournalHighWaterQuery)), CultureInfo.InvariantCulture);
    }

    /// <summary>The newest chronicle entry's public id, the line an update's record holds.</summary>
    private static string NewestEntry(string file)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        return Assert.Single(Rows(connection, "SELECT public_id FROM journal ORDER BY id DESC LIMIT 1"));
    }

    /// <summary>
    /// The migration can be taken back on an installation that has campaigns: their reference to a project goes first,
    /// where they stand, so the projects can go after it, and a role's note at the client level or at a project's, which
    /// the schema before has no place for, goes before the projects do. Taken back, the installation is as it stood —
    /// every row, index, trigger, column and reference — and it migrates again.
    /// </summary>
    [Fact]
    public void Taken_back_on_an_installation_with_campaigns_it_is_as_it_stood_and_migrates_again()
    {
        using var dir = Seeded();
        var before = Snapshot(dir.Paths.DatabaseFile);
        new DatabaseMigrator(dir.Paths).Migrate();
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, "INSERT INTO role_notes (campaign_id, project_id, role, note_json, note_hash, note_bytes, updated_by_type, created_at, updated_at) VALUES (NULL, NULL, 'researcher', '{}', 'sha256:c', 2, 'role', '2026-10-01', '2026-10-01')");
            Execute(connection, "INSERT INTO role_notes (campaign_id, project_id, role, note_json, note_hash, note_bytes, updated_by_type, created_at, updated_at) VALUES (NULL, 1, 'researcher', '{}', 'sha256:p', 2, 'role', '2026-10-01', '2026-10-01')");
        }

        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        AssertAsItStood(dir, dir.Paths.DatabaseFile, before);
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            var shape = ShapeOf(connection);
            Assert.True(
                shape.SequenceEqual(before.Shape),
                $"Taken back, the shape differs:{Environment.NewLine}lost: {string.Join("; ", before.Shape.Except(shape))}{Environment.NewLine}gained: {string.Join("; ", shape.Except(before.Shape))}");
            var orphans = Rows(connection, "PRAGMA foreign_key_check");
            Assert.True(orphans.Count == 0, $"Rows refer to rows that are not there: {string.Join("; ", orphans)}");
        }

        var again = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Equal(AppliedFrom(dir, ThisMigration), again.NewlyApplied);
        using var after = Open(dir.Paths.DatabaseFile);
        Assert.Equal(["cmp_draft|1", "cmp_gone|1", "cmp_live|1"], Rows(after, "SELECT c.public_id, p.is_default FROM campaigns c JOIN projects p ON p.id = c.project_id ORDER BY c.public_id"));
    }

    /// <summary>A database exactly as the installation stood before the migration: every row, no table it makes, no row of it in the history.</summary>
    private static void AssertAsItStood(TempDataDir dir, string file, State before)
    {
        // Not pooled, so nothing holds a backup open once it has been read: a later start may let go of it.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        foreach (var (table, rows) in before.Rows)
        {
            Assert.Equal(rows, Dump(connection, table, before.Columns[table]));
        }

        Assert.Empty(Rows(connection, "SELECT name FROM sqlite_master WHERE name IN ('projects', 'preferences', 'ef_temp_campaigns', 'ef_temp_role_notes')"));
        Assert.Equal(before.Columns["campaigns"], Rows(connection, "SELECT name FROM pragma_table_info('campaigns') ORDER BY cid"));
        Assert.Equal(before.Triggers, Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name"));
        Assert.Equal(
            ["0"],
            Rows(connection, $"SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{Applied(dir, ThisMigration)}'"));
    }

    /// <summary>The script EF Core runs for this migration, from the one before.</summary>
    private static string Script(TempDataDir dir)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return db.GetInfrastructure().GetRequiredService<IMigrator>().GenerateScript(PreviousMigration, Applied(dir, ThisMigration));
    }

    /// <summary>
    /// Stops a migration where a command starts whose text holds the words given, as a process ended there would stop it,
    /// having counted the commits before it and the projects the database held then.
    /// </summary>
    private sealed class Interruption(string words) : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public int Commits { get; private set; }

        public int? ProjectsWhenInterrupted { get; private set; }

        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Commits++;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (command.CommandText.Contains(words, StringComparison.Ordinal))
            {
                using var reading = command.Connection!.CreateCommand();
                reading.Transaction = command.Transaction;
                reading.CommandText = "SELECT count(*) FROM projects";
                ProjectsWhenInterrupted = Convert.ToInt32(reading.ExecuteScalar(), CultureInfo.InvariantCulture);
                throw new InvalidOperationException("Interrupted where the test stops the migration.");
            }

            return result;
        }
    }

    /// <summary>An answer that must be a success, read as JSON; any other is reported whole.</summary>
    private static async Task<JsonObject> OkAsync(RuntimeApiFixture api, string operation, object body)
    {
        var (status, text) = await api.PostAsync(operation, body, Ct);
        Assert.True(status == System.Net.HttpStatusCode.OK, $"{operation} answered {(int)status}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    /// <summary>What SQLite says it will do for a statement, one step a line.</summary>
    private static List<string> PlanOf(SqliteConnection connection, string statement) =>
        Rows(connection, "EXPLAIN QUERY PLAN " + statement).Select(row => row.Split('|')[3]).ToList();

    /// <summary>Whether a step of a plan reads the table named, rather than a subquery or a list it made.</summary>
    private static bool Reads(string step, string table) =>
        step.StartsWith($"SCAN {table}", StringComparison.Ordinal) || step.StartsWith($"SEARCH {table}", StringComparison.Ordinal);

    /// <summary>The populated installation, at the migration before this one.</summary>
    private static TempDataDir Seeded()
    {
        var dir = new TempDataDir();
        SeedInto(dir.Paths);
        return dir;
    }

    private static void SeedInto(JasonPaths paths)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        using var connection = Open(paths.DatabaseFile);
        Execute(connection, Seed);
    }

    /// <summary>What a database holds, read to be compared: every row of <see cref="Tables"/>, their columns, every trigger, and its shape.</summary>
    private sealed record State(
        Dictionary<string, List<string>> Rows,
        Dictionary<string, List<string>> Columns,
        List<string> Triggers,
        List<string> Shape);

    private static State Snapshot(string file)
    {
        using var connection = Open(file);
        var columns = Tables.ToDictionary(table => table, table => Rows(connection, $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid"), StringComparer.Ordinal);
        var rows = Tables.ToDictionary(table => table, table => Dump(connection, table, columns[table]), StringComparer.Ordinal);
        foreach (var (table, held) in rows)
        {
            Assert.True(held.Count > 0, $"The seed leaves '{table}' empty, so the rebuild could not be seen to keep its rows.");
        }

        return new State(rows, columns, Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name"), ShapeOf(connection));
    }

    /// <summary>
    /// A database's shape, a line a thing, in order: every index and trigger with its definition, every column of every
    /// table with its type, whether it may be null and its default, and every reference a table makes — each line begun by
    /// its key, the kind, the table and the name.
    /// </summary>
    private static List<string> ShapeOf(SqliteConnection connection)
    {
        var shape = Rows(connection, "SELECT type || ' ' || tbl_name || '.' || name || ' | ' || sql FROM sqlite_master WHERE type IN ('index', 'trigger') AND sql IS NOT NULL");
        foreach (var table in Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND substr(name, 1, 7) <> 'sqlite_' AND substr(name, 1, 4) <> '__EF'"))
        {
            shape.AddRange(Rows(connection, $"SELECT 'column {table}.' || name || ' | ' || type || ' notnull=' || \"notnull\" || ' default=' || coalesce(dflt_value, 'none') || ' pk=' || pk FROM pragma_table_info('{table}')"));
            shape.AddRange(Rows(connection, $"SELECT 'reference {table}.' || \"from\" || ' | ' || \"table\" || '(' || \"to\" || ') on delete ' || on_delete || ' on update ' || on_update FROM pragma_foreign_key_list('{table}')"));
        }

        return [.. shape.Order(StringComparer.Ordinal)];
    }

    /// <summary>The tables this migration changes; what a later migration adds elsewhere is not its gain.</summary>
    private static readonly string[] OwnTables = ["campaigns", "projects", "preferences", "role_notes"];

    /// <summary>The table a key of <see cref="ShapeOf"/> names: between its kind and its dot.</summary>
    private static string TableOf(string key) => key[(key.IndexOf(' ', StringComparison.Ordinal) + 1)..key.IndexOf('.', StringComparison.Ordinal)];

    /// <summary>The key a line of <see cref="ShapeOf"/> begins with: its kind, its table and its name.</summary>
    private static string KeyOf(string line) => line[..line.IndexOf(" | ", StringComparison.Ordinal)];

    /// <summary>Every row of a table, the columns named, in the order of its key; a value's type is written beside it so a number that became text shows.</summary>
    private static List<string> Dump(SqliteConnection connection, string table, IReadOnlyList<string> columns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(column => $"\"{column}\""))} FROM \"{table}\" ORDER BY rowid";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var row = new StringBuilder(table);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                row.Append('|').Append(columns[index]).Append('=')
                    .Append(value is DBNull ? "null" : $"{value.GetType().Name}:{Convert.ToString(value, CultureInfo.InvariantCulture)}");
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    /// <summary>The full name of the migration whose name ends with the one given, as the assembly lists it.</summary>
    private static string Applied(TempDataDir dir, string suffix)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return Assert.Single(db.Database.GetMigrations(), name => name.EndsWith("_" + suffix, StringComparison.Ordinal));
    }

    /// <summary>
    /// What a start from the migration before this one applies: this migration and every one the tree carries after it,
    /// so the assertion holds on a branch where later migrations follow.
    /// </summary>
    private static List<string> AppliedFrom(TempDataDir dir, string suffix)
    {
        var from = Applied(dir, suffix);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return db.Database.GetMigrations().SkipWhile(name => name != from).ToList();
    }

    private static SqliteConnection Open(string file)
    {
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(file));
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string statement)
    {
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }

    /// <summary>Every row, its columns joined by a bar.</summary>
    private static List<string> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
        }

        return rows;
    }
}
