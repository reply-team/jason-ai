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

    /// <summary>The characters of a ULID as <see cref="New"/> writes it: Crockford's base 32, uppercase.</summary>
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>How many characters follow the prefix and its underscore.</summary>
    private const int UlidLength = 26;

    /// <summary>
    /// Whether <paramref name="value"/> has the shape <see cref="New"/> gives an id of <paramref name="prefix"/>: the
    /// prefix, an underscore, and 26 uppercase Crockford characters. It says nothing about whether such an id exists.
    /// </summary>
    public static bool IsWellFormed(string prefix, string? value)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return value is not null
            && value.Length == prefix.Length + 1 + UlidLength
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value[prefix.Length] == '_'
            && value.AsSpan(prefix.Length + 1).IndexOfAnyExcept(Crockford) < 0;
    }
}
