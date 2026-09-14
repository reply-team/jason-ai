namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// A directory of programs a test invents, to be handed to a <see cref="TestSearchPath"/>. Resolving a declared
/// executable must never depend on what the machine running the tests happens to have installed, and a test
/// about a file that cannot be started has to be able to create exactly that file.
/// </summary>
public sealed class TestPrograms : IDisposable
{
    public TestPrograms()
    {
        Root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>A program the resolver accepts: an <c>.exe</c> on Windows, a file with the execute bit elsewhere.</summary>
    public string AddProgram(string name) => Add(name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty), executable: true);

    /// <summary>A file with exactly this name, executable or not — for the cases a resolver must refuse.</summary>
    public string Add(string fileName, bool executable = false)
    {
        var file = Path.Combine(Root, fileName);
        File.WriteAllText(file, "not a real program");
        if (executable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return file;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
