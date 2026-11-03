using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jason.Cli.Process;

/// <summary>
/// Windows' clipboard, as Unicode text: opened for a message-only window of this process, emptied, given one block of
/// memory, closed. Another program holding the clipboard is waited for briefly, then said as <c>clipboard_busy</c>.
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

        // A clipboard opened with no window has no owner, and Windows then refuses the data: the window owns it.
        var window = CreateWindowExW(0, "STATIC", null, 0, 0, 0, 0, 0, MessageOnly, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            problem = Unavailable;
            return false;
        }

        try
        {
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                if (OpenClipboard(window))
                {
                    try
                    {
                        return Put(text, out problem);
                    }
                    finally
                    {
                        _ = CloseClipboard();
                    }
                }

                Thread.Sleep(PauseMilliseconds);
            }

            problem = Busy;
            return false;
        }
        finally
        {
            _ = DestroyWindow(window);
        }
    }

    private static bool Put(string text, out string? problem)
    {
        problem = Unavailable;
        if (!EmptyClipboard())
        {
            return false;
        }

        var memory = GlobalAlloc(Moveable, (UIntPtr)((text.Length + 1) * sizeof(char)));
        if (memory == IntPtr.Zero)
        {
            return false;
        }

        var locked = GlobalLock(memory);
        if (locked == IntPtr.Zero)
        {
            _ = GlobalFree(memory);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, locked, text.Length);
            Marshal.WriteInt16(locked, text.Length * sizeof(char), 0);
        }
        finally
        {
            _ = GlobalUnlock(memory);
        }

        // Once Windows has taken the block it is Windows' to free; refused, it is still ours.
        if (SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
        {
            _ = GlobalFree(memory);
            return false;
        }

        problem = null;
        return true;
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
