using System.Text.Json.Nodes;
using Jason.Cli;

namespace Jason.Cli.Tests;

public class RequestBodyTests
{
    [Fact]
    public void An_empty_body_serializes_as_an_empty_object() =>
        Assert.Equal("{}", RequestBody.Serialize(RequestBody.Empty()));

    [Fact]
    public void Serialization_is_compact() =>
        Assert.Equal("{\"a\":1,\"b\":{\"c\":true}}", RequestBody.Serialize(new JsonObject { ["a"] = 1, ["b"] = new JsonObject { ["c"] = true } }));

    [Fact]
    public async Task An_object_file_is_read_and_options_are_laid_over_it()
    {
        using var file = new TempFile("{\"name\":\"from the file\",\"reason\":\"kept\"}");

        var body = await RequestBody.FromFileAsync(file.Path, TextReader.Null, null, TestContext.Current.CancellationToken);
        body.Set("name", "from the option");

        Assert.Equal("{\"name\":\"from the option\",\"reason\":\"kept\"}", RequestBody.Serialize(body));
    }

    [Fact]
    public async Task An_array_file_is_wrapped_under_the_array_property()
    {
        using var file = new TempFile("[{\"contact_id\":\"cnt_A\"},{\"first_name\":\"Ada\"}]");

        var body = await RequestBody.FromFileAsync(file.Path, TextReader.Null, "contacts", TestContext.Current.CancellationToken);

        Assert.Equal("{\"contacts\":[{\"contact_id\":\"cnt_A\"},{\"first_name\":\"Ada\"}]}", RequestBody.Serialize(body));
    }

    [Fact]
    public async Task A_dash_reads_standard_input()
    {
        var body = await RequestBody.FromFileAsync("-", new StringReader("{\"name\":\"piped\"}"), null, TestContext.Current.CancellationToken);

        Assert.Equal("{\"name\":\"piped\"}", RequestBody.Serialize(body));
    }

    [Fact]
    public async Task An_array_without_an_array_property_is_a_usage_error()
    {
        using var file = new TempFile("[1,2]");

        var failure = await Assert.ThrowsAsync<UsageException>(() => RequestBody.FromFileAsync(file.Path, TextReader.Null, null, TestContext.Current.CancellationToken));

        Assert.Contains("JSON object", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_scalar_top_level_is_a_usage_error()
    {
        using var file = new TempFile("42");

        await Assert.ThrowsAsync<UsageException>(() => RequestBody.FromFileAsync(file.Path, TextReader.Null, "contacts", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Malformed_json_is_a_usage_error()
    {
        using var file = new TempFile("{ not json");

        await Assert.ThrowsAsync<UsageException>(() => RequestBody.FromFileAsync(file.Path, TextReader.Null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_missing_file_is_a_usage_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"), "absent.json");

        var failure = await Assert.ThrowsAsync<UsageException>(() => RequestBody.FromFileAsync(missing, TextReader.Null, null, TestContext.Current.CancellationToken));

        Assert.Contains("absent.json", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_standard_input_is_a_usage_error()
    {
        await Assert.ThrowsAsync<UsageException>(() => RequestBody.FromFileAsync("-", new StringReader(string.Empty), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Set_skips_nulls_by_default_and_writes_them_when_asked()
    {
        Assert.Equal("{}", RequestBody.Serialize(RequestBody.Empty().Set("name", null)));
        Assert.Equal("{\"name\":null}", RequestBody.Serialize(RequestBody.Empty().Set("name", null, onlyIfNotNull: false)));
    }

    [Fact]
    public void Set_returns_the_same_body_so_calls_chain() =>
        Assert.Equal(
            "{\"campaign_id\":\"cmp_A\",\"limit\":25}",
            RequestBody.Serialize(RequestBody.Empty().Set("campaign_id", "cmp_A").Set("limit", 25).Set("cursor", null)));

    [Fact]
    public void Set_actor_writes_the_actor_and_an_absent_actor_writes_nothing()
    {
        Assert.Equal(
            "{\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}",
            RequestBody.Serialize(RequestBody.Empty().SetActor(ActorOption.Parse("role:planner"))));
        Assert.Equal("{}", RequestBody.Serialize(RequestBody.Empty().SetActor(null)));
    }
}

/// <summary>A JSON file that exists only for the duration of one test.</summary>
public sealed class TempFile : IDisposable
{
    public TempFile(string content)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, content);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
