using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// The one door every child process comes out of. What it sets is the whole of its job, and
/// <c>NoChildGetsAWindowTests</c> in the application's tests holds the rest of the product to it.
/// </summary>
public class ChildProcessTests
{
    [Fact]
    [Critical(Family.B1, "the door's two settings, observed on a real child")]
    public void A_child_is_started_with_no_window_and_without_the_shell()
    {
        var start = ChildProcess.StartInfo(FullPath);

        Assert.Equal(FullPath, start.FileName);
        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
        Assert.Equal((false, false, false), (start.RedirectStandardInput, start.RedirectStandardOutput, start.RedirectStandardError));
    }

    /// <summary>
    /// A child that speaks to Jason has all three streams, each UTF-8 without a byte-order mark: written at the start of
    /// a pipe, a preamble is three bytes a node program's JSON.parse refuses.
    /// </summary>
    [Fact]
    [Critical(Family.F4, "stdin encoding of a Jason child")]
    public void A_child_that_speaks_to_jason_is_spoken_to_in_utf_8_without_a_byte_order_mark()
    {
        var start = ChildProcess.StartInfo(FullPath, ChildKind.SpeaksToJason);

        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
        Assert.Equal((true, true, true), (start.RedirectStandardInput, start.RedirectStandardOutput, start.RedirectStandardError));
        foreach (var encoding in new[] { start.StandardInputEncoding, start.StandardOutputEncoding, start.StandardErrorEncoding })
        {
            Assert.IsType<UTF8Encoding>(encoding);
            Assert.True(encoding!.GetPreamble().Length == 0, $"The child's stream is {encoding.EncodingName} with a {encoding.GetPreamble().Length}-byte preamble.");
        }
    }

    /// <summary>
    /// A system tool writes in the console's code page, so its output is read in that page: no encoding is chosen,
    /// which is how .NET reads it. Its input is this process's own unless the caller asks for one of its own to close.
    /// </summary>
    [Theory]
    [Critical(Family.F4, "a system tool's output decoding")]
    [InlineData(ChildKind.SystemTool, false)]
    [InlineData(ChildKind.SystemToolWithInput, true)]
    public void A_system_tool_is_read_in_the_console_s_code_page(ChildKind kind, bool input)
    {
        var start = ChildProcess.StartInfo(FullPath, kind);

        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
        Assert.Equal((input, true, true), (start.RedirectStandardInput, start.RedirectStandardOutput, start.RedirectStandardError));
        Assert.Equal<Encoding?>([null, null, null], [start.StandardInputEncoding, start.StandardOutputEncoding, start.StandardErrorEncoding]);
    }

    /// <summary>A program named by its full path on whichever system runs the row.</summary>
    private static readonly string FullPath = Path.Combine(Path.GetTempPath(), "program");

    /// <summary>
    /// Every child is started by its full path, so the door refuses a name that is not one through either of its ways in
    /// and under every kind: a bare name, a relative path, nothing at all. A name the operating system looked up would be
    /// looked up in places no caller chose, the working folder and on Windows the folder of the program that starts it.
    /// </summary>
    [Theory]
    [InlineData(ChildKind.SystemTool, "schtasks")]
    [InlineData(ChildKind.SystemTool, "launchctl")]
    [InlineData(ChildKind.SystemToolWithInput, "powershell.exe")]
    [InlineData(ChildKind.SystemToolWithInput, "bin/tool")]
    [InlineData(ChildKind.SystemTool, "")]
    [InlineData(ChildKind.SpeaksToJason, "dotnet")]
    [InlineData(ChildKind.SpeaksToJason, "./agent")]
    [InlineData(null, "rundll32")]
    [InlineData(null, "xdg-open")]
    [InlineData(null, "bin/tool")]
    [InlineData(null, "")]
    public void A_child_named_by_anything_but_its_full_path_is_refused(ChildKind? kind, string program)
    {
        var refused = Assert.ThrowsAny<ArgumentException>(() => kind is { } known ? ChildProcess.StartInfo(program, known) : ChildProcess.StartInfo(program));

        Assert.Equal("fileName", refused.ParamName);
    }

    /// <summary>
    /// On Windows a path that names no drive is no full path: one from the top of the current drive, which names whatever
    /// drive the process stands on, and one relative to a drive's current folder are refused like a bare name; a path with
    /// its drive is taken.
    /// </summary>
    [Fact]
    public void On_windows_a_path_that_names_no_drive_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drives are Windows' own.");

        foreach (var program in new[] { @"\Windows\System32\schtasks.exe", "/usr/bin/x-terminal-emulator", "C:schtasks.exe" })
        {
            Assert.Equal("fileName", Assert.ThrowsAny<ArgumentException>(() => ChildProcess.StartInfo(program)).ParamName);
            foreach (var kind in Enum.GetValues<ChildKind>())
            {
                Assert.Equal("fileName", Assert.ThrowsAny<ArgumentException>(() => ChildProcess.StartInfo(program, kind)).ParamName);
            }
        }

        var schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        Assert.Equal(schtasks, ChildProcess.StartInfo(schtasks, ChildKind.SystemTool).FileName);
    }

    /// <summary>A full path is started as it was given, through either way in and under every kind.</summary>
    [Fact]
    public void A_full_path_is_started_as_it_was_given()
    {
        Assert.Equal(FullPath, ChildProcess.StartInfo(FullPath).FileName);
        foreach (var kind in Enum.GetValues<ChildKind>())
        {
            Assert.Equal(FullPath, ChildProcess.StartInfo(FullPath, kind).FileName);
        }
    }

    [Fact]
    public void A_kind_the_door_does_not_know_is_refused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => ChildProcess.StartInfo(FullPath, (ChildKind)99));

        Assert.Equal("kind", refused.ParamName);
    }

    /// <summary>
    /// What a start is made under on Windows: the loader's error boxes off on the starting thread, on that thread
    /// alone and for the start alone, with whatever mode the thread already had kept. A file named like a program that
    /// is no program image otherwise makes the starting process put a modal "Unsupported 16-Bit Application" box on
    /// the desktop. No test can see that box headless, and a process whose parent's own mode already has the flag
    /// would never show it, so this holds the mode itself, read back from Windows, from a thread set to a known mode
    /// first so that what the start adds is all that is read.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(SemNoGpFaultErrorBox)]
    public void On_Windows_a_start_is_made_with_the_loaders_error_boxes_off_on_the_starting_thread_alone(uint threadHad)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The error mode is a Windows setting; elsewhere a file that is no program is an error code and never a box.");
        Assert.True(SetThreadErrorMode(threadHad, out var original), $"Windows refused the thread mode 0x{threadHad:X4}.");
        try
        {
            var processBefore = GetErrorMode();

            (uint Thread, uint Process) during;
            using (StartErrorMode.Enter())
            {
                during = (GetThreadErrorMode(), GetErrorMode());
            }

            var after = GetThreadErrorMode();
            Assert.True(during.Thread == (threadHad | SemFailCriticalErrors | SemNoOpenFileErrorBox), $"The starting thread's mode was 0x{during.Thread:X4} during the start, from 0x{threadHad:X4}.");
            Assert.True(during.Process == processBefore, $"The process's mode became 0x{during.Process:X4}, from 0x{processBefore:X4}.");
            Assert.True(after == threadHad, $"The starting thread kept 0x{after:X4} after the start, from 0x{threadHad:X4}.");
        }
        finally
        {
            SetThreadErrorMode(original, out _);
        }
    }

    /// <summary>
    /// A start that fails gives the thread its mode back like one that succeeds: the failure is the caller's to
    /// report, unchanged, and the thread goes on to whatever it does next as it was.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void On_Windows_a_start_that_fails_leaves_the_threads_error_mode_as_it_was(bool heldProcess)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The error mode is a Windows setting.");
        var missing = ChildProcess.StartInfo(Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"), "program.exe"));
        using var process = new Process { StartInfo = missing };
        Assert.True(SetThreadErrorMode(0, out var original), "Windows refused the thread mode 0.");
        try
        {
            var refused = heldProcess
                ? Assert.Throws<Win32Exception>(() => ChildProcess.Start(process))
                : Assert.Throws<Win32Exception>(() => ChildProcess.Start(missing));

            var after = GetThreadErrorMode();
            Assert.True(refused.NativeErrorCode is 2 or 3, $"A missing program was refused with {refused.NativeErrorCode}: {refused.Message}");
            Assert.True(after == 0, $"The thread kept 0x{after:X4} after a failed start, from 0x0000.");
        }
        finally
        {
            SetThreadErrorMode(original, out _);
        }
    }

    /// <summary>
    /// The mode is in force inside the start itself, not before or after it. In .NET the redirected output reader is
    /// built inside <c>Process.Start</c>, on the starting thread, right after <c>CreateProcess</c>, so an encoding that
    /// reads the thread's mode when it makes its decoder sees the mode the start ran under. The program started is a
    /// valid one, so no box is ever at stake.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void On_Windows_the_error_mode_is_in_force_inside_the_start_itself(bool heldProcess)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The error mode is a Windows setting.");
        var seenBy = new ModeSeenByTheDecoder();
        var start = ChildProcess.StartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ChildKind.SpeaksToJason);
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("exit");
        start.StandardOutputEncoding = seenBy;
        Assert.True(SetThreadErrorMode(0, out var original), "Windows refused the thread mode 0.");
        try
        {
            Process? process;
            if (heldProcess)
            {
                process = new Process { StartInfo = start };
                ChildProcess.Start(process);
            }
            else
            {
                process = ChildProcess.Start(start);
            }

            using (process)
            {
                process!.WaitForExit(10_000);
            }

            var after = GetThreadErrorMode();
            Assert.True(seenBy.Seen == (SemFailCriticalErrors | SemNoOpenFileErrorBox), $"Inside the start the thread's mode was 0x{seenBy.Seen:X4}.");
            Assert.True(after == 0, $"The thread kept 0x{after:X4} after the start, from 0x0000.");
        }
        finally
        {
            SetThreadErrorMode(original, out _);
        }
    }

    private sealed class ModeSeenByTheDecoder : UTF8Encoding
    {
        public uint? Seen { get; private set; }

        public override Decoder GetDecoder()
        {
            Seen ??= GetThreadErrorMode();
            return base.GetDecoder();
        }
    }

    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);
}
