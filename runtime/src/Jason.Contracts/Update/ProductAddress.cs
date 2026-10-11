namespace Jason.Contracts.Update;

/// <summary>
/// The product's own address, named once. Everything that fetches a release names it: the runtime's update
/// feed, the downloads composed from that feed, both install scripts and every install line. It redirects every
/// path under <c>/releases/</c> to the same path in the public repository's releases on GitHub.
/// </summary>
/// <remarks>
/// The paths are GitHub's, kept as they are, so the two URL rules in <see cref="UpdateFeed"/> work in front of it
/// unchanged, and the store behind it can change without any of them changing. What stays on GitHub stays there by
/// name: the licence page at a tag, links into the tree, and the skills source, which is a <c>git</c> clone that a
/// redirect does not serve.
/// </remarks>
public static class ProductAddress
{
    /// <summary>The product's front door: a page about Jason.</summary>
    public const string Root = "https://jasonharness.ai";

    /// <summary>
    /// The base every release request is composed from — <c>/latest/download/&lt;asset&gt;</c>,
    /// <c>/download/v&lt;version&gt;/&lt;asset&gt;</c> or <c>/tag/v&lt;version&gt;</c> — and never a request by itself.
    /// </summary>
    public const string Releases = Root + "/releases";
}
