using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests;

/// <summary>An isolated data directory per test. Never points at the real ~/.jason.</summary>
public sealed class TempDataDir : IDisposable
{
    public TempDataDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Paths = new JasonPaths(root);
    }

    public JasonPaths Paths { get; }

    public void Dispose()
    {
        // The provider pools connections per connection string and only closes idle ones when the process
        // exits. Hundreds of test databases left pooled make that exit take seconds — long enough for the test
        // runner's watchdog to give up on a slow machine — and keep the files open, so the directory could not
        // be removed either. Releasing this database's pool here closes its connections now.
        using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(Paths.DatabaseFile)))
        {
            SqliteConnection.ClearPool(connection);
        }

        try
        {
            Directory.Delete(Paths.Root, recursive: true);
        }
        catch (IOException)
        {
            // A child process or the OS may still hold a handle; leaving a temp directory behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
