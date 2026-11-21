using System.Globalization;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Harness;

/// <summary>
/// The name of a campaign's folder: the moment the campaign was made, in the local time of the computer the runtime runs
/// on, to the second, and the last six characters of its public id, lowercase — <c>2026-11-21-084507-7h5nrd</c> for
/// <c>cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD</c> made at 08:45:07 there. It is made from the campaign's <c>created_at</c>, never
/// from its name, so a name is free to change and the folder is never renamed; the same campaign always has the same
/// leaf (<c>docs/projects.md</c>, §8).
/// </summary>
public static partial class CampaignLeaf
{
    /// <summary>How many characters of the public id's end the leaf carries.</summary>
    public const int TailLength = 6;

    /// <summary>The leaf of the campaign <paramref name="publicId"/> made at <paramref name="createdAt"/> (UTC), read in <paramref name="zone"/>.</summary>
    public static string Of(string publicId, DateTime createdAt, TimeZoneInfo zone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        ArgumentNullException.ThrowIfNull(zone);
        var id = publicId.Trim();
        if (id.Length < TailLength)
        {
            throw new ArgumentException("A public id is longer than the leaf's tail.", nameof(publicId));
        }

        var utc = createdAt.Kind == DateTimeKind.Utc ? createdAt : DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return local.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + "-" + id[^TailLength..].ToLowerInvariant();
    }

    /// <summary>Whether <paramref name="leaf"/> has a leaf's shape: a moment to the second and a six-character tail.</summary>
    public static bool IsLeaf(string? leaf) => leaf is not null && Shape().IsMatch(leaf);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}-\d{6}-[0-9a-z]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
