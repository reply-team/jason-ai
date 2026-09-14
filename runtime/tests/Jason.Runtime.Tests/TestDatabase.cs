using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests;

/// <summary>A migrated, isolated SQLite database per test; open as many contexts on it as the test needs.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly TempDataDir _dir = new();

    public TestDatabase()
    {
        Directory.CreateDirectory(_dir.Paths.StateDirectory);
        Options = JasonDbContext.CreateOptions(_dir.Paths.DatabaseFile);
        using var db = new JasonDbContext(Options);
        db.Database.Migrate();
    }

    public DbContextOptions<JasonDbContext> Options { get; }

    public string File => _dir.Paths.DatabaseFile;

    public JasonDbContext Open() => new(Options);

    public void Dispose() => _dir.Dispose();
}

/// <summary>A clock the test controls.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}
