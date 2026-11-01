namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The two lines an install says about where Jason will keep what it learns about the person's business, as both scripts
/// print them and the install page shows them. One text for the scripts' runs, their source and the page, so that the
/// three cannot come to say different things.
/// </summary>
internal static class HarnessRootLines
{
    /// <summary>The second line: where a campaign that names no project goes.</summary>
    public const string DefaultProject =
        "Campaigns you create without naming a project go to the project `Default`; rename it, or make one project per product.";

    /// <summary>How the first line opens, for finding it whatever the path in it.</summary>
    public const string Opening = "Jason keeps what it learns about your business in plain files you can read and edit";

    /// <summary>
    /// The first line, with the folder the harness root's default is on that machine. "By default", because a script cannot
    /// know the root a runtime uses: the settings can name another, and only the runtime reads them.
    /// </summary>
    public static string Where(string root) =>
        $"{Opening}, one folder per project and campaign, in `{root}` by default; to keep them somewhere else, set `Harness:Root` in the settings before your first campaign.";
}
