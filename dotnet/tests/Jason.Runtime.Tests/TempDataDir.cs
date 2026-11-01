using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests;

/// <summary>An isolated data directory per test, and a home of its own beside it. Never points at the real ~/.jason or ~/Jason.</summary>
public sealed class TempDataDir : IDisposable
{
    public TempDataDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Paths = new JasonPaths(root);
    }

    public JasonPaths Paths { get; }

    /// <summary>
    /// The home directory a runtime on this data directory is given (<see cref="HomeOf"/>), so the harness root its first
    /// create makes lands in a folder this test owns and removes.
    /// </summary>
    public string Home => HomeOf(Paths.Root);

    /// <summary>
    /// The home a test runtime on <paramref name="dataDirectory"/> is given: beside it, never inside it, since the harness
    /// root is made in the home and may never be in the data directory. One rule for the runtimes started in the test
    /// process and the ones a test spawns, so a test that removes one removes the other.
    /// </summary>
    public static string HomeOf(string dataDirectory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)) + "-home";

    /// <summary>Removes the home <see cref="HomeOf"/> gives a data directory, when a runtime made one.</summary>
    public static void RemoveHome(string dataDirectory) => TryDelete(HomeOf(dataDirectory));

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

        TryDelete(Paths.Root);
        TryDelete(Home);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
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
