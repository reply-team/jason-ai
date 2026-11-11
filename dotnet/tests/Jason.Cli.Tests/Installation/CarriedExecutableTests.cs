using Jason.Cli.Installation;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// A jason the desktop application carries is known by the record its package holds beside it, <c>version.json</c>, and
/// never by a path: the file the desktop's preparation writes, in the shape every desktop package already holds.
/// </summary>
public sealed class CarriedExecutableTests : IDisposable
{
    private readonly TempPaths _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void An_executable_with_the_record_beside_it_is_carried_and_says_the_version_the_package_was_prepared_as()
    {
        var executable = Executable("bundle");
        CarriedRecord.Beside(executable);

        var carried = CarriedExecutable.Of(executable);

        Assert.NotNull(carried);
        Assert.Equal(Path.GetFullPath(executable), carried.Executable);
        Assert.Equal("0.34.0", carried.Version);
    }

    /// <summary>A candidate's package carries a candidate's version, and that is a record too.</summary>
    [Fact]
    public void A_candidates_record_is_a_record()
    {
        var executable = Executable("bundle");
        CarriedRecord.Beside(executable, "{\"version\":\"0.35.0-rc.1\"}");

        Assert.Equal("0.35.0-rc.1", CarriedExecutable.Of(executable)?.Version);
    }

    /// <summary>
    /// Where the executable is installed says nothing: the same folder names, without the record, are an installation of
    /// Jason's own, and a folder named like no package at all, with it, is carried.
    /// </summary>
    [Fact]
    public void The_path_says_nothing_and_the_record_decides()
    {
        var bundleWithout = Executable(Path.Combine("Jason.app", "Contents", "Resources", "bundle"));
        var anywhereWith = Executable("anywhere");
        CarriedRecord.Beside(anywhereWith);

        Assert.Null(CarriedExecutable.Of(bundleWithout));
        Assert.NotNull(CarriedExecutable.Of(anywhereWith));
    }

    /// <summary>
    /// A file of that name that is not the record is somebody else's: not JSON, not an object, no version, a version that
    /// is not one. The executable beside it is Jason's own and its verbs act as they always have.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[\"0.34.0\"]")]
    [InlineData("{}")]
    [InlineData("{\"version\":34}")]
    [InlineData("{\"version\":\"thirty-four\"}")]
    [InlineData("{\"name\":\"some-node-package\",\"version_hint\":\"0.34.0\"}")]
    public void A_file_of_that_name_that_is_not_the_record_carries_nothing(string text)
    {
        var executable = Executable("bundle");
        CarriedRecord.Beside(executable, text);

        Assert.Null(CarriedExecutable.Of(executable));
    }

    /// <summary>A folder in the record's place is no record.</summary>
    [Fact]
    public void A_folder_in_the_records_place_carries_nothing()
    {
        var executable = Executable("bundle");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(executable)!, CarriedExecutable.RecordName));

        Assert.Null(CarriedExecutable.Of(executable));
    }

    /// <summary>The environment's executable is the one asked about, as every verb that asks names it.</summary>
    [Fact]
    public void The_environment_is_asked_about_the_file_it_names()
    {
        var executable = Executable("bundle");
        var env = new CliEnvironment(TextWriter.Null, TextWriter.Null, _dir.Paths, InstallPath: executable);

        Assert.Null(CarriedExecutable.Of(env));
        CarriedRecord.Beside(executable);
        Assert.Equal(Path.GetFullPath(executable), CarriedExecutable.Of(env)?.Executable);
    }

    private string Executable(string folder)
    {
        var directory = Path.Combine(_dir.Paths.Root, "install", folder);
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "jason.exe" : "jason");
        File.WriteAllText(executable, "jason 0.34.0");
        return executable;
    }
}
