using Jason.Cli.Uninstall;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// This machine's <see cref="IInstallationRemover.RunningFrom"/>, asked about a real process: the look the uninstall makes
/// for an open Jason Desktop before it removes the application.
/// </summary>
public sealed class MachineRemoverRunningFromTests : IDisposable
{
    private readonly TempPaths _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// On macOS a <c>jason</c> typed in Terminal is in the terminal's POSIX session, and Jason.app, started by the system,
    /// never is; .NET's <c>Process.SessionId</c> there is that POSIX session. So the application is stood in for by a process
    /// in a session of its own (<c>setsid</c>), running an image named and placed as the application's executable, and it
    /// has to be found.
    /// </summary>
    [Fact]
    public void On_macOS_the_application_running_in_another_session_is_found()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Process.SessionId is the POSIX session on macOS; CI's macOS leg runs this row.");

        var folder = Path.Combine(_dir.Paths.Root, "Applications", "Jason.app", "Contents", "MacOS");
        Directory.CreateDirectory(folder);
        var image = Path.Combine(folder, "Jason");
        File.Copy("/bin/sleep", image);

        // Perl, which macOS carries, makes the child a session of its own and then becomes the image, keeping its id.
        var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/perl")
        {
            ArgumentList = { "-MPOSIX", "-e", "POSIX::setsid() or die; exec { $ARGV[0] } @ARGV or die", image, "120" },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var remover = InstallationRemovers.ForThisMachine();
            IReadOnlyList<int> found = [];
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < until && !found.Contains(child.Id))
            {
                Thread.Sleep(100);
                found = remover.RunningFrom(image);
            }

            using (var seen = System.Diagnostics.Process.GetProcessById(child.Id))
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
            {
                Assert.NotEqual(self.SessionId, seen.SessionId);
            }

            Assert.Contains(child.Id, found);
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }
}
