using Jason.Contracts.Ui;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Ui;

/// <summary>
/// The headers every answer under <c>/ui</c> carries, and the content types of the files a build may hold. Anything else
/// is not served. Every rule here reads the layout, a directory of the build, and never a file's name: <c>assets/</c>
/// chooses a caching rule, <c>sealed/</c> a header set.
/// </summary>
public static class UiFiles
{
    public const string ContentSecurityPolicy = "default-src 'self'; connect-src 'self' http://127.0.0.1:*; frame-ancestors 'none'; base-uri 'none'";

    /// <summary>
    /// The policy of a file under <c>sealed/</c>, content the UI does not trust: its own inline script and style, fonts and
    /// images as <c>data:</c> addresses, and nothing else — no address to fetch or open a socket to, no frame, worker,
    /// plugin, form target or base. Only a page of the UI's own origin may frame it. Its <c>sandbox</c>, honoured from a
    /// header whether the file is framed or opened by itself, gives it an origin of its own that is no one's, so it holds
    /// no storage of the runtime's origin and none of its own; it keeps scripts, and forms that raise their events for its
    /// script, which <c>form-action</c> keeps from going anywhere. It never keeps the same origin, which with scripts would
    /// let the file lift its own sandbox.
    /// </summary>
    public const string SealedContentSecurityPolicy =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src data:; img-src data:; "
        + "connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; child-src 'none'; worker-src 'none'; "
        + "manifest-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'self'; sandbox allow-scripts allow-forms";

    /// <summary>The browser features a file under <c>sealed/</c> may ask for, each allowed to no origin, its own included.</summary>
    public static readonly IReadOnlyList<string> SealedDeniedFeatures =
    [
        "accelerometer",
        "autoplay",
        "bluetooth",
        "camera",
        "clipboard-read",
        "clipboard-write",
        "display-capture",
        "encrypted-media",
        "fullscreen",
        "gamepad",
        "geolocation",
        "gyroscope",
        "hid",
        "idle-detection",
        "keyboard-map",
        "local-fonts",
        "magnetometer",
        "microphone",
        "midi",
        "otp-credentials",
        "payment",
        "picture-in-picture",
        "publickey-credentials-create",
        "publickey-credentials-get",
        "screen-wake-lock",
        "serial",
        "storage-access",
        "usb",
        "window-management",
        "xr-spatial-tracking",
    ];

    /// <summary>The console's set: its policy, no sniffing, no referrer, and no caching until a file is served.</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> ConsoleHeaders =
    [
        new("Content-Security-Policy", ContentSecurityPolicy),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Cache-Control", "no-store"),
    ];

    /// <summary>The sealed set: the sealed policy, every feature denied, no names looked up ahead, no reading by another origin.</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> SealedHeaders =
    [
        new("Content-Security-Policy", SealedContentSecurityPolicy),
        new("Permissions-Policy", string.Join(", ", SealedDeniedFeatures.Select(feature => feature + "=()"))),
        new("X-Content-Type-Options", "nosniff"),
        new("X-DNS-Prefetch-Control", "off"),
        new("Referrer-Policy", "no-referrer"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
        new("Cache-Control", "no-store"),
    ];

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

    /// <summary>
    /// The header set for a path under <c>/ui/</c>, as the request names it: the sealed set for a path under
    /// <c>sealed/</c>, else the console's. A file's name never takes part.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> HeadersFor(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        return UiAssetLayout.IsSealed(relative) ? SealedHeaders : ConsoleHeaders;
    }

    /// <summary>The console's set, on an answer that serves no file of a build: the door's.</summary>
    public static void ApplyHeaders(HttpResponse response) => Apply(response, ConsoleHeaders);

    /// <summary>The set <see cref="HeadersFor"/> chooses, set before the file is resolved, so a 404 carries it too.</summary>
    public static void ApplyHeaders(HttpResponse response, string relative) => Apply(response, HeadersFor(relative));

    /// <summary>A hashed asset is immutable; the index and everything else is read fresh. Set on a file that is served, never on a 404, which a browser would keep as long.</summary>
    public static string CacheControl(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        return relative.StartsWith(UiAssetLayout.AssetsDirectory + "/", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-store";
    }

    private static void Apply(HttpResponse response, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        ArgumentNullException.ThrowIfNull(response);
        foreach (var (name, value) in headers)
        {
            response.Headers[name] = value;
        }
    }
}
