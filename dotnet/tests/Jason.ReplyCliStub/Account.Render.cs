using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// Reply's own filling-in of a direct send: every tag in the subject and the body of an email is read before anything
/// is sent, over the contact's fields, its custom fields by key and the mailbox's names and address, and every tag in a
/// LinkedIn message or InMail over the same fields of the contact's and the LinkedIn account's first name and name. The
/// core of Reply's syntax and no more — a value, a value with a quoted fallback, <c>#if</c> on a value or on its being,
/// or not being, a quoted text, <c>else</c> and <c>/if</c> — with white space free inside the braces, as Reply reads
/// it. What counts as no value and how a comparison compares are as Reply's rendering reads it; its public pages say
/// neither. A tag it cannot fill, or cannot read, refuses the send as reply-cli 0.17.2 answers it.
/// </summary>
internal static partial class Account
{
    private const string Unfillable = "RenderingFail: One or more values for variables are missing or incorrect.";

    private const string Unclosed = "Reached end of template before the expression was closed.";

    /// <summary>
    /// The text rendered field by field, or the refusal of the first field Reply cannot fill or read, as reply-cli 0.17.2
    /// answers it: <c>contact.missing_variables</c> with Reply's own words, listing the names the field holds no value
    /// for, as Reply spells them, or <c>contact.rendering_failed</c> in Reply's own words where it cannot read a tag.
    /// Reply decodes the body once before it reads it; whether it decodes a subject nothing Reply says tells, so the subject is read as written.
    /// </summary>
    private static (JsonObject? Rendered, Ending? Refusal) RenderedEmail(JsonObject text, JsonObject contact, JsonObject mailbox, JsonObject ids)
    {
        var rendered = new JsonObject();
        foreach (var (field, value) in text)
        {
            var source = (string?)value ?? string.Empty;
            var (written, problem, missing) = Render(field == "body" ? WebUtility.HtmlDecode(source) : source, name => ValueOf(name, contact, mailbox));
            if (problem is not null)
            {
                return (null, Refused(
                    "contact.rendering_failed", "Reply could not render the email's template, so nothing was sent.",
                    status: 400, replyCode: "contact.renderingFailed", detail: problem, ids: ids));
            }

            if (missing.Count > 0)
            {
                return (null, Refused(
                    "contact.missing_variables", "The email holds a template variable Reply could not fill, so nothing was sent.",
                    status: 400, replyCode: "contact.missingVariables", detail: Unfillable, ids: ids,
                    details: new JsonObject { ["missing_variables"] = new JsonArray([.. missing.Select(name => JsonValue.Create(name))]) }));
            }

            rendered[field] = Sanitized(written!);
        }

        return (rendered, null);
    }

    /// <summary>
    /// A LinkedIn message or InMail rendered field by field, or Reply's refusal of the first tag it cannot fill or read. The text is not
    /// HTML: it is read and rendered as written. A send Reply does not make within the call, held for a later moment or
    /// queued behind the account's other sends, is rendered when it goes, which this stand-in never reaches, so it is
    /// neither rendered nor refused here.
    /// </summary>
    private static (JsonObject? Rendered, Ending? Refusal) RenderedLinkedIn(JsonObject text, JsonObject contact, JsonObject account, JsonObject ids)
    {
        var rendered = new JsonObject();
        foreach (var (field, value) in text)
        {
            var (written, problem, missing) = Render((string?)value ?? string.Empty, name => LinkedInValueOf(name, contact, account));
            if (problem is not null || missing.Count > 0)
            {
                return (null, Refused(
                    "linkedin.missing_variables", "The text holds a template variable Reply could not fill, so nothing was sent.",
                    status: 400, replyCode: "directLinkedIn.missingVariables", ids: ids));
            }

            rendered[field] = written;
        }

        return (rendered, null);
    }

    /// <summary>
    /// The value Reply fills a name with in a LinkedIn text for this contact from this account, or null when it holds none:
    /// the account carries a first name and a name, and no last name or address.
    /// </summary>
    private static string? LinkedInValueOf(string name, JsonObject contact, JsonObject account)
    {
        var sender = Text(account, "name").Trim();
        var space = sender.IndexOf(' ', StringComparison.Ordinal);
        return name switch
        {
            "SendingLinkedIn.SenderFirstName" => space < 0 ? sender : sender[..space],
            "SendingLinkedIn.SenderName" => sender,
            _ when name.StartsWith("Sending", StringComparison.Ordinal) => null,
            _ => ValueOf(name, contact, new JsonObject()),
        };
    }

    /// <summary>The tag Reply was seen to keep in a direct email: the line break the plugin writes, which both sends kept.</summary>
    private static readonly string[] KeptTags = ["br"];

    /// <summary>
    /// What Reply sends of a rendered text: a tag it does not keep is dropped with everything after it. A tag starts at a
    /// <c>&lt;</c> followed by an ASCII letter, <c>/</c>, <c>!</c> or <c>?</c>, as an HTML reader takes it. A live account
    /// stored a direct email whose body held <c>&amp;lt;name@example.com&amp;gt;</c> in the middle of a line cut right
    /// before it, its later lines gone, and its subject, which held <c>&lt;name@example.com&gt;</c> as written, cut the
    /// same way; <c>a &amp;lt; b</c> and <c>&amp;lt;3</c> arrived whole, and <c>&lt;br&gt;</c> was kept. That every other
    /// tag, an end tag, a comment or a <c>&lt;?</c> among them, is cut the same way is this stub's assumption, which no send
    /// showed. Reply's public pages say nothing of it. A tag's name is read coarsely, everything up to the next
    /// <c>&gt;</c> with a leading <c>/</c> trimmed; nothing the plugin writes reaches that.
    /// </summary>
    private static string Sanitized(string text)
    {
        for (var at = text.IndexOf('<', StringComparison.Ordinal); at >= 0 && at + 1 < text.Length; at = text.IndexOf('<', at + 1))
        {
            if (!char.IsAsciiLetter(text[at + 1]) && text[at + 1] is not ('/' or '!' or '?'))
            {
                continue;
            }

            var close = text.IndexOf('>', at);
            if (close < 0 || !KeptTags.Contains(text[(at + 1)..close].TrimStart('/'), StringComparer.OrdinalIgnoreCase))
            {
                return text[..at];
            }
        }

        return text;
    }

    /// <summary>The value Reply fills a name with for this contact from this mailbox, or null when it holds none.</summary>
    private static string? ValueOf(string name, JsonObject contact, JsonObject mailbox)
    {
        var sender = Text(mailbox, "name").Trim();
        var space = sender.IndexOf(' ', StringComparison.Ordinal);
        return name switch
        {
            "FirstName" => Text(contact, "firstName"),
            "LastName" => Text(contact, "lastName"),
            "Company" => Text(contact, "company"),
            "Title" => Text(contact, "title"),
            "Email" => Text(contact, "email"),
            "SendingEmail.SenderFirstName" => space < 0 ? sender : sender[..space],
            "SendingEmail.SenderLastName" => space < 0 ? null : sender[(space + 1)..].Trim(),
            "SendingEmail.SenderName" => sender,
            "SendingEmail.Email" => Text(mailbox, "address"),
            _ => (contact["customFields"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(field => Text(field, "key") == name)
                .Select(field => Text(field, "value"))
                .FirstOrDefault(),
        };
    }

    /// <summary>
    /// Whether a value is written rather than its fallback: null, empty, and exactly <c>false</c> or <c>0</c> are no value,
    /// white space is one.
    /// </summary>
    private static bool Fills(string? value) => !string.IsNullOrEmpty(value) && value is not ("false" or "0");

    /// <summary>Whether a condition on a value holds: as a fallback reads it, save that <c>false</c> is no value in any case.</summary>
    private static bool Holds(string? value) => Fills(value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One template rendered; or the words Reply refuses a tag it cannot read with; or the names it holds no value for,
    /// each once in the order they stand.
    /// </summary>
    private static (string? Text, string? Problem, IReadOnlyList<string> Missing) Render(string template, Func<string, string?> valueOf)
    {
        var text = new StringBuilder();
        var missing = new List<string>();

        // Each open #if: whether its branch now being read is written, and whether its else has been met.
        var open = new Stack<(bool Writes, bool Holds, bool InElse)>();
        var index = 0;
        while (index < template.Length)
        {
            var start = template.IndexOf("{{", index, StringComparison.Ordinal);
            var writing = open.All(frame => frame.Writes);
            if (start < 0)
            {
                if (writing)
                {
                    text.Append(template, index, template.Length - index);
                }

                break;
            }

            if (writing)
            {
                text.Append(template, index, start - index);
            }

            var end = template.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                return (null, Unclosed, []);
            }

            var tag = template[(start + 2)..end].Trim();
            index = end + 2;
            if (index < template.Length && template[index] == '}')
            {
                // A third closing brace is read as the close of a tag Reply does not fill here. Nothing Reply says tells
                // what its rendering makes of one; the template standard refuses it, so nothing the plugin writes reaches
                // this.
                return (null, Unfillable, []);
            }

            if (tag.StartsWith("#if", StringComparison.Ordinal))
            {
                if (Test(tag[3..].Trim(), valueOf) is not { } holds)
                {
                    return (null, Unfillable, []);
                }

                open.Push((holds, holds, false));
            }
            else if (tag == "else")
            {
                if (open.Count == 0 || open.Peek().InElse)
                {
                    return (null, Unfillable, []);
                }

                var frame = open.Pop();
                open.Push((!frame.Holds, frame.Holds, true));
            }
            else if (tag == "/if")
            {
                if (open.Count == 0)
                {
                    return (null, Unfillable, []);
                }

                open.Pop();
            }
            else
            {
                var (name, fallback) = Split(tag, '|');
                if (!IsReplyName(name) || (fallback is not null && QuotedText(fallback) is null))
                {
                    return (null, Unfillable, []);
                }

                if (!writing)
                {
                    continue;
                }

                var value = valueOf(name);
                if (Fills(value))
                {
                    text.Append(value);
                }
                else if (fallback is not null)
                {
                    text.Append(QuotedText(fallback));
                }
                else if (!missing.Contains(name, StringComparer.Ordinal))
                {
                    missing.Add(name);
                }
            }
        }

        if (open.Count != 0)
        {
            return (null, Unfillable, []);
        }

        return missing.Count == 0 ? (text.ToString(), null, []) : (null, null, missing);
    }

    /// <summary>Whether an <c>#if</c>'s test holds, or null when Reply cannot read it.</summary>
    private static bool? Test(string test, Func<string, string?> valueOf)
    {
        // An operator is looked for only before the quoted text, which may hold one as text: Reply's rendering reads the
        // operator right after the name.
        var quote = test.IndexOf('\'', StringComparison.Ordinal);
        var before = quote < 0 ? test : test[..quote];
        foreach (var comparison in new[] { "==", "!=" })
        {
            var at = before.IndexOf(comparison, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var name = test[..at].Trim();
            if (!IsReplyName(name) || QuotedText(test[(at + 2)..]) is not { } quoted)
            {
                return null;
            }

            var same = Same(valueOf(name) ?? string.Empty, quoted);
            return comparison == "==" ? same : !same;
        }

        return IsReplyName(test) ? Holds(valueOf(test)) : null;
    }

    /// <summary>Whether a value and a quoted text are the same: two numbers as numbers, anything else as written.</summary>
    private static bool Same(string value, string quoted) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var left)
        && double.TryParse(quoted, NumberStyles.Float, CultureInfo.InvariantCulture, out var right)
            ? left == right
            : string.Equals(value, quoted, StringComparison.Ordinal);

    private static (string Name, string? After) Split(string tag, char by)
    {
        var at = tag.IndexOf(by, StringComparison.Ordinal);
        return at < 0 ? (tag, null) : (tag[..at].Trim(), tag[(at + 1)..]);
    }

    /// <summary>The text between single quotes that make up the whole of <paramref name="written"/>, white space aside.</summary>
    private static string? QuotedText(string written)
    {
        var trimmed = written.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'' && trimmed[1..^1].IndexOf('\'', StringComparison.Ordinal) < 0
            ? trimmed[1..^1]
            : null;
    }

    private static bool IsReplyName(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.');
}
