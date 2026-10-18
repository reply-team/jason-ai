using Jason.Contracts.Ui;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Ui;

/// <summary>The headers every answer under <c>/ui</c> carries, and the content types of the files a build may hold. Anything else is not served.</summary>
public static class UiFiles
{
    public const string ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".map"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
        [".txt"] = "text/plain; charset=utf-8",
    };

    public static string? ContentType(string path) =>
        ContentTypes.TryGetValue(Path.GetExtension(path), out var type) ? type : null;

    /// <summary>The headers every answer under <c>/ui</c> carries, the door's included: the policy, no sniffing, no referrer, and no caching until a file is served.</summary>
    public static void ApplyHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.CacheControl = "no-store";
    }

    /// <summary>A hashed asset is immutable; the index and everything else is read fresh. Set on a file that is served, never on a 404, which a browser would keep as long.</summary>
    public static string CacheControl(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        return relative.StartsWith(UiAssetLayout.AssetsDirectory + "/", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-store";
    }
}
