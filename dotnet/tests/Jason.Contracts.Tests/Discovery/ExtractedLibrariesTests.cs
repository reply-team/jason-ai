using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// Where a single-file build unpacks the native libraries it carries. The running build's own folder is the one the .NET
/// host names among the folders it searched, <c>&lt;base&gt;/&lt;program&gt;/&lt;bundle id&gt;</c>, and never the
/// folder the executable is in; where nothing names it, the base is the one the host is told to use, or by rule the
/// temporary folder's <c>.net</c> on Windows and the home's <c>.net</c> everywhere else.
/// </summary>
public class ExtractedLibrariesTests
{
    [Fact]
    public void Outside_windows_a_build_unpacks_under_the_home_and_never_the_temporary_folder()
    {
        Assert.Equal("/home/someone/.net", ExtractedLibraries.DefaultBase(null, windows: false, "/home/someone", "/tmp/"));
        Assert.Equal("/Users/someone/.net", ExtractedLibraries.DefaultBase("", windows: false, "/Users/someone/", "/var/folders/x/T/"));
    }

    [Fact]
    public void On_windows_a_build_unpacks_under_the_temporary_folder()
    {
        Assert.Equal(
            @"C:\Users\someone\AppData\Local\Temp\.net",
            ExtractedLibraries.DefaultBase(null, windows: true, @"C:\Users\someone", @"C:\Users\someone\AppData\Local\Temp\"));
    }

    [Fact]
    public void A_base_the_host_is_told_to_use_is_the_base_on_every_system()
    {
        Assert.Equal("/srv/unpacked", ExtractedLibraries.DefaultBase("/srv/unpacked", windows: false, "/home/someone", "/tmp"));
        Assert.Equal(@"D:\unpacked", ExtractedLibraries.DefaultBase(@"D:\unpacked", windows: true, @"C:\Users\someone", @"C:\Temp"));
    }

    [Fact]
    public void The_builds_own_folder_is_the_one_named_for_its_program_among_those_the_host_searched()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-extracted");
        var installed = Path.Combine(root, "bin");
        var executable = Path.Combine(installed, "jason");
        var own = Path.Combine(root, ".net", "jason", "a1b2c3");
        var another = Path.Combine(root, ".net", "other", "d4e5f6");
        var searched = string.Join(Path.PathSeparator, installed, another, own + Path.DirectorySeparatorChar);

        Assert.Equal([own], ExtractedLibraries.Candidates(searched, executable));
    }

    [Fact]
    public void A_build_whose_host_searched_only_its_own_folder_unpacked_nothing()
    {
        var installed = Path.Combine(Path.GetTempPath(), "jason-extracted", "bin");

        Assert.Empty(ExtractedLibraries.Candidates(installed, Path.Combine(installed, "jason")));
        Assert.Empty(ExtractedLibraries.Candidates("", Path.Combine(installed, "jason")));
    }

    /// <summary>This test process is no single-file build, so it names no folder of its own rather than a guess.</summary>
    [Fact]
    public void A_process_that_is_no_single_file_build_names_no_folder_of_its_own()
    {
        Assert.False(SelfExecutable.IsSingleFile);
        Assert.Null(ExtractedLibraries.OfThisProcess());
    }
}
