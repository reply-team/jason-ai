namespace Jason.Contracts.Skills;

/// <summary>
/// Why a role's skill cannot be read or given to the role, as a closed set of words: one per reason the runtime tells
/// apart. A problem's sentence says what is wrong and what to do about it; its code says which problem it is, and is
/// what travels where the sentence does not — a diagnostic bundle withholds the sentence, because it can carry an
/// operating system's message, and keeps the code.
/// </summary>
public static class SkillProblemCodes
{
    /// <summary>The role skills directory is there and cannot be listed.</summary>
    public const string SkillsDirectoryUnreadable = "skills_directory_unreadable";

    /// <summary>A role's directory could not be read through: the operating system refused part of it.</summary>
    public const string RoleDirectoryUnreadable = "role_directory_unreadable";

    /// <summary>A role's directory nests deeper than a skill is read.</summary>
    public const string RoleDirectoryTooDeep = "role_directory_too_deep";

    /// <summary>A role's directory moved while it was being read, as a deployment renaming into place does.</summary>
    public const string RoleDirectoryChanged = "role_directory_changed";

    /// <summary>A role's directory holds no <see cref="RoleSkillRules.SkillFile"/>: copied, and it teaches nothing.</summary>
    public const string SkillFileMissing = "skill_file_missing";

    /// <summary>The skill file is a link, which a launch does not copy.</summary>
    public const string SkillFileIsLink = "skill_file_is_link";

    /// <summary>The skill file could not be read, or the operating system would not say what it is.</summary>
    public const string SkillFileUnreadable = "skill_file_unreadable";

    /// <summary>The skill file gives itself no name: no front matter, no name in it, or an empty one.</summary>
    public const string SkillNameMissing = "skill_name_missing";

    /// <summary>The skill names itself something other than the role it is deployed as.</summary>
    public const string SkillNameMismatch = "skill_name_mismatch";

    /// <summary>The skill's tree is over <c>Roles:MaxSkillBytes</c>.</summary>
    public const string SkillTooLarge = "skill_too_large";

    /// <summary>Every code, in the order the page's table lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SkillsDirectoryUnreadable,
        RoleDirectoryUnreadable,
        RoleDirectoryTooDeep,
        RoleDirectoryChanged,
        SkillFileMissing,
        SkillFileIsLink,
        SkillFileUnreadable,
        SkillNameMissing,
        SkillNameMismatch,
        SkillTooLarge,
    ];

    /// <summary>The words an operating system's refusal is named by, beside the codes that carry one.</summary>
    public static IReadOnlyList<string> Exceptions { get; } =
        [nameof(UnauthorizedAccessException), nameof(IOException), nameof(ArgumentException), OtherException];

    /// <summary>The word for an exception that is none of the three.</summary>
    public const string OtherException = "other";

    /// <summary>
    /// An exception named by the closed word it is an instance of, in this order: a refusal of access, any other
    /// input or output failure, an argument the system would not take, else <see cref="OtherException"/>. A file path
    /// too long or a directory not found reads as <c>IOException</c>; the type's own name, which could be anybody's, never
    /// travels.
    /// </summary>
    public static string ExceptionWord(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UnauthorizedAccessException => nameof(UnauthorizedAccessException),
            IOException => nameof(IOException),
            ArgumentException => nameof(ArgumentException),
            _ => OtherException,
        };
    }

    /// <summary>
    /// The code for a role directory that could not be read through: a tree that nests too deep, or that moved, has its
    /// own; anything else the operating system refused is <see cref="RoleDirectoryUnreadable"/>.
    /// </summary>
    public static string OfUnreadable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            RoleSkillTooDeep => RoleDirectoryTooDeep,
            RoleSkillTreeChanged => RoleDirectoryChanged,
            _ => RoleDirectoryUnreadable,
        };
    }
}
