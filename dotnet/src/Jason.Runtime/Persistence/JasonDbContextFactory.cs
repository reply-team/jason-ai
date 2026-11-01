using Microsoft.EntityFrameworkCore.Design;

namespace Jason.Runtime.Persistence;

/// <summary>Used only by <c>dotnet ef</c> when scaffolding migrations; never at runtime.</summary>
public sealed class JasonDbContextFactory : IDesignTimeDbContextFactory<JasonDbContext>
{
    public JasonDbContext CreateDbContext(string[] args) => new(JasonDbContext.CreateOptions("design-time.db"));
}
