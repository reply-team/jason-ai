using Jason.Runtime.Configuration;
using Jason.Runtime.Harness;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// The folder policy for a test that builds a service by hand: over a home the test owns, so every folder a create makes
/// is removed with the test's own directory, never one in the account's home.
/// </summary>
public static class TestHarness
{
    /// <summary>The folders, with the root at <paramref name="root"/> when given and at <c>Jason</c> in <paramref name="home"/> otherwise.</summary>
    public static HarnessFolders Folders(string home, string? root = null) =>
        new(Settings(new HarnessOptions { Root = root }), new HarnessHome(home));

    /// <summary>
    /// The folders for a test's database: the home is the one <see cref="TempDataDir.HomeOf"/> gives the database's data
    /// directory, which that directory's owner removes.
    /// </summary>
    public static HarnessFolders FoldersFor(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var file = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        var data = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(file)))!;
        return Folders(TempDataDir.HomeOf(data));
    }

    /// <summary>The root <see cref="FoldersFor"/> makes its folders under.</summary>
    public static string RootFor(JasonDbContext db) => FoldersFor(db).Root;

    /// <summary>The harness seam over options the test holds.</summary>
    public static LiveSettings<HarnessOptions> Settings(HarnessOptions options) =>
        new(new TestOptionsMonitor<HarnessOptions>(options), NullLogger<LiveSettings<HarnessOptions>>.Instance, HarnessOptions.Section);
}
