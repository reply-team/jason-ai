namespace Jason.Cli.Tests.Documentation;

public partial class PageClaimsTests
{
    /// <summary>
    /// The desktop's page says what a chat does in this version: a conversation's turns run in a folder of their own, so
    /// the folder Auto-edit writes in without a card, and the one outside which the data directory is refused, is the
    /// turn's, the shared one only for a conversation an earlier version made; a chat an earlier version used opens in Ask
    /// first and says why once; and the Feed has its row in both modes and its page in Search. The console's page says
    /// which chats a browser tab sends to, as the desktop's does.
    /// </summary>
    [Fact]
    public void The_pages_say_the_turns_folder_the_way_back_from_an_earlier_desktop_and_where_the_feed_and_a_tab_stand()
    {
        var desktop = Flat(Page("docs", "desktop.md"));
        Assert.Contains("six file commands run without a card when every path they name is inside the turn's folder, the conversation's own or, for a conversation an earlier version made, the shared one: `mkdir`, `touch`, `mv`, `cp`, `rm` and `rmdir`", desktop, StringComparison.Ordinal);
        Assert.Contains("and every path strictly inside the turn's folder once links are followed", desktop, StringComparison.Ordinal);
        Assert.Contains("a file in the turn's folder named like one of the six", desktop, StringComparison.Ordinal);
        Assert.Contains("a read or a write that names a path under the data directory outside the turn's folder (the settings file", desktop, StringComparison.Ordinal);
        Assert.Contains("to the `.claude`, `.agents` and `.codex` folders of the turn's folder and to its `.mcp.json`.", desktop, StringComparison.Ordinal);
        Assert.Contains("a read outside the turn's folder and the added folders", desktop, StringComparison.Ordinal);
        foreach (var old in new[] { "strictly inside the chat folder", "a file in the chat folder named", "outside the chat folder (the settings file", "folders of the chat folder", "a read outside the chat folder" })
        {
            Assert.DoesNotContain(old, desktop, StringComparison.Ordinal);
        }

        Assert.Contains("If you go back to an earlier version of Jason Desktop and then return, a chat that version used opens in Ask first, unless it is in Plan only, and says once why: an earlier version keeps no chat's mode but Plan only.", desktop, StringComparison.Ordinal);
        Assert.Contains("drawn in the sidebar under **Needs you**, in Live and in the Demo, with a dot while it holds lines you have not seen; Search opens it too, as its page **Feed**.", desktop, StringComparison.Ordinal);

        var ui = Flat(Page("docs", "ui.md"));
        Assert.Contains("it sends only to a chat in Ask first, Plan only or Full access, a turn it sends to a chat in Full access running in Ask first, says of a permission card or a plan card that it is answered in Jason Desktop", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("it sends only to a chat in Ask first or Plan only", ui, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both desktop pages say, where they describe the scrub, what a Full access turn carries without it, in the code's own
    /// words; and they quote the chat's sentences for a bypass that is not available as the chat says them: "right now"
    /// where the cause holds a minute, and "until Jason Desktop starts again" only where a tool ran that the check never saw.
    /// </summary>
    [Fact]
    public void The_desktop_pages_say_what_a_full_access_turn_carries_and_how_long_a_bypass_not_available_holds()
    {
        const string carried = "a key and the credentials the turn carries reach the commands the agent runs";
        var desktop = Flat(Page("docs", "desktop.md"));
        var readme = Flat(Page("desktop", "README.md"));
        Assert.Contains(carried, desktop, StringComparison.Ordinal);
        Assert.Contains(carried, readme, StringComparison.Ordinal);

        Assert.Contains("the cause after \"Full access is not available here right now\": Claude Code started the turn in another mode", desktop, StringComparison.Ordinal);
        Assert.Contains("the chat says \"Full access is not available here until Jason Desktop starts again\" with that cause after it", desktop, StringComparison.Ordinal);
        Assert.Contains("\"Full access is not available here right now: <why>\" where the cause holds a minute", readme, StringComparison.Ordinal);
        Assert.Contains("\"Full access is not available here until Jason Desktop starts again: <why>\" where a tool ran that the hook never saw", readme, StringComparison.Ordinal);
        foreach (var page in new[] { desktop, readme })
        {
            Assert.DoesNotContain("until Jason Desktop starts again: Claude Code", page, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Full access is not available here until Jason Desktop starts again\", with the cause after it, where Claude Code", page, StringComparison.Ordinal);
        }
    }
}
