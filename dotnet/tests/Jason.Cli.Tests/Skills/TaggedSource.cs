using Jason.Cli.Process;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// A git source shaped the way this repository is — an interactive skill, a role and a business skill — with one
/// commit per tag, each saying which tag it is, cloned over <c>file://</c> and never over a network.
/// </summary>
/// <remarks>
/// Two tags are what an upgrade looks like from the skills' side: the ref a deployment was made at, and the ref the
/// build that reads it now carries. A directory source cannot show that, because a directory has no ref at all.
/// </remarks>
internal static class TaggedSource
{
    /// <summary>The body every skill has at a tag, so a deployment says which tag it came from.</summary>
    public static string Body(string tag) => $"the body at {tag}";

    /// <summary>A repository with these tags, oldest first, and its <c>file://</c> address.</summary>
    public static async Task<string> CreateAsync(TempPaths dir, CancellationToken cancellationToken, params string[] tags)
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "origin")).FullName;
        var git = ProgramRunners.ForThisMachine();

        await RunAsync(git, root, ["init", "-b", "main"], cancellationToken);
        foreach (var tag in tags)
        {
            Skill(Path.Combine(root, "skills", "runtime", "operating"), "operating", Body(tag));
            Skill(Path.Combine(root, "skills", "runtime", "roles", "researcher"), "researcher", Body(tag));
            Skill(Path.Combine(root, "skills", "business", "campaign-planning"), "campaign-planning", Body(tag));

            await RunAsync(git, root, ["add", "-A"], cancellationToken);
            await RunAsync(git, root, ["-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-m", tag], cancellationToken);
            await RunAsync(git, root, ["tag", tag], cancellationToken);
        }

        return new Uri(root).AbsoluteUri;
    }

    private static void Skill(string directory, string name, string body)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: one line\n---\n\n{body}\n");
    }

    private static async Task RunAsync(IProgramRunner git, string root, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync("git", arguments, root, TimeSpan.FromSeconds(60), cancellationToken);
        Assert.True(
            result.ExitCode == 0,
            $"'git {string.Join(' ', arguments)}' failed while building this test's repository: {result.StandardError}");
    }
}
