using System.Globalization;

namespace Jason.Contracts.Skills;

/// <summary>A skill's tree nests deeper than anything reading it will follow.</summary>
/// <remarks>
/// A refusal rather than a recursion, because the runtime walks these directories on every
/// <c>system.info</c> call: unbounded, a deep enough tree ends the process, and a stack overflow cannot be
/// caught by anything.
/// </remarks>
public sealed class RoleSkillTooDeep(string path, int depth)
    : IOException($"'{path}' nests deeper than {depth} directories, which is deeper than a skill is read.");

/// <summary>The tree moved while it was being read.</summary>
/// <remarks>
/// Raised rather than swallowed because it is the one IO failure that is not a fault: something renamed the
/// role's directory, which is what a deployment does. The caller's answer is to read again, not to report an
/// unreadable skill — and before this existed the launcher could not tell the two apart, so it reported
/// having been taught a tree it had only partly copied.
/// </remarks>
public sealed class RoleSkillTreeChanged(string path, Exception cause)
    : IOException($"'{path}' moved while it was being read.", cause);

/// <summary>Why a role's skill cannot be given to the role, in the words the launcher refuses with.</summary>
public sealed record RoleSkillProblem(string Message);

/// <summary>
/// One reading of one role directory: what is in it, how large it is, and what is wrong with it — all from a
/// single walk.
/// </summary>
/// <remarks>
/// One walk and not two, because the two callers are a launcher copying the tree and a runtime reporting its
/// size, and a second walk can see a different tree from the first: a deployment renames these directories. A
/// total measured from a different walk than the file list is a measurement of something else.
/// </remarks>
/// <param name="Exists">
/// False when the directory is not there, which is not a problem: a role with no skill was never given one.
/// </param>
/// <param name="HasSkillFile">
/// False when the directory holds no <see cref="RoleSkillRules.SkillFile"/>. The launcher delivers such a
/// directory and a host loads nothing from it, so it is not a refusal — but it is a deployment nobody meant
/// to make, and the caller that is writing one can still refuse to.
/// </param>
/// <param name="Measurements">
/// Every file with its own length, in the order the walk found them. A caller checking that a tree did not
/// move under it needs this rather than the total: a change that grows one file by what it takes from another
/// leaves the path list and the sum identical, and is still two trees blended.
/// </param>
public sealed record RoleSkillReading(
    bool Exists,
    bool HasSkillFile,
    IReadOnlyList<string> Files,
    long Bytes,
    RoleSkillProblem? Problem,
    IReadOnlyList<RoleSkillFile> Measurements);

/// <summary>One file of a skill, and how long it was when the walk saw it.</summary>
public sealed record RoleSkillFile(string Path, long Bytes);

/// <summary>
/// The two rules a role's skill must satisfy before a launch can be taught it: it must name itself after the
/// directory it lives in, and its tree must fit the cap the runtime enforces.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the launcher because two programs apply them and only one of them may see the other.
/// The launcher refuses an attempt on these; an installer has to apply them <em>before</em> it writes,
/// because a deployment that fails them turns a role with no skill — which runs — into a role whose every
/// launch is refused. The CLI may never reference the runtime, so the contracts are the only place both can
/// read one rule rather than two that agree until somebody edits one.
/// </para>
/// </remarks>
public static class RoleSkillRules
{
    /// <summary>The file a skill introduces itself in; a directory without one teaches a host nothing.</summary>
    public const string SkillFile = "SKILL.md";

    /// <summary>How far into a file a skill's front matter may be looked for; a real one is a handful of lines.</summary>
    private const int FrontMatterLines = 64;

    /// <summary>
    /// One walk: the files, their total, and the two refusals in the order the launcher applies them — the
    /// name first, the size second.
    /// </summary>
    public static RoleSkillReading Read(string roleDirectory, string role, int maxSkillBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (!Directory.Exists(roleDirectory))
        {
            return new RoleSkillReading(false, false, [], 0, null, []);
        }

        var introduction = Path.Combine(roleDirectory, SkillFile);
        var hasSkillFile = File.Exists(introduction);
        if (hasSkillFile && Named(introduction) is var named && named != role)
        {
            // A host answers this mismatch by ignoring the skill and saying nothing, and an agent that was
            // never taught its job reads afterwards as a model that refused to do it. Reported here instead,
            // where it can be read: a skill is looked up by the directory it lives in, and must name itself
            // the same way.
            return new RoleSkillReading(
                true,
                true,
                [],
                0,
                new RoleSkillProblem(
                    $"The skill in '{roleDirectory}' names itself '{named ?? "nothing"}', and role '{role}' looks its skill up as '{role}'. A host answers that mismatch by ignoring the skill without reporting it."),
                []);
        }

        var files = Files(roleDirectory);
        var measurements = Measurements(files);
        var bytes = measurements.Sum(file => file.Bytes);

        // A skill that was configured and did not arrive is the failure worth refusing over: the role would do
        // the job untaught, at the price of a real launch, and the only trace would be a log line nobody is
        // reading at the time. Left behind for being too large is the same thing to the agent as left behind
        // for being misnamed, so it is answered the same way.
        return bytes > maxSkillBytes
            ? new RoleSkillReading(
                true,
                hasSkillFile,
                files,
                bytes,
                new RoleSkillProblem(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The skill in '{roleDirectory}' is {bytes} bytes and Roles:MaxSkillBytes allows {maxSkillBytes}, so it cannot be given to the role. Trim the skill, or raise Roles:MaxSkillBytes.")),
                measurements)
            : new RoleSkillReading(true, hasSkillFile, files, bytes, null, measurements);
    }

    /// <summary>
    /// Every real file under the skill, and nothing a link points at: a link reaches outside the directory
    /// somebody meant to hand over, so it is neither copied nor followed — on the way down through the
    /// directories as much as at the file itself.
    /// </summary>
    public static IReadOnlyList<string> Files(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var files = new List<string>();
        Walk(directory, files, MaxDepth);
        return files;
    }

    /// <summary>
    /// How deep a skill may nest. The walk recurses, and the runtime now runs it on every
    /// <c>system.info</c> call rather than only at a launch — so a deeply nested tree would end the runtime
    /// process outright, and a stack overflow is the one failure nothing can catch. A skill is a handful of
    /// files beside a document; anything past this is not one.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>Every file <see cref="Files"/> found, with its own length.</summary>
    public static IReadOnlyList<RoleSkillFile> Measurements(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return [.. files.Select(file => new RoleSkillFile(file, Length(file)))];
    }

    /// <summary>The total of what <see cref="Files"/> found.</summary>
    public static long Measure(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        long bytes = 0;
        foreach (var file in files)
        {
            bytes += Length(file);
        }

        return bytes;
    }

    /// <summary>
    /// The name a skill gives itself, or null. Front matter is the first block between two <c>---</c> lines; a
    /// file without one names nothing, which a host answers exactly as it answers a wrong name — it loads no
    /// skill — so both are treated the same way. A file that is a link names nothing either: it is not copied,
    /// so what a host would read is not what was inspected here.
    /// </summary>
    public static string? Named(string skillFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillFile);

        if (IsLink(skillFile))
        {
            return null;
        }

        string[] lines;
        try
        {
            lines = [.. File.ReadLines(skillFile).Take(FrontMatterLines)];
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // The one place this mattered most and was answered worst. A file that is gone is not a file that
            // names nothing: the caller reads null as "the skill names itself something else" and refuses the
            // attempt as invalid, sending the operator to fix front matter that is already correct. And
            // SKILL.md is the one file every deployment touches, so this is where a rename is most likely to
            // be met. It is the tree moving, and the answer is to read again.
            throw new RoleSkillTreeChanged(skillFile, exception);
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

    private static void Walk(string directory, List<string> files, int depth)
    {
        if (depth <= 0)
        {
            throw new RoleSkillTooDeep(directory, MaxDepth);
        }

        IReadOnlyList<string> entries;
        IReadOnlyList<string> children;
        try
        {
            entries = Directory.GetFiles(directory);
            children = Directory.GetDirectories(directory);
        }
        catch (DirectoryNotFoundException exception)
        {
            // The directory was there when this walk reached it and is not there now, which is what a
            // deployment renaming into place looks like from in here.
            throw new RoleSkillTreeChanged(directory, exception);
        }

        foreach (var file in entries)
        {
            if (!IsLink(file))
            {
                files.Add(file);
            }
        }

        foreach (var child in children)
        {
            if (IsLink(child))
            {
                continue;
            }

            Walk(child, files, depth - 1);
        }
    }

    /// <summary>
    /// Whether this path is a link, and therefore neither copied nor followed.
    /// </summary>
    /// <remarks>
    /// Unreadable answers true — unreadable is not copyable either, and this is the cheapest way to say so.
    /// A path that is <em>gone</em> does not, because that is not a link and not an unreadable file: it is
    /// this walk losing a race with a deployment, and answering true would drop the file from the tree being
    /// delivered and report the rest as a whole skill.
    /// </remarks>
    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new RoleSkillTreeChanged(path, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    /// <summary>
    /// One file's length. A file that cannot be read counts as nothing, which is what this has always done;
    /// a file that is not there any more is the signal a rename gives, and counting it as nothing would
    /// understate the bytes an attempt records while reporting the teach as complete.
    /// </summary>
    private static long Length(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new RoleSkillTreeChanged(file, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
