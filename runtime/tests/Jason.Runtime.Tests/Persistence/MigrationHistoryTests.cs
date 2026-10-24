using System.Security.Cryptography;
using Jason.Runtime.Tests.Documentation;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A migration that has been committed is never edited: a database that has applied it is not told again, so a change
/// to it reaches only the databases made after the change. Each migration file, and the designer file beside it, is held
/// to the digest it had when it was committed, from a list a new migration appends its two lines to. The model snapshot
/// is not on the list, since every new migration changes it.
/// </summary>
/// <remarks>
/// <para>
/// The digest is taken over the file with its line endings as LF, so a checkout that writes CRLF reads the same bytes.
/// </para>
/// <para>
/// A change to the list itself is what these tests cannot see: a line that changes or disappears in a diff is refused in
/// review unless its migration was added on that same branch and has not been merged anywhere; the reviewer is the last check.
/// </para>
/// </remarks>
public class MigrationHistoryTests
{
    private const string MigrationsDirectory = "runtime/src/Jason.Runtime/Persistence/Migrations";

    private const string ListFile = "runtime/tests/Jason.Runtime.Tests/Persistence/MigrationDigests.txt";

    [Fact]
    [Critical(Family.P2, "a committed migration never changes")]
    public void Every_committed_migration_is_the_bytes_it_was_committed_with()
    {
        var root = Publication.RepositoryRoot();
        var listed = Listed(root);
        var files = Files(root);

        var changed = listed
            .Where(entry => files.Contains(entry.Key))
            .Select(entry => (Name: entry.Key, Listed: entry.Value, Now: Digest(root, entry.Key)))
            .Where(file => !string.Equals(file.Listed, file.Now, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            changed.Count == 0,
            string.Join(
                Environment.NewLine,
                changed.Select(file =>
                    $"{file.Name} is not the bytes it was committed with: {ListFile} says {file.Listed}, the file is {file.Now}. "
                    + "A committed migration is never edited: restore it from the commit before your change (git log -- "
                    + $"{MigrationsDirectory}/{file.Name} shows it; git restore --source=<that commit> -- {MigrationsDirectory}/{file.Name}) "
                    + "and make the change in a new migration. Only if this migration was added on your own branch and has not been merged "
                    + $"anywhere yet, replace its line in {ListFile} with: {Line(file.Name, file.Now)}")));
    }

    [Fact]
    [Critical(Family.P2, "every migration is on the digest list")]
    public void Every_migration_file_is_on_the_list_and_every_listed_file_exists()
    {
        var root = Publication.RepositoryRoot();
        var listed = Listed(root);
        var files = Files(root);

        var unlisted = files.Where(name => !listed.ContainsKey(name)).ToList();
        var missing = listed.Keys.Where(name => !files.Contains(name)).Order(StringComparer.Ordinal).ToList();

        // One assertion for both, missing first: a committed migration renamed shows as one of each, and restoring it is
        // the advice that comes before listing a new name.
        var problems = new List<string>();
        if (missing.Count > 0)
        {
            problems.Add(
                $"{ListFile} lists files that are not in {MigrationsDirectory}: {string.Join(", ", missing)}. A committed migration is "
                + "never removed or renamed: restore it. Only if it was added on your own branch and has not been merged anywhere yet, "
                + "remove its line.");
        }

        if (unlisted.Count > 0)
        {
            problems.Add(
                $"A migration file is not on {ListFile}. A new migration appends one line per file to its end, in this order:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, unlisted.Select(name => Line(name, Digest(root, name)))));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The migration files, by name, in the order their timestamps give: every C# file but the model snapshot.</summary>
    private static List<string> Files(string root) =>
    [
        .. Directory.EnumerateFiles(Path.Combine(root, MigrationsDirectory), "*.cs")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The list's lines, <c>name sha256:hex</c>, by name; a line starting with <c>#</c> and a blank line say nothing.</summary>
    private static Dictionary<string, string> Listed(string root)
    {
        var listed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(root, ListFile)))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ');
            Assert.True(parts is [_, var digest] && digest.StartsWith("sha256:", StringComparison.Ordinal), $"Not a line of the list: '{line}'");
            Assert.True(listed.TryAdd(parts[0], parts[1]), $"Listed twice: {parts[0]}");
        }

        return listed;
    }

    /// <summary>The sha256 of the file with every CRLF read as LF.</summary>
    private static string Digest(string root, string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(root, MigrationsDirectory, name));
        var normalized = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == '\r' && i + 1 < bytes.Length && bytes[i + 1] == '\n')
            {
                continue;
            }

            normalized.Add(bytes[i]);
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData([.. normalized]));
    }

    private static string Line(string name, string digest) => $"{name} {digest}";
}
