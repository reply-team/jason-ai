using System.Runtime.Versioning;
using Jason.Cli.Installation;
using Jason.Cli.Uninstall;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// The jason a terminal opened from now on finds by name, read as that terminal reads it. The Windows Paths are values of
/// the test's own handed through the places, so no row reads or writes a registry key; the folders and files are the
/// test's own.
/// </summary>
public sealed class NewTerminalTests : IDisposable
{
    private readonly TempPaths _tree = new();

    public void Dispose() => _tree.Dispose();

    /// <summary>
    /// A new terminal's Path is the machine's, then the account's: a jason on the machine's Path is found before the
    /// entry this run appended to the account's, wherever that entry stands.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_Windows_a_jason_on_the_machine_s_Path_comes_before_the_account_s_entry()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A Path's kind is the Windows registry's.");
        var decoy = Jason("machine-decoy", "jason.exe");
        var bundle = Jason("bundle", "jason.exe");
        var places = Windows(new StoredPath(Folder(decoy), RegistryValueKind.ExpandString), new StoredPath(Folder(bundle), RegistryValueKind.ExpandString));

        Assert.Equal(decoy, NewTerminal.FindsOnWindows(places));
    }

    /// <summary>The entry this run appended goes last on the account's Path, so an earlier entry there answers first.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_Windows_an_earlier_entry_of_the_account_s_Path_comes_before_its_own_appended_last()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A Path's kind is the Windows registry's.");
        var decoy = Jason("account-decoy", "jason.exe");
        var bundle = Jason("bundle", "jason.exe");
        var places = Windows(null, new StoredPath($"{Folder(decoy)};{Folder(bundle)}", RegistryValueKind.ExpandString));

        Assert.Equal(decoy, NewTerminal.FindsOnWindows(places));

        var first = Windows(null, new StoredPath($"{Folder(bundle)};{Folder(decoy)}", RegistryValueKind.ExpandString));
        Assert.Equal(bundle, NewTerminal.FindsOnWindows(first));
    }

    /// <summary>
    /// Each Path is read as its kind says: a <c>REG_EXPAND_SZ</c> entry is expanded, a <c>REG_SZ</c> one is taken as
    /// written, which no shell expands.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_Windows_each_Path_is_expanded_as_its_kind_says()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A Path's kind is the Windows registry's.");
        var name = "JASON_TEST_NEW_TERMINAL_" + Guid.NewGuid().ToString("N");
        var decoy = Jason("expanded", "jason.exe");
        System.Environment.SetEnvironmentVariable(name, Folder(decoy));
        try
        {
            Assert.Equal(decoy, NewTerminal.FindsOnWindows(Windows(new StoredPath($"%{name}%", RegistryValueKind.ExpandString), null)));
            Assert.Null(NewTerminal.FindsOnWindows(Windows(new StoredPath($"%{name}%", RegistryValueKind.String), null)));
            Assert.Null(NewTerminal.FindsOnWindows(Windows(null, new StoredPath($"%{name}%", RegistryValueKind.String))));
            Assert.Equal(decoy, NewTerminal.FindsOnWindows(Windows(null, new StoredPath($"%{name}%", RegistryValueKind.ExpandString))));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>In each folder the name is tried with each extension PATHEXT lists, in its order; a bare name never runs.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_Windows_the_extensions_are_tried_in_PATHEXT_order()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A Path's kind is the Windows registry's.");
        var exe = Jason("both", "jason.exe");
        var folder = Folder(exe);
        var cmd = Path.Combine(folder, "jason.cmd");
        File.WriteAllText(cmd, "@echo off");
        File.WriteAllText(Path.Combine(folder, "jason"), "bare");
        var path = new StoredPath(folder, RegistryValueKind.ExpandString);

        Assert.Equal(exe, NewTerminal.FindsOnWindows(Windows(null, path) with { PathExtensions = ".COM;.EXE;.BAT;.CMD" }));
        Assert.Equal(cmd, NewTerminal.FindsOnWindows(Windows(null, path) with { PathExtensions = ".CMD;.EXE" }));
        Assert.Null(NewTerminal.FindsOnWindows(Windows(null, path) with { PathExtensions = ".BAT;.PS1" }));
    }

    /// <summary>
    /// The PATH a terminal has, with <c>~/.local/bin</c> in front where the profile lines put it there, which prepend it:
    /// without them, a jason earlier on that PATH answers; a file that may not be run is passed over, as a shell passes it.
    /// </summary>
    [Fact]
    public void On_macOS_the_profile_lines_put_local_bin_in_front_of_an_earlier_jason()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A macOS PATH separates its folders with ':', which a Windows path holds after its drive.");
        var decoy = Jason("usr-local-bin", "jason");
        var wrapper = Jason(Path.Combine("home", ".local", "bin"), "jason");
        var notRunnable = Jason("not-runnable", "jason");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(decoy, (UnixFileMode)0b111_101_101);
            File.SetUnixFileMode(wrapper, (UnixFileMode)0b111_101_101);
            File.SetUnixFileMode(notRunnable, (UnixFileMode)0b110_100_100);
        }

        var localBin = Folder(wrapper);
        var path = $"{Folder(notRunnable)}:{Folder(decoy)}:{localBin}";

        Assert.Equal(decoy, NewTerminal.FindsOnMac(path, localBin, inFront: false));
        Assert.Equal(wrapper, NewTerminal.FindsOnMac(path, localBin, inFront: true));
        Assert.Equal(wrapper, NewTerminal.FindsOnMac(null, localBin, inFront: true));
        Assert.Null(NewTerminal.FindsOnMac(null, localBin, inFront: false));
    }

    /// <summary>
    /// What counts as this jason: the file itself, or the application's wrapper naming it. A wrapper naming another jason,
    /// or another file, does not.
    /// </summary>
    [Fact]
    public void This_jason_is_the_file_itself_or_the_wrapper_that_runs_it()
    {
        var image = Jason("bundle", "jason");
        var other = Jason("other", "jason");
        var wrapper = Path.Combine(Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "wrappers")).FullName, "jason");
        File.WriteAllText(wrapper, CommandFiles.Wrapper(image, null));
        var another = Path.Combine(Folder(wrapper), "another");
        File.WriteAllText(another, CommandFiles.Wrapper(other, null));

        Assert.True(NewTerminal.Runs(image, image));
        Assert.True(NewTerminal.Runs(wrapper, image));
        Assert.False(NewTerminal.Runs(other, image));
        Assert.False(NewTerminal.Runs(another, image));
        Assert.False(NewTerminal.Runs(null, image));
    }

    /// <summary>
    /// What is said of another file first, by what it is: a command-line install of Jason's moves by its own update, a
    /// package manager's is the manager's to take off, and anything else is the person's, removed or taken off the PATH by
    /// them; each says it was left as it is.
    /// </summary>
    [Fact]
    public void Another_file_first_is_named_with_what_removes_it()
    {
        const string Found = "/usr/local/bin/jason";
        const string Carried = "/Applications/Jason.app/Contents/Resources/bundle/jason";
        var owner = new PackageOwner(PackageOwner.Homebrew, "jason", "/opt/homebrew/Cellar/jason/0.48.5/bin/jason", "/opt/homebrew/Cellar/jason/0.48.5/bin", Found, Found, "brew upgrade jason", "brew uninstall jason");

        Assert.Equal(
            $"A terminal opened from now on finds {Found} first by the name jason, not this jason ({Carried}): it is a command-line install of Jason, and its own `jason update apply` moves it into Jason Desktop. It was left as it is.",
            NewTerminal.Problem(Found, Carried, commandLineInstall: true, owner: null));
        Assert.Equal(
            $"A terminal opened from now on finds {Found} first by the name jason, not this jason ({Carried}): Homebrew installed it, and `brew uninstall jason` takes it off. It was left as it is.",
            NewTerminal.Problem(Found, Carried, commandLineInstall: false, owner));
        Assert.Equal(
            $"A terminal opened from now on finds {Found} first by the name jason, not this jason ({Carried}). It is not a file of Jason's, so it was left as it is: remove it, or take its folder off the PATH.",
            NewTerminal.Problem(Found, Carried, commandLineInstall: false, owner: null));
    }

    [SupportedOSPlatform("windows")]
    private SetupPlaces Windows(StoredPath? machine, StoredPath? account) =>
        new(SetupSystem.Windows, Path.Combine(_tree.Paths.Root, "home"), Path.Combine(_tree.Paths.Root, "local"), @"Software\Jason.Tests\never-opened", null)
        {
            MachinePath = () => machine,
            AccountPath = new AccountPathInMemory(account),
            PathExtensions = ".COM;.EXE;.BAT;.CMD",
            Temporary = _tree.Paths.Root,
        };

    /// <summary>A file of that name in a folder of the test's own, by its full path.</summary>
    private string Jason(string folder, string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, folder)).FullName;
        var file = Path.Combine(directory, name);
        File.WriteAllText(file, "jason");
        return Path.GetFullPath(file);
    }

    private static string Folder(string file) => Path.GetDirectoryName(file)!;
}
