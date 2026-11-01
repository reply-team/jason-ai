using System.Text.RegularExpressions;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// Every request <c>jason update</c> makes about a release goes through one construction: the check's read of the
/// feed, and apply's read of the release's own feed and its download of the archive. The check's redirect row proves
/// that construction follows the product's address. This row holds the other two to it, so a client built anywhere
/// else in the update code is refused here rather than left to follow redirects by chance.
/// </summary>
public partial class UpdateHttpTests
{
    [Fact]
    public void Every_request_about_a_release_is_sent_through_the_one_client()
    {
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "dotnet", "src", "Jason.Cli", "Update"), "*.cs")
            .Where(file => Path.GetFileName(file) != "UpdateHttp.cs")
            .Append(Path.Combine(root, "dotnet", "src", "Jason.Cli", "Commands", "UpdateCommands.cs"))
            .Order(StringComparer.Ordinal)
            .ToList();

        var built = new List<string>();
        var sent = 0;
        foreach (var file in files)
        {
            var code = File.ReadAllText(file);
            built.AddRange(Construction().Matches(code).Select(match => $"{Path.GetFileName(file)}: {match.Value}"));
            sent += Sent().Count(code);
        }

        Assert.Empty(built);
        Assert.Equal(3, sent);
    }

    /// <summary>A client or a handler made in place: <c>new HttpClient(…)</c>, a handler, or a target-typed <c>new(…)</c>.</summary>
    [GeneratedRegex(@"new\s+(?:HttpClient|HttpClientHandler|SocketsHttpHandler)\b|HttpClient\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex Construction();

    [GeneratedRegex(@"UpdateHttp\.Client\(env\)")]
    private static partial Regex Sent();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
