using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A <see cref="TempTree"/> that holds git repositories, for the tests of what a guard reads in a repository. git
/// makes its object files read-only, and Windows deletes no read-only file, so they are made writable before the
/// tree is removed.
/// </summary>
internal sealed class GitTrees : IDisposable
{
    private readonly TempTree tree = new();

    /// <summary>The tree's own root, which <see cref="Dispose"/> removes.</summary>
    public string Root => tree.Root;

    /// <inheritdoc cref="TempTree.NewDirectory"/>
    public string NewDirectory(string name) => tree.NewDirectory(name);

    /// <summary>A new git repository named <paramref name="name"/>, whose one commit holds <paramref name="files"/>.</summary>
    public string NewRepository(string name, params (string Path, string Text)[] files)
    {
        var root = tree.NewDirectory(name);
        Git(root, "init", "--quiet");
        foreach (var (path, text) in files)
        {
            Write(root, path, text);
        }

        Git(root, "add", "-A");
        Git(root, "commit", "--quiet", "--allow-empty", "-m", "Import");
        return root;
    }

    /// <summary>A file under <paramref name="root"/>, its directories made first, at a path written with forward slashes.</summary>
    public static void Write(string root, string path, string text)
    {
        var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(tree.Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        tree.Dispose();
    }
}
