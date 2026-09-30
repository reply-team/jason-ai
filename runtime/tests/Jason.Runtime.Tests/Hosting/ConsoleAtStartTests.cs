using System.Text;
using System.Text.Json.Nodes;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// What a detached runtime found attached to it when it let go of its standard streams, and the one line that
/// says so at every start.
/// </summary>
/// <remarks>
/// On Windows 11 24H2 and later the executable's <c>consoleAllocationPolicy</c> means a runtime started by a logon
/// task has no console at all. An older Windows ignores the setting and hands it one, whose window a person sees
/// until the runtime frees it. The log line is how anybody can tell afterwards which of the two happened, without
/// having watched the screen.
/// </remarks>
public class ConsoleAtStartTests
{
    private const string Template = ProcessDetacher.LogTemplate;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A console nobody could see is not the window a person saw: a runtime started with <c>CreateNoWindow</c>
    /// may be handed a console with no window at all, and freeing that one says nothing about the policy.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, ConsoleAtStart.None)]
    [InlineData(true, true, true, ConsoleAtStart.Freed)]
    [InlineData(true, false, true, ConsoleAtStart.FreedHidden)]
    [InlineData(true, true, false, ConsoleAtStart.NotFreed)]
    [InlineData(true, false, false, ConsoleAtStart.NotFreed)]
    public void What_was_attached_what_showed_and_what_was_freed_is_what_is_said(
        bool attached, bool visible, bool freed, ConsoleAtStart expected) =>
        Assert.Equal(expected, ProcessDetacher.Outcome(attached, visible, freed));

    /// <summary>
    /// The one line that says, at every start, what console the runtime found. "none" on 24H2 and later;
    /// "freed" where it was handed a console with a visible window and let go of it, which is the window a
    /// person saw open and close at logon; "freed_hidden" where the console it let go of had no window anyone
    /// could see.
    /// </summary>
    [Theory]
    [InlineData(ConsoleAtStart.None, "none")]
    [InlineData(ConsoleAtStart.Freed, "freed")]
    [InlineData(ConsoleAtStart.FreedHidden, "freed_hidden")]
    [InlineData(ConsoleAtStart.NotFreed, "not_freed")]
    public async Task A_detached_runtime_says_what_console_it_found(ConsoleAtStart console, string word)
    {
        using var dir = new TempDataDir();
        await using (await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet with { ConsoleAtStart = console }, Ct))
        {
        }

        var said = Lines(dir).Where(line => (string?)line["@mt"] == Template).ToList();
        Assert.Equal(word, (string?)Assert.Single(said)["Console"]);
    }

    [Fact]
    public async Task A_runtime_nobody_detached_says_nothing_about_a_console()
    {
        using var dir = new TempDataDir();
        await using (await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct))
        {
        }

        Assert.DoesNotContain(Lines(dir), line => (string?)line["@mt"] == Template);
    }

    /// <summary>
    /// On a Windows that ignores the policy, the standard error a logon task hands the runtime is a console, and
    /// the detacher frees that console. A refusal written to it afterwards (another runtime already owns this data
    /// directory) threw out of the refusal it was carrying, and the process ended in an exception rather than
    /// with its answer.
    /// </summary>
    [Fact]
    public async Task A_refusal_nobody_can_hear_is_still_a_refusal()
    {
        using var dir = new TempDataDir();
        await using var first = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        var exit = await RuntimeHost.RunAsync(dir.Paths, TestRuntimeOptions.Quiet with { Refusals = new GoneConsole() }, Ct);

        Assert.Equal(1, exit);
    }

    /// <summary>Every line of the runtime's log, read after it stopped, so no file is still being written.</summary>
    private static List<JsonNode> Lines(TempDataDir dir) =>
    [
        .. Directory.GetFiles(dir.Paths.LogsDirectory)
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Length > 0)
            .Select(line => JsonNode.Parse(line)!),
    ];

    /// <summary>A writer over a console handle that was freed: every write, flush and close fails as that one does.</summary>
    private sealed class GoneConsole : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw new IOException("The handle is invalid.");

        public override Task WriteLineAsync(string? value) => throw new IOException("The handle is invalid.");

        public override Task FlushAsync(CancellationToken cancellationToken) => throw new IOException("The handle is invalid.");

        public override ValueTask DisposeAsync() => throw new IOException("The handle is invalid.");
    }
}
