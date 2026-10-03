namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_responder_is_told_that_a_reply_is_read_only_through_the_provider()
    {
        var skill = Flattened("responder");

        // The roster calls this role "handles replies", and the one way this build reads a reply is a provider
        // operation's result. A responder that assumed a verb of Jason's read a mailbox would hunt for it and then
        // invent it, which is the failure this whole section of the pack exists to prevent.
        Assert.Contains("This build reads a reply only through the provider.", skill, StringComparison.Ordinal);
        Assert.Contains("no verb of Jason's reads a mailbox", skill, StringComparison.Ordinal);
        Assert.Contains("fail with a code rather than imagining one", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("responder");
    }
}
