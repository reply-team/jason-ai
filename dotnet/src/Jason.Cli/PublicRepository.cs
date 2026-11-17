namespace Jason.Cli;

/// <summary>
/// Where Jason is developed in the open, and where a person writes when something is wrong: the one place this program
/// names either. The skills come from its releases or a clone of it, a problem is reported as an issue in it, and a
/// file that carries what people typed goes to the support address its README gives under "Getting help" instead.
/// </summary>
public static class PublicRepository
{
    /// <summary>The repository's address.</summary>
    public const string Url = "https://github.com/reply-team/jason-ai";

    /// <summary>
    /// The README's "Getting help": where the support address is given, to which help with a person's own installation
    /// and a diagnostic file made with the journal's entries are sent — never an issue, which is public the moment it is
    /// submitted. The address itself is the README's to give; this program names no vendor.
    /// </summary>
    public const string GettingHelp = Url + "#getting-help";

    /// <summary>
    /// The release page's section on going back from 0.49, to which <c>jason update rollback</c> points once a command-line install
    /// was moved into Jason Desktop: the one thing it says for now is that the command does not do it.
    /// </summary>
    public const string GoingBackFrom049 = Url + "/blob/main/docs/release-and-update.md#going-back-from-049";
}
