using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// What <c>install.sh</c>'s PATH step writes, and what it tells the person to do next. A desktop's terminal starts a shell
/// that is not a login shell: GNOME Terminal's bash reads <c>~/.bashrc</c> and never <c>~/.profile</c>, which the desktop
/// reads once, at sign-in. With the line in the login profile alone, every new terminal after an install answered that
/// there was no <c>jason</c> until the account signed out and in, while the installer's last words were "open a new
/// terminal". So the line goes into the file an interactive shell reads as well, where the shell has one, and where it has
/// none the sentence says to sign out and in.
/// </summary>
public sealed partial class InstallNoticesTests
{
    /// <summary>For bash and zsh the line goes into the login profile and into the file every interactive shell reads.</summary>
    [Theory]
    [InlineData("/bin/bash", ".profile", ".bashrc")]
    [InlineData("/usr/bin/zsh", ".zprofile", ".zshrc")]
    public void Install_sh_puts_the_line_where_a_terminal_without_a_login_reads_it(string shell, string login, string interactive)
    {
        var installer = Installer.For(Sh, _tree);
        installer.Shell = shell;

        var run = installer.Run(installer.Feed("0.6.0"), modifyPath: true);

        Assert.True(run.Exit == 0, run.Stderr);
        var line = PathEntry.ExportLine(installer.InstallDir);
        foreach (var file in (string[])[login, interactive])
        {
            Assert.Equal($"\n{PathEntry.Marker}\n{line}\n", File.ReadAllText(Path.Combine(installer.Home, file)));
        }

        Assert.Contains(
            $"Added {installer.InstallDir} to the PATH in {Path.Combine(installer.Home, login)} and {Path.Combine(installer.Home, interactive)}; open a new terminal, or run: {line}",
            Lines(run.Stdout));
    }

    /// <summary>Run again, it finds the line in both files and writes neither, and says nothing about the PATH.</summary>
    [Fact]
    public void Install_sh_run_again_adds_no_second_line_to_either_file()
    {
        var installer = Installer.For(Sh, _tree);
        installer.Shell = "/bin/bash";
        var feed = installer.Feed("0.6.0");
        Assert.Equal(0, installer.Run(feed, modifyPath: true).Exit);
        var bashrc = Path.Combine(installer.Home, ".bashrc");
        var profile = Path.Combine(installer.Home, ".profile");
        var written = (File.ReadAllText(profile), File.ReadAllText(bashrc));

        var again = installer.Run(feed, modifyPath: true);

        Assert.True(again.Exit == 0, again.Stderr);
        Assert.Equal(written, (File.ReadAllText(profile), File.ReadAllText(bashrc)));
        Assert.DoesNotContain(Lines(again.Stdout), said => said.StartsWith("Added ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Where the login profile already carries the line, as an install by an earlier release left it, the run adds it to
    /// <c>~/.bashrc</c> alone and says so.
    /// </summary>
    [Fact]
    public void Install_sh_over_an_earlier_install_adds_the_line_to_the_interactive_file_alone()
    {
        var installer = Installer.For(Sh, _tree);
        installer.Shell = "/bin/bash";
        var line = PathEntry.ExportLine(installer.InstallDir);
        var profile = Path.Combine(installer.Home, ".profile");
        File.WriteAllText(profile, $"# mine\n\n{PathEntry.Marker}\n{line}\n");

        var run = installer.Run(installer.Feed("0.6.0"), modifyPath: true);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal($"# mine\n\n{PathEntry.Marker}\n{line}\n", File.ReadAllText(profile));
        Assert.Equal($"\n{PathEntry.Marker}\n{line}\n", File.ReadAllText(Path.Combine(installer.Home, ".bashrc")));
        Assert.Contains($"Added {installer.InstallDir} to the PATH in {Path.Combine(installer.Home, ".bashrc")}; open a new terminal, or run: {line}", Lines(run.Stdout));
    }

    /// <summary>An interactive bash that is not a login shell, which is what a desktop's terminal starts, finds <c>jason</c>.</summary>
    [Fact]
    public void A_new_terminal_without_a_login_finds_jason_after_the_install()
    {
        var installer = Installer.For(Sh, _tree);
        Assert.SkipUnless(File.Exists("/bin/bash"), "This machine has no /bin/bash to start as a terminal would.");
        installer.Shell = "/bin/bash";
        Assert.Equal(0, installer.Run(installer.Feed("0.6.0"), modifyPath: true).Exit);

        var found = RunToEnd("/bin/bash", new Dictionary<string, string> { ["HOME"] = installer.Home }, ["-i", "-c", "command -v jason"]);

        Assert.Equal(installer.Target, Lines(found.Stdout).LastOrDefault());
    }

    /// <summary>
    /// Another shell has no such file the installer can name, so the line is in the login profile alone, which a desktop
    /// reads at sign-in: the sentence says to sign out and back in, and never to open a new terminal.
    /// </summary>
    [Fact]
    public void Install_sh_says_to_sign_out_and_in_where_the_shell_has_no_interactive_file()
    {
        var installer = Installer.For(Sh, _tree);
        installer.Shell = "/bin/sh";

        var run = installer.Run(installer.Feed("0.6.0"), modifyPath: true);

        Assert.True(run.Exit == 0, run.Stderr);
        var line = PathEntry.ExportLine(installer.InstallDir);
        Assert.Equal($"\n{PathEntry.Marker}\n{line}\n", File.ReadAllText(Path.Combine(installer.Home, ".profile")));
        Assert.False(File.Exists(Path.Combine(installer.Home, ".bashrc")));
        Assert.False(File.Exists(Path.Combine(installer.Home, ".zshrc")));
        Assert.Contains(
            $"Added {installer.InstallDir} to the PATH in {Path.Combine(installer.Home, ".profile")}; sign out and back in for a new terminal to find it, or run: {line}",
            Lines(run.Stdout));
    }
}
