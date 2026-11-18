using System.Globalization;
using Jason.Cli.Files;
using Jason.Contracts.Discovery;
using Jason.Contracts.Skills;

namespace Jason.Cli.Skills;

/// <summary>
/// A skill folder a deployment met in a root under the name Jason ships it by, which the root's record does not name: where,
/// in which pack, by which name, whether the root is the roles home, which the runtime teaches roles from, and whether what
/// stands there is a link rather than a folder of its own.
/// </summary>
public sealed record UnrecordedSkill(string Root, string Pack, string Name, bool RolesHome = false, bool Link = false)
{
    /// <summary>The folder itself.</summary>
    public string Folder => Path.Combine(Root, Name);
}

/// <summary>
/// What the one rule made of a plan before anything is written: the plan without the skills it leaves alone, those skills,
/// and the ones it adopts (<see cref="SkillsDeployer.Judge"/>); and, for a dry run, each root whose record cannot be read,
/// which a deployment refuses whole and so judges nothing in.
/// </summary>
public sealed record SkillsJudgement(SkillsPlan Plan, IReadOnlyList<UnrecordedSkill> LeftAlone, IReadOnlyList<UnrecordedSkill> Adopted)
{
    /// <summary>What a deployment would say of each root it refuses because its record cannot be read: a dry run's alone.</summary>
    public IReadOnlyList<string> Refused { get; init; } = [];

    /// <summary>
    /// What is said, in every answer that names them: each root a deployment would refuse, each skill adopted as Jason's, and,
    /// one sentence per root, the skills left alone as the person's. The same sentences at every door, so an install, an
    /// update and the set-up never say one thing two ways.
    /// </summary>
    public IEnumerable<string> Sentences =>
        Refused.Concat(Adopted.Select(SkillsDeployer.AdoptedSentence)).Concat(SkillsDeployer.LeftAloneSentences(LeftAlone));
}

/// <summary>What a deployment did, so the verb can report it rather than claim it.</summary>
/// <param name="Notes">
/// Things worth telling the operator that are not failures: a skill adopted as Jason's, and one left alone as the person's
/// (<see cref="SkillsJudgement.Sentences"/>). Said each time, never only the first: a person who missed it once meets it
/// again at the next deployment, for as long as the folder stands there.
/// </param>
public sealed record DeploymentReport(
    int Written,
    int Unchanged,
    IReadOnlyList<string> Edited,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => Problems.Count == 0;

    /// <summary>
    /// Each pack, and the root, that this run left on disk with no record because a skill of it was refused. The verb
    /// says which run completes it: an update moves recorded packs only, so it is not one.
    /// </summary>
    public IReadOnlyList<(string Pack, string Root)> Unrecorded { get; init; } = [];

    /// <summary>The skills this run left alone as the person's: nothing of them written, nothing recorded.</summary>
    public IReadOnlyList<UnrecordedSkill> LeftAlone { get; init; } = [];

    /// <summary>The skills this run adopted: byte for byte the ones Jason ships, recorded now, nothing written.</summary>
    public IReadOnlyList<UnrecordedSkill> Adopted { get; init; } = [];
}

/// <summary>Writes a plan, one whole skill at a time where no file of the person's is in it, and writes the record last.</summary>
public static class SkillsDeployer
{
    /// <summary>
    /// The one rule every door is held to, applied here and nowhere else: a skill of the plan whose folder is already in its
    /// root, under the name Jason ships it by, and which that root's record does not name, is the person's unless it is
    /// byte for byte the one Jason ships. The second is <em>adopted</em>: recorded, nothing written. The first is <em>left
    /// alone</em>: taken out of the plan, so nothing of it is written or recorded and no flag reaches it, and named in the
    /// answer every time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every door deploys through <see cref="Apply"/>, which judges first: <c>jason install</c>, its role skills, the
    /// finishing step and an update's skills step, <c>jason skills install</c> and <c>jason skills update</c>, into a harness
    /// root or the role skills root alike. There is no exception by root and no flag that marks one as Jason's own, since
    /// such a flag would be a way to write over anyone's folder.
    /// </para>
    /// <para>
    /// Whether a name is recorded is asked of the whole root, every pack in its record: a skill one pack recorded is Jason's
    /// whichever pack now ships it. A link stands where a folder would and is never adopted, since what it names is not in
    /// the root: removing it by receipt later would reach outside. A crash's remnant — a skill an earlier deployment moved
    /// into place before its record was written — is adopted where it is this release's, and otherwise left alone and
    /// named, never completed in silence: from the disk alone it is the person's folder, and nothing can tell it apart.
    /// </para>
    /// </remarks>
    public static SkillsJudgement Judge(SkillsPlan plan, string? rolesHome = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var records = new Dictionary<string, SkillsRecord>(StringComparer.Ordinal);
        var refused = new List<string>();
        var unreadable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in plan.Roots)
        {
            try
            {
                records[root] = SkillsRecord.Read(root) ?? new SkillsRecord(SkillsRecord.CurrentVersion, []);
            }
            catch (SkillsRecordUnreadable refusal)
            {
                // A deployment refuses this root whole before anything is judged or written, so a dry run says that, and
                // never that a skill there would be left alone or adopted.
                unreadable.Add(root);
                refused.Add($"{refusal.Message} A deployment refuses {root} whole: nothing would be written or recorded there.");
            }
        }

        var judged = Judge(plan with { Packs = [.. plan.Packs.Where(pack => !unreadable.Contains(pack.Root))] }, records, rolesHome);
        return judged with { Refused = refused };
    }

    private static SkillsJudgement Judge(SkillsPlan plan, Dictionary<string, SkillsRecord> records, string? rolesHome)
    {
        var leftAlone = new List<UnrecordedSkill>();
        var adopted = new List<UnrecordedSkill>();
        var packs = new List<PlannedPack>();
        foreach (var pack in plan.Packs)
        {
            var named = records.TryGetValue(pack.Root, out var record)
                ? record.Packs.SelectMany(deployment => deployment.Files).Select(file => file.Path.Split('/')[0]).ToHashSet(StringComparer.Ordinal)
                : [];
            var roles = rolesHome is not null && HarnessLocators.SamePath.Equals(pack.Root, rolesHome);
            var units = new List<PlannedUnit>();
            foreach (var unit in pack.Units)
            {
                if (named.Contains(unit.Name) || !Occupied(unit.Unit))
                {
                    units.Add(unit);
                    continue;
                }

                var link = IsLink(unit.Unit);
                if (!link && Current(unit))
                {
                    adopted.Add(new UnrecordedSkill(pack.Root, pack.Pack, unit.Name, roles));
                    units.Add(unit);
                    continue;
                }

                leftAlone.Add(new UnrecordedSkill(pack.Root, pack.Pack, unit.Name, roles, link));
            }

            packs.Add(pack with { Units = units });
        }

        return new SkillsJudgement(plan with { Packs = packs }, leftAlone, adopted);
    }

    /// <summary>What deploys an agent host's folder again: the recorded deployments, from the source each was made from.</summary>
    public const string RedeployHost = "jason skills update";

    /// <summary>What deploys the roles home again: the role skills, as `jason status` names their repair.</summary>
    public const string RedeployRoles = "jason skills install --roles-only";

    /// <summary>
    /// What is said of the skills left alone as the person's, one sentence per root naming every one of them there, the same
    /// at every door: whose Jason takes them for, what it means for the host or the role, and, last, the one way to have
    /// Jason's instead. "Takes it for" and not "is": a remnant of an older Jason with no record reads the same on disk, and
    /// the sentence stays true of it.
    /// </summary>
    public static IEnumerable<string> LeftAloneSentences(IEnumerable<UnrecordedSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        return skills
            .GroupBy(skill => skill.Root, StringComparer.Ordinal)
            .Select(root => LeftAloneSentence(root.DistinctBy(skill => skill.Name, StringComparer.Ordinal).OrderBy(skill => skill.Name, StringComparer.Ordinal).ToList()));
    }

    /// <summary>The sentence for one skill left alone: the singular form of <see cref="LeftAloneSentences"/>.</summary>
    public static string LeftAloneSentence(UnrecordedSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return LeftAloneSentence([skill]);
    }

    /// <summary>The sentence for the skills left alone in one root, in its singular or its plural form.</summary>
    private static string LeftAloneSentence(IReadOnlyList<UnrecordedSkill> skills)
    {
        var first = skills[0];
        var names = Listed(skills.Select(skill => $"'{skill.Name}'"));
        var what = Things(skills.Select(skill => skill.Link).ToList());
        if (skills.Count == 1)
        {
            return first.RolesHome
                ? $"{first.Folder} is a role skill under a name Jason ships that no record of Jason's names there, so Jason takes it for yours: "
                    + $"left as it is, nothing written or recorded, and no flag writes over it; the runtime teaches the role {names} from that {what}. "
                    + $"To have the one Jason ships, move or remove that {what}, then run `{RedeployRoles}`."
                : $"{first.Folder} is a skill under a name Jason ships that no record of Jason's names there, so Jason takes it for yours: "
                    + $"left as it is, nothing written or recorded, and no flag writes over it; Jason's {names} is not installed there, "
                    + $"and an agent reading {first.Root} that is asked to use {names} reads that {what}. "
                    + $"To have the one Jason ships, move or remove that {what}, then run `{RedeployHost}`.";
        }

        var paths = Listed(skills.Select(skill => skill.Folder));
        return first.RolesHome
            ? $"{paths} are role skills under names Jason ships that no record of Jason's names there, so Jason takes them for yours: "
                + $"left as they are, nothing written or recorded, and no flag writes over them; the runtime teaches the roles {names} from those {what}. "
                + $"To have the ones Jason ships, move or remove those {what}, then run `{RedeployRoles}`."
            : $"{paths} are skills under names Jason ships that no record of Jason's names there, so Jason takes them for yours: "
                + $"left as they are, nothing written or recorded, and no flag writes over them; Jason's {names} are not installed there, "
                + $"and an agent reading {first.Root} that is asked to use one of them reads that {(skills.All(skill => skill.Link) ? "link" : skills.Any(skill => skill.Link) ? "folder or link" : "folder")}. "
                + $"To have the ones Jason ships, move or remove those {what}, then run `{RedeployHost}`.";
    }

    /// <summary>A list in a sentence: "a", "a and b", "a, b and c".</summary>
    internal static string Listed(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count <= 1 ? string.Concat(all) : string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1];
    }

    /// <summary>What stands there, by whether each is a link: "folder" or "link" for one, "folders", "links" or "folders and links" for more.</summary>
    internal static string Things(IReadOnlyList<bool> links) =>
        links.Count == 1 ? (links[0] ? "link" : "folder")
        : links.All(link => link) ? "links"
        : links.Any(link => link) ? "folders and links"
        : "folders";

    /// <summary>What is said of a skill adopted as Jason's.</summary>
    public static string AdoptedSentence(UnrecordedSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return $"{skill.Folder} is byte for byte the skill Jason ships, and no record of Jason's named it: recorded as Jason's now, nothing written.";
    }

    /// <summary>
    /// Whether anything stands at a path that a deployment must not write over: a file, a link, a dangling one included, or a
    /// folder holding anything but the system's litter (<see cref="SystemLitter"/>). A folder that holds nothing, or only
    /// that, is unoccupied: the skill is written and recorded there as into no folder at all. The status checks read a folder
    /// left alone by this same test.
    /// </summary>
    internal static bool Occupied(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || File.Exists(path))
        {
            return true;
        }

        return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any(entry => !SystemLitter.Is(entry));
    }

    /// <summary>Whether a path is a link, a junction included, rather than a folder of its own.</summary>
    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    /// <summary>
    /// Applies the plan. Each skill is assembled beside its destination and moved into place, so a launch
    /// reading a role's tree sees the tree that was there or the tree that arrived and never a blend of the
    /// two. A skill holding a file of the person's is the exception: it is written file by file where it stands,
    /// with no reader waited for, so a launch may read some of its files new and some old. The person's file has
    /// already made it a blend of two hands, theirs and Jason's, which they reconcile. The record goes in last, so a
    /// crash half-way through reads as incomplete next time rather than as a deployment that is current.
    /// </summary>
    /// <param name="force">
    /// Overwrite files the operator has edited. Without it they are reported and kept, and everything else is
    /// written: an installer that quietly reverts somebody's edit is a data-loss bug wearing a convenience label,
    /// and one that writes nothing over it leaves the person their edit or their skills, never both — which an
    /// uninstall that keeps an edited file on purpose put to them on the very next install.
    /// </param>
    public static DeploymentReport Apply(SkillsPlan plan, JasonPaths paths, bool force, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);

        var problems = new List<string>();
        var notes = new List<string>();
        var records = new Dictionary<string, SkillsRecord>(StringComparer.Ordinal);
        foreach (var root in plan.Roots)
        {
            if (Existing(root, problems) is not { } record)
            {
                continue;
            }

            records[root] = record;
        }

        if (problems.Count > 0)
        {
            return new DeploymentReport(0, 0, [], problems, notes);
        }

        // The one rule, before anything else is judged: what it leaves alone is out of the plan from here on, so nothing
        // below, --force included, can reach it, and what it adopts is current and recorded as it is.
        var judgement = Judge(plan, records, paths.RoleSkillsDirectory);
        plan = judgement.Plan;
        notes.AddRange(judgement.Sentences);

        // Everything is judged before anything is written, for the same reason the plan itself is: what is kept is
        // decided once, and the report names exactly that.
        var (edited, retired) = Edited(plan, records);
        HashSet<string> keep = force ? [] : edited.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = retired.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var written = 0;
        var unchanged = 0;
        var staged = new List<string>();
        var leftUnrecorded = new List<(string Pack, string Root)>();

        foreach (var root in plan.Roots)
        {
            var staging = SkillsSwap.StagingFor(paths, root);
            SkillsSwap.Collect(staging);
            staged.Add(staging);

            var deployments = new List<SkillsDeployment>(records[root].Packs);
            var rewrite = false;

            foreach (var pack in plan.Packs.Where(pack => string.Equals(pack.Root, root, StringComparison.Ordinal)))
            {
                var recorded = deployments.FirstOrDefault(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal));

                var mine = new List<DeployedFile>();
                var kept = false;
                foreach (var unit in pack.Units)
                {
                    if (Current(unit))
                    {
                        unchanged += unit.Files.Count;
                        mine.AddRange(Planned(root, unit));
                        continue;
                    }

                    // A skill holding a file the person edited, or one they put in it, is never replaced whole: that
                    // would take their file with the old tree. Its other files are written where they stand instead.
                    if (unit.Files.Select(file => file.Target).Concat(Inside(unit)).Any(keep.Contains))
                    {
                        var placed = InPlace(root, unit, keep, dropped, recorded, staging);
                        written += placed.Written;
                        unchanged += placed.Unchanged;
                        mine.AddRange(placed.Files);
                        if (placed.Failed)
                        {
                            // The files it reached are on disk as it left them and recorded so; every other file of the
                            // skill the record named is as it was, and keeps its old entry. The old digests recorded for
                            // files already moved into place read them as the person's edits from then on, and a file new
                            // in this release was in no record and removed by no uninstall.
                            problems.Add($"'{unit.Name}' could not be written beside the file(s) you edited in it.");
                            var reached = placed.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
                            mine.AddRange(Previously(recorded, Relative(root, unit.Unit)).Where(file => !reached.Contains(file.Path)));
                            kept = true;
                        }

                        continue;
                    }

                    if (Swap(unit, staging, clock) is { } refusal)
                    {
                        problems.Add(refusal);

                        // The live tree is still the old one, so the record must still describe the old one.
                        // Writing the digests of bytes that were never written is how a transient refusal --
                        // a launch reading this role's skill, the exact case the design exists for -- becomes
                        // permanent: the next run compares disk against a record that never matched it,
                        // calls the difference the operator's own edit, and refuses until somebody types
                        // --force about a file Jason wrote. It also falsifies the message printed a moment
                        // ago, which says nothing was changed for this role.
                        mine.AddRange(Previously(recorded, Relative(root, unit.Unit)));
                        kept = true;
                        continue;
                    }

                    written += unit.Files.Count;
                    mine.AddRange(Planned(root, unit));
                }

                // With no record of the pack there is no old deployment to keep, and a record at the new ref with a refused
                // skill's files left out would be a lie: the pack read as current, `jason status` named no repair, and
                // the next update found nothing to move. So the pack stays unrecorded. The skills that did land are on disk
                // without a record, as after a crash, each byte for byte this release's, and the next install adopts them
                // and writes the record.
                if (kept && recorded is null)
                {
                    leftUnrecorded.Add((pack.Pack, root));
                    continue;
                }

                // And so must the rest of what the record says about this pack. A skill still holding the old tree is
                // the old deployment's, so its source, ref and commit stay where they were, beside the files as they
                // now are on disk: recorded at the new ref, the pack read as current, `jason status` named no repair,
                // and the next update found nothing to move.
                // The skills of this pack the rule left alone in this root are named with it, for `jason status` to say.
                var alone = judgement.LeftAlone
                    .Where(skill => string.Equals(skill.Root, root, StringComparison.Ordinal) && string.Equals(skill.Pack, pack.Pack, StringComparison.Ordinal))
                    .Select(skill => skill.Name)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                var next = kept && recorded is not null
                    ? recorded with { Files = mine, LeftAlone = alone }
                    : new SkillsDeployment(
                        pack.Pack,
                        plan.Source.Source,
                        plan.Source.Ref,
                        plan.Source.RefOverridden,
                        plan.Source.Commit,
                        clock.GetUtcNow(),
                        mine)
                    { LeftAlone = alone };

                // A run that changed nothing rewrites nothing, not even the time it ran. "It wrote nothing"
                // has to mean the bytes under this root are the bytes that were there, and a record whose
                // timestamp moved is a byte that changed.
                if (recorded is not null && Same(recorded, next))
                {
                    continue;
                }

                deployments.RemoveAll(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal));
                deployments.Add(next);
                rewrite = true;
            }

            if (!rewrite)
            {
                continue;
            }

            try
            {
                SkillsRecord.Write(root, new SkillsRecord(SkillsRecord.CurrentVersion, deployments));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"The record in '{root}' could not be written: {exception.Message}");
            }
        }

        // Nothing of this deployment's own is left in somebody's home directory. The per-unit child went as
        // each skill landed; this is the parent, which nothing removed and which would otherwise sit in a
        // harness root for good.
        foreach (var directory in staged)
        {
            SkillsSwap.Tidy(directory);
        }

        return new DeploymentReport(written, unchanged, edited, problems, notes) { Unrecorded = leftUnrecorded, LeftAlone = judgement.LeftAlone, Adopted = judgement.Adopted };
    }

    /// <summary>
    /// Whether a recorded deployment says the same thing as the one about to replace it, the time it ran
    /// aside. Every field but that one is about what is on disk; that one is about when somebody typed.
    /// </summary>
    private static bool Same(SkillsDeployment recorded, SkillsDeployment next) =>
        (recorded with { InstalledAt = default, Files = [], LeftAlone = [] }) == (next with { InstalledAt = default, Files = [], LeftAlone = [] })
        && recorded.Files.SequenceEqual(next.Files)
        && recorded.LeftAlone.SequenceEqual(next.LeftAlone, StringComparer.Ordinal);

    /// <summary>
    /// Whether what is on disk is already exactly this skill: the same files, each with the same digest, and
    /// nothing besides. A deployment that would change nothing renames nothing, which is what makes installing
    /// twice cost nothing and closes the window entirely in the commonest case of all.
    /// </summary>
    private static bool Current(PlannedUnit unit)
    {
        if (!Directory.Exists(unit.Unit))
        {
            return false;
        }

        var onDisk = Directory.GetFiles(unit.Unit, "*", SearchOption.AllDirectories);
        if (onDisk.Length != unit.Files.Count)
        {
            return false;
        }

        foreach (var file in unit.Files)
        {
            if (!File.Exists(file.Target) || !string.Equals(SkillsRecord.Digest(file.Target), file.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every file in a deployed skill's directory, the plan's or not.</summary>
    private static IEnumerable<string> Inside(PlannedUnit unit) =>
        Directory.Exists(unit.Unit) ? Directory.GetFiles(unit.Unit, "*", SearchOption.AllDirectories) : [];

    /// <summary>
    /// A skill holding a file the person edited, written file by file where it stands: every file of the plan that is
    /// not theirs is written unless it is already current, each moved into place whole, theirs is left as it is, and a
    /// file of Jason's that this release no longer ships is deleted. What it did up to a file it could not write or
    /// delete is returned with <see cref="InPlaceResult.Failed"/>, so that the record states what is on disk.
    /// </summary>
    /// <remarks>
    /// A kept file is recorded with the digest the record already had for it — what Jason wrote there — so the next run
    /// tells it for an edit again rather than for a file that is merely out of date, and an uninstall keeps it. Not one
    /// tree swapped for another, so a launch reading this skill while it is written can see some files new and some not,
    /// and no reader is waited for; the person's edit has already made it a blend of two hands. Each file lands in the
    /// root's staging directory, on the same volume, and is moved from there: a kill between the two leaves the landing
    /// where the next run collects it, never inside the skill, where it would be taken for the person's file.
    /// </remarks>
    private static InPlaceResult InPlace(string root, PlannedUnit unit, HashSet<string> keep, HashSet<string> dropped, SkillsDeployment? recorded, string staging)
    {
        var written = 0;
        var unchanged = 0;
        var files = new List<DeployedFile>();
        foreach (var file in unit.Files)
        {
            var relative = Relative(root, file.Target);
            if (keep.Contains(file.Target))
            {
                // A file the plan names is the person's edit only where the record says what Jason wrote there; the plan's
                // digest in its place would name bytes never written.
                var wrote = recorded?.Files.FirstOrDefault(entry => string.Equals(entry.Path, relative, StringComparison.Ordinal))?.Sha256
                    ?? throw new InvalidOperationException($"'{relative}' is kept as an edit with no record of what was written there.");
                files.Add(new DeployedFile(relative, wrote));
                continue;
            }

            if (File.Exists(file.Target) && string.Equals(SkillsRecord.Digest(file.Target), file.Sha256, StringComparison.Ordinal))
            {
                files.Add(new DeployedFile(relative, file.Sha256));
                unchanged++;
                continue;
            }

            var landing = Path.Combine(staging, $"{Path.GetFileName(file.Target)}{SkillsSwap.Landing}{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(staging);
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                File.Copy(file.Source, landing, overwrite: true);
                File.Move(landing, file.Target, overwrite: true);
                files.Add(new DeployedFile(relative, file.Sha256));
                written++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Delete(landing);
                }
                catch (Exception left) when (left is IOException or UnauthorizedAccessException)
                {
                }

                return new InPlaceResult(written, unchanged, files, Failed: true);
            }
        }

        foreach (var retired in Inside(unit).Where(dropped.Contains).ToList())
        {
            try
            {
                File.Delete(retired);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new InPlaceResult(written, unchanged, files, Failed: true);
            }
        }

        return new InPlaceResult(written, unchanged, files, Failed: false);
    }

    /// <summary>What <see cref="InPlace"/> did: the files it reached, each as it now is on disk, and whether it stopped short.</summary>
    private sealed record InPlaceResult(int Written, int Unchanged, List<DeployedFile> Files, bool Failed);

    /// <summary>The whole skill, assembled beside its destination and moved into place.</summary>
    private static string? Swap(PlannedUnit unit, string staging, TimeProvider clock)
    {
        var staged = Path.Combine(staging, $"{unit.Name}.staging-{Guid.NewGuid():N}");
        try
        {
            foreach (var file in unit.Files)
            {
                var target = Path.Combine(staged, Path.GetRelativePath(unit.Unit, file.Target));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file.Source, target, overwrite: true);
            }

            return SkillsSwap.Replace(staged, unit.Unit, staging, clock);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"'{unit.Name}' could not be written: {exception.Message}";
        }
        finally
        {
            if (Directory.Exists(staged))
            {
                try
                {
                    DirectoryTrees.Delete(staged);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Files on disk that differ from what the record says was written there. Differing from the record is the
    /// operator's own edit; differing from the source while matching the record is simply out of date. And, apart, the
    /// files of Jason's this plan no longer ships: named by the record, and on disk at the digest it records.
    /// </summary>
    private static (IReadOnlyList<string> Edited, IReadOnlyList<string> Retired) Edited(SkillsPlan plan, Dictionary<string, SkillsRecord> records)
    {
        var edited = new List<string>();
        var retired = new List<string>();
        foreach (var pack in plan.Packs)
        {
            var known = records[pack.Root].Packs
                .FirstOrDefault(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal))
                ?.Files.ToDictionary(file => file.Path, file => file.Sha256, StringComparer.Ordinal) ?? [];

            foreach (var file in pack.Files)
            {
                if (!File.Exists(file.Target)
                    || !known.TryGetValue(Relative(pack.Root, file.Target), out var wrote))
                {
                    continue;
                }

                var onDisk = SkillsRecord.Digest(file.Target);
                if (!string.Equals(onDisk, wrote, StringComparison.Ordinal)
                    && !string.Equals(onDisk, file.Sha256, StringComparison.Ordinal))
                {
                    edited.Add(file.Target);
                }
            }

            // And every file inside a deployed skill that this plan does not name. A skill is replaced whole
            // -- the old tree is moved aside and removed -- so a file the operator put in one is destroyed by
            // an ordinary re-run, silently, with no --force asked for and nothing said afterwards. Walking
            // only the plan's own files could never see it: the plan does not know it exists.
            // A file the record names, on disk at the digest it records, is not one: it is a file of Jason's that a later
            // release dropped, and it goes with the skill rather than staying for good as the person's.
            foreach (var unit in pack.Units.Where(unit => Directory.Exists(unit.Unit)))
            {
                var ours = unit.Files.Select(file => file.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var found in Directory.GetFiles(unit.Unit, "*", SearchOption.AllDirectories))
                {
                    if (ours.Contains(found))
                    {
                        continue;
                    }

                    var jasons = known.TryGetValue(Relative(pack.Root, found), out var wrote)
                        && string.Equals(SkillsRecord.Digest(found), wrote, StringComparison.Ordinal);
                    (jasons ? retired : edited).Add(found);
                }
            }
        }

        return (edited, retired);
    }

    private static string Relative(string root, string target) =>
        Path.GetRelativePath(root, target).Replace('\\', '/');

    /// <summary>What this skill's files are, once they are on disk as the plan describes them.</summary>
    private static IEnumerable<DeployedFile> Planned(string root, PlannedUnit unit) =>
        unit.Files.Select(file => new DeployedFile(Relative(root, file.Target), file.Sha256));

    /// <summary>
    /// What the record already said about one skill, for a replacement that was refused. A record describes
    /// what is on disk, and what is on disk for that skill is what was there before.
    /// </summary>
    private static IEnumerable<DeployedFile> Previously(SkillsDeployment? recorded, string unit) =>
        recorded?.Files.Where(file => file.Path.StartsWith(unit + "/", StringComparison.Ordinal)) ?? [];

    private static SkillsRecord? Existing(string root, List<string> problems)
    {
        try
        {
            return SkillsRecord.Read(root) ?? new SkillsRecord(SkillsRecord.CurrentVersion, []);
        }
        catch (SkillsRecordUnreadable unreadable)
        {
            // Never guessed at: an uninstall reading a record this build wrote over would remove the paths it
            // understands and leave the rest, while reporting that it removed everything.
            problems.Add(unreadable.Message);
            return null;
        }
    }
}
