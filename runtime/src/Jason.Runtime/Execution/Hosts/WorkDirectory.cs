using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// What preparing a work directory came to: ready, or the one reason the attempt cannot run. A refusal ends the
/// attempt before a child exists and carries the code it ends with, because a skill that quietly did not arrive
/// reads afterwards as a model that ignored it.
/// </summary>
public sealed record WorkDirectoryReport(string? RefusalCode, string? Message)
{
    public static WorkDirectoryReport Ready { get; } = new(null, null);

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
    public const string SkillFile = "SKILL.md";

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
    private static WorkDirectoryReport Teach(string host, string? role, string roleSkillsRoot, int maxSkillBytes)
    {
        if (string.IsNullOrWhiteSpace(role) || role != Path.GetFileName(role) || role is "." or "..")
        {
            return WorkDirectoryReport.Ready;
        }

        var source = Path.Combine(roleSkillsRoot, role);
        if (!Directory.Exists(source))
        {
            // The brief travels in the envelope. A role without a skill was never given one, which is not a
            // failure of this attempt.
            return WorkDirectoryReport.Ready;
        }

        var introduction = Path.Combine(source, SkillFile);
        if (File.Exists(introduction) && Named(introduction) is var named && named != role)
        {
            // A host answers this by ignoring the skill and saying nothing, and an agent that was never taught
            // its job reads afterwards as a model that refused to do it. Refused here instead, where it can be
            // read: a skill is looked up by the directory it lives in, and it must name itself the same way.
            return WorkDirectoryReport.Refused(
                AttemptErrors.RoleSkillInvalid,
                $"The skill in '{source}' names itself '{named ?? "nothing"}', and role '{role}' looks its skill up as '{role}'. A host answers that mismatch by ignoring the skill without reporting it.");
        }

        var files = new List<string>();
        long bytes = 0;
        foreach (var file in Files(source))
        {
            bytes += Length(file);
            files.Add(file);
        }

        // A skill that was configured and did not arrive is the failure worth refusing over: the role would do
        // the job untaught, at the price of a real launch, and the only trace would be a log line nobody is
        // reading at the time. Left behind for being too large is the same thing to the agent as left behind for
        // being misnamed, so it is answered the same way.
        if (bytes > maxSkillBytes)
        {
            return WorkDirectoryReport.Refused(
                AttemptErrors.RoleSkillInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The skill in '{source}' is {bytes} bytes and Roles:MaxSkillBytes allows {maxSkillBytes}, so it cannot be given to the role. Trim the skill, or raise Roles:MaxSkillBytes."));
        }

        var target = Path.Combine(host, SkillsDirectory, role);
        foreach (var file in files)
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        return WorkDirectoryReport.Ready;
    }

    /// <summary>
    /// Every real file under the skill, and nothing a link points at: a link reaches outside the directory
    /// somebody meant to hand over, so it is neither copied nor followed — on the way down through the
    /// directories as much as at the file itself.
    /// </summary>
    private static IEnumerable<string> Files(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!IsLink(file))
            {
                yield return file;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (IsLink(child))
            {
                continue;
            }

            foreach (var file in Files(child))
            {
                yield return file;
            }
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Unreadable is not copyable either, and this is the cheapest way to say so.
            return true;
        }
    }

    private static long Length(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>How far into a file a skill's front matter may be looked for; a real one is a handful of lines.</summary>
    private const int FrontMatterLines = 64;

    /// <summary>
    /// The name a skill gives itself, or null. Front matter is the first block between two <c>---</c> lines; a
    /// file without one names nothing, which a host answers exactly as it answers a wrong name — it loads no
    /// skill — so both are refused the same way. A file that is a link names nothing either: it is not copied,
    /// so what a host would read is not what was inspected here.
    /// </summary>
    private static string? Named(string file)
    {
        if (IsLink(file))
        {
            return null;
        }

        string[] lines;
        try
        {
            lines = [.. File.ReadLines(file).Take(FrontMatterLines)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        foreach (var line in lines.Skip(1))
        {
            if (line.Trim() == "---")
            {
                break;
            }

            if (!line.StartsWith("name:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line["name:".Length..].Trim().Trim('"', '\'');
            return value.Length == 0 ? null : value;
        }

        return null;
    }
}
