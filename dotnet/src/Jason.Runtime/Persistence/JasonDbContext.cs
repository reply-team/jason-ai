using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Decisions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

public sealed class JasonDbContext(DbContextOptions<JasonDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactChannel> ContactChannels => Set<ContactChannel>();

    public DbSet<CampaignContact> CampaignContacts => Set<CampaignContact>();

    public DbSet<Suppression> Suppressions => Set<Suppression>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<WorkItemWait> WorkItemWaits => Set<WorkItemWait>();

    public DbSet<WorkItemWaitItem> WorkItemWaitItems => Set<WorkItemWaitItem>();

    public DbSet<ProviderBlock> ProviderBlocks => Set<ProviderBlock>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<ExternalId> ExternalIds => Set<ExternalId>();

    public DbSet<CampaignRoute> CampaignRoutes => Set<CampaignRoute>();

    public DbSet<CampaignChannelRoute> CampaignChannelRoutes => Set<CampaignChannelRoute>();

    public DbSet<Approval> Approvals => Set<Approval>();

    public DbSet<Decision> Decisions => Set<Decision>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<ExecutionProfile> ExecutionProfiles => Set<ExecutionProfile>();

    public DbSet<ExecutionProfileRevision> ExecutionProfileRevisions => Set<ExecutionProfileRevision>();

    public DbSet<RoleNote> RoleNotes => Set<RoleNote>();

    public DbSet<JournalEntry> Journal => Set<JournalEntry>();

    public DbSet<UiToken> UiTokens => Set<UiToken>();

    public DbSet<EventSource> EventSources => Set<EventSource>();

    public DbSet<ProviderEvent> Events => Set<ProviderEvent>();

    public DbSet<EventAttribution> EventAttributions => Set<EventAttribution>();

    public DbSet<EventBackfill> EventBackfills => Set<EventBackfill>();

    public DbSet<Preference> Preferences => Set<Preference>();

    public DbSet<SubscriptionCursor> SubscriptionCursors => Set<SubscriptionCursor>();

    public DbSet<SubscriptionRun> SubscriptionRuns => Set<SubscriptionRun>();

    public DbSet<Audience> Audiences => Set<Audience>();

    public DbSet<AudienceSearch> AudienceSearches => Set<AudienceSearch>();

    public DbSet<AudienceLanding> AudienceLandings => Set<AudienceLanding>();

    /// <summary>
    /// The one connection string for a database file. Spelled once because the provider pools connections per
    /// connection string: anything that wants to release a file's pooled connections must name it exactly.
    /// </summary>
    public static string ConnectionString(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        return $"Data Source={databaseFile};Default Timeout=30";
    }

    /// <summary>
    /// One place for the SQLite options, the interceptors and the events EF Core raises; used by CreateOptions, the
    /// migrator and the DI registration.
    /// </summary>
    /// <remarks>
    /// EF Core's own event for a query whose results failed to read is not raised. It writes the exception whole — into
    /// its message and beside it. A column a converter of the runtime's reads no longer brings a reader's words to it: the
    /// converter refuses by the column's name and quotes nothing. A value the database provider converts itself, before
    /// any converter runs, still does — a hand-edited timestamp fails with the provider's own parse error, which quotes
    /// the text — and that is what this holds against. The exception still reaches the code that ran the query, and that
    /// code's own line says what happened, a read failure by its kind: the event only repeated it, whole. A test pins the
    /// event's absence, because with every converted column refusing quietly nothing else would show it had come back.
    /// One event rather than EF Core's query category raised past
    /// Error in the logger, because that category also carries EF Core's warnings about how a query is written, and
    /// because an option of the context holds for whatever logger the context is given, not for the runtime's file alone.
    /// <para>
    /// A journal line of a reserved kind is held to its document by <see cref="JournalLineInterceptor"/>, and
    /// <paramref name="lines"/> says what happens to one that is off it, and every caller says it, for there is no
    /// default: strict under test, logged in an installation. The host passes the mode its options name, the migrator
    /// the logged one with its own logger, and every test strict; an installation's context that forgot would otherwise
    /// refuse a line rather than log it.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string databaseFile, JournalLines lines)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(lines);
        return builder
            .UseSqlite(ConnectionString(databaseFile))
            .AddInterceptors(
                new AppendOnlyInterceptor(),
                new JournalLineInterceptor(lines),
                new TextFoldInterceptor(),
                new CustomValuesInterceptor(),
                new LinkedInKeyInterceptor())
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.QueryIterationFailed));
    }

    /// <summary>
    /// One connection string for the whole runtime: WAL is set by the migrator, busy timeout here. Strict, for the tests
    /// and the design-time tooling that call it; no context of a running installation is made here.
    /// </summary>
    public static DbContextOptions<JasonDbContext> CreateOptions(string databaseFile)
    {
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        Configure(builder, databaseFile, JournalLines.Strict);
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
        configurationBuilder.Properties<WaitMode>().HaveConversion<SnakeCaseEnumConverter<WaitMode>>().HaveMaxLength(32);
        configurationBuilder.Properties<WaitStatus>().HaveConversion<SnakeCaseEnumConverter<WaitStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<WaitReleaseReason>().HaveConversion<SnakeCaseEnumConverter<WaitReleaseReason>>().HaveMaxLength(32);
        configurationBuilder.Properties<ApprovalStatus>().HaveConversion<SnakeCaseEnumConverter<ApprovalStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<DecisionStatus>().HaveConversion<SnakeCaseEnumConverter<DecisionStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<RouteScope>().HaveConversion<SnakeCaseEnumConverter<RouteScope>>().HaveMaxLength(32);
        configurationBuilder.Properties<AgentHostKind>().HaveConversion<SnakeCaseEnumConverter<AgentHostKind>>().HaveMaxLength(32);
        configurationBuilder.Properties<LineageState>().HaveConversion<SnakeCaseEnumConverter<LineageState>>().HaveMaxLength(32);
        configurationBuilder.Properties<ProviderBlockScope>().HaveConversion<SnakeCaseEnumConverter<ProviderBlockScope>>().HaveMaxLength(32);
        configurationBuilder.Properties<ProviderBlockStatus>().HaveConversion<SnakeCaseEnumConverter<ProviderBlockStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<EventBackfillStatus>().HaveConversion<SnakeCaseEnumConverter<EventBackfillStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<SubscriptionRunStatus>().HaveConversion<SnakeCaseEnumConverter<SubscriptionRunStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<JsonObject>().HaveConversion<JsonObjectConverter, JsonObjectComparer>();
        // A function a query calls on a JSON object column (CustomValues) takes the column as the column stores it.
        configurationBuilder.DefaultTypeMapping<JsonObject>().HasConversion<JsonObjectConverter>();
        configurationBuilder.Properties<JsonArray>().HaveConversion<JsonArrayConverter, JsonArrayComparer>();
        configurationBuilder.Properties<JsonNode>().HaveConversion<JsonNodeConverter, JsonNodeComparer>();
        configurationBuilder.Properties<AttemptErrorDto>().HaveConversion<JsonTextConverter<AttemptErrorDto>, JsonTextComparer<AttemptErrorDto>>();
        configurationBuilder.Properties<AttemptLaunchDto>().HaveConversion<JsonTextConverter<AttemptLaunchDto>, JsonTextComparer<AttemptLaunchDto>>();
        configurationBuilder.Properties<AttemptProvenanceDto>().HaveConversion<JsonTextConverter<AttemptProvenanceDto>, JsonTextComparer<AttemptProvenanceDto>>();
        configurationBuilder.Properties<List<string>>().HaveConversion<StringListConverter, StringListComparer>();
        configurationBuilder.Properties<List<AgentTool>>().HaveConversion<JsonTextConverter<List<AgentTool>>, JsonTextComparer<List<AgentTool>>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDbFunction(typeof(TextFold).GetMethod(nameof(TextFold.Fold))!).HasName(TextFold.FunctionName);
        modelBuilder.HasDbFunction(typeof(CustomValues).GetMethod(nameof(CustomValues.Of))!).HasName(CustomValues.FunctionName);

        modelBuilder.Entity<Project>(project =>
        {
            project.HasKey(p => p.Id);
            project.Property(p => p.PublicId).HasMaxLength(40);
            project.HasIndex(p => p.PublicId).IsUnique();
            project.Property(p => p.Name).HasMaxLength(200);

            // The slug names the project's folder and is a handle a person types, so two projects never share one.
            project.Property(p => p.Slug).HasMaxLength(40);
            project.HasIndex(p => p.Slug).IsUnique();

            // Exactly one project is the default, and the database is what says so: a second row claiming it is refused.
            project.HasIndex(p => p.IsDefault)
                .IsUnique()
                .HasFilter("is_default = 1")
                .HasDatabaseName("ix_projects_one_default");
            project.Property(p => p.FolderPath).HasMaxLength(1024);
        });

        modelBuilder.Entity<Campaign>(campaign =>
        {
            // A campaign's project is a project that exists, and a project's campaigns are read by the project, in the
            // order a listing pages them. The database holds the column required by two triggers rather than NOT NULL,
            // because it was added to campaigns where they stood (ProjectsAndPreferences); here it is required as it is.
            campaign.HasOne(c => c.Project).WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
            campaign.HasIndex(c => new { c.ProjectId, c.PublicId }).HasDatabaseName("ix_campaigns_project");
            campaign.Property(c => c.FolderPath).HasMaxLength(1024);

            campaign.HasKey(c => c.Id);
            campaign.Property(c => c.PublicId).HasMaxLength(40);
            campaign.HasIndex(c => c.PublicId).IsUnique();
            campaign.Property(c => c.Name).HasMaxLength(200);
            campaign.Property(c => c.ExecutionProfile).HasMaxLength(64);
            campaign.Property(c => c.Context).HasColumnName("context_json").IsRequired().HasDefaultValueSql("'{}'");
            campaign.ToTable(t => t.HasCheckConstraint("ck_campaigns_context_json", "json_valid(context_json)"));
            campaign.Property(c => c.ManagerEventWatermark).HasDefaultValue(0);
            campaign.Property(c => c.ResponderWatermark).HasDefaultValue(0);
            campaign.Property(c => c.AudienceWatermark).HasDefaultValue(0);

            // One key names one campaign, and the database is what says so: two creates deciding "is it free?" by
            // looking would both see yes. A campaign made without a key is in nobody's way.
            campaign.Property(c => c.IdempotencyKey).HasMaxLength(128);
            campaign.Property(c => c.IdempotencyDigest).HasMaxLength(71);
            campaign.HasIndex(c => c.IdempotencyKey)
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL")
                .HasDatabaseName("ix_campaigns_one_per_idempotency_key");
            campaign.HasMany(c => c.Members).WithOne(m => m.Campaign).HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Restrict);

            // The read still outstanding is a work item that exists. Nothing cascades: a work item is never deleted, and
            // the campaign's row is not where its life is decided.
            campaign.Property(c => c.InboundOffset).HasDefaultValue(0);
            campaign.Property(c => c.InboundFailures).HasDefaultValue(0);
            campaign.HasOne<WorkItem>().WithMany().HasForeignKey(c => c.InboundSyncItemId).OnDelete(DeleteBehavior.Restrict);
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

            // One key names one item in its campaign; the same key in another campaign is another item.
            item.Property(w => w.IdempotencyKey).HasMaxLength(128);
            item.Property(w => w.IdempotencyDigest).HasMaxLength(71);

            // A doubt's sentence, plain text and never JSON, so no converter quotes it into an error.
            item.Property(w => w.AskPersonNote).HasMaxLength(DoubtCodes.MaxNoteLength);
            item.Property(w => w.ReviewNote).HasMaxLength(DoubtCodes.MaxNoteLength);
            item.HasIndex(w => new { w.CampaignId, w.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL")
                .HasDatabaseName("ix_work_items_one_per_campaign_idempotency_key");

            // One open check-in per campaign, enforced where two scans cannot argue about it. A user back after
            // days offline finds one review waiting, not a pile — and the summon can be written as the ordinary
            // "look, then insert" because the database, not the look, is what decides.
            item.HasIndex(w => w.CampaignId)
                .IsUnique()
                .HasFilter("role = 'manager' AND created_by_type = 'system' AND status IN ('created','scheduled','processing','awaiting_approval')")
                .HasDatabaseName("ix_work_items_one_open_check_in_per_campaign");
            item.HasIndex(w => new { w.Status, w.NotBefore });
            item.HasIndex(w => new { w.Status, w.DueAt });
            item.HasIndex(w => new { w.Status, w.FinishedAt });
            item.HasIndex(w => w.ContactId);

            // A campaign's work by person, in order: how the campaign's people are counted, one pass per campaign asked.
            item.HasIndex(w => new { w.CampaignId, w.ContactId });

            // A campaign's finished work by when it finished: what a period of the campaigns asked reads, and no other's.
            item.HasIndex(w => new { w.CampaignId, w.FinishedAt }).HasFilter("finished_at IS NOT NULL");
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

        modelBuilder.Entity<Decision>(decision =>
        {
            decision.HasKey(d => d.Id);
            decision.Property(d => d.PublicId).HasMaxLength(40);
            decision.HasIndex(d => d.PublicId).IsUnique();
            decision.Property(d => d.Question).HasMaxLength(DecisionLimits.MaxQuestionLength);
            decision.Property(d => d.Answer).HasMaxLength(DecisionLimits.MaxAnswerLength);
            decision.Property(d => d.ChosenOption).HasMaxLength(DecisionLimits.MaxOptionLabelLength);
            decision.Property(d => d.AnsweredById).HasMaxLength(100);
            decision.Property(d => d.AnswerJournalEntryId).HasMaxLength(40);
            decision.Property(d => d.Options).HasColumnName("options_json");
            decision.Property(d => d.References).HasColumnName("references_json");
            decision.ToTable(t =>
            {
                t.HasCheckConstraint("ck_decisions_options_json", "options_json IS NULL OR json_valid(options_json)");
                t.HasCheckConstraint("ck_decisions_references_json", "references_json IS NULL OR json_valid(references_json)");
            });

            // Two people answering the same question must not both win, and the status is what they both
            // change — the guard an approval's status already carries, for the same reason.
            decision.Property(d => d.Status).IsConcurrencyToken();

            // How the summon names the decision that woke it: one lookup by the line the answer wrote.
            decision.HasIndex(d => d.AnswerJournalEntryId);
            decision.HasIndex(d => new { d.Status, d.RaisedAt });
            decision.HasIndex(d => d.CampaignId);
            decision.HasOne(d => d.Campaign).WithMany().HasForeignKey(d => d.CampaignId).OnDelete(DeleteBehavior.Restrict);
            decision.HasOne(d => d.WorkItem).WithMany().HasForeignKey(d => d.WorkItemId).OnDelete(DeleteBehavior.Restrict);
            decision.HasOne(d => d.Attempt).WithMany().HasForeignKey(d => d.AttemptId).OnDelete(DeleteBehavior.Restrict);
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
            revision.Property(r => r.Tools).HasColumnName("tools_json").IsRequired().HasDefaultValueSql("'[]'");
            revision.ToTable(t =>
            {
                t.HasCheckConstraint("ck_execution_profile_revisions_args_json", "json_valid(args_json)");
                t.HasCheckConstraint("ck_execution_profile_revisions_deny_json", "json_valid(deny_json)");
                t.HasCheckConstraint("ck_execution_profile_revisions_tools_json", "json_valid(tools_json)");
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

        modelBuilder.Entity<UiToken>(token =>
        {
            token.HasKey(t => t.Id);
            token.Property(t => t.PublicId).HasMaxLength(40);
            token.HasIndex(t => t.PublicId).IsUnique();
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.Property(t => t.IssuedTo).HasMaxLength(100);
        });

        modelBuilder.Entity<RoleNote>(note =>
        {
            note.HasKey(n => n.Id);
            note.Property(n => n.Role).HasMaxLength(64);
            note.Property(n => n.NoteHash).HasMaxLength(80);
            note.Property(n => n.UpdatedById).HasMaxLength(100);
            note.Property(n => n.Note).HasColumnName("note_json").IsRequired().HasDefaultValueSql("'{}'");
            note.ToTable(t =>
            {
                t.HasCheckConstraint("ck_role_notes_note_json", "json_valid(note_json)");

                // A note is at one level: a campaign's, a project's, or — naming neither — the client's. A campaign's
                // note is in the campaign's project already, and that is never stored twice.
                t.HasCheckConstraint("ck_role_notes_one_level", "campaign_id IS NULL OR project_id IS NULL");
            });

            // One document per role at each level, which is what makes a write a replacement rather than an append:
            // there is nowhere for a second note to go.
            note.HasIndex(n => new { n.CampaignId, n.Role })
                .IsUnique()
                .HasFilter("campaign_id IS NOT NULL")
                .HasDatabaseName("ix_role_notes_one_per_campaign_role");
            note.HasIndex(n => new { n.ProjectId, n.Role })
                .IsUnique()
                .HasFilter("project_id IS NOT NULL")
                .HasDatabaseName("ix_role_notes_one_per_project_role");
            note.HasIndex(n => n.Role, "ix_role_notes_one_per_client_role")
                .IsUnique()
                .HasFilter("campaign_id IS NULL AND project_id IS NULL");

            // A note belongs to its campaign or its project and is not their business: nothing cascades, the way
            // nothing else in this schema does.
            note.HasOne(n => n.Campaign).WithMany().HasForeignKey(n => n.CampaignId).OnDelete(DeleteBehavior.Restrict);
            note.HasOne(n => n.Project).WithMany().HasForeignKey(n => n.ProjectId).OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<WorkItemWait>(wait =>
        {
            wait.HasKey(w => w.Id);

            // Releasing a wait and cancelling its item must not both win, and the status is what they both change.
            wait.Property(w => w.Status).IsConcurrencyToken();

            // One open wait per item: an item is waiting for one thing at a time, and the database is what says so.
            wait.HasIndex(w => w.WorkItemId).IsUnique().HasFilter("status = 'open'").HasDatabaseName("ix_work_item_waits_one_open_per_item");

            // An item's waits by how they stand: the resume step finds each waiting item's open wait here, and a wait that has
            // closed is found by its item only here.
            wait.HasIndex(w => new { w.WorkItemId, w.Status });
            wait.HasOne(w => w.WorkItem).WithMany().HasForeignKey(w => w.WorkItemId).OnDelete(DeleteBehavior.Restrict);
            wait.HasOne(w => w.Attempt).WithMany().HasForeignKey(w => w.AttemptId).OnDelete(DeleteBehavior.Restrict);
            wait.HasOne(w => w.ResumedByAttempt).WithMany().HasForeignKey(w => w.ResumedByAttemptId).OnDelete(DeleteBehavior.Restrict);
            wait.HasMany(w => w.Items).WithOne(i => i.Wait).HasForeignKey(i => i.WaitId).OnDelete(DeleteBehavior.Restrict);

            // The waits a provider block holds, by how they stand: a trial picks one of a block's open waits here.
            wait.HasOne(w => w.ProviderBlock).WithMany().HasForeignKey(w => w.ProviderBlockId).OnDelete(DeleteBehavior.Restrict);
            wait.HasIndex(w => new { w.ProviderBlockId, w.Status });
        });

        modelBuilder.Entity<ProviderBlock>(block =>
        {
            block.HasKey(b => b.Id);
            block.Property(b => b.PublicId).HasMaxLength(40);
            block.HasIndex(b => b.PublicId).IsUnique();
            block.Property(b => b.PluginId).HasMaxLength(64);
            block.Property(b => b.BindingIdentity).HasMaxLength(71);
            block.Property(b => b.SenderExternalId).HasMaxLength(256);
            block.Property(b => b.Reason).HasMaxLength(32);
            block.Property(b => b.Link).HasMaxLength(2048);
            block.Property(b => b.ClearedById).HasMaxLength(100);

            // The two halves of one rule, one open block per binding, scope and sender: SQLite holds nulls distinct, so
            // an account block, which names no sender, is held unique by an index of its own.
            block.HasIndex(b => new { b.PluginId, b.BindingIdentity })
                .IsUnique()
                .HasFilter("status = 'open' AND scope = 'account'")
                .HasDatabaseName("ix_provider_blocks_one_open_per_account");
            block.HasIndex(b => new { b.PluginId, b.BindingIdentity, b.SenderExternalId })
                .IsUnique()
                .HasFilter("status = 'open' AND scope = 'sender'")
                .HasDatabaseName("ix_provider_blocks_one_open_per_sender");

            // The blocks whose trial is due, by how they stand and when: the resume step's trial query reads them here.
            block.HasIndex(b => new { b.Status, b.NextTrialAt });

            // The blocks of one status newest first, as block.list pages them: its order and its cursor are this index's.
            block.HasIndex(b => new { b.Status, b.OpenedAt, b.PublicId });

            // Two calls that get past one block at once must not both clear it, and the status is what a clear changes.
            block.Property(b => b.Status).IsConcurrencyToken();
            block.ToTable(t => t.HasCheckConstraint("ck_provider_blocks_sender", "(scope = 'sender') = (sender_external_id IS NOT NULL)"));

            // Nothing a block refers to goes away under it.
            block.HasOne(b => b.OpenedByAttempt).WithMany().HasForeignKey(b => b.OpenedByAttemptId).OnDelete(DeleteBehavior.Restrict);
            block.HasOne(b => b.ClearedByAttempt).WithMany().HasForeignKey(b => b.ClearedByAttemptId).OnDelete(DeleteBehavior.Restrict);
            block.HasOne(b => b.TrialWorkItem).WithMany().HasForeignKey(b => b.TrialWorkItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkItemWaitItem>(named =>
        {
            // A wait names an item once; the key also serves the resume step, which reads a wait's items by the wait.
            named.HasKey(i => new { i.WaitId, i.ChildWorkItemId });
            named.HasIndex(i => i.ChildWorkItemId);
            named.HasOne(i => i.ChildWorkItem).WithMany().HasForeignKey(i => i.ChildWorkItemId).OnDelete(DeleteBehavior.Restrict);
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

            // The inbound read finds the person a thread belongs to by the identifier the provider gave them.
            pin.HasIndex(p => new { p.PluginId, p.Kind, p.Value }).HasDatabaseName("ix_external_ids_plugin_kind_value");
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

        modelBuilder.Entity<CampaignChannelRoute>(route =>
        {
            route.HasKey(r => r.Id);
            route.Property(r => r.Operation).HasMaxLength(200);
            route.Property(r => r.Channel).HasMaxLength(32);
            route.Property(r => r.PluginId).HasMaxLength(64);
            route.Property(r => r.Binding).HasColumnName("binding_json");
            route.ToTable(t => t.HasCheckConstraint("ck_campaign_channel_routes_binding_json", "binding_json IS NULL OR json_valid(binding_json)"));
            route.HasIndex(r => new { r.CampaignId, r.Operation, r.Channel }).IsUnique();
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

            // The summon reads one campaign's chronicle above an id, on every scan of every campaign.
            entry.HasIndex(e => new { e.CampaignId, e.Id }).HasDatabaseName("ix_journal_campaign_id_id");

            // The inbound read says each observation once per campaign, and the database is what says so: the step looks
            // the page's keys up here before it writes, and a second line under a key a campaign already holds is
            // refused. Only that kind is indexed, so the largest table carries no index for every other line.
            entry.HasIndex(e => new { e.CampaignId, e.Kind, e.Key })
                .IsUnique()
                .HasFilter("kind = 'inbound_reply_observed'")
                .HasDatabaseName("ix_journal_campaign_kind_key");

            // A provider event is said once per campaign in the same way, in an index of its own beside the inbound
            // read's: one key may be said in two campaigns, never twice in one.
            entry.HasIndex(e => new { e.CampaignId, e.Kind, e.Key }, "ix_journal_campaign_kind_key_events")
                .IsUnique()
                .HasFilter("kind = 'provider_event_observed'");

            // A send seen to have gone out is said once, under the key of the work item that accepted it, in an index of
            // its own: the reads that find it look the key up first, and a second line is refused.
            entry.HasIndex(e => new { e.CampaignId, e.Kind, e.Key }, "ix_journal_campaign_kind_key_sends")
                .IsUnique()
                .HasFilter("kind = 'message_sent_observed'");

            // A search ends once, under the key of its work item. The search's row is marked in the same change set, and a
            // second line — two writers deciding the same ending at once — is refused here.
            entry.HasIndex(e => new { e.CampaignId, e.Kind, e.Key }, "ix_journal_campaign_kind_key_searches_ended")
                .IsUnique()
                .HasFilter("kind = 'audience_search_ended'");

            // A gap in an account's events is said once. Its line names no campaign, and SQLite keeps every null apart from
            // every other in a unique index, so the index is on the kind and the key alone.
            entry.HasIndex(e => new { e.Kind, e.Key }, "ix_journal_kind_key_gaps")
                .IsUnique()
                .HasFilter("kind = 'events_gap_observed'");

            // A provider block is said once per campaign whose work it holds, and the database is what says so. The key,
            // the block's public id, leads, because the block's clear reads every campaign it was said in by it.
            entry.HasIndex(e => new { e.Kind, e.Key, e.CampaignId }, "ix_journal_kind_key_campaign_blocks")
                .IsUnique()
                .HasFilter("kind = 'provider_blocked'");
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

            // The opt-outs of a period and a campaign's additions and removals of a period, read for every campaign's
            // numbers: each kind's own lines by time, so the read grows with the period, never with the journal.
            entry.HasIndex(e => e.Ts, "ix_journal_suppression_added_ts").HasFilter("kind = 'suppression_added'");
            entry.HasIndex(e => new { e.CampaignId, e.Ts }, "ix_journal_contacts_added_campaign_ts").HasFilter("kind = 'contacts_added'");
            entry.HasIndex(e => new { e.CampaignId, e.Ts }, "ix_journal_contacts_removed_campaign_ts").HasFilter("kind = 'contacts_removed'");

            // The reserve step reads a campaign's latest line of each of its two kinds on every recount, and only those
            // lines are indexed.
            entry.HasIndex(e => new { e.CampaignId, e.Id }, "ix_journal_audience_reserve_low_campaign_id").HasFilter("kind = 'audience_reserve_low'");
            entry.HasIndex(e => new { e.CampaignId, e.Id }, "ix_journal_audience_reserve_recovered_campaign_id").HasFilter("kind = 'audience_reserve_recovered'");

            // One person's lines, newest first: their timeline, and the newest of them is their latest activity.
            entry.HasIndex(e => new { e.ContactId, e.PublicId });
            entry.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventSource>(source =>
        {
            source.ToTable("event_sources");
            source.HasKey(s => s.Id);
            source.Property(s => s.PublicId).HasMaxLength(40);
            source.HasIndex(s => s.PublicId).IsUnique();
            source.Property(s => s.PluginId).HasMaxLength(64);
            source.Property(s => s.BindingIdentity).HasMaxLength(71);
            source.Property(s => s.Cursor).HasMaxLength(2048);
            source.Property(s => s.LastCode).HasMaxLength(64);
            source.Property(s => s.LastClass).HasMaxLength(16);

            // One account is one source: a plugin and the identity of the binding a route names for it.
            source.HasIndex(s => new { s.PluginId, s.BindingIdentity }).IsUnique();
        });

        modelBuilder.Entity<ProviderEvent>(stored =>
        {
            stored.ToTable("events", t => t.HasCheckConstraint("ck_events_data_json", "json_valid(data_json)"));
            stored.HasKey(e => e.Id);
            stored.Property(e => e.PublicId).HasMaxLength(40);
            stored.HasIndex(e => e.PublicId).IsUnique();
            stored.Property(e => e.PluginId).HasMaxLength(64);
            stored.Property(e => e.Kind).HasMaxLength(64);
            stored.Property(e => e.Key).HasMaxLength(71);
            stored.Property(e => e.Data).HasColumnName("data_json");
            stored.Property(e => e.ReadId).HasMaxLength(40);

            // An event read twice is stored once, and the database is what says so.
            stored.HasIndex(e => e.Key).IsUnique();
            stored.HasIndex(e => new { e.SourceId, e.OccurredAt });

            // Nothing a stored event refers to goes away under it.
            stored.HasOne(e => e.Source).WithMany().HasForeignKey(e => e.SourceId).OnDelete(DeleteBehavior.Restrict);
            stored.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventAttribution>(attribution =>
        {
            attribution.ToTable("event_attributions");
            attribution.HasKey(a => a.Id);
            attribution.Property(a => a.Rule).HasMaxLength(32);

            // A campaign is told of an event once, and a campaign's events are read by the campaign.
            attribution.HasIndex(a => new { a.EventId, a.CampaignId }).IsUnique();
            attribution.HasIndex(a => new { a.CampaignId, a.EventId });
            attribution.HasOne(a => a.Event).WithMany().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Restrict);
            attribution.HasOne(a => a.Campaign).WithMany().HasForeignKey(a => a.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventBackfill>(backfill =>
        {
            backfill.ToTable("event_backfills");
            backfill.HasKey(b => b.Id);
            backfill.Property(b => b.PublicId).HasMaxLength(40);
            backfill.HasIndex(b => b.PublicId).IsUnique();

            // The span's two ends are named for what they bound: "from" and "to" are words of the SQL they would be read in.
            backfill.Property(b => b.From).HasColumnName("range_from");
            backfill.Property(b => b.To).HasColumnName("range_to");
            backfill.Property(b => b.Cursor).HasMaxLength(2048);
            backfill.Property(b => b.Failures).HasDefaultValue(0);
            backfill.Property(b => b.ActorId).HasMaxLength(100);
            backfill.Property(b => b.Reason).HasMaxLength(2000);
            backfill.Property(b => b.Stored).HasDefaultValue(0);
            backfill.Property(b => b.Duplicates).HasDefaultValue(0);
            backfill.Property(b => b.LastCode).HasMaxLength(64);
            backfill.Property(b => b.LastClass).HasMaxLength(16);

            // A source's waiting spans are read oldest first and counted against a bound, by the source.
            backfill.HasIndex(b => new { b.SourceId, b.Status });

            // A span waits once: asked for again while it waits, the request is the one already waiting, and the database
            // is what says so. Once it has ended, the same span may be asked for again.
            backfill.HasIndex(b => new { b.SourceId, b.From, b.To }, "ix_event_backfills_one_pending_range")
                .IsUnique()
                .HasFilter("status = 'pending'");
            backfill.HasOne(b => b.Source).WithMany().HasForeignKey(b => b.SourceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Preference>(preference =>
        {
            preference.HasKey(p => p.Id);

            // One value per key, at the client level: there is nowhere for a second to go.
            preference.Property(p => p.Key).HasMaxLength(64);
            preference.HasIndex(p => p.Key).IsUnique();
            preference.Property(p => p.Value).HasColumnName("value_json").IsRequired();
            preference.ToTable(t => t.HasCheckConstraint("ck_preferences_value_json", "json_valid(value_json)"));
            preference.Property(p => p.ValueHash).HasMaxLength(80);
            preference.Property(p => p.UpdatedById).HasMaxLength(100);
        });

        modelBuilder.Entity<Audience>(audience =>
        {
            audience.ToTable("audiences", t => t.HasCheckConstraint("ck_audiences_definition_json", "json_valid(definition_json)"));
            audience.HasKey(a => a.Id);
            audience.Property(a => a.PublicId).HasMaxLength(40);
            audience.HasIndex(a => a.PublicId).IsUnique();
            audience.Property(a => a.Definition).HasColumnName("definition_json").IsRequired();
            audience.Property(a => a.DefinitionHash).HasMaxLength(71);

            // A campaign's audience is found by its number and by its content, one row for each: the database says so, and
            // the latest number is read off the same index the next one is made from.
            audience.HasIndex(a => new { a.CampaignId, a.Number }).IsUnique();
            audience.HasIndex(a => new { a.CampaignId, a.DefinitionHash }).IsUnique();
            audience.HasOne(a => a.Campaign).WithMany().HasForeignKey(a => a.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AudienceSearch>(search =>
        {
            search.ToTable("audience_searches", t =>
            {
                t.HasCheckConstraint(
                    "ck_audience_searches_state",
                    "state IS NULL OR state IN ('in_progress', 'finished', 'exhausted', 'paused', 'paused_for_credits', 'paused_for_limit')");
                t.HasCheckConstraint(
                    "ck_audience_searches_ended_as",
                    "(ended_as IS NULL AND ended_at IS NULL) OR (ended_as IN ('finished', 'exhausted', 'stalled', 'cancelled') AND ended_at IS NOT NULL)");
            });
            search.HasKey(s => s.Id);
            search.Property(s => s.PluginId).HasMaxLength(64);
            search.Property(s => s.Reference).HasMaxLength(256);
            search.Property(s => s.Cursor).HasMaxLength(2048);
            search.Property(s => s.State).HasMaxLength(32);
            search.Property(s => s.EndedAs).HasMaxLength(32);

            // One row per search, and the database is what says so: the change set that ends the search makes it once.
            search.HasIndex(s => s.SearchWorkItemId).IsUnique();

            // The poll step reads the searches still followed, and only those: a campaign's ended searches never cost it.
            search.HasIndex(s => s.CampaignId)
                .HasFilter("ended_as IS NULL")
                .HasDatabaseName("ix_audience_searches_in_flight");
            search.HasIndex(s => s.AudienceId);
            search.HasOne(s => s.Campaign).WithMany().HasForeignKey(s => s.CampaignId).OnDelete(DeleteBehavior.Restrict);
            search.HasOne(s => s.Audience).WithMany().HasForeignKey(s => s.AudienceId).OnDelete(DeleteBehavior.Restrict);
            search.HasOne(s => s.SearchWorkItem).WithMany().HasForeignKey(s => s.SearchWorkItemId).OnDelete(DeleteBehavior.Restrict);
            search.HasOne(s => s.ReadWorkItem).WithMany().HasForeignKey(s => s.ReadWorkItemId).OnDelete(DeleteBehavior.Restrict);
            search.Property(s => s.Failures).HasDefaultValue(0);
        });

        modelBuilder.Entity<AudienceLanding>(landing =>
        {
            landing.ToTable("audience_landings");
            landing.HasKey(l => l.Id);

            // One membership, one origin: a search that lands a person the campaign already holds makes no row.
            landing.HasIndex(l => l.CampaignContactId).IsUnique();
            landing.HasIndex(l => l.AudienceId);
            landing.HasIndex(l => l.SearchWorkItemId);
            landing.HasOne(l => l.Membership).WithOne(m => m.Landing).HasForeignKey<AudienceLanding>(l => l.CampaignContactId).OnDelete(DeleteBehavior.Restrict);
            landing.HasOne(l => l.Audience).WithMany().HasForeignKey(l => l.AudienceId).OnDelete(DeleteBehavior.Restrict);
            landing.HasOne(l => l.SearchWorkItem).WithMany().HasForeignKey(l => l.SearchWorkItemId).OnDelete(DeleteBehavior.Restrict);
            landing.HasOne(l => l.ReadWorkItem).WithMany().HasForeignKey(l => l.ReadWorkItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubscriptionCursor>(cursor =>
        {
            cursor.ToTable("subscription_cursors");
            cursor.HasKey(c => c.Id);

            // One position per kind: there is nowhere for a second to go.
            cursor.Property(c => c.Kind).HasMaxLength(64);
            cursor.HasIndex(c => c.Kind).IsUnique();
        });

        modelBuilder.Entity<SubscriptionRun>(run =>
        {
            run.ToTable("subscription_runs", t =>
            {
                t.HasCheckConstraint("ck_subscription_runs_handlers_json", "json_valid(handlers_json)");
                t.HasCheckConstraint("ck_subscription_runs_envelope_json", "envelope_json IS NULL OR json_valid(envelope_json)");
            });
            run.HasKey(r => r.Id);
            run.Property(r => r.PublicId).HasMaxLength(40);
            run.HasIndex(r => r.PublicId).IsUnique();
            run.Property(r => r.Kind).HasMaxLength(64);
            run.Property(r => r.LinePublicId).HasMaxLength(40);
            run.Property(r => r.Handlers).HasColumnName("handlers_json").IsRequired();
            run.Property(r => r.Envelope).HasColumnName("envelope_json");
            run.Property(r => r.FailedPluginId).HasMaxLength(64);
            run.Property(r => r.FailureClass).HasMaxLength(16);
            run.Property(r => r.FailureCode).HasMaxLength(64);

            // A kind's runs, newest last: the page a person reads and the prune at each insert.
            run.HasIndex(r => new { r.Kind, r.Id });

            // One chain in flight per kind, held where two writers cannot argue about it.
            run.HasIndex(r => r.Kind, "ix_subscription_runs_one_running_per_kind")
                .IsUnique()
                .HasFilter("status = 'running'");
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(SnakeCase.Convert(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase.Convert(property.GetColumnName()));

                // A stored value that cannot be read is refused by the column it came from, which a converter made
                // by convention for every property of its type cannot know until it is told.
                if (property.GetValueConverter() is IStoredColumnConverter stored)
                {
                    property.SetValueConverter(stored.ForColumn($"{entity.GetTableName()}.{property.GetColumnName()}"));
                }
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
