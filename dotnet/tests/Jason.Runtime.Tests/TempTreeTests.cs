namespace Jason.Runtime.Tests;

/// <summary>
/// The helper that exists so that a test which makes directories also removes them. A cleanup nobody asserts is
/// a cleanup that quietly stops happening.
/// </summary>
public class TempTreeTests
{
    [Fact]
    public void A_tree_is_gone_when_it_is_disposed()
    {
        string root;
        using (var tree = new TempTree())
        {
            root = tree.Root;
            var package = tree.NewDirectory("fake");
            File.WriteAllText(Path.Combine(package, "plugin.yaml"), "id: fake");
            Assert.True(Directory.Exists(package));
        }

        Assert.False(Directory.Exists(root));
    }

    /// <summary>Two packages of the same name in one test is an ordinary thing to want, and must not collide.</summary>
    [Fact]
    public void Two_directories_of_one_name_are_two_directories()
    {
        using var tree = new TempTree();

        var first = tree.NewDirectory("fake");
        var second = tree.NewDirectory("fake");

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.Equal("fake", Path.GetFileName(first));
    }

    /// <summary>Everything it makes is inside its own root, so disposing the root is the whole of the cleanup.</summary>
    [Fact]
    public void Everything_it_makes_is_inside_its_own_root()
    {
        using var tree = new TempTree();

        Assert.StartsWith(tree.Root + Path.DirectorySeparatorChar, tree.NewDirectory("fake"), StringComparison.Ordinal);
    }
}
