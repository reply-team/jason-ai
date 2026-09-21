namespace Jason.App.Tests.Skills;

/// <summary>
/// The second pack, held by the guards that apply to it. It is not the runtime pack with a different path:
/// nothing here is launched, nothing here has a roster, and nothing here has been read by a host against this
/// runtime — so the launch contract, the roster guard and the body digest are absent by decision rather than
/// by oversight.
/// </summary>
/// <remarks>
/// The knowledge in this pack was written before this runtime existed, in a repository that shipped it beside
/// two other packs. What it teaches is the SDR profession, which does not change because the software under it
/// did; what it said about where work lives, and about which document decides what an operation is, does. The
/// guards here hold that boundary rather than the prose.
/// </remarks>
public class BusinessPackTests
{
    [Fact]
    public void The_business_pack_ships_nine_skills_and_a_catalog()
    {
        var skills = SkillPack.Business();

        var names = skills.Select(skill => skill.Name).Order().ToArray();
        Assert.True(
            names is
            [
                "approval-boundaries", "audience-building", "campaign-launch", "campaign-planning",
                "inbox-triage", "linkedin-guardrails", "performance-analysis", "sdr-operations",
                "sending-guardrails",
            ],
            $"skills/business holds [{string.Join(", ", names)}]. The pack is nine skills; a tenth that arrived "
            + "without a guard, or one that went missing in a move, is what this names.");

        Assert.All(skills, skill => Assert.False(
            skill.IsRole,
            $"'{skill.Name}' is in skills/business, and nothing there is ever launched as a role."));

        Assert.True(File.Exists(SkillPack.BusinessCatalog()), "The business pack has no catalog.");

        // sdr-operations is the contract the other eight argue from, and the only one that arrives as a
        // directory rather than a file. A move that silently dropped its references would leave eight skills
        // citing a document that is not here.
        var operations = Assert.Single(skills, skill => skill.Name == "sdr-operations");
        var carried = Directory.EnumerateFiles(operations.Directory, "*", SearchOption.AllDirectories).Count();
        Assert.True(carried == 50, $"sdr-operations arrived with {carried} files and it has fifty.");
    }
}
