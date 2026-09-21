using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Jason.Contracts.Json;

namespace Jason.Cli.Skills;

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
public sealed record SkillsDeployment(
    string Pack,
    string Source,
    string Ref,
    bool RefOverridden,
    string Commit,
    DateTimeOffset InstalledAt,
    IReadOnlyList<DeployedFile> Files);

/// <summary>Every pack Jason put in one root, in one file beside them.</summary>
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

        return record;
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

    /// <summary>The digest of one file's bytes, spelled one way so two of them can be compared.</summary>
    public static string Digest(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
