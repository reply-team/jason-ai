using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jason.Cli.Process;

/// <summary>
/// Windows' clipboard, as Unicode text: one block of memory filled first, then the clipboard opened for a message-only
/// window of this process, emptied, given the block, closed. Another program holding the clipboard is waited for briefly,
/// then said as <c>clipboard_busy</c>. The block is freed on every way out short of Windows taking it.
/// </summary>
/// <remarks>
/// No test runs it: a run would replace what the person running the suite had copied. What it does is proved by a
/// person's click on a real toast.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboard : IClipboard
{
    /// <summary>The longest text it puts on the clipboard; a prompt is far shorter.</summary>
    public const int MaxText = 16_384;

    public const string Busy = "clipboard_busy";

    public const string TooLong = "clipboard_text_too_long";

    public const string Unavailable = "clipboard_unavailable";

    private const int Attempts = 10;

    private const int PauseMilliseconds = 50;

    private const uint UnicodeText = 13;

    private const uint Moveable = 0x0002;

    /// <summary>The parent that makes a window message-only: never shown, never on the desktop.</summary>
    private static readonly IntPtr MessageOnly = new(-3);

    public bool TrySetText(string text, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxText)
        {
            problem = TooLong;
            return false;
        }

        // The block is filled before the clipboard is opened, so the clipboard is held no longer than handing it over takes.
        var memory = Filled(text);
        if (memory == IntPtr.Zero)
        {
            problem = Unavailable;
            return false;
        }

        var taken = false;
        try
        {
            // A clipboard opened with no window has no owner, and Windows then refuses the data: the window owns it.
            var window = CreateWindowExW(0, "STATIC", null, 0, 0, 0, 0, 0, MessageOnly, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (window == IntPtr.Zero)
            {
                problem = Unavailable;
                return false;
            }

            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    if (OpenClipboard(window))
                    {
                        try
                        {
                            taken = EmptyClipboard() && SetClipboardData(UnicodeText, memory) != IntPtr.Zero;
                            problem = taken ? null : Unavailable;
                            return taken;
                        }
                        finally
                        {
                            _ = CloseClipboard();
                        }
                    }

                    if (attempt == Attempts)
                    {
                        problem = Busy;
                        return false;
                    }

                    Thread.Sleep(PauseMilliseconds);
                }
            }
            finally
            {
                _ = DestroyWindow(window);
            }
        }
        finally
        {
            // Once Windows has taken the block it is Windows' to free; on every other way out it is still ours.
            if (!taken)
            {
                _ = GlobalFree(memory);
            }
        }
    }

    /// <summary>A movable block holding <paramref name="text"/> and its terminating NUL, unlocked; zero where none could be had.</summary>
    private static IntPtr Filled(string text)
    {
        var memory = GlobalAlloc(Moveable, (UIntPtr)((text.Length + 1) * sizeof(char)));
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var locked = GlobalLock(memory);
        if (locked == IntPtr.Zero)
        {
            _ = GlobalFree(memory);
            return IntPtr.Zero;
        }

        var filled = false;
        try
        {
            Marshal.Copy(text.ToCharArray(), 0, locked, text.Length);
            Marshal.WriteInt16(locked, text.Length * sizeof(char), 0);
            filled = true;
        }
        finally
        {
            _ = GlobalUnlock(memory);
            if (!filled)
            {
                _ = GlobalFree(memory);
            }
        }

        return memory;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
