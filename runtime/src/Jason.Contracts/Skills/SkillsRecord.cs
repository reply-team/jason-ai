using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Jason.Contracts.Json;

namespace Jason.Contracts.Skills;

/// <summary>There is a record file in this root and it is not one this build can read.</summary>
/// <remarks>
/// Distinct from "there is none", which is an answer. A root whose record cannot be read is reported as
/// unknown and nothing guesses at it: an uninstall that took an unreadable record for an absent one would
/// remove nothing and report success, and the paths it should have removed are exactly the ones it no longer
/// knows about.
/// </remarks>
public sealed class SkillsRecordUnreadable(string root, string reason, Exception? cause = null)
    : Exception($"The skills record in '{root}' could not be read: {reason}", cause);

/// <summary>One file this installer wrote, and the digest of what it wrote there.</summary>
public sealed record DeployedFile(string Path, string Sha256);

/// <summary>
/// One pack as deployed into one root: where it came from, which ref, which commit, when, and every path
/// written with a digest each.
/// </summary>
/// <param name="RefOverridden">
/// True when the ref came from a flag rather than from the pin this build carries, so that "is this current?"
/// can be answered honestly about a deployment that deliberately is not.
/// </param>
/// <param name="Commit">
/// The commit the files came from, or null when the source was a directory read where it stood. A working
/// tree has no commit, and recording one would say the deployment came from a state nothing can go back to.
/// </param>
public sealed record SkillsDeployment(
    string Pack,
    string Source,
    string Ref,
    bool RefOverridden,
    string? Commit,
    DateTimeOffset InstalledAt,
    IReadOnlyList<DeployedFile> Files);

/// <summary>Every pack Jason put in one root, in one file beside them.</summary>
/// <remarks>
/// In the contracts rather than in the CLI that writes it, for the same reason the rules are: two programs
/// need it and only one of them may see the other. The launcher reads it to tell a role that never had a
/// skill from one whose directory is being replaced this instant — the first runs untaught and is recorded
/// as such, the second is a race and must not be.
/// </remarks>
/// <remarks>
/// One file per root rather than one per pack, because a harness root holds skills from everywhere and the
/// question asked of it is "what did Jason put here?" — which is one question with one answer.
/// </remarks>
public sealed record SkillsRecord(int Version, IReadOnlyList<SkillsDeployment> Packs)
{
    /// <summary>The name it goes under, beside the skills rather than inside any of them.</summary>
    public const string FileName = ".jason-skills.json";

    /// <summary>What this build writes, and the highest it knows how to read.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The record in a root, or null where there is none. Unreadable is <em>not</em> null: it throws, because
    /// the two answers lead to opposite acts.
    /// </summary>
    public static SkillsRecord? Read(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var file = System.IO.Path.Combine(root, FileName);
        string text;
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            text = File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SkillsRecordUnreadable(root, exception.Message, exception);
        }

        SkillsRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<SkillsRecord>(text, JasonJson.Options);
        }
        catch (JsonException exception)
        {
            throw new SkillsRecordUnreadable(root, "it is not the JSON document this build writes", exception);
        }

        if (record is null)
        {
            throw new SkillsRecordUnreadable(root, "it is empty");
        }

        if (record.Version > CurrentVersion)
        {
            // A newer Jason wrote it. Reading it as though it were this version is how an uninstall removes
            // the paths it understands and leaves the ones it does not, while reporting that it removed
            // everything.
            throw new SkillsRecordUnreadable(
                root,
                string.Create(CultureInfo.InvariantCulture, $"it is version {record.Version} and this build reads up to {CurrentVersion}"));
        }

        Validate(root, record);
        return record;
    }

    /// <summary>
    /// Everything a record names is there, and every path it names is a file under the root it sits in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deserializer fills in what the document says, and a document can say <c>null</c> for a list: every
    /// verb that read such a record then threw a NullReferenceException naming no file, and an uninstall that met
    /// one could remove nothing at all. So a missing part is the same answer a record this build cannot parse
    /// gets — unreadable, naming the root.
    /// </para>
    /// <para>
    /// And the paths are held to the root. An uninstall removes what a record names, and the installer compares
    /// what is on disk against it; a record naming <c>../outside/SKILL.md</c>, or a rooted path, had the
    /// uninstall remove a file outside the root and empty the directories above it. This product writes a
    /// record's paths relative, with forward slashes, and never with a colon or a backslash in them, so anything
    /// else is not a record it wrote.
    /// </para>
    /// </remarks>
    private static void Validate(string root, SkillsRecord record)
    {
        if (record.Packs is null)
        {
            throw new SkillsRecordUnreadable(root, "it names no packs");
        }

        foreach (var pack in record.Packs)
        {
            if (pack is null || string.IsNullOrWhiteSpace(pack.Pack) || pack.Source is null || pack.Ref is null)
            {
                throw new SkillsRecordUnreadable(root, "a pack in it has no name, source or ref");
            }

            if (pack.Files is null)
            {
                throw new SkillsRecordUnreadable(root, $"the pack '{pack.Pack}' in it names no files");
            }

            foreach (var file in pack.Files)
            {
                if (file is null || file.Path is null || string.IsNullOrWhiteSpace(file.Sha256))
                {
                    throw new SkillsRecordUnreadable(root, $"a file of the pack '{pack.Pack}' in it has no path or no digest");
                }

                if (!Inside(root, file.Path))
                {
                    throw new SkillsRecordUnreadable(
                        root,
                        $"it names '{file.Path}', which is not a file under that root, and nothing outside the root a record sits in is ever removed or compared by it");
                }
            }
        }
    }

    /// <summary>Whether a path a record names is a file under its root, spelled the way this product spells one.</summary>
    private static bool Inside(string root, string path)
    {
        if (path.Length == 0 || path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal)
            || path.Split('/').Any(segment => segment == ".."))
        {
            return false;
        }

        var outer = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        return full.StartsWith(outer, StringComparison.Ordinal) && full.Length > outer.Length;
    }

    /// <summary>
    /// Written whole or not at all: to a temporary sibling, then renamed over the old one. A rename of a file
    /// is atomic, and a half-written record is the one state nothing could recover from — it would read as a
    /// finished deployment and name files that were never written.
    /// </summary>
    public static void Write(string root, SkillsRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(root);
        var file = System.IO.Path.Combine(root, FileName);
        var staged = file + ".writing";
        File.WriteAllText(staged, JsonSerializer.Serialize(record, JasonJson.Options));
        File.Move(staged, file, overwrite: true);
    }

    /// <summary>
    /// The role names a record in the role skills root says are deployed there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what tells a role that never had a skill from one whose directory is being replaced this
    /// instant. Both look identical from the disk — the directory is not there — and they are opposite facts:
    /// the first runs untaught and is recorded as such, the second is a race and must never be reported as a
    /// role that was taught nothing.
    /// </para>
    /// <para>
    /// It never throws. A launch is not the place to discover that a record is malformed, and answering "no
    /// record" restores exactly what the launcher did before this existed.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> RolesDeployedIn(string roleSkillsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleSkillsRoot);

        SkillsRecord? record;
        try
        {
            record = Read(roleSkillsRoot);
        }
        catch (SkillsRecordUnreadable)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in record?.Packs.SelectMany(pack => pack.Files) ?? [])
        {
            var segment = file.Path.Split('/', 2)[0];
            if (segment.Length > 0)
            {
                roles.Add(segment);
            }
        }

        return roles;
    }

    /// <summary>The digest of one file's bytes, spelled one way so two of them can be compared.</summary>
    public static string Digest(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
