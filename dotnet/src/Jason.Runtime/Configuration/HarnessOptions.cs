using Jason.Contracts.Harness;

namespace Jason.Runtime.Configuration;

/// <summary>
/// Where the harness root is: the visible folder that holds the person's memory files, one folder for each project and
/// each campaign below it (<c>docs/projects.md</c>). Read through its seam at every create and every move, so an edit
/// applies to the next one; it moves nothing already made and re-records nothing.
/// </summary>
public sealed class HarnessOptions
{
    public const string Section = "Harness";

    /// <summary>The root's name when the settings name none: a folder of this name in the home directory, beside <c>~/.jason</c>.</summary>
    public const string DefaultFolder = HarnessRootRule.DefaultFolder;

    /// <summary>
    /// The root as an absolute path. Left out, written as nothing or as white space, it is <see cref="DefaultFolder"/> in
    /// the home directory. Never the home directory, a folder holding it or the top of a drive, nor the data directory, a
    /// folder inside it or one holding it.
    /// </summary>
    public string? Root { get; set; }
}
