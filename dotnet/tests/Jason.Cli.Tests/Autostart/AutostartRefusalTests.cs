using System.Text;
using Jason.Cli.Autostart;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// What a refused registration leaves on disk, which is nothing. Every platform writes its document before it
/// asks the tool to take it — the Task Scheduler is handed a file, and so is systemd — so a tool that refuses
/// leaves a document behind that registers nothing and says, to anybody reading the directory, that something
/// is registered.
/// </summary>
/// <remarks>
/// Found by the hand check: an `enable` from an unelevated prompt was refused and left
/// <c>&lt;data&gt;/autostart/jason-task.xml</c> on disk. The recorder here is the tool — it records that it was
/// asked and then refuses — which is the seam that lets the file half of a registrar be tested without a
/// machine that has a Task Scheduler on it.
/// </remarks>
public sealed class AutostartRefusalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-autostart-tests", Guid.NewGuid().ToString("N"));

    private static AutostartException Refusal => new(AutostartCodes.Refused, "schtasks refused: ERROR: Access is denied.");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// The defect itself: a refusal takes the document back out, and the directory it had to make goes with it.
    /// Left there, it is a file named after a registration on a machine that has none.
    /// </summary>
    [Fact]
    [Critical(Family.I4, "a refused enable is undone")]
    public void A_refused_registration_leaves_nothing_behind()
    {
        var directory = Path.Combine(_root, "autostart");
        var document = Path.Combine(directory, "jason-task.xml");
        var asked = 0;

        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.WriteThen(document, "<Task/>", Encoding.Unicode, () =>
            {
                asked++;
                Assert.True(File.Exists(document), "the tool is handed the document, so it has to be there when it is asked");
                throw Refusal;
            }));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Equal(1, asked);
        Assert.False(File.Exists(document), "the document a refused registration wrote is still there");
        Assert.False(Directory.Exists(directory), "and so is the directory it had to make for it");
    }

    /// <summary>
    /// A user unit on a home with no <c>.config</c>: the write makes <c>.config</c>, <c>.config/systemd</c> and
    /// <c>.config/systemd/user</c>, and a refused enable takes all three away again, so the home is as it was.
    /// </summary>
    [Fact]
    public void A_refused_user_unit_on_a_home_with_no_config_leaves_no_config()
    {
        var home = Path.Combine(_root, "home");
        Directory.CreateDirectory(home);
        var unit = Path.Combine(home, ".config", "systemd", "user", "jason.service");

        Assert.Throws<AutostartException>(
            () => AutostartRegistrars.WriteThen(unit, "[Service]\nExecStart=/opt/Jason/resources/bundle/jason\n", new UTF8Encoding(false), () => throw Refusal));

        Assert.True(Directory.Exists(home), "the home was there before and stays");
        Assert.Empty(Directory.EnumerateFileSystemEntries(home));
    }

    /// <summary>
    /// A write that made the directories and then could not write the document, here a name no file system takes, never
    /// reaches the tool and leaves none of them.
    /// </summary>
    [Fact]
    public void A_write_that_fails_after_making_the_directories_leaves_none()
    {
        var home = Path.Combine(_root, "home");
        Directory.CreateDirectory(home);
        var unit = Path.Combine(home, ".config", "systemd", "user", new string('j', 300) + ".service");
        var asked = 0;

        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.WriteThen(unit, "[Service]\n", new UTF8Encoding(false), () => asked++));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("could not be written", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, asked);
        Assert.Empty(Directory.EnumerateFileSystemEntries(home));
    }

    /// <summary>
    /// A parent that was there before stays, and so does one somebody else put a file in while the tool was asked: the
    /// put-back stops at the first directory that is not empty.
    /// </summary>
    [Fact]
    public void A_refused_user_unit_takes_back_only_the_empty_directories_it_made()
    {
        var home = Path.Combine(_root, "home");
        var config = Path.Combine(home, ".config");
        Directory.CreateDirectory(config);
        var systemd = Path.Combine(config, "systemd");
        var unit = Path.Combine(systemd, "user", "jason.service");

        Assert.Throws<AutostartException>(
            () => AutostartRegistrars.WriteThen(unit, "[Service]\n", new UTF8Encoding(false), () =>
            {
                File.WriteAllText(Path.Combine(systemd, "other.conf"), "another program's");
                throw Refusal;
            }));

        Assert.False(Directory.Exists(Path.Combine(systemd, "user")));
        Assert.True(File.Exists(Path.Combine(systemd, "other.conf")));
        Assert.True(Directory.Exists(config));
    }

    /// <summary>
    /// And only that. A directory that was already there, with somebody else's file in it, is not tidied up on
    /// the way out — the refusal is about one document.
    /// </summary>
    [Fact]
    public void A_refused_registration_takes_back_its_own_document_and_nothing_else()
    {
        var directory = Path.Combine(_root, "autostart");
        Directory.CreateDirectory(directory);
        var neighbour = Path.Combine(directory, "notes.txt");
        File.WriteAllText(neighbour, "something a person put here");

        Assert.Throws<AutostartException>(
            () => AutostartRegistrars.WriteThen(Path.Combine(directory, "jason-task.xml"), "<Task/>", Encoding.Unicode, () => throw Refusal));

        Assert.False(File.Exists(Path.Combine(directory, "jason-task.xml")));
        Assert.True(Directory.Exists(directory), "a directory that was already there is not the registration's to delete");
        Assert.Equal("something a person put here", File.ReadAllText(neighbour));
    }

    /// <summary>
    /// A document that was already there is put back exactly, byte for byte. On two of the three platforms the
    /// document <b>is</b> the registration — a plist under the account's Library, a unit under its config — so
    /// a refusal that deleted the one it found would take a working registration away with it, and a refusal
    /// that left its own in place would have replaced one registration's command line with another's.
    /// </summary>
    [Fact]
    public void A_refused_registration_puts_back_the_document_it_found()
    {
        var directory = Path.Combine(_root, "systemd");
        Directory.CreateDirectory(directory);
        var unit = Path.Combine(directory, "jason.service");
        var before = new UTF8Encoding(false).GetBytes("[Service]\nExecStart=/opt/jason/jason runtime run --detached\n");
        File.WriteAllBytes(unit, before);

        Assert.Throws<AutostartException>(
            () => AutostartRegistrars.WriteThen(unit, "[Service]\nExecStart=/somewhere/else\n", new UTF8Encoding(false), () => throw Refusal));

        Assert.True(File.Exists(unit), "the document that was already there is gone");
        Assert.Equal(before, File.ReadAllBytes(unit));
    }

    /// <summary>And a registration that was taken keeps its document, which is the whole point of writing it.</summary>
    [Fact]
    public void A_registration_that_was_taken_keeps_its_document()
    {
        var document = Path.Combine(_root, "autostart", "jason-task.xml");

        AutostartRegistrars.WriteThen(document, "<Task/>", Encoding.Unicode, () => { });

        Assert.True(File.Exists(document));
        Assert.Equal("<Task/>", File.ReadAllText(document, Encoding.Unicode));
    }
}
