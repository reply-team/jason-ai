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

        // Every skill is a direct child of the pack root. This is not tidiness: a host looks for
        // <name>/SKILL.md one level below a declared path and does not recurse, so a skill that moved a
        // level deeper is still in the repository, still passes every other guard here, and is delivered
        // to nobody. Asserting IsRole here would prove nothing — the walk is told this pack has no roles,
        // so the flag reports what it was handed rather than what is on disk.
        foreach (var skill in skills)
        {
            var parent = Path.GetDirectoryName(skill.Directory);
            Assert.True(
                string.Equals(parent, SkillPack.BusinessRoot(), StringComparison.Ordinal),
                $"'{skill.Name}' sits at '{skill.Directory}', which is not directly under the pack root. A "
                + "host does not recurse, so this skill would be offered and never delivered.");
        }

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

    /// <summary>
    /// Two documents in this repository name the same operations, and only one of them is enforced. The
    /// contract under <c>docs/contracts/operations</c> is data the runtime validates against; this pack is
    /// knowledge a person's agent reads. A skill that names an enforced operation without pointing at the
    /// enforced document is how a reader comes to believe the pack is the contract — and then writes a plan
    /// the runtime refuses.
    /// </summary>
    /// <remarks>
    /// Per skill, not per file. The skill is the unit a host loads and the door a reader comes in through; the
    /// YAML families and the reference documents are reached from it. Per file, the rule would fire on data
    /// that arrived byte-identical and the fix would be a pointer injected into a machine-readable catalogue,
    /// which is a worse outcome than the problem.
    /// </remarks>
    [Fact]
    public void A_skill_that_names_an_enforced_operation_points_at_the_contract()
    {
        var enforced = Directory
            .EnumerateFiles(
                Path.Combine(SkillPack.RepositoryRoot(), "docs", "contracts", "operations"),
                "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToArray();
        Assert.NotEmpty(enforced);

        foreach (var skill in SkillPack.Business())
        {
            var named = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(skill.Directory, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var operation in enforced.Where(one => text.Contains(one, StringComparison.Ordinal)))
                {
                    named.Add(operation);
                }
            }

            if (named.Count == 0)
            {
                continue;
            }

            Assert.True(
                File.ReadAllText(skill.File).Contains("docs/contracts", StringComparison.Ordinal),
                $"'{skill.Name}' names [{string.Join(", ", named)}], which this runtime enforces from "
                + "docs/contracts/operations, and its SKILL.md points at nothing. One of the two documents is "
                + "data the runtime validates against and the other is knowledge; a skill that names an "
                + "operation without saying which it is leaves the reader to guess.");
        }
    }

    [Fact]
    public void Every_business_skill_is_in_the_catalog_and_every_entry_is_a_skill()
    {
        var catalog = File.ReadAllText(SkillPack.BusinessCatalog());
        var linked = PackCatalog.Links(catalog).ToList();

        foreach (var skill in SkillPack.Business())
        {
            var link = Path.GetRelativePath(SkillPack.BusinessRoot(), skill.File).Replace('\\', '/');
            Assert.True(
                linked.Contains(link, StringComparer.Ordinal),
                $"'{skill.Name}' is a skill in this pack and the catalog does not link it as '{link}'. "
                + $"The catalog links: {string.Join(", ", linked)}");
        }

        foreach (var link in linked)
        {
            Assert.True(
                File.Exists(Path.Combine(SkillPack.BusinessRoot(), link)),
                $"The catalog links '{link}', and there is no skill there.");
        }
    }

    /// <summary>
    /// A digest in a catalog records that a named host read an exact body. Nothing here has been read, so a
    /// row carrying one would be a claim about a reading that never happened.
    /// </summary>
    [Fact]
    public void No_catalog_row_records_a_reading_that_never_happened()
    {
        Assert.DoesNotContain("sha256:", File.ReadAllText(SkillPack.BusinessCatalog()), StringComparison.Ordinal);
    }

    /// <summary>
    /// This pack prints no <c>jason</c> commands: it teaches provider-neutral operations, and its nine skills
    /// carry no fenced block at all. The guard is here for the day one appears — if a text prints a command,
    /// the command has to be real — and it is proven by a probe, because a guard over an empty set proves
    /// nothing by passing.
    /// </summary>
    /// <remarks>
    /// The line is typed through the runtime pack's own path, which holds four seams: its own data directory,
    /// a process table that launches nothing, a release feed that throws, and no autostart registrar, which
    /// production answers with a refusal. The business pack inherits all four by using that method rather than
    /// a second copy of it.
    /// </remarks>
    [Fact]
    public async Task Any_jason_command_this_pack_prints_is_a_real_one()
    {
        foreach (var skill in SkillPack.Business())
        {
            foreach (var command in SkillPack.PrintedCommands(skill.File))
            {
                await DocumentedSkillCommandsTests.AssertUnderstoodAsync(command, skill.File);
            }
        }
    }

    private static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(SkillPack.BusinessRoot(), "*", SearchOption.AllDirectories);
}
