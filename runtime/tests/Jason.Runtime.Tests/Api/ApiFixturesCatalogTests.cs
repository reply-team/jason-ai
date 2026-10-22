using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Api;

/// <summary>The catalog and the directory are the same list, and every file is a scrubbed exchange of the operation its directory names.</summary>
public partial class ApiFixturesCatalogTests
{
    [Fact]
    [Critical(Family.B6, "the UI fixtures are held one to one")]
    public void The_files_on_disk_are_exactly_the_catalogued_cases()
    {
        var root = ApiFixtures.Root;
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')[..^".json".Length])
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

        Assert.Equal(ApiFixtures.Cases.Order(StringComparer.Ordinal), files);
        Assert.Equal(ApiFixtures.Cases.Order(StringComparer.Ordinal), ApiFixtures.Cases);
    }

    [Theory]
    [MemberData(nameof(CriticalEveryCase))]
    public void Every_file_is_a_scrubbed_exchange_of_its_operation(string @case)
    {
        var text = File.ReadAllText(ApiFixtures.PathOf(@case));
        var exchange = Assert.IsType<JsonObject>(JsonNode.Parse(text));
        Assert.Equal(@case.Split('/')[0], (string?)exchange["operation"]);
        Assert.Equal(@case.Split('/')[1], (string?)exchange["case"]);
        Assert.IsType<JsonObject>(exchange["request"]);
        Assert.True(exchange["status"]!.GetValue<int>() is >= 200 and < 600);
        Assert.IsType<JsonObject>(exchange["response"]);
        Assert.DoesNotMatch(Ulid(), text);
        Assert.DoesNotMatch(Hex64(), text);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), text, StringComparison.Ordinal);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
    }

    public static TheoryData<string> EveryCase() => [.. ApiFixtures.Cases];

    // Critical: a recorded exchange is scrubbed.
    public static TheoryData<string> CriticalEveryCase() =>
        CriticalRows.Mark(EveryCase(), Family.B6, data => data is ["approval.approve/already-decided"]);

    [GeneratedRegex("[a-z]{2,8}_[0-9A-HJKMNP-TV-Z]{26}")]
    private static partial Regex Ulid();

    [GeneratedRegex("sha256:[0-9a-f]{64}")]
    private static partial Regex Hex64();
}
