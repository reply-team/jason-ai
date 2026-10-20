using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// One text that finds a person by any part of what they are known by: a name (the whole name included), a company, a
/// title, any channel value. Compared folded on both sides, so case, accents and script never decide; the characters a
/// pattern would read as wildcards are escaped, so they match as written; a text that is a phone number once its spaces,
/// dots, dashes and brackets go also matches by its digits, which is how a stored number is kept; and a pasted LinkedIn
/// link matches by its profile path, whatever it carried after it.
/// </summary>
internal static class ContactSearch
{
    public const int MaxLength = 200;

    private const string Escape = TextFold.Escape;

    public static IQueryable<Contact> Apply(IQueryable<Contact> query, string text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(text);

        var pattern = TextFold.Containing(text);
        var digits = ChannelRules.DigitsOfPhone(text);
        var profile = ChannelRules.LinkedInProfilePath(text);
        // The profile path ends the stored link, or a query follows it; either way "/in/ada" never finds "/in/ada-lovelace".
        var profileEnd = profile is null ? null : "%" + TextFold.Escaped(profile);
        var profileQuery = profile is null ? null : "%" + TextFold.Escaped(profile) + "?%";

        return query.Where(contact =>
            EF.Functions.Like(TextFold.Fold(contact.FirstName), pattern, Escape)
            || EF.Functions.Like(TextFold.Fold(contact.LastName), pattern, Escape)
            || EF.Functions.Like(TextFold.Fold((contact.FirstName ?? string.Empty) + " " + (contact.LastName ?? string.Empty)), pattern, Escape)
            || EF.Functions.Like(TextFold.Fold(contact.Company), pattern, Escape)
            || EF.Functions.Like(TextFold.Fold(contact.Title), pattern, Escape)
            || contact.Channels.Any(entry => EF.Functions.Like(TextFold.Fold(entry.Value), pattern, Escape))
            || (digits != null && contact.Channels.Any(entry => entry.Value.Contains(digits)))
            || (profileEnd != null && contact.Channels.Any(entry => entry.Channel == ChannelRules.LinkedIn
                && (EF.Functions.Like(TextFold.Fold(entry.Value), profileEnd, Escape) || EF.Functions.Like(TextFold.Fold(entry.Value), profileQuery!, Escape)))));
    }
}
