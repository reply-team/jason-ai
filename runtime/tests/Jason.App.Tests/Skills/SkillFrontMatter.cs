namespace Jason.App.Tests.Skills;

/// <summary>
/// The front matter, read the way the pack agreed to write it: one line per key, nothing folded or wrapped,
/// and everything this pack invents of its own under <c>metadata</c>.
/// </summary>
/// <remarks>
/// Three readers parse this front matter — the launcher's, the role guard's and this one — and none of them
/// is a YAML parser, because the contracts, the CLI and the host stay YAML-free. A continuation line is the
/// one construct that would make three line-oriented readers disagree with each other while each looked
/// correct, so the shape is guarded rather than accommodated: anything that is not a key at column zero or a
/// metadata entry indented by exactly two spaces is reported as a problem.
/// </remarks>
/// <param name="Close">
/// The line that closed the front matter, counted from zero, or -1 where nothing did. It is here because
/// the body begins on the next line, and the body is what a <c>verified</c> row records the digest of:
/// one reader answers where the front matter ends, rather than two agreeing by habit.
/// </param>
internal sealed record SkillFrontMatter(
    IReadOnlyList<string> Keys,
    IReadOnlyDictionary<string, string> Top,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<string> Problems,
    int Close)
{
    public static SkillFrontMatter Read(string file)
    {
        var lines = File.ReadAllLines(file);
        var keys = new List<string>();
        var top = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();

        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
        {
            return new SkillFrontMatter([], top, metadata, ["it does not open with front matter"], -1);
        }

        var inMetadata = false;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimEnd() == "---")
            {
                return new SkillFrontMatter(keys, top, metadata, problems, index);
            }

            if (line.Length == 0)
            {
                problems.Add($"line {index + 1} is blank, and front matter here is one line per key");
                continue;
            }

            if (line[0] != ' ')
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    problems.Add($"line {index + 1} is neither a key at column zero nor a metadata entry: '{line}'");
                    continue;
                }

                var key = line[..colon];
                keys.Add(key);
                top[key] = line[(colon + 1)..].Trim().Trim('"', '\'');
                inMetadata = key == "metadata";
                continue;
            }

            if (!inMetadata || !line.StartsWith("  ", StringComparison.Ordinal) || line[2] == ' ')
            {
                problems.Add($"line {index + 1} is indented and is not a metadata entry: '{line}'");
                continue;
            }

            var child = line[2..];
            var separator = child.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                problems.Add($"line {index + 1} is under metadata and is not a key: '{line}'");
                continue;
            }

            metadata[child[..separator]] = child[(separator + 1)..].Trim().Trim('"', '\'');
        }

        problems.Add("the front matter is never closed");
        return new SkillFrontMatter(keys, top, metadata, problems, -1);
    }
}
