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

    /// <summary>
    /// The second pack: the SDR profession's own knowledge, which a person's session reads and no launch ever
    /// does. Nothing here is a role, so the directory that tells one from the other in the runtime pack has no
    /// counterpart — and the walk is told so rather than inferring it from a path that happens not to begin
    /// with <c>roles/</c>.
    /// </summary>
    public static string BusinessRoot() => Path.Combine(RepositoryRoot(), "skills", "business");

    public static string Catalog() => Path.Combine(Root(), "README.md");

    public static string BusinessCatalog() => Path.Combine(BusinessRoot(), "README.md");

    public static IReadOnlyList<PackSkill> All() => Walk(Root(), rolesLive: true);

    public static IReadOnlyList<PackSkill> Business() => Walk(BusinessRoot(), rolesLive: false);

    private static IReadOnlyList<PackSkill> Walk(string root, bool rolesLive)
    {
        var skills = new List<PackSkill>();
        foreach (var file in Directory.EnumerateFiles(root, SkillFile, SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(file)!;
            var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
            skills.Add(new PackSkill(
                Path.GetFileName(directory),
                directory,
                file,
                rolesLive && relative.StartsWith("roles/", StringComparison.Ordinal)));
        }

        skills.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        return skills;
    }

    public static PackSkill Find(string name) => Assert.Single(All(), skill => skill.Name == name);

    /// <summary>Every <c>jason …</c> line a skill prints, read from the file.</summary>
    public static IReadOnlyList<string> PrintedCommands(string file) =>
        PrintedCommands(System.IO.File.ReadAllLines(file));

    /// <summary>
    /// Every <c>jason …</c> line inside a fenced block, which is where a page prints what to type, and as
    /// <em>whole lines</em>: a guard that searched the text for a substring would let a printed command grow
    /// an option that nothing ever runs, which is the one thing these guards exist to prevent.
    /// </summary>
    /// <remarks>
    /// A fence is opened by three backticks or by three tildes and closed only by the marker that opened it, as
    /// Markdown has it. A reader that knew only the backticks let a command printed between tildes go unread by every
    /// guard that asks what a page prints.
    /// </remarks>
    public static IReadOnlyList<string> PrintedCommands(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return
        [
            .. Fenced(lines)
                .Where(line => line.Fenced)
                .Select(line => line.Text.Trim())
                .Where(text => text.StartsWith("jason ", StringComparison.Ordinal)),
        ];
    }

    /// <summary>The marker a line opens or closes a fenced block with — three backticks or three tildes — or null.</summary>
    public static string? FenceMarker(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var start = line.TrimStart();
        return start.StartsWith("```", StringComparison.Ordinal) ? "```"
            : start.StartsWith("~~~", StringComparison.Ordinal) ? "~~~"
            : null;
    }

    /// <summary>
    /// Every line that is not a fence marker, with whether it sits inside a fenced block. The one place the fence rule is
    /// written: a fence is opened by either marker and closed only by the one that opened it.
    /// </summary>
    public static IEnumerable<(string Text, bool Fenced)> Fenced(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string? fence = null;
        foreach (var line in lines)
        {
            var marker = FenceMarker(line);
            if (marker is not null && (fence is null || fence == marker))
            {
                fence = fence is null ? marker : null;
                continue;
            }

            yield return (line, fence is not null);
        }
    }

    /// <summary>
    /// The text a host would read, digested. The front matter is deliberately not part of it: promoting a
    /// skill from draft to verified edits the front matter, so a digest that covered it could never be
    /// recorded — the act of recording it would invalidate it.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised although this repository is <c>eol=lf</c> in every working tree: that is
    /// belt and braces against an editor that writes CRLF, not a platform difference.
    /// </remarks>
    public static string BodyDigest(string file) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Body(file))));

    /// <summary>
    /// The text a host reads: everything below the front matter, line endings normalised and trailing space
    /// trimmed. Where the front matter ends is asked of the reader that parses it, rather than worked out a
    /// second time here — a file whose front matter is malformed has no body at all, and taking the first
    /// <c>---</c> line instead would digest whatever happened to follow the next rule in the text.
    /// </summary>
    public static string Body(string file)
    {
        var front = SkillFrontMatter.Read(file);
        Assert.True(
            front.Close >= 0 && front.Problems.Count == 0,
            $"'{file}' does not read as front matter, so it has no body to digest: "
            + (front.Problems.Count > 0 ? string.Join("; ", front.Problems) : "it never closes"));

        var lines = System.IO.File.ReadAllText(file)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        return string.Join('\n', lines.Skip(front.Close + 1)).TrimEnd();
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
