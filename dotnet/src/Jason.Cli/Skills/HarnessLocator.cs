using Jason.Contracts.Api;
using Jason.Contracts.Hosts;

namespace Jason.Cli.Skills;

/// <summary>One place a person's agent harnesses look for skills.</summary>
/// <param name="Hosts">
/// The hosts that read it, in the vocabulary execution profiles already use and in the order of the table of hosts:
/// two hosts reading one folder are one root. Empty for a directory named with <c>--root</c> and no <c>--host</c>.
/// </param>
public sealed record HarnessRoot(IReadOnlyList<AgentHostKind> Hosts, string Directory);

/// <summary>
/// Where the person's own harnesses keep skills.
/// </summary>
/// <remarks>
/// Behind an interface, and one of the two seams here whose null default is a refusal rather than the real
/// thing. Three guards in this repository type documented command lines <em>for real</em>; the day a page
/// prints a verb that deploys skills, a locator that worked this out for itself would write into the home
/// directory of whoever ran the suite, and into three CI runners'. So forgetting costs a refusal, which is
/// the wrong answer to get by accident and a harmless one to get.
/// </remarks>
public interface IHarnessLocator
{
    /// <summary>The roots that are on this machine. A harness that is not installed is not a root.</summary>
    IReadOnlyList<HarnessRoot> Detect();

    /// <summary>
    /// Every folder a deployment may have been recorded in, whether or not a host that reads it is still on this machine:
    /// what uninstall reads, since several hosts read a folder outside their own marker and a person may have removed
    /// every one of them before Jason. Where the locator is not made from a home, none.
    /// </summary>
    IReadOnlyList<HarnessRoot> Recorded() => [];
}

/// <summary>The locators this build knows how to make.</summary>
public static class HarnessLocators
{
    /// <summary>
    /// When two roots are one folder: case is ignored on Windows, and elsewhere the comparison is ordinal. That is a rule
    /// of this build, not a reading of the file system — a volume elsewhere that ignores case, as macOS's does by default,
    /// still has two spellings read as two roots. Detection, the status check and uninstall read roots by this one
    /// comparer, so none folds two roots another keeps apart.
    /// </summary>
    public static StringComparer SamePath { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>This machine's, read from the user profile.</summary>
    public static IHarnessLocator ForThisMachine() =>
        ForHome(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// The same composition against a named home directory: what <see cref="ForThisMachine"/> is, with the one
    /// thing a test may not touch supplied instead of read.
    /// </summary>
    public static IHarnessLocator ForHome(string home) => ForHome(home, AgentHosts.All);

    /// <summary>The same over a table of hosts other than the one this build ships, so the rules are proved for many.</summary>
    internal static IHarnessLocator ForHome(string home, IReadOnlyList<AgentHostFacts> hosts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(hosts);
        return new HomeLocator(home, hosts);
    }

    /// <summary>
    /// One named directory, which is what <c>--root</c> is: detection replaced rather than filtered, so that
    /// installing into a harness this build has never heard of is one flag instead of a careful combination.
    /// </summary>
    /// <param name="host">The host <c>--host</c> labels the directory with, or null for none.</param>
    public static IHarnessLocator At(string directory, AgentHostKind? host = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return new NamedLocator(new HarnessRoot(host is { } kind ? [kind] : [], Path.GetFullPath(directory)));
    }

    private sealed class HomeLocator(string home, IReadOnlyList<AgentHostFacts> hosts) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect()
        {
            // The host's marker, not the skills directory: a person who has a host and has never installed a skill
            // still has somewhere for one to go, and creating that directory is the installer's business rather than
            // detection's. A marker may be a file, where two hosts keep one folder and only a file tells them apart.
            var roots = new List<(string Directory, List<AgentHostKind> Hosts)>();
            foreach (var facts in hosts)
            {
                var marker = AgentHosts.Under(home, facts.HomeMarker);
                if (!(facts.MarkerKind == HomeMarkerKind.File ? File.Exists(marker) : Directory.Exists(marker)))
                {
                    continue;
                }

                Add(roots, facts);
            }

            return [.. roots.Select(root => new HarnessRoot(root.Hosts, root.Directory))];
        }

        public IReadOnlyList<HarnessRoot> Recorded()
        {
            // Every skills folder of the table, its host's marker there or not, one root where hosts share a folder.
            var roots = new List<(string Directory, List<AgentHostKind> Hosts)>();
            foreach (var facts in hosts)
            {
                Add(roots, facts);
            }

            return [.. roots.Select(root => new HarnessRoot(root.Hosts, root.Directory))];
        }

        private void Add(List<(string Directory, List<AgentHostKind> Hosts)> roots, AgentHostFacts facts)
        {
            var skills = AgentHosts.Under(home, facts.HomeSkills);
            var index = roots.FindIndex(root => SamePath.Equals(root.Directory, skills));
            if (index < 0)
            {
                roots.Add((skills, [facts.Kind]));
            }
            else
            {
                roots[index].Hosts.Add(facts.Kind);
            }
        }
    }

    private sealed class NamedLocator(HarnessRoot root) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => [root];
    }
}
