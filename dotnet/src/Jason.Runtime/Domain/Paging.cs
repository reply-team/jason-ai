using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Jason.Contracts.Api;

namespace Jason.Runtime.Domain;

/// <summary>
/// Keyset pagination over public ids: a cursor is the last item the caller saw, so inserts and deletes during a
/// walk never shift a page or hide a row the way an offset would.
/// </summary>
public static class Paging
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static int ResolveLimit(int? limit)
    {
        if (limit is null)
        {
            return DefaultLimit;
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw new ValidationException([new ErrorDetail("limit", "invalid", string.Create(CultureInfo.InvariantCulture, $"limit must be between 1 and {MaxLimit}."))]);
        }

        return limit.Value;
    }

    /// <summary>Opaque to the caller on purpose: the encoding is ours to change.</summary>
    public static string EncodeCursor(string publicId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        return Base64Url.EncodeToString(StrictUtf8.GetBytes(publicId));
    }

    public static string? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException)
        {
            throw new ValidationException([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);
        }
    }

    /// <summary>fetched holds up to limit + 1 rows in list order; the page keeps limit and points at the last kept row.</summary>
    public static Page<TDto> ToPage<TEntity, TDto>(IReadOnlyList<TEntity> fetched, int limit, Func<TEntity, string> publicId, Func<TEntity, TDto> map)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(publicId);
        ArgumentNullException.ThrowIfNull(map);

        var hasMore = fetched.Count > limit;
        var kept = hasMore ? fetched.Take(limit).ToList() : fetched;
        return new Page<TDto>([.. kept.Select(map)], hasMore ? EncodeCursor(publicId(kept[^1])) : null);
    }
}
