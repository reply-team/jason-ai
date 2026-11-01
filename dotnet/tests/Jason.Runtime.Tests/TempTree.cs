namespace Jason.Runtime.Tests;

/// <summary>
/// A temporary directory tree one test owns and one test deletes. For the tests that need files rather than a
/// data directory — a package to read, a program to resolve — where <see cref="TempDataDir"/> would be a
/// database nobody opens.
/// </summary>
/// <remarks>
/// It exists because a helper that creates a directory per call and deletes none leaves one behind per call,
/// and nobody notices until a machine is holding tens of thousands of them: this suite had left 51,626 under
/// <c>%TEMP%\jason-tests</c>, 108 of them from a single run of the manifest tests. One tree per test, one
/// delete per tree.
/// </remarks>
public sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The tree's own root, which is what <see cref="Dispose"/> removes.</summary>
    public string Root { get; }

    /// <summary>
    /// A new directory with this name inside the tree. The name is kept — a package directory's name is the
    /// plugin's id and a test may be about exactly that — so each one gets a parent of its own and two calls
    /// with the same name do not collide.
    /// </summary>
    public string NewDirectory(string name)
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A child process or the OS may still hold a handle; leaving a temp directory behind is harmless,
            // which is the same thing TempDataDir says for the same reason.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
