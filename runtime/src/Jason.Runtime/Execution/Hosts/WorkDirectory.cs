using Jason.Contracts.Api;
using Jason.Contracts.Skills;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// What preparing a work directory came to: ready, or the one reason the attempt cannot run. A refusal ends the
/// attempt before a child exists and carries the code it ends with, because a skill that quietly did not arrive
/// reads afterwards as a model that ignored it.
/// </summary>
/// <param name="Rescued">
/// This attempt lost a race with a deployment and was saved by reading the tree a second time. It is on the
/// report so a test can prove the second read ran at all — a race test that never exercised the rescue would
/// pass for the wrong reason — and so the launcher can log that it happened.
/// </param>
public sealed record WorkDirectoryReport(string? RefusalCode, string? Message, RoleSkillDto? Skill = null, bool Rescued = false)
{
    public static WorkDirectoryReport Ready { get; } = new(null, null);

    /// <summary>Ready, and carrying what the role was taught so the attempt can record it.</summary>
    public static WorkDirectoryReport Taught(RoleSkillDto skill, bool rescued = false) => new(null, null, skill, rescued);

    public static WorkDirectoryReport Refused(string code, string message) => new(code, message);
}

/// <summary>
/// What the agent finds in the directory it is started in: what it may not do, and how to do the job.
/// <para>
/// Only one half of a policy can travel here. A directory the runtime creates is not a workspace the host
/// trusts, and an allow rule in an untrusted directory is ignored without being reported — so what the agent
/// may do goes on the command line, where it is read, and what it may not do goes here, where a deny rule is
/// honoured whether or not the directory is trusted. Verified against Claude Code 2.1.275.
/// </para>
/// </summary>
public static class WorkDirectory
{
    /// <summary>The directory a host reads its project settings and project skills from.</summary>
    public const string HostDirectory = ".claude";

    /// <summary>Where a host looks a skill up: one directory per skill, named as the skill names itself.</summary>
    public const string SkillsDirectory = "skills";

    /// <summary>The file a skill introduces itself in; a directory without one teaches a host nothing.</summary>
    public const string SkillFile = RoleSkillRules.SkillFile;

    private static readonly JsonSerializerOptions Settings = new() { WriteIndented = true };

    /// <summary>
    /// Writes the settings file, then copies the role's skill in under the name the host will look it up by.
    /// </summary>
    /// <param name="workDir">The attempt's own directory; the child's working directory.</param>
    /// <param name="deny">What the launched agent may not do, in the host's own vocabulary.</param>
    /// <param name="role">The role being run, which is also the name its skill must carry.</param>
    /// <param name="roleSkillsRoot">Where role skills are kept: one directory per role.</param>
    /// <param name="maxSkillBytes">How large a role's skill may be before it is left where it is.</param>
    public static WorkDirectoryReport Prepare(
        string workDir,
        IReadOnlyList<string> deny,
        string? role,
        string roleSkillsRoot,
        int maxSkillBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(deny);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleSkillsRoot);

        var host = Path.Combine(workDir, HostDirectory);
        Directory.CreateDirectory(host);

        var settings = new JsonObject
        {
            ["permissions"] = new JsonObject
            {
                ["deny"] = new JsonArray([.. deny.Select(rule => JsonValue.Create(rule))]),
            },
        };
        File.WriteAllText(Path.Combine(host, "settings.json"), settings.ToJsonString(Settings));

        return Teach(host, role, roleSkillsRoot, maxSkillBytes);
    }

    /// <summary>
    /// The role's skill, if it has one. A role's name is the roster's own spelling — lowercase letters, digits
    /// and hyphens — so it is one directory name and never a path; anything else names no skill directory here.
    /// </summary>
    /// <remarks>
    /// What makes a skill deliverable is <see cref="RoleSkillRules"/>, in the contracts, because an installer
    /// writing into this root has to apply the same two rules before it writes: a deployment that fails them
    /// turns a role with no skill — which runs — into a role whose every launch is refused.
    /// </remarks>
    private static WorkDirectoryReport Teach(string host, string? role, string roleSkillsRoot, int maxSkillBytes)
    {
        if (string.IsNullOrWhiteSpace(role) || role != Path.GetFileName(role) || role is "." or "..")
        {
            return WorkDirectoryReport.Ready;
        }

        try
        {
            return Deliver(host, role, roleSkillsRoot, maxSkillBytes, rescued: false);
        }
        catch (RoleSkillTreeChanged)
        {
            // A deployment renamed this role's tree while it was being read. Read it again from the top: the
            // second read sees the tree that arrived, or sees nothing, and either of those is a whole answer.
        }

        var lost = Path.Combine(host, SkillsDirectory, role);
        Discard(lost);

        try
        {
            return Deliver(host, role, roleSkillsRoot, maxSkillBytes, rescued: true);
        }
        catch (RoleSkillTreeChanged changed)
        {
            // Twice running, which takes two deployments inside one launch's copy. Refused rather than run
            // untaught: a skill that was configured and did not arrive is what this launcher already refuses
            // over, and the role would otherwise do the job untaught at the price of a real launch.
            Discard(lost);
            return WorkDirectoryReport.Refused(
                AttemptErrors.RoleSkillUnreadable,
                $"The skill for role '{role}' could not be read consistently: a deployment was in flight and a second read of it lost the race too. {changed.Message}");
        }
    }

    /// <summary>One read of the role's tree, and the copy of exactly what that read found.</summary>
    private static WorkDirectoryReport Deliver(string host, string role, string roleSkillsRoot, int maxSkillBytes, bool rescued)
    {
        var source = Path.Combine(roleSkillsRoot, role);
        var reading = RoleSkillRules.Read(source, role, maxSkillBytes);
        if (!reading.Exists)
        {
            // The brief travels in the envelope. A role without a skill was never given one, which is not a
            // failure of this attempt — but it is recorded, because "was this role taught anything?" is a
            // question about a finished attempt that nothing else can answer afterwards.
            return WorkDirectoryReport.Taught(new RoleSkillDto(false, role, 0), rescued);
        }

        if (reading.Problem is { } problem)
        {
            return WorkDirectoryReport.Refused(AttemptErrors.RoleSkillInvalid, problem.Message);
        }

        BetweenReadAndCopy.Value?.Invoke();

        var target = Path.Combine(host, SkillsDirectory, role);
        foreach (var file in reading.Files)
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            try
            {
                File.Copy(file, copy, overwrite: true);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // The path was read before the rename and is dead after it. On a platform where a directory
                // can be renamed out from under an open tree, this is where losing the race is felt.
                throw new RoleSkillTreeChanged(file, exception);
            }
        }

        // The copy reads by path, and a path resolves to whatever is there when it is used rather than to what
        // the walk saw. So a deployment that renamed a same-shaped tree into place mid-copy would be copied
        // without a single failure, and reported with the byte count of the tree that is no longer there.
        // Reading the tree again brackets the copy: a different file list or a different total means it moved.
        var after = RoleSkillRules.Read(source, role, maxSkillBytes);
        if (!after.Exists || after.Bytes != reading.Bytes || !after.Files.SequenceEqual(reading.Files, StringComparer.Ordinal))
        {
            throw new RoleSkillTreeChanged(source, new IOException("The tree read before the copy is not the tree that is there after it."));
        }

        return WorkDirectoryReport.Taught(new RoleSkillDto(true, role, reading.Bytes), rescued);
    }

    /// <summary>
    /// What a lost read had already copied. It goes before the second read, so no part of the tree that was
    /// there survives into the one that arrived — a blend is the outcome this whole arrangement exists to
    /// make impossible.
    /// </summary>
    private static void Discard(string target)
    {
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The second read overwrites everything it copies, so a directory that will not go is survivable.
            // What would not be is a file the new tree does not have, and that is what this removes.
        }
    }

    /// <summary>
    /// Runs once between reading this role's tree and copying it, so a test can make the rename a deployment
    /// performs happen at the one instant that matters. Null everywhere but that test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <see cref="AsyncLocal{T}"/> and not a static field. A static one is process-global mutable state in
    /// an assembly whose test collections run in parallel, set from inside its own callback: a test that armed
    /// it would arm it for whatever ran beside it, and the failure would land in the other test.
    /// </para>
    /// <para>
    /// It exists because the alternative is a probabilistic test of the one path that exists to make a
    /// probabilistic failure impossible — a guard that can pass without ever exercising what it guards. It is
    /// why this assembly carries the repository's only friend declaration.
    /// </para>
    /// </remarks>
    internal static AsyncLocal<Action?> BetweenReadAndCopy { get; } = new();
}
