using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Persistence;

public sealed class JasonDbContext(DbContextOptions<JasonDbContext> options) : DbContext(options)
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactChannel> ContactChannels => Set<ContactChannel>();

    public DbSet<CampaignContact> CampaignContacts => Set<CampaignContact>();

    public DbSet<Suppression> Suppressions => Set<Suppression>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<ExternalId> ExternalIds => Set<ExternalId>();

    public DbSet<CampaignRoute> CampaignRoutes => Set<CampaignRoute>();

    public DbSet<Approval> Approvals => Set<Approval>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<ExecutionProfile> ExecutionProfiles => Set<ExecutionProfile>();

    public DbSet<ExecutionProfileRevision> ExecutionProfileRevisions => Set<ExecutionProfileRevision>();

    public DbSet<JournalEntry> Journal => Set<JournalEntry>();

    /// <summary>
    /// The one connection string for a database file. Spelled once because the provider pools connections per
    /// connection string: anything that wants to release a file's pooled connections must name it exactly.
    /// </summary>
    public static string ConnectionString(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        return $"Data Source={databaseFile};Default Timeout=30";
    }

    /// <summary>One place for the SQLite options and interceptors; used by CreateOptions, the migrator and the DI registration.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string databaseFile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseSqlite(ConnectionString(databaseFile))
            .AddInterceptors(new AppendOnlyInterceptor());
    }

    /// <summary>One connection string for the whole runtime: WAL is set by the migrator, busy timeout here.</summary>
    public static DbContextOptions<JasonDbContext> CreateOptions(string databaseFile)
    {
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        Configure(builder, databaseFile);
        return builder.Options;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<CampaignStatus>().HaveConversion<SnakeCaseEnumConverter<CampaignStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<MembershipState>().HaveConversion<SnakeCaseEnumConverter<MembershipState>>().HaveMaxLength(32);
        configurationBuilder.Properties<ActorType>().HaveConversion<SnakeCaseEnumConverter<ActorType>>().HaveMaxLength(32);
        configurationBuilder.Properties<WorkItemKind>().HaveConversion<SnakeCaseEnumConverter<WorkItemKind>>().HaveMaxLength(32);
        configurationBuilder.Properties<WorkItemStatus>().HaveConversion<SnakeCaseEnumConverter<WorkItemStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<AttemptStatus>().HaveConversion<SnakeCaseEnumConverter<AttemptStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<ApprovalStatus>().HaveConversion<SnakeCaseEnumConverter<ApprovalStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<RouteScope>().HaveConversion<SnakeCaseEnumConverter<RouteScope>>().HaveMaxLength(32);
        configurationBuilder.Properties<AgentHostKind>().HaveConversion<SnakeCaseEnumConverter<AgentHostKind>>().HaveMaxLength(32);
        configurationBuilder.Properties<LineageState>().HaveConversion<SnakeCaseEnumConverter<LineageState>>().HaveMaxLength(32);
        configurationBuilder.Properties<JsonObject>().HaveConversion<JsonObjectConverter, JsonObjectComparer>();
        configurationBuilder.Properties<JsonNode>().HaveConversion<JsonNodeConverter, JsonNodeComparer>();
        configurationBuilder.Properties<AttemptErrorDto>().HaveConversion<JsonTextConverter<AttemptErrorDto>, JsonTextComparer<AttemptErrorDto>>();
        configurationBuilder.Properties<AttemptLaunchDto>().HaveConversion<JsonTextConverter<AttemptLaunchDto>, JsonTextComparer<AttemptLaunchDto>>();
        configurationBuilder.Properties<AttemptProvenanceDto>().HaveConversion<JsonTextConverter<AttemptProvenanceDto>, JsonTextComparer<AttemptProvenanceDto>>();
        configurationBuilder.Properties<List<string>>().HaveConversion<StringListConverter, StringListComparer>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Campaign>(campaign =>
        {
            campaign.HasKey(c => c.Id);
            campaign.Property(c => c.PublicId).HasMaxLength(40);
            campaign.HasIndex(c => c.PublicId).IsUnique();
            campaign.Property(c => c.Name).HasMaxLength(200);
            campaign.Property(c => c.ExecutionProfile).HasMaxLength(64);
            campaign.Property(c => c.Context).HasColumnName("context_json").IsRequired().HasDefaultValueSql("'{}'");
            campaign.ToTable(t => t.HasCheckConstraint("ck_campaigns_context_json", "json_valid(context_json)"));
            campaign.HasMany(c => c.Members).WithOne(m => m.Campaign).HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Contact>(contact =>
        {
            contact.HasKey(c => c.Id);
            contact.Property(c => c.PublicId).HasMaxLength(40);
            contact.HasIndex(c => c.PublicId).IsUnique();
            contact.Property(c => c.FirstName).HasMaxLength(200);
            contact.Property(c => c.LastName).HasMaxLength(200);
            contact.Property(c => c.Company).HasMaxLength(200);
            contact.Property(c => c.Title).HasMaxLength(200);
            contact.Property(c => c.TimeZone).HasMaxLength(100);
            contact.Property(c => c.Custom).HasColumnName("custom_json").IsRequired().HasDefaultValueSql("'{}'");
            contact.ToTable(t => t.HasCheckConstraint("ck_contacts_custom_json", "json_valid(custom_json)"));
            contact.HasMany(c => c.Channels).WithOne(ch => ch.Contact).HasForeignKey(ch => ch.ContactId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContactChannel>(channel =>
        {
            channel.HasKey(c => c.Id);
            channel.Property(c => c.Channel).HasMaxLength(32);
            channel.Property(c => c.Value).HasMaxLength(500);
            channel.Property(c => c.Label).HasMaxLength(100);
            channel.Property(c => c.Data).HasColumnName("data_json");
            channel.HasIndex(c => new { c.Channel, c.Value });
            channel.HasIndex(c => new { c.ContactId, c.Channel, c.Value }).IsUnique();
            channel.HasIndex(c => new { c.ContactId, c.Channel }).IsUnique().HasFilter("is_primary = 1").HasDatabaseName("ix_contact_channels_one_primary_per_channel");
            channel.ToTable(t => t.HasCheckConstraint("ck_contact_channels_data_json", "data_json IS NULL OR json_valid(data_json)"));
        });

        modelBuilder.Entity<CampaignContact>(membership =>
        {
            membership.HasKey(m => m.Id);
            membership.HasIndex(m => new { m.CampaignId, m.ContactId }).IsUnique();
            membership.HasIndex(m => m.ContactId);
            membership.HasOne(m => m.Contact).WithMany().HasForeignKey(m => m.ContactId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Suppression>(suppression =>
        {
            suppression.HasKey(s => s.Id);
            suppression.Property(s => s.PublicId).HasMaxLength(40);
            suppression.HasIndex(s => s.PublicId).IsUnique();
            suppression.Property(s => s.Channel).HasMaxLength(32);
            suppression.Property(s => s.Value).HasMaxLength(500);
            suppression.Property(s => s.Reason).HasMaxLength(2000);
            suppression.HasIndex(s => new { s.Channel, s.Value }).IsUnique();
        });

        modelBuilder.Entity<WorkItem>(item =>
        {
            item.HasKey(w => w.Id);
            item.Property(w => w.PublicId).HasMaxLength(40);
            item.HasIndex(w => w.PublicId).IsUnique();
            item.Property(w => w.Role).HasMaxLength(64);
            item.Property(w => w.Operation).HasMaxLength(200);
            item.Property(w => w.ExecutionProfile).HasMaxLength(100);
            item.Property(w => w.LineageProfileName).HasMaxLength(64);
            item.Property(w => w.LineageFromAttemptId).HasMaxLength(40);
            item.Property(w => w.CreatedById).HasMaxLength(100);
            item.Property(w => w.Context).HasColumnName("context_json").IsRequired().HasDefaultValueSql("'{}'");
            item.Property(w => w.ResultFormat).HasColumnName("result_format_json");
            item.Property(w => w.Result).HasColumnName("result_json");
            item.Property(w => w.LastError).HasColumnName("last_error_json");

            // Two writers racing over one item — the dispatcher claiming it and a caller cancelling it — must not
            // both win, and the status is what they both change.
            item.Property(w => w.Status).IsConcurrencyToken();
            item.ToTable(t =>
            {
                t.HasCheckConstraint("ck_work_items_context_json", "json_valid(context_json)");
                t.HasCheckConstraint("ck_work_items_result_format_json", "result_format_json IS NULL OR json_valid(result_format_json)");
                t.HasCheckConstraint("ck_work_items_result_json", "result_json IS NULL OR json_valid(result_json)");
                t.HasCheckConstraint("ck_work_items_last_error_json", "last_error_json IS NULL OR json_valid(last_error_json)");
            });
            item.HasIndex(w => new { w.CampaignId, w.Status });
            item.HasIndex(w => new { w.Status, w.NotBefore });
            item.HasIndex(w => new { w.Status, w.DueAt });
            item.HasIndex(w => w.ContactId);
            item.HasOne(w => w.Campaign).WithMany().HasForeignKey(w => w.CampaignId).OnDelete(DeleteBehavior.Restrict);
            item.HasOne(w => w.Contact).WithMany().HasForeignKey(w => w.ContactId).OnDelete(DeleteBehavior.Restrict);
            item.HasMany(w => w.Attempts).WithOne(a => a.WorkItem).HasForeignKey(a => a.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Approval>(approval =>
        {
            approval.HasKey(a => a.Id);
            approval.Property(a => a.PublicId).HasMaxLength(40);
            approval.HasIndex(a => a.PublicId).IsUnique();
            approval.Property(a => a.Operation).HasMaxLength(200);
            approval.Property(a => a.PluginId).HasMaxLength(64);
            approval.Property(a => a.BindingIdentity).HasMaxLength(80);
            approval.Property(a => a.SubjectHash).HasMaxLength(80);
            approval.Property(a => a.PluginSnapshotId).HasMaxLength(40);
            approval.Property(a => a.RoutingSnapshotId).HasMaxLength(40);
            approval.Property(a => a.Reason).HasMaxLength(64);
            approval.Property(a => a.DecidedById).HasMaxLength(100);
            approval.Property(a => a.Subject).HasColumnName("subject_json").IsRequired();
            approval.Property(a => a.Preview).HasColumnName("preview_json").IsRequired();
            approval.ToTable(t =>
            {
                t.HasCheckConstraint("ck_approvals_subject_json", "json_valid(subject_json)");
                t.HasCheckConstraint("ck_approvals_preview_json", "json_valid(preview_json)");
            });

            // Two people answering the same preview, or a decision racing the cancellation of the work it is
            // about, must not both win — and the status is what they both change. The guard is the same one a
            // work item's status carries, so a decision is one UPDATE that either moves a pending row or moves
            // nothing at all. Today the item's own guard is what refuses the races that exist; this one is what
            // keeps the rule true of the row itself.
            approval.Property(a => a.Status).IsConcurrencyToken();

            // One live decision per item, for the same reason there is one live attempt: two people answering
            // two previews of the same work is a race nobody could read afterwards.
            approval.HasIndex(a => a.WorkItemId).IsUnique().HasFilter("status = 'pending'").HasDatabaseName("ix_approvals_one_pending_per_item");
            approval.HasIndex(a => new { a.Status, a.RequestedAt });
            approval.HasIndex(a => a.CampaignId);
            approval.HasOne(a => a.WorkItem).WithMany().HasForeignKey(a => a.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExecutionProfile>(profile =>
        {
            profile.HasKey(p => p.Id);
            profile.Property(p => p.PublicId).HasMaxLength(40);
            profile.HasIndex(p => p.PublicId).IsUnique();
            profile.Property(p => p.Name).HasMaxLength(64);
            profile.HasIndex(p => p.Name).IsUnique();
            profile.Property(p => p.Description).HasMaxLength(500);

            // A revision belongs to its profile and outlives nothing: profiles are disabled rather than deleted,
            // so there is no cascade to arrange.
            profile.HasMany(p => p.Revisions).WithOne(r => r.Profile).HasForeignKey(r => r.ProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExecutionProfileRevision>(revision =>
        {
            revision.HasKey(r => r.Id);
            revision.Property(r => r.Program).HasMaxLength(500);
            revision.Property(r => r.CliCommand).HasMaxLength(200);
            revision.Property(r => r.HostVersionVerified).HasMaxLength(100);
            revision.Property(r => r.CreatedById).HasMaxLength(100);
            revision.Property(r => r.Args).HasColumnName("args_json").IsRequired().HasDefaultValueSql("'[]'");
            revision.Property(r => r.Deny).HasColumnName("deny_json").IsRequired().HasDefaultValueSql("'[]'");
            revision.ToTable(t =>
            {
                t.HasCheckConstraint("ck_execution_profile_revisions_args_json", "json_valid(args_json)");
                t.HasCheckConstraint("ck_execution_profile_revisions_deny_json", "json_valid(deny_json)");
            });

            // Revision numbers are the profile's own sequence, and an attempt names one of them for ever.
            revision.HasIndex(r => new { r.ProfileId, r.Number })
                .IsUnique().HasDatabaseName("ix_execution_profile_revisions_one_per_number");
        });

        modelBuilder.Entity<Report>(report =>
        {
            report.HasKey(r => r.Id);
            report.Property(r => r.PublicId).HasMaxLength(40);
            report.HasIndex(r => r.PublicId).IsUnique();
            report.Property(r => r.ReporterId).HasMaxLength(100);
            report.Property(r => r.Effect).HasMaxLength(200);
            report.Property(r => r.Tool).HasMaxLength(200);
            report.Property(r => r.Provider).HasMaxLength(200);
            report.Property(r => r.Account).HasMaxLength(200);
            report.Property(r => r.Operation).HasMaxLength(200);
            report.Property(r => r.IdempotencyKey).HasMaxLength(200);
            report.Property(r => r.AssertionHash).HasMaxLength(80);
            report.Property(r => r.Summary).HasMaxLength(2000);
            report.Property(r => r.Reason).HasMaxLength(2000);
            report.Property(r => r.Assertion).HasColumnName("assertion_json").IsRequired();
            report.ToTable(t => t.HasCheckConstraint("ck_reports_assertion_json", "json_valid(assertion_json)"));

            // A retry is matched by the reporter's own key, and where they gave none, by what they said. Both are
            // scoped to the reporter: two people describing the same effect are two assertions, and collapsing
            // them would throw one reporter's word away.
            report.HasIndex(r => new { r.ReporterType, r.ReporterId, r.IdempotencyKey })
                .IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ix_reports_one_per_reporter_key");
            report.HasIndex(r => new { r.ReporterType, r.ReporterId, r.AssertionHash })
                .IsUnique().HasFilter("idempotency_key IS NULL").HasDatabaseName("ix_reports_one_per_reporter_content");

            report.HasIndex(r => r.CampaignId);
            report.HasIndex(r => r.ContactId);
            report.HasIndex(r => r.WorkItemId);
            report.HasIndex(r => r.ReceivedAt);

            // Correlation is a reference, not ownership: a report outlives the work it mentions and nothing
            // about it cascades.
            report.HasOne(r => r.Campaign).WithMany().HasForeignKey(r => r.CampaignId).OnDelete(DeleteBehavior.Restrict);
            report.HasOne(r => r.Contact).WithMany().HasForeignKey(r => r.ContactId).OnDelete(DeleteBehavior.Restrict);
            report.HasOne(r => r.WorkItem).WithMany().HasForeignKey(r => r.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Attempt>(attempt =>
        {
            attempt.HasKey(a => a.Id);
            attempt.Property(a => a.PublicId).HasMaxLength(40);
            attempt.HasIndex(a => a.PublicId).IsUnique();
            attempt.Property(a => a.ExecutionProfile).HasMaxLength(100);
            attempt.Property(a => a.ContextSnapshot).HasColumnName("context_snapshot_json").IsRequired().HasDefaultValueSql("'{}'");
            attempt.Property(a => a.Error).HasColumnName("error_json");
            attempt.Property(a => a.Launch).HasColumnName("launch_json");
            attempt.Property(a => a.Provenance).HasColumnName("provenance_json");
            attempt.ToTable(t =>
            {
                t.HasCheckConstraint("ck_attempts_context_snapshot_json", "json_valid(context_snapshot_json)");
                t.HasCheckConstraint("ck_attempts_error_json", "error_json IS NULL OR json_valid(error_json)");
                t.HasCheckConstraint("ck_attempts_launch_json", "launch_json IS NULL OR json_valid(launch_json)");

                // provenance_json is guarded by triggers instead: SQLite cannot add a check constraint to a
                // table that already exists without rebuilding it, and rewriting every attempt row of every
                // database is too much to pay for a guarantee two triggers give exactly as well.
            });
            attempt.HasIndex(a => new { a.WorkItemId, a.Number }).IsUnique();
            attempt.HasIndex(a => a.Status);

            // Single-flight is a database guarantee, not a service convention: one live attempt per work item.
            attempt.HasIndex(a => a.WorkItemId).IsUnique().HasFilter("status IN ('scheduled','running')").HasDatabaseName("ix_attempts_one_live_per_item");
        });

        modelBuilder.Entity<Role>(role =>
        {
            role.HasKey(r => r.Id);
            role.Property(r => r.PublicId).HasMaxLength(40);
            role.HasIndex(r => r.PublicId).IsUnique();
            role.Property(r => r.Name).HasMaxLength(64);
            role.HasIndex(r => r.Name).IsUnique();
            role.Property(r => r.Description).HasMaxLength(500);
            role.Property(r => r.ExecutionProfile).HasMaxLength(64);
            role.Property(r => r.EntryCommand).HasColumnName("entry_command_json").IsRequired().HasDefaultValueSql("'[]'");
            role.Property(r => r.ProfileDefaults).HasColumnName("profile_defaults_json").IsRequired().HasDefaultValueSql("'{}'");
            role.ToTable(t =>
            {
                t.HasCheckConstraint("ck_roles_entry_command_json", "json_valid(entry_command_json)");
                t.HasCheckConstraint("ck_roles_profile_defaults_json", "json_valid(profile_defaults_json)");
            });
        });

        modelBuilder.Entity<ExternalId>(pin =>
        {
            pin.HasKey(p => p.Id);
            pin.Property(p => p.PluginId).HasMaxLength(64);
            pin.Property(p => p.Kind).HasMaxLength(64);
            pin.Property(p => p.Value).HasMaxLength(500);
            pin.Property(p => p.DivergedValue).HasMaxLength(500);
            pin.Property(p => p.RecordedByAttemptId).HasMaxLength(40);
            pin.Property(p => p.DivergedByAttemptId).HasMaxLength(40);

            // One plugin knows one entity by one identifier per kind; SQLite treats nulls as distinct, so the
            // contact index ignores campaign pins and the campaign index ignores contact pins.
            pin.HasIndex(p => new { p.ContactId, p.PluginId, p.Kind }).IsUnique();
            pin.HasIndex(p => new { p.CampaignId, p.PluginId, p.Kind }).IsUnique();
            pin.ToTable(t => t.HasCheckConstraint(
                "ck_external_ids_one_entity",
                "(contact_id IS NOT NULL AND campaign_id IS NULL) OR (contact_id IS NULL AND campaign_id IS NOT NULL)"));

            // A pin is meaningless without the thing it names, so it goes when that goes.
            pin.HasOne(p => p.Contact).WithMany(c => c!.ExternalIds).HasForeignKey(p => p.ContactId).OnDelete(DeleteBehavior.Cascade);
            pin.HasOne(p => p.Campaign).WithMany(c => c!.ExternalIds).HasForeignKey(p => p.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CampaignRoute>(route =>
        {
            route.HasKey(r => r.Id);
            route.Property(r => r.Operation).HasMaxLength(200);
            route.Property(r => r.PluginId).HasMaxLength(64);
            route.Property(r => r.Binding).HasColumnName("binding_json");
            route.ToTable(t => t.HasCheckConstraint("ck_campaign_routes_binding_json", "binding_json IS NULL OR json_valid(binding_json)"));
            route.HasIndex(r => new { r.CampaignId, r.Operation }).IsUnique();

            // The default route is the row with no operation, and nulls are distinct in the index above: only a
            // filtered index can say "at most one of those per campaign".
            route.HasIndex(r => r.CampaignId).IsUnique().HasFilter("operation IS NULL").HasDatabaseName("ix_campaign_routes_one_default_per_campaign");
            route.HasOne(r => r.Campaign).WithMany().HasForeignKey(r => r.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalEntry>(entry =>
        {
            // The snake_case loop below would name this table "journal_entries"; the chronicle is one table called "journal".
            entry.ToTable("journal", t =>
            {
                t.HasCheckConstraint("ck_journal_old_json", "old_json IS NULL OR json_valid(old_json)");
                t.HasCheckConstraint("ck_journal_new_json", "new_json IS NULL OR json_valid(new_json)");
            });
            entry.HasKey(e => e.Id);
            entry.Property(e => e.PublicId).HasMaxLength(40);
            entry.HasIndex(e => e.PublicId).IsUnique();
            entry.Property(e => e.ActorId).HasMaxLength(100);
            entry.Property(e => e.Kind).HasMaxLength(64);
            entry.Property(e => e.Key).HasMaxLength(200);
            entry.Property(e => e.Old).HasColumnName("old_json");
            entry.Property(e => e.New).HasColumnName("new_json");
            entry.Property(e => e.Reason).HasMaxLength(2000);

            // Public ids as plain text with no foreign key: the chronicle outlives what it describes, and a
            // constraint here would also force SQLite to rebuild the append-only table to add the columns.
            entry.Property(e => e.WorkItemId).HasMaxLength(40);
            entry.Property(e => e.AttemptId).HasMaxLength(40);
            entry.HasIndex(e => new { e.CampaignId, e.PublicId });
            entry.HasIndex(e => new { e.WorkItemId, e.PublicId });
            entry.HasIndex(e => e.Kind);
            entry.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(SnakeCase.Convert(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase.Convert(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(SnakeCase.Convert(key.GetName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(SnakeCase.Convert(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(SnakeCase.Convert(index.GetDatabaseName()!));
            }
        }
    }
}
