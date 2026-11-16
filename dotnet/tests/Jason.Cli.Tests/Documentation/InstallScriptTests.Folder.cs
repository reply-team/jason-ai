using System.Runtime.InteropServices;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The install directory each script is given, refused before anything is written where it cannot be carried: a name
/// holding the separator of PATH can be on no PATH at all, under the option that leaves PATH alone too, and is said in the
/// words the status check uses; a name the script's own entry cannot write, a <c>%NAME%</c> Windows would read in the
/// user's Path or a character the profile line reads inside double quotes, is the installer's own limit, refused only
/// where that entry is written. Every run row is pointed at an empty feed, so a run that got past the refusal would stop at
/// the manifest that is not there, before the PATH is reached, and its temporary folder is inside the test's tree.
/// </summary>
public partial class InstallScriptTests
{
    /// <summary>The separator's sentence in each script is the one the status check and <c>session open</c> say.</summary>
    [Theory]
    [InlineData("install.ps1", ';')]
    [InlineData("install.sh", ':')]
    public void Each_script_says_the_separators_refusal_in_the_shared_words(string script, char separator)
    {
        // The predicate names the folder first; what follows it is the clause the scripts say after theirs.
        var folder = "x" + separator;
        var clause = SearchPathEntry.Problem(folder, separator)![folder.Length..].TrimStart();

        Assert.Contains(clause, Code(script), StringComparison.Ordinal);
    }

    /// <summary>Read, so it holds on every system: each refusal comes before the temporary folder, the first thing written.</summary>
    [Theory]
    [InlineData("install.ps1", "New-Item -ItemType Directory -Path $tmp", "which separates the entries of PATH", "This installer cannot write")]
    [InlineData("install.sh", "TMP=\"$(mktemp -d)\"", "which separates the entries of PATH", "this installer cannot write")]
    public void Both_scripts_refuse_the_folder_before_they_write_anything(string script, string firstWrite, string separator, string ownLimit)
    {
        var code = Code(script);
        var written = code.IndexOf(firstWrite, StringComparison.Ordinal);

        Assert.True(written > 0, $"{script} no longer makes its temporary folder with {firstWrite}.");
        Assert.InRange(code.IndexOf(separator, StringComparison.Ordinal), 0, written);
        Assert.InRange(code.IndexOf(ownLimit, StringComparison.Ordinal), 0, written);
    }

    /// <summary>A directory holding <c>;</c> is refused, with -NoModifyPath and without it, before a temporary folder or the directory exists.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Install_ps1_refuses_a_directory_holding_the_separator_of_path(bool noModifyPath)
    {
        using var tree = new TempTree();
        var install = Path.Combine(tree.Root, "a;b", "jason");

        var (exit, _, stderr, temp) = RunPs1(tree, install, noModifyPath);

        Assert.NotEqual(0, exit);
        Assert.Contains($"The install directory {install} holds ';', which separates the entries of PATH, so no PATH entry can name it; nothing was installed", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.json", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(install));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    /// <summary>
    /// A directory whose name holds a <c>%NAME%</c> of a variable would be written into the user's Path, which Windows
    /// expands, as another folder: refused where the script writes that entry, naming both ways on, and not under
    /// -NoModifyPath, where it writes none.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Install_ps1_refuses_a_directory_windows_would_read_as_another_only_where_it_writes_the_path(bool noModifyPath)
    {
        using var tree = new TempTree();
        var install = Path.Combine(tree.Root, "%JASON_TEST_DEFINED%", "jason");

        var (exit, _, stderr, temp) = RunPs1(tree, install, noModifyPath);

        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(install));
        if (noModifyPath)
        {
            Assert.DoesNotContain("cannot write", stderr, StringComparison.Ordinal);
            Assert.Contains("manifest.json", stderr, StringComparison.Ordinal);
            return;
        }

        Assert.Contains($"This installer cannot write {install} into your PATH:", stderr, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(tree.Root, "elsewhere", "jason"), stderr, StringComparison.Ordinal);
        Assert.Contains("choose another -InstallDir, or install with -NoModifyPath and add the directory to your PATH yourself", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.json", stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    /// <summary>A directory holding <c>:</c> is refused, with --no-modify-path and without it, before a temporary folder or the directory exists.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Install_sh_refuses_a_directory_holding_the_separator_of_path(bool noModifyPath)
    {
        using var tree = new TempTree();
        var install = Path.Combine(tree.Root, "a:b", "bin");

        var (exit, _, stderr, temp) = RunSh(tree, install, noModifyPath);

        Assert.NotEqual(0, exit);
        Assert.Contains($"install.sh: the install directory {install} holds ':', which separates the entries of PATH, so no PATH entry can name it; nothing was installed", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.json", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(install));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    /// <summary>
    /// A character the profile line reads inside double quotes is refused where the script writes that line, naming both
    /// ways on, and not under --no-modify-path, where it writes none.
    /// </summary>
    [Theory]
    [InlineData("$", false)]
    [InlineData("\"", false)]
    [InlineData("`", false)]
    [InlineData("\\", false)]
    [InlineData("\n", false)]
    [InlineData("$", true)]
    [InlineData("\n", true)]
    public void Install_sh_refuses_a_directory_its_profile_line_cannot_write_only_where_it_writes_it(string character, bool noModifyPath)
    {
        using var tree = new TempTree();
        var install = Path.Combine(tree.Root, "a" + character + "b", "bin");

        var (exit, _, stderr, temp) = RunSh(tree, install, noModifyPath);

        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(install));
        if (noModifyPath)
        {
            Assert.DoesNotContain("cannot write", stderr, StringComparison.Ordinal);
            Assert.Contains("manifest.json", stderr, StringComparison.Ordinal);
            return;
        }

        Assert.Contains("install.sh: this installer cannot write ", stderr, StringComparison.Ordinal);
        Assert.Contains("choose another --install-dir, or install with --no-modify-path and add the directory to your PATH yourself", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.json", stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    /// <summary>
    /// <c>install.ps1</c> with <paramref name="install"/> and an empty feed, its temporary folder and the folder of its
    /// default directory inside the tree, and one variable defined for a row's <c>%NAME%</c>. The temporary folder is
    /// returned to be read.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr, string Temp) RunPs1(TempTree tree, string install, bool noModifyPath)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 refuses a PowerShell that is not on Windows, so its rules can only be run on Windows.");

        // The refusal's own words, on one line: PowerShell's error view wraps a long message to the console's width and
        // colours it, so the message is written by a catch of the row's own rather than by that view.
        var temp = tree.NewDirectory("temp");
        var script = Path.Combine(RepositoryRoot(), "install", "install.ps1");
        var call = $"& {Quoted(script)} -InstallDir {Quoted(install)} -Feed {Quoted(tree.NewDirectory("feed"))}{(noModifyPath ? " -NoModifyPath" : string.Empty)}";
        var (exit, stdout, stderr) = Pwsh(
            new Dictionary<string, string> { ["LOCALAPPDATA"] = tree.Root, ["TEMP"] = temp, ["TMP"] = temp, ["JASON_TEST_DEFINED"] = "elsewhere" },
            "-ExecutionPolicy", "Bypass", "-Command", $"try {{ {call} }} catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}");
        return (exit, stdout, stderr, temp);
    }

    /// <summary>
    /// <c>install.sh</c> with <paramref name="install"/> and an empty feed, its home and temporary folder inside the tree;
    /// run only where a release is built for the machine, which the script checks first.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr, string Temp) RunSh(TempTree tree, string install, bool noModifyPath)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "install.sh refuses a Windows shell by name; CI's Linux and macOS legs run this row.");
        Assert.SkipUnless(
            (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
            || (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64),
            "No release is built for this machine, and install.sh refuses it by name.");

        var temp = tree.NewDirectory("temp");
        string[] arguments =
        [
            Path.Combine(RepositoryRoot(), "install", "install.sh"),
            "--feed", tree.NewDirectory("feed"), "--install-dir", install,
            .. noModifyPath ? new[] { "--no-modify-path" } : [],
        ];
        var (exit, stdout, stderr) = Run(
            "/bin/sh",
            new Dictionary<string, string> { ["HOME"] = tree.NewDirectory("home"), ["TMPDIR"] = temp, ["SHELL"] = "/bin/sh" },
            arguments);
        return (exit, stdout, stderr, temp);
    }
}
