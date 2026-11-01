using System.Runtime.InteropServices;
using Jason.Contracts;

namespace Jason.Cli.Doctor;

/// <summary>
/// Where a problem goes once <c>jason doctor</c> has written its file: an issue in the public repository, through its
/// problem form, with the file attached — or, for a file made with the journal's entries, the support address and never
/// an issue. The command leads the person to the form and sends nothing: opening the issue is the person's act, and
/// the command keeps its promise of reaching no network.
/// </summary>
public static class ProblemReport
{
    /// <summary>The form's file under <c>.github/ISSUE_TEMPLATE/</c>, which the address names.</summary>
    public const string Template = "problem.yml";

    /// <summary>The form's field for the version, filled in from this build.</summary>
    public const string VersionField = "version";

    /// <summary>The form's field for the platform, filled in from this machine.</summary>
    public const string PlatformField = "platform";

    /// <summary>The form's address as the help states it, the two filled-in fields by name.</summary>
    public const string UrlShape = PublicRepository.Url + "/issues/new?template=" + Template + "&" + VersionField + "=<version>&" + PlatformField + "=<platform>";

    /// <summary>What a default file is and where a file with the journal's entries goes instead, in one sentence each.</summary>
    public const string MadeToBeShared =
        "The file is made to be shared; read it first. A file made with --include-journal goes only to the support address under Getting help, "
        + PublicRepository.GettingHelp + ", never to an issue.";

    /// <summary>What a file made with the journal's entries says first.</summary>
    public const string JournalToSupport =
        "This file was made with --include-journal, so it goes only to the support address under Getting help, " + PublicRepository.GettingHelp + ", never to an issue.";

    /// <summary>
    /// The platform this command runs on, as a bundle's versions name it: the system's description and its architecture.
    /// </summary>
    public static string Platform => $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    /// <summary>The form's address with this build's version and this machine's platform filled in, each encoded as a query value is.</summary>
    public static string Url =>
        $"{PublicRepository.Url}/issues/new?template={Template}&{VersionField}={Uri.EscapeDataString(JasonVersion.Current)}&{PlatformField}={Uri.EscapeDataString(Platform)}";

    /// <summary>
    /// What is printed after a file is written and its warnings: the form, then what the file is for; with the journal's
    /// entries, whose warnings have just said that the file goes to the support address only (<see cref="JournalToSupport"/>
    /// among them), the form for a file made without them.
    /// </summary>
    public static IReadOnlyList<string> Lines(bool includeJournal) =>
        includeJournal
            ? ["A problem is reported as an issue with a file made without --include-journal, which is made to be shared; read it first: " + Url]
            : ["Report a problem as an issue, with this file attached: " + Url, MadeToBeShared];
}
