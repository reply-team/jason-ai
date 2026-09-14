using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// What a campaign context is allowed to be. The size cap is not a storage worry: every attempt will carry the
/// context into a prompt, and knowledge a role cannot read in one go has stopped being shared knowledge.
/// </summary>
public static class ContextRules
{
    /// <summary>256 KiB of compact UTF-8.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Top level must be an object (the request type already guarantees it); enforces the size cap.</summary>
    public static void EnsureWithinLimits(JsonObject context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (JsonSerializer.SerializeToUtf8Bytes(context, JasonJson.Options).Length > MaxBytes)
        {
            throw DomainErrors.ContextTooLarge(MaxBytes);
        }
    }
}
