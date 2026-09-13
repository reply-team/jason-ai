namespace Jason.Contracts.Ids;

/// <summary>
/// Public identifiers are type-prefixed ULIDs (<c>cmp_…</c>, <c>wi_…</c>, <c>rt_…</c>): self-describing for
/// agents, time-ordered for SQLite, and the only identifiers that ever leave the runtime.
/// </summary>
public static class PublicId
{
    public static string New(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length is < 2 or > 8 || !prefix.All(static c => c is >= 'a' and <= 'z'))
        {
            throw new ArgumentException("A public-id prefix is two to eight lowercase ASCII letters.", nameof(prefix));
        }

        return $"{prefix}_{Ulid.NewUlid()}";
    }
}
