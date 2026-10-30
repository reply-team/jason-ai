using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// <c>inbox changes [--cursor &lt;c&gt; | --since &lt;moment&gt;] [--top &lt;n&gt;]</c>, answered as reply-cli 0.17.0 documents it over
/// Reply's change feed: the threads that moved, oldest first by their last activity and then by thread, read from a cursor,
/// from a moment, or from the start of the last 30 days, which is all the feed holds. An item is Reply's own, passed
/// through as the CLI passes it — identifiers, moments and flags, no subject, preview or name. The cursor is the position
/// a page ended at, and an empty page hands back one at the moment it was read, ahead of the one it was given. A cursor or
/// a moment older than the window is <c>inbox.cursor_expired</c>; a cursor Reply never issued, or one ahead of its clock,
/// is <c>inbox.cursor_refused</c>, the CLI's word for Reply's refusal without a code of its own that names the cursor. A
/// thread marked <c>_deleted</c> is not in the feed.
/// </summary>
internal static partial class Account
{
    /// <summary>What a test plants on a thread that Reply deleted: the inbox's own files still hold it, the feed does not.</summary>
    private const string DeletedMark = "_deleted";

    /// <summary>What a test plants to have every page hand back an empty cursor, which reply-cli passes on unread.</summary>
    private const string FeedFile = "feed.json";

    private static readonly TimeSpan EventWindow = TimeSpan.FromDays(30);

    /// <summary>How far ahead of the clock a cursor's position may be before Reply refuses it as one it never issued.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>A moment with its offset, which is what <c>--since</c> takes: a date alone or a zoneless time is a usage error.</summary>
    private static readonly Regex WithOffset = new(@"T[0-9:.]+(Z|[+-][0-9]{2}:?[0-9]{2})$", RegexOptions.CultureInvariant);

    private static Ending InboxChanges(string root, Parsed line)
    {
        var cursor = line.Option("--cursor");
        var since = line.Option("--since");
        if (cursor is not null && since is not null)
        {
            return Refused("usage.inbox", "Give at most one of --cursor and --since.", exit: 2);
        }

        var (top, badTop) = RangeOf(line, "--top", 1, 1000, 100);
        if (badTop is not null)
        {
            return badTop;
        }

        DateTimeOffset? from = null;
        if (since is not null)
        {
            if (!WithOffset.IsMatch(since.Trim()) || MomentOf(since) is not { } moment)
            {
                return Refused("usage.date", "--since must be a moment with its offset, e.g. 2026-11-03T14:00:00Z.", exit: 2);
            }

            from = moment;
        }

        if (!Offered(root, "inbox_changes"))
        {
            return NotOffered("inbox_changes", 0, []);
        }

        var now = DateTimeOffset.UtcNow;
        var floor = now - EventWindow;
        (DateTimeOffset At, int Thread) after;
        if (cursor is not null)
        {
            // A thread's last activity is its last message's sent time, which may run a little ahead of Reply's clock.
            if (PositionOf(cursor) is not { } position || position.At > now + ClockSkew)
            {
                return Refused(
                    "inbox.cursor_refused", "Reply did not accept the cursor: it is not one this endpoint issued.", status: 400,
                    details: new JsonObject { ["fields"] = new JsonArray("cursor") });
            }

            after = position;
        }
        else
        {
            // A moment reads the threads that moved at or after it; no thread is numbered 0.
            after = (from ?? floor, 0);
        }

        if (after.At < floor)
        {
            return Refused(
                "inbox.cursor_expired", "The cursor or the moment is older than the 30 days Reply's change feed holds.",
                status: 400, replyCode: "inboxThread.changeWindowExpired");
        }

        var moved = Records(root, ThreadsFile)
            .Where(thread => thread[DeletedMark] is not JsonValue deleted || !deleted.GetValue<bool>())
            .Select(thread => (Thread: thread, At: LastActivity(thread), Id: thread["id"]!.GetValue<int>()))
            .Where(entry => entry.At >= floor && (entry.At > after.At || (entry.At == after.At && entry.Id > after.Thread)))
            .OrderBy(entry => entry.At)
            .ThenBy(entry => entry.Id)
            .ToList();
        var page = moved.Take(top).ToList();
        var end = page.Count > 0 ? (page[^1].At, page[^1].Id) : (now > after.At ? now : after.At, 0);

        return Done(new JsonObject
        {
            ["items"] = new JsonArray([.. page.Select(entry => (JsonNode)EventItem(entry.Thread))]),
            ["has_more"] = moved.Count > page.Count,
            ["cursor"] = EmptyCursors(root) ? string.Empty : CursorOf(end),
        });
    }

    /// <summary>One thread as the feed gives it: Reply's own camelCase keys, its flags sticky, and its last message's direction.</summary>
    private static JsonObject EventItem(JsonObject thread)
    {
        var item = new JsonObject
        {
            ["threadId"] = thread["id"]!.DeepClone(),
            ["contactId"] = thread["contact"]?["id"]?.DeepClone(),
            ["channel"] = Text(thread, "channel") is "email" or "linkedIn" ? Text(thread, "channel") : "unknown",
            ["lastActivityDate"] = thread["lastActivityDate"]?.DeepClone(),
        };
        if (thread["sequence"]?["id"] is JsonValue sequence)
        {
            item["sequenceId"] = sequence.DeepClone();
        }

        if (thread["category"] is JsonObject category)
        {
            item["category"] = new JsonObject { ["id"] = category["id"]?.DeepClone(), ["name"] = category["name"]?.DeepClone() };
        }

        item["hasMeetingIntent"] = thread["hasMeetingIntent"] is JsonValue intent && intent.GetValue<bool>();
        item["flags"] = new JsonObject
        {
            ["hasReply"] = Inbound(thread),
            ["hasBounce"] = false,
            ["hasAutoReply"] = false,
            ["hasOutOfOffice"] = false,
        };

        var last = (thread["messages"] as JsonArray ?? []).OfType<JsonObject>()
            .OrderBy(message => DateTimeOffset.TryParse(Text(message, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue)
            .LastOrDefault();
        if (last is not null)
        {
            item["lastMessage"] = new JsonObject
            {
                ["messageId"] = last["messageId"]?.DeepClone(),
                ["isOutbound"] = last["isOutbound"] is JsonValue outbound && outbound.GetValue<bool>(),
                ["date"] = last["date"]?.DeepClone(),
            };
        }

        return item;
    }

    private static bool EmptyCursors(string root)
    {
        var file = Path.Combine(root, FeedFile);
        return File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject feed && feed["empty_cursor"] is JsonValue value && value.GetValue<bool>();
    }

    /// <summary>A position as this stand-in writes its cursors: the moment's ticks and the thread, base64url without padding.</summary>
    private static string CursorOf((DateTimeOffset At, int Thread) position) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(
            position.At.UtcTicks.ToString(CultureInfo.InvariantCulture) + "." + position.Thread.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (DateTimeOffset At, int Thread)? PositionOf(string cursor)
    {
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            var parts = Encoding.ASCII.GetString(Convert.FromBase64String(padded)).Split('.');
            return parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTimeOffset.MaxValue.UtcTicks
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var thread)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), thread)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
