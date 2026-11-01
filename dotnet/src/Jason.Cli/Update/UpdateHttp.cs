namespace Jason.Cli.Update;

/// <summary>
/// The one client every request about a release goes through: <c>update check</c>'s read of the feed, and
/// <c>update apply</c>'s read of the release's own feed and its download of the archive.
/// </summary>
/// <remarks>
/// A test's handler when it gave one, and the network otherwise. The network's client follows redirects, as .NET's
/// does unless told not to, and the product depends on that: the default feed is on the product's own address, which
/// answers every request with a redirect to where the release is stored. One construction, so the test that runs a
/// check through such a redirect holds all three requests to it.
/// </remarks>
internal static class UpdateHttp
{
    public static HttpClient Client(CliEnvironment env) =>
        env.HttpHandler is null ? new HttpClient() : new HttpClient(env.HttpHandler, disposeHandler: false);
}
