namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The customising skill says which parts of a role keep it honest before an edit loosens one. Under an automatic send
    /// mode three of those parts are what a message stands on when nobody reads it: the critic's pass, the drafting role's
    /// doubts and the responder's judgement of a reply. Without naming them, an edit that makes the critic pass more would
    /// let more go out unread and the person would not hear it first.
    /// </summary>
    [Fact]
    public void The_customising_skill_names_what_an_automatic_send_stands_on_beside_the_launch_contract()
    {
        var paragraph = Assert.Single(
            File.ReadAllText(SkillPack.Find("customising-jason").File).ReplaceLineEndings("\n").Split("\n\n"),
            text => Whitespace().Replace(text, " ").Contains("keep a role honest", StringComparison.Ordinal));
        var flattened = Whitespace().Replace(paragraph, " ");

        Assert.Contains("automatic send mode", flattened, StringComparison.Ordinal);
        Assert.Contains("the critic's pass", flattened, StringComparison.Ordinal);
        Assert.Contains("the doubts the role that drafted it raises", flattened, StringComparison.Ordinal);
        Assert.Contains("the responder's judgement of a reply", flattened, StringComparison.Ordinal);
    }
}
