using Jason.App;

namespace Jason.App.Tests;

/// <summary>
/// A <c>jason:</c> link as the first argument is the link mode, whatever follows it; a link anywhere else is a verb's
/// argument. Only the choice is held here: no row runs a mode, so no row can open a browser or write the clipboard.
/// </summary>
public sealed class LinkModeTests
{
    [Theory]
    [InlineData(new[] { "jason://attention" }, Mode.Link)]
    [InlineData(new[] { "JASON://attention/approvals/apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6" }, Mode.Link)]
    [InlineData(new[] { "jason:anything", "--human" }, Mode.Link)]
    [InlineData(new[] { "ui", "jason://attention" }, Mode.Cli)]
    [InlineData(new[] { "jason" }, Mode.Cli)]
    public void A_link_first_is_the_link_mode(string[] args, Mode expected) =>
        Assert.Equal(expected, ModeRouter.Select(args));
}
