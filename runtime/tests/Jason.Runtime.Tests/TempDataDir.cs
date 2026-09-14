using Jason.Contracts.Discovery;

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
