using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Jason.Cli.Process;

namespace Jason.Cli.Tests.Process;

/// <summary>
/// What a child started to outlive the CLI is kept from: every handle the CLI holds as inheritable, read from the system's
/// own list of its handles, or, where that list cannot be had, the CLI's three standard streams. The rule is held here with
/// its sources given, and the list against this process's own handles, read and never changed. That a real runtime started
/// under a handed pipe no longer holds it is held by the executable itself, in <c>HandedHandleTests</c>.
/// </summary>
public sealed class StandardStreamsTests
{
    private static readonly IntPtr[] Standard = [new(0x10), new(0x14), new(0x18)];

    /// <summary>
    /// Linux: a descriptor this process holds open across exec, as Jason Desktop's Electron hands a jason its resource
    /// files, reaches a child started after it until it is kept out, and no longer after; the process keeps it open itself.
    /// Catches the runtime a Desktop's jason starts holding the Desktop's files, an AppImage's mount among them, for as long
    /// as it runs.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("linux")]
    public void On_linux_a_descriptor_held_open_across_exec_is_kept_out_of_the_children_started_after()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Descriptors and close-on-exec are read from Linux's /proc here.");
        var file = Path.GetTempFileName();
        var fd = open(file, 0);
        Assert.True(fd > 2, "the descriptor could not be opened");
        try
        {
            Assert.Equal("held", ChildSees(fd));

            StandardStreams.KeepOutOfChildren();

            Assert.Equal("free", ChildSees(fd));
            Assert.True(File.Exists($"/proc/self/fd/{fd}"), "this process lost the descriptor itself");
        }
        finally
        {
            _ = close(fd);
            File.Delete(file);
        }
    }

    private static string ChildSees(int fd)
    {
        var start = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"[ -e /proc/self/fd/{fd} ] && echo held || echo free");
        using var child = System.Diagnostics.Process.Start(start)!;
        var said = child.StandardOutput.ReadToEnd().Trim();
        child.WaitForExit();
        return said;
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int open([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, int flags);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [Fact]
    public void Where_the_list_cannot_be_had_the_three_standard_streams_are_kept_out_and_nothing_else()
    {
        var cleared = new List<IntPtr>();

        StandardStreams.KeepOutOfChildren(() => null, () => Standard, cleared.Add);

        Assert.Equal(Standard, cleared);
    }

    [Fact]
    public void Where_the_list_does_not_add_up_it_is_no_list_and_the_three_standard_streams_are_kept_out()
    {
        // A count of three handles in a list that holds room for one, and a list too short for its own count.
        var list = Snapshot(IntPtr.Size, (0x40, true));
        Write(list, 0, IntPtr.Size, 3);

        Assert.Null(StandardStreams.Inheritable(list, IntPtr.Size));
        Assert.Null(StandardStreams.Inheritable(list.AsSpan(0, IntPtr.Size), IntPtr.Size));

        var cleared = new List<IntPtr>();
        StandardStreams.KeepOutOfChildren(() => StandardStreams.Inheritable(list, IntPtr.Size), () => Standard, cleared.Add);
        Assert.Equal(Standard, cleared);
    }

    /// <summary>
    /// A list whose handles are not handles - one zero, one not a multiple of four, as a list read at the wrong layout
    /// gives - is no list, whatever its count says, and the three standard streams are kept out instead.
    /// </summary>
    [Theory]
    [InlineData(8, 0x0)]
    [InlineData(8, 0x2ca)]
    [InlineData(4, 0x0)]
    [InlineData(4, 0x2ca)]
    public void Where_a_handle_the_list_gives_is_no_handle_it_is_no_list_and_the_three_standard_streams_are_kept_out(int pointerSize, long notAHandle)
    {
        var list = Snapshot(pointerSize, (0x14, true), (notAHandle, false), (0x5f0, true));
        Assert.Null(StandardStreams.Inheritable(list, pointerSize));

        var cleared = new List<IntPtr>();
        StandardStreams.KeepOutOfChildren(() => StandardStreams.Inheritable(list, pointerSize), () => Standard, cleared.Add);
        Assert.Equal(Standard, cleared);
    }

    /// <summary>
    /// The list as the system writes it, at either width a process's pointers have: the handles it marks inheritable are
    /// kept out, every one of them, and those it does not mark are never touched, a standard stream that is not
    /// inheritable among them.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void Where_the_list_is_had_every_handle_it_marks_inheritable_is_kept_out_and_no_other(int pointerSize)
    {
        var list = Snapshot(pointerSize, (0x10, false), (0x14, true), (0x2c8, true), (0x30, false), (0x5f0, true));
        var cleared = new List<IntPtr>();

        StandardStreams.KeepOutOfChildren(() => StandardStreams.Inheritable(list, pointerSize), () => Standard, cleared.Add);

        Assert.Equal([new IntPtr(0x14), new IntPtr(0x2c8), new IntPtr(0x5f0)], cleared);
    }

    /// <summary>
    /// The list against the system itself: a handle this process made inheritable is in it as inheritable, and one it did
    /// not is not. It changes no handle, so it is safe beside every other row.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_systems_list_of_this_processs_handles_tells_an_inheritable_handle_from_one_that_is_not()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The list of a process's handles is Windows'.");
        using var handed = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var kept = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);

        var inheritable = StandardStreams.InheritableHandles();

        Assert.NotNull(inheritable);
        Assert.Contains(handed.ClientSafePipeHandle.DangerousGetHandle(), inheritable);
        Assert.DoesNotContain(kept.ClientSafePipeHandle.DangerousGetHandle(), inheritable);
        Assert.DoesNotContain(handed.SafePipeHandle.DangerousGetHandle(), inheritable);
    }

    /// <summary>A <c>PROCESS_HANDLE_SNAPSHOT_INFORMATION</c> of these handles, laid out for pointers of that width.</summary>
    private static byte[] Snapshot(int pointerSize, params (long Handle, bool Inheritable)[] handles)
    {
        var header = 2 * pointerSize;
        var entry = (3 * pointerSize) + 16;
        var list = new byte[header + (handles.Length * entry)];
        Write(list, 0, pointerSize, handles.Length);
        for (var i = 0; i < handles.Length; i++)
        {
            var at = header + (i * entry);
            Write(list, at, pointerSize, handles[i].Handle);
            Write(list, at + pointerSize, pointerSize, 1);
            Write(list, at + (2 * pointerSize), pointerSize, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(at + (3 * pointerSize)), 0x001F01FF);
            BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(at + (3 * pointerSize) + 4), 37);

            // The attributes, with OBJ_INHERIT where the handle is inheritable, beside a bit that is not it.
            BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(at + (3 * pointerSize) + 8), handles[i].Inheritable ? 0x3u : 0x1u);
        }

        return list;
    }

    private static void Write(byte[] list, int at, int pointerSize, long value)
    {
        if (pointerSize == 8)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(list.AsSpan(at), (ulong)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(at), (uint)value);
        }
    }
}
