using Jason.Contracts.Api;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Persistence;

public sealed class JasonDbContext(DbContextOptions<JasonDbContext> options) : DbContext(options)
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();

    /// <summary>One connection string for the whole runtime: WAL is set by the migrator, busy timeout here.</summary>
    public static DbContextOptions<JasonDbContext> CreateOptions(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        return new DbContextOptionsBuilder<JasonDbContext>()
            .UseSqlite($"Data Source={databaseFile};Default Timeout=30")
            .Options;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Campaign>(campaign =>
        {
            campaign.HasKey(c => c.Id);
            campaign.Property(c => c.PublicId).HasMaxLength(40);
            campaign.HasIndex(c => c.PublicId).IsUnique();
            campaign.Property(c => c.Name).HasMaxLength(200);
            campaign.Property(c => c.Status)
                .HasMaxLength(32)
                .HasConversion(
                    status => SnakeCase.Convert(status.ToString()),
                    text => Enum.Parse<CampaignStatus>(text.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true));
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
