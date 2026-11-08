namespace Jason.Contracts.Update;

/// <summary>
/// The product's own address, named once. Everything that fetches a release names it: the runtime's update
/// feed, the downloads composed from that feed, both install scripts and every install line. It redirects every
/// path under <c>/releases/</c> to the same path in the public repository's releases on GitHub.
/// </summary>
/// <remarks>
/// The paths are GitHub's, kept as they are, so the two URL rules in <see cref="UpdateFeed"/> work in front of it
/// unchanged, and the store behind it can change without any of them changing. What stays on GitHub stays there by
/// name: the licence page at a tag, links into the tree, and the skills — their default, a release's own package of
/// them, read from GitHub's release download address directly, and a <c>git</c> source, a clone that a redirect does
/// not serve.
/// <para>
/// It is a host of its own under the product's domain, so the domain's apex can be a site about Jason without any
/// request the product makes passing through it. Releases from 0.7.0 to 0.23.x named the apex, which keeps
/// redirecting the same paths, so such an installation still finds its updates; one before 0.7.0 asks GitHub directly.
/// </para>
/// </remarks>
public static class ProductAddress
{
    /// <summary>The product's front door: its root answers with the public repository.</summary>
    public const string Root = "https://get.jasonharness.ai";

    /// <summary>
    /// The base every release request is composed from — <c>/latest/download/&lt;asset&gt;</c>,
    /// <c>/download/v&lt;version&gt;/&lt;asset&gt;</c> or <c>/tag/v&lt;version&gt;</c> — and never a request by itself.
    /// </summary>
    public const string Releases = Root + "/releases";
}
