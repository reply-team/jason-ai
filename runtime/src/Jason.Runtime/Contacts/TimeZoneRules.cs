using Jason.Runtime.Domain;

namespace Jason.Runtime.Contacts;

/// <summary>
/// A contact's time zone decides when it is civil to reach them, so only an IANA id is stored: a Windows id
/// names a different vocabulary on every machine, and a contact database has to survive being copied to one.
/// </summary>
public static class TimeZoneRules
{
    /// <summary>Trimmed id when it names an IANA zone, null when absent, null plus a recorded error when it does not.</summary>
    public static string? Normalize(string? raw, string field, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var id = raw.Trim();

        // The IANA-to-Windows mapping table is deliberately not the test: it is incomplete on some platforms
        // and would reject a perfectly good id there. What settles it is whether the zone the id resolves to
        // reports an IANA id of its own, which a Windows id such as "Pacific Standard Time" never does.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) || !zone.HasIanaId)
        {
            errors.Add(field, "invalid", "time_zone must be an IANA time zone id such as Europe/Kyiv.");
            return null;
        }

        return id;
    }
}
