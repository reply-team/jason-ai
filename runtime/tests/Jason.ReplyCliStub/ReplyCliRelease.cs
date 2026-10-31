using System.Text.RegularExpressions;

namespace Jason.ReplyCliStub;

/// <summary>
/// The reply-cli release this stand-in answers as, which is the oldest one the official package accepts: the first that
/// names the moment a direct send goes out after and the key it is made under, answers the send's reference, and reads a
/// send back by it, with <c>direct-send get</c>, and the first that reads Reply's change feed, with <c>inbox changes</c>,
/// all from 0.17.0; the first that reads <c>REPLY_CLIENT</c>, the name a call says it came through, from 0.17.2; and the
/// first that reads and cancels a LinkedIn send Reply holds for later by its <c>linkedin-scheduled-</c> reference, names a
/// cancel of a send Reply is sending, a cancel Reply could not confirm and a sending schedule that cannot time a send
/// each by a code of its own, and passes on the moment Reply expects to send one waiting for its schedule, from 0.18.0.
/// It is named once, here; the tests hold the package's manifest, its README and its changelog to it, so moving the
/// release is this line and the sentences they read.
/// </summary>
public static partial class ReplyCliRelease
{
    public const string Version = "0.18.0";

    /// <summary>
    /// The User-Agent this release sends to Reply: <c>reply-cli/&lt;version&gt;</c>, with the program named by
    /// <c>REPLY_CLIENT</c> before it when the value is one product token and its version, and the plain one otherwise.
    /// A value of any other shape is left out, silently, exactly as the CLI leaves it out, so a value the CLI would
    /// drop is seen dropped here rather than read as sent.
    /// </summary>
    public static string UserAgent(string? client)
    {
        var own = "reply-cli/" + Version;
        return client is not null && Client().IsMatch(client) ? client + " " + own : own;
    }

    // Copied from reply-cli 0.17.2, src/config.ts, the `CLIENT` pattern: one product token and its version. Its `$` is
    // JavaScript's, the end of the input, which is `\z` here; .NET's `$` would also take a value ending in a newline.
    [GeneratedRegex(@"\A[a-z][a-z0-9-]{0,31}/[0-9A-Za-z.+-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Client();
}
