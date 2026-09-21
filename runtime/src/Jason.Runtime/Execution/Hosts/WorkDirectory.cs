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
public sealed record WorkDirectoryReport(string? RefusalCode, string? Message, RoleSkillDto? Skill = null)
{
    public static WorkDirectoryReport Ready { get; } = new(null, null);

    /// <summary>Ready, and carrying what the role was taught so the attempt can record it.</summary>
    public static WorkDirectoryReport Taught(RoleSkillDto skill) => new(null, null, skill);

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

        var source = Path.Combine(roleSkillsRoot, role);
        var reading = RoleSkillRules.Read(source, role, maxSkillBytes);
        if (!reading.Exists)
        {
            // The brief travels in the envelope. A role without a skill was never given one, which is not a
            // failure of this attempt — but it is recorded, because "was this role taught anything?" is a
            // question about a finished attempt that nothing else can answer afterwards.
            return WorkDirectoryReport.Taught(new RoleSkillDto(false, role, 0));
        }

        if (reading.Problem is { } problem)
        {
            return WorkDirectoryReport.Refused(AttemptErrors.RoleSkillInvalid, problem.Message);
        }

        var target = Path.Combine(host, SkillsDirectory, role);
        foreach (var file in reading.Files)
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        return WorkDirectoryReport.Taught(new RoleSkillDto(true, role, reading.Bytes));
    }
}
