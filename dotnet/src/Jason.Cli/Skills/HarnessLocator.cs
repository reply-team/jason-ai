using Jason.Contracts.Api;

namespace Jason.Cli.Skills;

/// <summary>One place a person's agent harness looks for skills.</summary>
/// <param name="Host">Which harness it belongs to, in the vocabulary execution profiles already use.</param>
public sealed record HarnessRoot(AgentHostKind Host, string Directory);

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
}

/// <summary>The locators this build knows how to make.</summary>
public static class HarnessLocators
{
    /// <summary>The directory a Claude Code installation keeps its own configuration in.</summary>
    private const string ClaudeCodeDirectory = ".claude";

    /// <summary>And the directory inside it that holds one directory per skill.</summary>
    private const string SkillsDirectory = "skills";

    /// <summary>This machine's, read from the user profile.</summary>
    public static IHarnessLocator ForThisMachine() =>
        ForHome(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// The same composition against a named home directory: what <see cref="ForThisMachine"/> is, with the one
    /// thing a test may not touch supplied instead of read.
    /// </summary>
    public static IHarnessLocator ForHome(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        return new HomeLocator(home);
    }

    /// <summary>
    /// One named directory, which is what <c>--root</c> is: detection replaced rather than filtered, so that
    /// installing into a harness this build has never heard of is one flag instead of a careful combination.
    /// </summary>
    public static IHarnessLocator At(string directory, AgentHostKind host = AgentHostKind.ClaudeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return new NamedLocator(new HarnessRoot(host, Path.GetFullPath(directory)));
    }

    private sealed class HomeLocator(string home) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect()
        {
            var claude = Path.Combine(home, ClaudeCodeDirectory);

            // The harness's own directory, not the skills directory inside it: a person who has Claude Code
            // and has never installed a skill still has somewhere for one to go, and creating that directory
            // is the installer's business rather than detection's.
            return Directory.Exists(claude)
                ? [new HarnessRoot(AgentHostKind.ClaudeCode, Path.Combine(claude, SkillsDirectory))]
                : [];
        }
    }

    private sealed class NamedLocator(HarnessRoot root) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => [root];
    }
}
