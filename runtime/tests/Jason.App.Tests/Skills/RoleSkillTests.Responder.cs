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

    [Fact]
    public void The_responder_records_a_stop_in_order_and_proposes_taking_the_person_out()
    {
        var skill = Flattened("responder");

        // Somebody who said stop is the one case where waiting is the harm. Jason's own register first, because it
        // stops Jason's reach at once; then the provider's half, which runs without anybody approving it; and taking
        // them out of the campaign proposed for a person, never done on the responder's own say.
        var register = skill.IndexOf("jason suppression add --channel email", StringComparison.Ordinal);
        var optout = skill.IndexOf("--operation optout.record", StringComparison.Ordinal);
        var stop = skill.IndexOf("--operation enrollment.stop", StringComparison.Ordinal);
        Assert.True(register >= 0 && optout > register && stop > optout, "the register, then the opt-out, then the proposed stop");
        Assert.Contains("runs even while the campaign is paused", skill, StringComparison.Ordinal);
        Assert.Contains("waits for a person", skill, StringComparison.Ordinal);

        // A campaign that is archived takes no new work, and the provider's opt-out is account-wide.
        Assert.Contains("record the opt-out through any active or paused campaign they are a member of that is routed to the same provider account", skill, StringComparison.Ordinal);
        Assert.Contains("Do not remove them, or archive them or the campaign, until the opt-out has succeeded", skill, StringComparison.Ordinal);
        Assert.Contains("the provider's half waits, and say so", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("rather than acting on it yourself", skill, StringComparison.Ordinal);
    }

    /// <summary>A brief that names a read is a reply in that read's result, and a read that has not succeeded is no reply at all.</summary>
    [Fact]
    public void The_responder_reads_the_reply_from_the_read_its_brief_names()
    {
        var skill = Flattened("responder");

        Assert.Contains("When the brief names a `read`", skill, StringComparison.Ordinal);
        Assert.Contains("the reply is that item's result", skill, StringComparison.Ordinal);
        Assert.Contains("if it has not succeeded, fail with a code rather than answer a reply you have not seen", skill, StringComparison.Ordinal);
    }
}
