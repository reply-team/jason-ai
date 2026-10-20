using System.Runtime.InteropServices;

namespace Jason.Runtime.Tests;

/// <summary>
/// The tests that run with this process's console on a legacy code page, kept away from everything else in their
/// assembly while they run: a child another test started meanwhile, with no encoding chosen for it, would be spoken to
/// in that page. The page is set once for the collection and given back after it, by <see cref="LegacyConsolePage"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LegacyConsolePageCollection : ICollectionFixture<LegacyConsolePage>
{
    public const string Name = "legacy console page";
}

/// <summary>
/// This process's console on a legacy code page for as long as the fixture lives, and on its own pages again after.
/// </summary>
/// <remarks>
/// <para>
/// On Windows, .NET reads and writes a redirected standard stream that names no encoding in the console's code page:
/// the input page for what a child is sent, the output page for what it says. On a runner already at UTF-8 a stream
/// whose encoding was forgotten looks exactly like one that was chosen, so the rows about the rule run under a page
/// where forgetting shows. A process with no console at all is the same trap: .NET falls back to UTF-8 there, and the
/// rows would pass for the wrong reason. So the fixture fails, and says so, unless there is a console to set.
/// </para>
/// <para>
/// The pages are set through the console itself rather than through <see cref="Console.InputEncoding"/>, which would
/// also replace the writers the test platform may have put in place of this process's own output. Only code running in
/// this process reads this console: a child started without a window of its own is given a fresh console on the
/// system's own legacy page, <see cref="SystemPage"/>, whatever this one is set to. Off Windows there is no console page
/// and this does nothing; a redirected stream there is UTF-8 whatever a caller forgot.
/// </para>
/// </remarks>
public sealed class LegacyConsolePage : IDisposable
{
    /// <summary>The United States OEM page: é is one byte of it that UTF-8 cannot read, and an emoji is two question marks.</summary>
    public const uint Page = 437;

    /// <summary>Letters of four scripts and a symbol outside the basic plane: nothing the legacy page holds whole.</summary>
    public const string Text = "José 😀 Ünïcödé Кирилл 名前";

    private readonly uint _input;

    private readonly uint _output;

    public LegacyConsolePage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _input = GetConsoleCP();
        _output = GetConsoleOutputCP();
        Assert.True(_input != 0 && _output != 0, $"This test host has no console to set a page on (input page {_input}, output page {_output}), and without one .NET reads and writes a child's streams as UTF-8 whatever a caller forgot.");
        var set = SetConsoleCP(Page) & SetConsoleOutputCP(Page);
        Assert.True(set, $"The console's pages could not be set to {Page}: they are {GetConsoleCP()} for input and {GetConsoleOutputCP()} for output.");
    }

    /// <summary>
    /// The page a console made for a windowless child is given: the system's OEM page on Windows, which is what the
    /// plugin host's own process reads and writes in; nothing elsewhere.
    /// </summary>
    public static string SystemPage => OperatingSystem.IsWindows() ? GetOEMCP().ToString(System.Globalization.CultureInfo.InvariantCulture) : "none, off Windows";

    public void Dispose()
    {
        if (_input != 0)
        {
            SetConsoleCP(_input);
            SetConsoleOutputCP(_output);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);
}
