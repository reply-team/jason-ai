using System.Security;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// The XML a Windows toast is made from, as a plain string: a title and lines of text, each escaped as XML text, and
/// where a click goes, and for one item the Copy prompt button. Nothing here reaches Windows, so the text is the same on
/// every operating system a test runs on.
/// </summary>
public static class ToastXml
{
    /// <param name="launch">
    /// The <c>jason:</c> link a click launches by protocol, or null for a toast a click opens nothing from: the one shown
    /// where the link registration is another program's or could not be written.
    /// </param>
    /// <param name="copyPrompt">
    /// The <c>jason:</c> link the Copy prompt button launches by protocol, or null for a toast with no button: the list's,
    /// and any toast a click opens nothing from.
    /// </param>
    public static string Compose(string title, IReadOnlyList<string> lines, string? launch, string? copyPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(lines);
        var texts = string.Concat(lines.Select(line => $"<text>{SecurityElement.Escape(line)}</text>"));
        var toast = launch is null ? "<toast>" : $"<toast activationType=\"protocol\" launch=\"{SecurityElement.Escape(launch)}\">";
        return toast
            + "<visual><binding template=\"ToastGeneric\">"
            + $"<text>{SecurityElement.Escape(title)}</text>{texts}"
            + "</binding></visual>"
            + (copyPrompt is null ? string.Empty : $"<actions><action content=\"Copy prompt\" activationType=\"protocol\" arguments=\"{SecurityElement.Escape(copyPrompt)}\"/></actions>")
            + "</toast>";
    }
}
