using Jason.Cli.Session;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// macOS's opener once it has started, with the platform's own shell standing in for <c>/usr/bin/open</c>: it is on every
/// machine, and it opens nothing. And the script Terminal runs, run by <c>/bin/sh</c> where there is one, with a shell in
/// place of the host, so what reaches the host is read back rather than inferred from the script's text.
/// </summary>
public sealed class MacOpenerTests
{
    /// <summary>
    /// An opener that refuses says why on its standard error and exits with a code other than zero; the problem carries
    /// the code and the first line it wrote, so the person reads the opener's own reason.
    /// </summary>
    [Fact]
    public void An_opener_that_exits_other_than_zero_gives_its_first_error_line_with_the_code()
    {
        string[] fake = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "echo The application cannot be opened. 1>&2&echo a second line 1>&2&exit /b 1"]
            : ["/bin/sh", "-c", "echo 'The application cannot be opened.' >&2; echo 'a second line' >&2; exit 1"];
        var start = TerminalStartInfo.For(TerminalPlatform.MacOs, fake, Path.GetTempPath());

        using var process = ChildProcess.Start(start);
        Assert.NotNull(process);

        var problem = MacOpener.Problem(process, "open", TimeSpan.FromSeconds(30));

        Assert.Equal("open exited with code 1: The application cannot be opened.", problem);
    }

    /// <summary>
    /// The whole script run by <c>/bin/sh</c> from a folder of its own, as Terminal runs it, with no <c>JASON_DATA_DIR</c>
    /// in the shell's environment: the host it replaces itself with prints the variable it was given, which is the data
    /// directory exactly as the CLI named it, a space, an apostrophe, a dollar sign and a line break included, or nothing
    /// where the CLI named none. Runs where <c>/bin/sh</c> is (Linux and macOS), skipped on Windows.
    /// </summary>
    [Theory]
    [InlineData("/tmp/jason test")]
    [InlineData("/tmp/marta's jason")]
    [InlineData("/tmp/$HOME/jason")]
    [InlineData("/tmp/jason\nrm -rf ~")]
    [InlineData("/tmp/a 'b' $(c) `d` \"e\"\n$f")]
    [InlineData(null)]
    public async Task The_shell_hands_the_host_the_data_directory_exactly(string? dataDirectory)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || !File.Exists("/bin/sh"), "The script is read by /bin/sh, which this machine does not have.");

        var folder = Directory.CreateTempSubdirectory("jason-script-folder-").FullName;
        var scriptFolder = Directory.CreateTempSubdirectory("jason-script-").FullName;
        try
        {
            var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;
            var launch = TerminalPlan.Compose(TerminalPlatform.MacOs, found, folder, ["/bin/sh", "-c", "printf '%s' \"$JASON_DATA_DIR\""], "apr_1", dataDirectory);
            var script = Path.Combine(scriptFolder, launch.ScriptName!);
            File.WriteAllText(script, launch.Script);

            var start = ChildProcess.StartInfo("/bin/sh", ChildKind.SpeaksToJason);
            start.ArgumentList.Add(script);
            start.WorkingDirectory = Path.GetTempPath();
            start.Environment.Remove(JasonPaths.DataDirectoryVariable);
            using var process = ChildProcess.Start(start);
            Assert.NotNull(process);
            process.StandardInput.Close();
            var printed = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var finished = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(finished.Token);

            Assert.Equal(string.Empty, await errors);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(dataDirectory ?? string.Empty, await printed);
            Assert.False(Directory.Exists(scriptFolder), "The script did not remove itself and its folder.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            if (Directory.Exists(scriptFolder))
            {
                Directory.Delete(scriptFolder, recursive: true);
            }
        }
    }
}
