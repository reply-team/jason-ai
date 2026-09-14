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

    public DbSet<JournalEntry> Journal => Set<JournalEntry>();

    /// <summary>One place for the SQLite options and interceptors; used by CreateOptions, the migrator and the DI registration.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string databaseFile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        return builder
            .UseSqlite($"Data Source={databaseFile};Default Timeout=30")
            .AddInterceptors(new AppendOnlyJournalInterceptor());
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
        configurationBuilder.Properties<JsonObject>().HaveConversion<JsonObjectConverter, JsonObjectComparer>();
        configurationBuilder.Properties<JsonNode>().HaveConversion<JsonNodeConverter, JsonNodeComparer>();
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
            entry.HasIndex(e => new { e.CampaignId, e.PublicId });
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
