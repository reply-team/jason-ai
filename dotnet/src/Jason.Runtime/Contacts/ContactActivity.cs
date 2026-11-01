using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Contacts;

/// <summary>
/// People in the order something last happened to them: by the newest journal line about each, newest first, and the
/// people with no line of their own after everyone else, by id. Public ids of the journal are monotonic, so the newest
/// line is the greatest id. A page's cursor names the last row's line and person, under a prefix no public id carries.
/// </summary>
internal static class ContactActivity
{
    private const string Prefix = "activity|";

    /// <summary>
    /// One row of the order: the person, and the id of their newest line, or null. Set by an initializer rather than a
    /// constructor, because the database orders and filters by its members and cannot see through a constructor.
    /// </summary>
    public sealed class Row
    {
        public required string PublicId { get; init; }

        public string? LastLine { get; init; }
    }

    public static IQueryable<Row> Rows(IQueryable<Contact> contacts, JasonDbContext db) =>
        contacts.Select(contact => new Row
        {
            PublicId = contact.PublicId,
            LastLine = db.Journal.Where(entry => entry.ContactId == contact.Id).Max(entry => entry.PublicId),
        });

    public static IQueryable<Row> After(IQueryable<Row> rows, string? decodedCursor)
    {
        if (decodedCursor is null)
        {
            return rows;
        }

        if (!decodedCursor.StartsWith(Prefix, StringComparison.Ordinal) || decodedCursor[Prefix.Length..].Split('|') is not [var line, var id])
        {
            throw InvalidCursor();
        }

        return line.Length == 0
            ? rows.Where(row => row.LastLine == null && string.Compare(row.PublicId, id) > 0)
            : rows.Where(row => row.LastLine == null
                || string.Compare(row.LastLine, line) < 0
                || (row.LastLine == line && string.Compare(row.PublicId, id) > 0));
    }

    public static IOrderedQueryable<Row> Ordered(IQueryable<Row> rows) =>
        rows.OrderBy(row => row.LastLine == null).ThenByDescending(row => row.LastLine).ThenBy(row => row.PublicId);

    public static string CursorKey(Row row) => Prefix + (row.LastLine ?? string.Empty) + "|" + row.PublicId;

    /// <summary>A cursor from the activity order is no cursor of the order by id.</summary>
    public static bool IsActivityCursor(string decodedCursor) => decodedCursor.StartsWith(Prefix, StringComparison.Ordinal);

    public static ValidationException InvalidCursor() =>
        new([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);
}
