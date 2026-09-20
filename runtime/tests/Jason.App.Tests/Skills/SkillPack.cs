using System.Security.Cryptography;
using System.Text;

namespace Jason.App.Tests.Skills;

/// <summary>One skill of the pack as it sits on disk.</summary>
/// <param name="IsRole">
/// A launched role's skill rather than an interactive one. They are different texts for different readers —
/// one may ask a person questions, the other cannot ask anybody anything — so the guards that hold them
/// differ, and the directory is what says which is which.
/// </param>
internal sealed record PackSkill(string Name, string Directory, string File, bool IsRole);

/// <summary>
/// Every skill this repository ships, found the way the guards need them: from the repository rather than
/// from the copy beside the tests, so a guard reports the file somebody would edit.
/// </summary>
internal static class SkillPack
{
    public const string SkillFile = "SKILL.md";

    public static string Root() => Path.Combine(RepositoryRoot(), "skills", "runtime");

    public static string Catalog() => Path.Combine(Root(), "README.md");

    public static IReadOnlyList<PackSkill> All()
    {
        var root = Root();
        var skills = new List<PackSkill>();
        foreach (var file in Directory.EnumerateFiles(root, SkillFile, SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(file)!;
            var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
            skills.Add(new PackSkill(
                Path.GetFileName(directory),
                directory,
                file,
                relative.StartsWith("roles/", StringComparison.Ordinal)));
        }

        skills.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        return skills;
    }

    public static PackSkill Find(string name) => Assert.Single(All(), skill => skill.Name == name);

    /// <summary>
    /// The text a host would read, digested. The front matter is deliberately not part of it: promoting a
    /// skill from draft to verified edits the front matter, so a digest that covered it could never be
    /// recorded — the act of recording it would invalidate it.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised although this repository is <c>eol=lf</c> in every working tree: that is
    /// belt and braces against an editor that writes CRLF, not a platform difference.
    /// </remarks>
    public static string BodyDigest(string file)
    {
        var text = System.IO.File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
        var close = text.IndexOf("\n---\n", StringComparison.Ordinal);
        Assert.True(close > 0, $"'{file}' does not close its front matter.");

        var body = text[(close + 5)..].TrimEnd();
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
