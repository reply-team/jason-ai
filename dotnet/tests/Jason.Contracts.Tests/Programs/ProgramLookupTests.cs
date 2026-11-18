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

    /// <summary>
    /// The lookup never leaves the entry it walks. A name with a drive in it is no file name: joined to an entry it
    /// stays itself, and <c>C:tool</c> is the current folder of drive C, so the walk would answer with whatever stands
    /// there, whatever the search path says. It is not found, though a file of that name stands in the current folder.
    /// Windows alone has a drive; the win-x64 leg of CI runs this row.
    /// </summary>
    [Fact]
    public void A_drive_relative_name_is_not_found_though_a_file_of_that_name_stands_in_the_current_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows names a drive.");
        var current = Directory.GetCurrentDirectory();
        var name = "tool-" + Guid.NewGuid().ToString("N");
        var stray = Path.Combine(current, name + ".exe");
        File.WriteAllText(stray, "not a real program");
        try
        {
            var onPath = new ProgramSearchPath(_directory, ".COM;.EXE;.BAT;.CMD");
            var found = ProgramLookup.Find($"{current[0]}:{name}", onPath, onPath);

            Assert.Equal(ProgramLookupOutcome.NotFound, found.Outcome);
            Assert.Null(found.Path);
            Assert.Null(found.File);
        }
        finally
        {
            File.Delete(stray);
        }
    }

    /// <summary>
    /// Only an entry of the search path that is a full path is looked in. An empty entry, <c>.</c> and a relative entry
    /// each name the working folder, or a folder under it, wherever the process happens to stand; a program standing
    /// there is not found through them, though the same file is found by its folder's full path.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("relative")]
    public void An_entry_of_the_search_path_that_is_not_a_full_path_is_not_looked_in(string entry)
    {
        var current = Directory.GetCurrentDirectory();
        var folder = entry == "relative" ? Path.Combine(current, "lookup-" + Guid.NewGuid().ToString("N")) : current;
        var written = entry == "relative" ? Path.GetFileName(folder) : entry;
        var name = "tool-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        var decoy = Program(folder, name);
        try
        {
            var walked = new ProgramSearchPath(string.Join(Path.PathSeparator, written, _directory), PathExt);
            var found = ProgramLookup.Find(name, walked, walked);

            Assert.Equal(ProgramLookupOutcome.NotFound, found.Outcome);
            Assert.Null(found.Path);

            var byFullPath = new ProgramSearchPath(folder, PathExt);
            Assert.Equal(decoy, ProgramLookup.Find(name, byFullPath, byFullPath).Path, ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            File.Delete(decoy);
            if (folder != current)
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// A name is found in the entries of the search path alone, in their order: a program of the same name in the working
    /// folder, or in the folder of the program that looks, is never taken first.
    /// </summary>
    [Fact]
    public void A_program_in_the_working_folder_or_the_applications_folder_is_never_taken_before_the_search_path()
    {
        var name = "tool-" + Guid.NewGuid().ToString("N");
        var real = Program(_directory, name);
        var decoys = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            .Select(Path.GetFullPath)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Select(folder => Program(folder, name))
            .ToList();
        try
        {
            var onPath = new ProgramSearchPath(_directory, PathExt);
            Assert.Equal(real, ProgramLookup.Find(name, onPath, onPath).Path, ignoreCase: OperatingSystem.IsWindows());

            var elsewhere = new ProgramSearchPath(Path.Combine(_directory, "empty"), PathExt);
            Assert.Equal(ProgramLookupOutcome.NotFound, ProgramLookup.Find(name, elsewhere, elsewhere).Outcome);
        }
        finally
        {
            decoys.ForEach(File.Delete);
        }
    }

    private static string? PathExt => OperatingSystem.IsWindows() ? ".COM;.EXE;.BAT;.CMD" : null;

    /// <summary>A file the lookup takes for a program, in <paramref name="folder"/>; it is never started.</summary>
    private static string Program(string folder, string name)
    {
        var file = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(file, "not a real program");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return file;
    }
}
