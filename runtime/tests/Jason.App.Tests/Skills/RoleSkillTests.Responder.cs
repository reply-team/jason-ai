namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_responder_is_told_that_this_build_has_no_inbound_reply_to_read()
    {
        var skill = Flattened("responder");

        // The roster calls this role "handles replies" and this build has nothing that reads one. A responder
        // that assumed otherwise would hunt for the verb and then invent it, which is the failure this whole
        // section of the pack exists to prevent.
        Assert.Contains("This build has no inbound reply to read", skill, StringComparison.Ordinal);
        Assert.Contains("fail with a code rather than imagining one", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("responder");
    }
}
