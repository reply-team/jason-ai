using System.IO.Compression;
using System.Text;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// A skills package as a release publishes one, built by the test: <c>skills/</c> with both packs in it — each with its
/// <c>.claude-plugin/plugin.json</c>, a role and a skill whose text names the tag — and a <c>source.json</c> at the root.
/// </summary>
/// <remarks>
/// The tree is the one <see cref="TaggedSource"/> commits, so a test can deploy from either and read the same files.
/// </remarks>
internal static class SkillsPackage
{
    /// <summary>A commit of the test's own, forty hexadecimal digits.</summary>
    public const string Commit = "c0ffee0000000000000000000000000000c0ffee";

    /// <summary>The repository a package names, the public one, as the release workflow writes it.</summary>
    public const string Repository = PublicRepository.Url;

    /// <summary>The files of a package published at <paramref name="tag"/>, by their names in the archive.</summary>
    public static IReadOnlyList<(string Name, string Text)> Files(string tag) =>
    [
        ("skills/README.md", "# Skills\n"),
        ("skills/runtime/.claude-plugin/plugin.json", "{ \"name\": \"jason-runtime\" }\n"),
        ("skills/runtime/operating/SKILL.md", Skill("operating", tag)),
        ("skills/runtime/roles/researcher/SKILL.md", Skill("researcher", tag)),
        ("skills/business/.claude-plugin/plugin.json", "{ \"name\": \"ai-sdr-core\" }\n"),
        ("skills/business/campaign-planning/SKILL.md", Skill("campaign-planning", tag)),
        ("source.json", Source(tag)),
    ];

    /// <summary>The package's bytes. <paramref name="entries"/> replaces its files, for a test about a package that is wrong.</summary>
    public static byte[] Zip(string tag, IEnumerable<(string Name, string Text)>? entries = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries ?? Files(tag))
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return memory.ToArray();
    }

    /// <summary>What a package's <c>source.json</c> says: the repository, the tag and the commit.</summary>
    public static string Source(string tag, string commit = Commit) =>
        $"{{\n  \"repository\": \"{Repository}\",\n  \"ref\": \"{tag}\",\n  \"commit\": \"{commit}\"\n}}\n";

    private static string Skill(string name, string tag) =>
        $"---\nname: {name}\ndescription: one line\n---\n\n{TaggedSource.Body(tag)}\n";
}
