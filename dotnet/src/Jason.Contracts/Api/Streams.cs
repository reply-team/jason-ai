namespace Jason.Contracts.Api;

/// <summary>
/// What the runtime pushes rather than answers once: the journal, line by line as it is written, over a WebSocket. A
/// stream is no operation of the catalog, because it is no request with one answer, and a WebSocket upgrade is a
/// <c>GET</c> by its protocol, so its route is the one the API serves to a <c>GET</c>. It reads what <c>journal.list</c>
/// reads and writes nothing. <c>jason journal watch</c> is its command, one to one.
/// </summary>
public static class Streams
{
    /// <summary>The journal's stream, named as an operation is: its noun, then what it does.</summary>
    public const string Journal = "journal.stream";

    /// <summary>The route the stream is opened on: its name under the API's version, as an operation's route is.</summary>
    public const string JournalRoute = "/" + ApiVersion.Current + "/" + Journal;

    /// <summary>The subprotocol a connection must offer and the one the runtime answers with.</summary>
    public const string JournalSubProtocol = "jason.journal.v1";

    /// <summary>
    /// A browser can set no header on a WebSocket, so a page offers its bearer as a second subprotocol, this prefix
    /// before the token. The runtime reads it on the stream's route alone and never answers with it.
    /// </summary>
    public const string BearerSubProtocolPrefix = "jason.bearer.";

    /// <summary>
    /// The query parameter that names where the stream starts: a cursor in the form <c>journal.list</c> pages by, and
    /// the stream sends the lines after the line it names.
    /// </summary>
    public const string SinceParameter = "since";

    /// <summary>Every stream the runtime has, by name, as <see cref="Operations"/> lists the operations.</summary>
    public static IReadOnlyList<string> All { get; } = [Journal];
}
