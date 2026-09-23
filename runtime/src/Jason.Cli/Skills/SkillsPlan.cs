using System.Globalization;
using Jason.Contracts.Api;
using Jason.Contracts.Skills;

namespace Jason.Cli.Skills;

/// <summary>One file a deployment would write, and the digest of what it would write there.</summary>
public sealed record PlannedFile(string Source, string Target, string Sha256);

/// <summary>One pack, bound for one root.</summary>
/// <param name="Unit">
/// The directory a whole skill goes into. Deployment is per unit rather than per file because that is what a
/// host looks a skill up by, and what a replacement has to be all-or-nothing about.
/// </param>
public sealed record PlannedUnit(string Name, string Unit, IReadOnlyList<PlannedFile> Files);

/// <summary>Everything one pack would put in one root.</summary>
public sealed record PlannedPack(string Pack, string Root, IReadOnlyList<PlannedUnit> Units)
{
    public IEnumerable<PlannedFile> Files => Units.SelectMany(unit => unit.Files);
}

/// <summary>What a deployment would do, decided in full before any of it is done.</summary>
/// <param name="Cap">The <c>Roles:MaxSkillBytes</c> this plan was measured against.</param>
/// <param name="CapFromRuntime">
/// Whether that cap was read from a running runtime. False means the documented default was used, which the
/// output says out loud — a pass against the wrong cap is not a pass.
/// </param>
public sealed record SkillsPlan(
    StagedSource Source,
    IReadOnlyList<PlannedPack> Packs,
    IReadOnlyList<string> Refusals,
    int Cap,
    bool CapFromRuntime)
{
    public IEnumerable<string> Roots => Packs.Select(pack => pack.Root).Distinct(StringComparer.Ordinal);
}

/// <summary>The two packs this repository ships, as the marketplace manifest names them.</summary>
public static class SkillPacks
{
    public const string Runtime = "jason-runtime-skills";

    public const string Business = "jason-business-skills";

    public static IReadOnlyList<string> All => [Runtime, Business];
}

/// <summary>
/// Composes a deployment from a staged source, and refuses one before a byte is written.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole reason the verb validates rather than writing and hoping. <see cref="RoleSkillRules"/>
/// is what the launcher refuses a role's skill on — a name that differs from its directory, a tree over the
/// cap — and nothing has ever deployed into that directory, so neither refusal has ever fired. The moment one
/// does, a bad deployment turns a role with no skill, which runs untaught, into a role whose every launch is
/// refused. That is strictly worse than the hole this closes, which is why a refusal here refuses the whole
/// deployment rather than the one skill.
/// </para>
/// <para>
/// The same rule the launcher applies, read from the contracts, rather than a second copy that agrees until
/// somebody edits one.
/// </para>
/// </remarks>
public static class SkillsPlanner
{
    /// <summary>The cap used when no runtime could be asked; the same number <c>RolesOptions</c> defaults to.</summary>
    public const int DocumentedCap = 1024 * 1024;

    /// <summary>
    /// What would be written where. A refusal here is a refusal of the whole deployment: a half-written role
    /// root is worse than an empty one, because the launcher refuses what it cannot make sense of.
    /// </summary>
    public static SkillsPlan Compose(
        StagedSource source,
        string roleRoot,
        IReadOnlyList<HarnessRoot> harnesses,
        int cap,
        bool capFromRuntime,
        string? onlyPack)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(harnesses);

        var packs = new List<PlannedPack>();
        var refusals = new List<string>();

        var runtimePack = Path.Combine(source.Directory, "skills", "runtime");
        var businessPack = Path.Combine(source.Directory, "skills", "business");

        // The role halves go to the runtime's own directory, always. That is the hole this closes, and it is
        // not optional: a runtime with no role skills launches every role untaught.
        if (Wanted(onlyPack, SkillPacks.Runtime))
        {
            var roles = Path.Combine(runtimePack, "roles");
            if (Directory.Exists(roles))
            {
                packs.Add(new PlannedPack(SkillPacks.Runtime, roleRoot, [.. Units(roles, roleRoot, cap, isRole: true, refusals)]));
            }

            foreach (var harness in harnesses)
            {
                packs.Add(new PlannedPack(SkillPacks.Runtime, harness.Directory, [.. Units(runtimePack, harness.Directory, cap, isRole: false, refusals)]));
            }
        }

        if (Wanted(onlyPack, SkillPacks.Business) && Directory.Exists(businessPack))
        {
            foreach (var harness in harnesses)
            {
                packs.Add(new PlannedPack(SkillPacks.Business, harness.Directory, [.. Units(businessPack, harness.Directory, cap, isRole: false, refusals)]));
            }
        }

        return new SkillsPlan(source, packs, refusals, cap, capFromRuntime);
    }

    private static bool Wanted(string? onlyPack, string pack) =>
        onlyPack is null || string.Equals(onlyPack, pack, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every skill directly under a pack root. Directly, and not recursively: a host discovers a skill at
    /// <c>skills/&lt;name&gt;/SKILL.md</c> and does not look deeper, so the role skills a level down are not
    /// part of the interactive pack and are deployed to the runtime's own directory instead.
    /// </summary>
    private static IEnumerable<PlannedUnit> Units(string packRoot, string targetRoot, int cap, bool isRole, List<string> refusals)
    {
        foreach (var directory in Directory.GetDirectories(packRoot).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (!isRole && string.Equals(name, "roles", StringComparison.Ordinal))
            {
                // The roles directory is a pack of its own with a different destination, not a skill.
                continue;
            }

            // A directory whose name begins with a dot is metadata beside the skills rather than one of them.
            // A host discovers a skill at `<root>/<name>/SKILL.md` and offers it under `<name>`, which nobody
            // publishes as `.claude-plugin` -- and both packs here carry exactly such a directory, the skills
            // marketplace manifest. Refusing them refused the whole deployment on this repository's own tree:
            // the one source `docs/INSTALL.md` tells a stranger to install from.
            //
            // By the shape rather than by the name. A list holding `.claude-plugin` would be a rule against
            // the one spelling somebody had thought of, which is how `--ref ..` got through a filter that
            // kept dots.
            if (name.StartsWith('.'))
            {
                continue;
            }

            var reading = RoleSkillRules.Read(directory, name, isRole ? cap : int.MaxValue);
            if (!reading.HasSkillFile)
            {
                refusals.Add($"'{name}' has no {RoleSkillRules.SkillFile}, so a host would load nothing from it. Nothing was written.");
                continue;
            }

            if (reading.Problem is { } problem)
            {
                refusals.Add($"{problem.Message} Nothing was written.");
                continue;
            }

            var target = Path.Combine(targetRoot, name);
            yield return new PlannedUnit(
                name,
                target,
                [.. reading.Files.Select(file => new PlannedFile(
                    file,
                    Path.Combine(target, Path.GetRelativePath(directory, file)),
                    SkillsRecord.Digest(file)))]);
        }
    }

    /// <summary>What a plan says it will do, in the order it will do it, printed before any of it happens.</summary>
    public static void Describe(TextWriter output, SkillsPlan plan, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(plan);

        output.WriteLine($"Source:  {plan.Source.Source} at {plan.Source.Ref}{(plan.Source.Commit is { } commit ? $" ({commit[..Math.Min(7, commit.Length)]})" : string.Empty)}");
        output.WriteLine(
            plan.CapFromRuntime
                ? string.Create(CultureInfo.InvariantCulture, $"Cap:     {plan.Cap} bytes, as this runtime reports it")
                : string.Create(CultureInfo.InvariantCulture, $"Cap:     {plan.Cap} bytes, the documented default: this runtime did not answer, so a lowered cap would not be caught here"));

        // Before anything is written, in every mode and not only under --dry-run. Writing into somebody's home
        // directory is not a silent act.
        //
        // And in the same words either way. This printed "Writing:" before validation had finished, so a run
        // that then refused read "Writing: ... Writing: ... Nothing was written." -- the present tense for a
        // decision not yet taken. What differs between a dry run and a real one is what happens next, not
        // what the plan is, so the plan is stated and the tense is left to the line below it.
        foreach (var pack in plan.Packs)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Target:  {pack.Units.Count} skills of {pack.Pack} into {pack.Root}"));
        }

        if (dryRun)
        {
            output.WriteLine("Dry run: nothing will be written.");
        }
    }
}
