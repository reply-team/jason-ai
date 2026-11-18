using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Jason.Cli.Process;

/// <summary>The CLI's own standard streams, the other handles it was handed, and the children that must not inherit them.</summary>
public static class StandardStreams
{
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private const int HandleFlagInherit = 0x00000001;

    /// <summary><c>ProcessHandleInformation</c>: the handles of one process, each with its attributes.</summary>
    private const int ProcessHandleInformation = 51;

    /// <summary><c>OBJ_INHERIT</c>, the attribute of a handle a new process is handed.</summary>
    private const uint ObjectInherit = 0x00000002;

    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    /// <summary>How many times the list is asked for again when the handles grew between the size it gave and the read.</summary>
    private const int Tries = 4;

    /// <summary>The largest list read: far more handles than a CLI holds, and a bound on what a wrong answer could cost.</summary>
    private const int LargestList = 64 * 1024 * 1024;

    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>The handle a process names itself by, which needs no opening and no closing.</summary>
    private static readonly IntPtr CurrentProcess = new(-1);

    /// <summary>
    /// A new process on Windows is handed every handle its parent left inheritable, whether or not it is told
    /// to use it. When the CLI itself runs under a redirect — a shell capturing its output, a build step, a
    /// test — its own standard streams are such handles, and so is any other handle the program that started it
    /// left inheritable: Windows PowerShell running <c>jason</c> in a pipeline hands it the pipes it was itself
    /// handed, beside the three it made. A process meant to outlive the CLI (the runtime, or the cleanup an
    /// uninstall leaves behind it) would hold them open for as long as it runs, and whoever reads them would wait
    /// for an end of file that never comes. The CLI has no child that should ever be handed one of them: every
    /// child this is called for has its three streams made for it, and none is told a handle. So every handle this
    /// process holds as inheritable stops being one before such a child starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handles are the system's own list of this process's handles (<c>NtQueryInformationProcess</c>,
    /// <c>ProcessHandleInformation</c>), each with its attributes, so only a handle the list says is inheritable is
    /// changed, and no value is ever guessed: asking about a value that is not a handle ends a process outright
    /// where strict handle checks are on. Where the list cannot be had, or does not add up, it falls back to this
    /// process's three standard streams, which is all it did before. A failure costs the caller nothing but the
    /// wait this avoids; there is nothing to report it to, and the launch itself is unaffected.
    /// </para>
    /// <para>
    /// What it does not promise: a start made on another thread at the same moment. .NET makes the ends of a
    /// child's pipes inheritable only while it starts that child, under a lock of its own, and closes them after;
    /// were this to run in that moment, it could take that child's streams from it, or ask about a handle just
    /// closed. That lock is .NET's and private, so none is taken here: every caller builds its start, calls this,
    /// and starts on the same thread, and the CLI starts no process on another thread.
    /// </para>
    /// <para>
    /// Unix needs none of this for the three streams: the child's descriptors 0, 1 and 2 are replaced outright.
    /// </para>
    /// </remarks>
    public static void KeepOutOfChildren()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeepOutOfChildren(InheritableHandles, StandardHandles, Clear);
    }

    /// <summary>
    /// The rule, with its three sources given: every handle <paramref name="inheritable"/> lists is cleared, or, where it
    /// lists none because the list could not be had (null), every handle of <paramref name="standard"/>.
    /// </summary>
    internal static void KeepOutOfChildren(Func<IReadOnlyList<IntPtr>?> inheritable, Func<IEnumerable<IntPtr>> standard, Action<IntPtr> clear)
    {
        ArgumentNullException.ThrowIfNull(inheritable);
        ArgumentNullException.ThrowIfNull(standard);
        ArgumentNullException.ThrowIfNull(clear);

        foreach (var handle in inheritable() ?? [.. standard()])
        {
            clear(handle);
        }
    }

    /// <summary>
    /// The handles this process holds as inheritable, from the system's own list of them; null where the list cannot be
    /// had or does not add up.
    /// </summary>
    internal static IReadOnlyList<IntPtr>? InheritableHandles()
    {
        var length = 16 * 1024;
        for (var tried = 0; tried < Tries; tried++)
        {
            var list = new byte[length];
            int status;
            int returned;
            try
            {
                status = NtQueryInformationProcess(CurrentProcess, ProcessHandleInformation, list, list.Length, out returned);
            }
            catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }

            if (status is StatusInfoLengthMismatch or StatusBufferTooSmall)
            {
                // The handles may grow between the size given and the next read: room for them, within the bound.
                var wanted = Math.Max(returned, length) * 2L;
                if (wanted > LargestList)
                {
                    return null;
                }

                length = (int)wanted;
                continue;
            }

            return status < 0 || returned <= 0 || returned > list.Length ? null : Inheritable(list.AsSpan(0, returned), IntPtr.Size);
        }

        return null;
    }

    /// <summary>
    /// The inheritable handles of a <c>PROCESS_HANDLE_SNAPSHOT_INFORMATION</c> as the system wrote it for a process whose
    /// pointers are <paramref name="pointerSize"/> bytes wide; null where its count does not fit its length, or where any
    /// handle it gives is zero or not a multiple of four, which no handle of a process is and a list read at the wrong
    /// layout gives at once.
    /// </summary>
    /// <remarks>
    /// The list is two pointer-wide numbers (the count of handles and one reserved), then one entry per handle: the handle,
    /// two pointer-wide counts, and four 32-bit numbers, the third of them the handle's attributes. Every size and offset is
    /// read from the pointer's width, so no width is assumed.
    /// </remarks>
    internal static IReadOnlyList<IntPtr>? Inheritable(ReadOnlySpan<byte> list, int pointerSize)
    {
        if (pointerSize is not (4 or 8))
        {
            return null;
        }

        var header = 2 * pointerSize;
        var entry = (3 * pointerSize) + (4 * sizeof(uint));
        var attributes = (3 * pointerSize) + (2 * sizeof(uint));
        if (list.Length < header)
        {
            return null;
        }

        var count = Pointer(list, pointerSize);
        if (count > (ulong)((list.Length - header) / entry))
        {
            return null;
        }

        var handles = new List<IntPtr>();
        for (var at = header; at < header + ((int)count * entry); at += entry)
        {
            var handle = Pointer(list[at..], pointerSize);
            if (handle == 0 || handle % 4 != 0)
            {
                return null;
            }

            if ((BinaryPrimitives.ReadUInt32LittleEndian(list[(at + attributes)..]) & ObjectInherit) != 0)
            {
                handles.Add(new IntPtr((long)handle));
            }
        }

        return handles;
    }

    private static ulong Pointer(ReadOnlySpan<byte> at, int pointerSize) =>
        pointerSize == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(at) : BinaryPrimitives.ReadUInt32LittleEndian(at);

    /// <summary>This process's three standard streams, where each is a handle.</summary>
    private static IEnumerable<IntPtr> StandardHandles()
    {
        foreach (var standardStream in new[] { StandardInputHandle, StandardOutputHandle, StandardErrorHandle })
        {
            var handle = GetStdHandle(standardStream);
            if (handle != IntPtr.Zero && handle != InvalidHandle)
            {
                yield return handle;
            }
        }
    }

    private static void Clear(IntPtr handle) => SetHandleInformation(handle, HandleFlagInherit, 0);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, int mask, int flags);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, byte[] information, int length, out int returned);
}
