using Jason.Contracts.Programs;

namespace Jason.Contracts.Tests.Programs;

/// <summary>
/// The lookup's own answer, as every caller reads it. Its readings of npm's shim are held by the callers' tests; what is
/// held here is what those callers tell apart by: a program found as itself is the file it was found as.
/// </summary>
public class ProgramLookupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public ProgramLookupTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// A program found by its name is started as the file the name came to, so the two are one path; only npm's shim
    /// sets them apart, and the status check says "npm's shim, which starts" on exactly that difference.
    /// </summary>
    [Fact]
    public void A_program_found_by_name_is_the_file_it_was_found_as()
    {
        var file = Path.Combine(_directory, OperatingSystem.IsWindows() ? "tool.exe" : "tool");
        File.WriteAllText(file, "not a real program");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var onPath = new ProgramSearchPath(_directory, OperatingSystem.IsWindows() ? ".COM;.EXE;.BAT;.CMD" : null);
        var found = ProgramLookup.Find("tool", onPath, onPath);

        Assert.Equal(ProgramLookupOutcome.Program, found.Outcome);
        Assert.Equal(found.File, found.Path);
        Assert.Equal(file, found.Path, ignoreCase: OperatingSystem.IsWindows());
        Assert.Empty(found.Launch);
    }
}
