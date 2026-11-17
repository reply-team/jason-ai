using System.Runtime.InteropServices;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The install directory <c>install.sh</c> is given on Linux, refused before anything is written where it cannot be
/// carried: a name holding the separator of PATH can be on no PATH at all, under the option that leaves PATH alone too,
/// and is said in the words the status check uses; a name the script's own entry cannot write, a character the profile
/// line reads inside double quotes, is the installer's own limit, refused only where that entry is written. On macOS and
/// Windows the scripts install Jason Desktop, take no install directory, and refuse one. Every run row is pointed at an empty feed, so a run that got past the refusal would stop at
/// the manifest that is not there, before the PATH is reached, and its temporary folder is inside the test's tree.
/// </summary>
public partial class InstallScriptTests
{
    /// <summary>The separator's sentence in each script is the one the status check and <c>session open</c> say.</summary>
    [Theory]
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
    [InlineData("install.sh", "TMP=\"$(mktemp -d)\"", "which separates the entries of PATH", "this installer cannot write")]
    public void Both_scripts_refuse_the_folder_before_they_write_anything(string script, string firstWrite, string separator, string ownLimit)
    {
        var code = Code(script);
        var written = code.IndexOf(firstWrite, StringComparison.Ordinal);

        Assert.True(written > 0, $"{script} no longer makes its temporary folder with {firstWrite}.");
        Assert.InRange(code.IndexOf(separator, StringComparison.Ordinal), 0, written);
        Assert.InRange(code.IndexOf(ownLimit, StringComparison.Ordinal), 0, written);
    }

    /// <summary>
    /// A space, an apostrophe and a letter outside ASCII are carried by the profile line's double quotes: not refused, with
    /// --no-modify-path and without it.
    /// </summary>
    [Theory]
    [InlineData(" ", false)]
    [InlineData("'", false)]
    [InlineData("é", false)]
    [InlineData(" ", true)]
    [InlineData("'", true)]
    [InlineData("é", true)]
    public void Install_sh_does_not_refuse_a_directory_its_profile_line_carries(string character, bool noModifyPath)
    {
        using var tree = new TempTree();
        var install = Path.Combine(tree.Root, "a" + character + "b", "bin");

        var (exit, _, stderr, _) = RunSh(tree, install, noModifyPath);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("cannot write", stderr, StringComparison.Ordinal);
        Assert.Contains("manifest.json", stderr, StringComparison.Ordinal);
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
    [InlineData("\"", true)]
    [InlineData("`", true)]
    [InlineData("\\", true)]
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
    /// <c>install.sh</c> with <paramref name="install"/> and an empty feed, its home and temporary folder inside the tree;
    /// run only where a release is built for the machine, which the script checks first.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr, string Temp) RunSh(TempTree tree, string install, bool noModifyPath)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "install.sh refuses a Windows shell by name; CI's Linux leg runs this row.");
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64,
            "install.sh takes an install directory on Linux on x64 alone: macOS installs Jason Desktop, and no other release is built.");

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
