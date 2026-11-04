using Jason.Contracts.Ids;

namespace Jason.Contracts.Notifications;

/// <summary>What a <c>jason:</c> link asks the executable to do.</summary>
public enum AttentionAct
{
    /// <summary>Open the UI on what waits, as <c>jason ui open</c> does.</summary>
    List,

    /// <summary>Open the UI on one item, as <c>jason ui open --approval</c> or <c>--question</c> does.</summary>
    Open,

    /// <summary>
    /// Put one item's prompt on the clipboard, the text <c>approval get --prompt</c> or <c>decision get --prompt</c>
    /// prints, and open nothing.
    /// </summary>
    Prompt,

    /// <summary>
    /// Start the person's own agent host on one item in a new terminal, as <c>jason session open --approval</c> or
    /// <c>--question</c> does, and decide nothing.
    /// </summary>
    Session,
}

/// <summary>Which kind of item a link names.</summary>
public enum AttentionKind
{
    /// <summary>An approval, <c>apr_…</c>, as <c>approval get</c> reads it.</summary>
    Approval,

    /// <summary>A question, a decision <c>dec_…</c>, as <c>decision get</c> reads it.</summary>
    Question,
}

/// <summary>One link, read: what to do, and the item it names where it names one.</summary>
public sealed record AttentionTarget(AttentionAct Act, AttentionKind? Kind, string? Id);

/// <summary>
/// The <c>jason:</c> links, one row per path: what a Windows toast's click carries, and the one parser the executable
/// reads them back with. The toast builds its links from these rows and the handler parses with them, so neither can
/// write or take a link the other does not know. The session link is written elsewhere, by the UI
/// (<c>ui/src/screens/attention/session-link.tsx</c>), which cannot call this table: its screen test holds what it writes,
/// and <c>SessionOpenerTests.The_uis_session_link_and_command_are_ones_this_build_takes</c> holds the same form to the row
/// here and to the parser.
/// </summary>
/// <remarks>
/// <para>
/// A link is <c>jason://attention</c>, the list, or <c>jason://attention/&lt;collection&gt;/&lt;id&gt;</c> with, per row,
/// one more segment or none. Scheme and host are read without regard to case, as a URI's are; the path is compared
/// exactly, lowercase, and one trailing slash is taken. Nothing in a link needs encoding, so nothing is decoded: a
/// <c>%</c>, a <c>?</c> or a <c>#</c> anywhere refuses it, as does an id of any shape but the one
/// <see cref="PublicId.New"/> gives that row's prefix.
/// </para>
/// <para>
/// A path is one row: a new act on an item is a row naming its collection and its last segment, and the parser needs
/// nothing else.
/// </para>
/// </remarks>
public static class AttentionLinks
{
    /// <summary>The scheme, registered per user on Windows.</summary>
    public const string Scheme = "jason";

    /// <summary>The one host a link names.</summary>
    public const string Host = "attention";

    /// <summary>The public-id prefix of an approval.</summary>
    public const string ApprovalPrefix = "apr";

    /// <summary>The public-id prefix of a question, which the runtime keeps as a decision.</summary>
    public const string QuestionPrefix = "dec";

    private const string Start = Scheme + "://";

    /// <summary>The link to what waits.</summary>
    public const string List = Start + Host;

    /// <summary>One path: the collection the item is in, its id's prefix, the segment after the id if any, and the act.</summary>
    private sealed record Row(AttentionKind Kind, string Collection, string Prefix, string? Suffix, AttentionAct Act);

    private static readonly Row[] Rows =
    [
        new(AttentionKind.Approval, "approvals", ApprovalPrefix, null, AttentionAct.Open),
        new(AttentionKind.Question, "questions", QuestionPrefix, null, AttentionAct.Open),
        new(AttentionKind.Approval, "approvals", ApprovalPrefix, "prompt", AttentionAct.Prompt),
        new(AttentionKind.Question, "questions", QuestionPrefix, "prompt", AttentionAct.Prompt),
        new(AttentionKind.Approval, "approvals", ApprovalPrefix, "session", AttentionAct.Session),
        new(AttentionKind.Question, "questions", QuestionPrefix, "session", AttentionAct.Session),
    ];

    /// <summary>The link that opens one item, or null for anything but a well-formed approval or question id.</summary>
    public static string? Item(string? id) => Link(id, AttentionAct.Open);

    /// <summary>
    /// The link that copies one item's prompt, a toast's Copy prompt button, or null for anything but a well-formed
    /// approval or question id.
    /// </summary>
    public static string? Prompt(string? id) => Link(id, AttentionAct.Prompt);

    /// <summary>
    /// The link that starts a session on one item, the UI's Open an AI session link, or null for anything but a
    /// well-formed approval or question id.
    /// </summary>
    public static string? Session(string? id) => Link(id, AttentionAct.Session);

    /// <summary>The link of an act on an item, from the row that holds both, or null where no row does.</summary>
    private static string? Link(string? id, AttentionAct act)
    {
        var row = Rows.FirstOrDefault(row => row.Act == act && PublicId.IsWellFormed(row.Prefix, id));
        return row is null ? null : $"{List}/{row.Collection}/{id}" + (row.Suffix is null ? string.Empty : "/" + row.Suffix);
    }

    /// <summary>The link read, or null for any link the rows do not hold.</summary>
    public static AttentionTarget? Parse(string? uri)
    {
        if (uri is null
            || !uri.StartsWith(Start, StringComparison.OrdinalIgnoreCase)
            || uri.AsSpan().IndexOfAny('%', '?', '#') >= 0)
        {
            return null;
        }

        var rest = uri[Start.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var host = slash < 0 ? rest : rest[..slash];
        if (!string.Equals(host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The slash after the host is the list's one trailing slash; after a path, one more is taken, and never two.
        var path = slash < 0 ? string.Empty : rest[(slash + 1)..];
        if (path.EndsWith('/'))
        {
            path = path[..^1];
            if (path.Length == 0)
            {
                return null;
            }
        }

        if (path.Length == 0)
        {
            return new AttentionTarget(AttentionAct.List, null, null);
        }

        var segments = path.Split('/');
        if (segments.Length is < 2 or > 3)
        {
            return null;
        }

        var suffix = segments.Length == 3 ? segments[2] : null;
        var row = Rows.FirstOrDefault(row =>
            string.Equals(row.Collection, segments[0], StringComparison.Ordinal)
            && string.Equals(row.Suffix, suffix, StringComparison.Ordinal));
        return row is not null && PublicId.IsWellFormed(row.Prefix, segments[1])
            ? new AttentionTarget(row.Act, row.Kind, segments[1])
            : null;
    }
}
