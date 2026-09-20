using System.Text;
using Jason.Cli.Autostart;

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
