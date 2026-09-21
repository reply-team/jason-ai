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

    /// <summary>
    /// One vocabulary for both packs. This pack arrived carrying <c>maturity: draft</c> beside
    /// <c>status: active</c> under the same <c>metadata</c> key this repository already defines as
    /// draft | verified — two words for one idea, and a third value for a key that has two.
    /// </summary>
    /// <remarks>
    /// Landing that unchanged does not merely fail a guard: it teaches the next reader the wrong word, in a
    /// tree where the right one is load-bearing. The front matter is normalised on import and the pack's
    /// catalog says so, because a later re-sync from upstream would otherwise restore four keys and fail here
    /// for reasons nobody could reconstruct.
    /// </remarks>
    [Fact]
    public void Every_business_skill_declares_itself_in_the_shape_the_pack_agreed()
    {
        var skills = SkillPack.Business();
        Assert.NotEmpty(skills);

        foreach (var skill in skills)
        {
            PackShape.ReadsAsTheSkillItIs(skill);
        }
    }

    /// <summary>
    /// Nothing in this pack has been read by a named host against this runtime, so nothing in it is verified
    /// and nothing carries a body digest. The day one is, it is promoted by the act the runtime pack uses:
    /// promote, reword, record the digest, then read.
    /// </summary>
    [Fact]
    public void No_business_skill_claims_a_host_has_read_it()
    {
        foreach (var skill in SkillPack.Business())
        {
            var status = SkillFrontMatter.Read(skill.File).Metadata.GetValueOrDefault("status");
            Assert.True(
                status == "draft",
                $"'{skill.Name}' says metadata.status is '{status}'. No host has read this pack against this "
                + "runtime, and 'verified' means a named host read this exact body and its digest is in the "
                + "catalog.");
        }
    }

    /// <summary>
    /// Every file of the pack, not only the nine a host loads: the catalogue's references and YAML families are
    /// where a reader ends up, and a pointer is as broken there as it is in a skill's own text.
    /// </summary>
    [Fact]
    public void No_business_file_points_at_a_pack_this_repository_does_not_ship()
    {
        foreach (var file in Files())
        {
            var found = AbsentPacks.Find(File.ReadAllText(file));
            Assert.True(
                found.Count == 0,
                $"'{file}' names [{string.Join(", ", found)}]. That pack is not in this repository, so the "
                + "sentence sends a reader — or an agent that cannot check — to a file that is not there.");
        }
    }

    /// <summary>
    /// The model this runtime replaced, refused in the second pack as it is in the first. The pack measured
    /// clean of all ten phrases on the day it moved, which is why it was worth moving; the guard is what keeps
    /// it clean afterwards.
    /// </summary>
    [Fact]
    public void No_business_file_teaches_the_model_this_runtime_replaced()
    {
        foreach (var file in Files())
        {
            var found = SupersededVocabulary.Find(File.ReadAllText(file));
            Assert.True(
                found.Count == 0,
                $"'{file}' uses [{string.Join(", ", found)}]. Operational state lives in the runtime, and the "
                + "runtime is what decides when work runs.");
        }
    }

    private static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(SkillPack.BusinessRoot(), "*", SearchOption.AllDirectories);
}
