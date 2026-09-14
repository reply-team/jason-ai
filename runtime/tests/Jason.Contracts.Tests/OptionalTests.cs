using System.Text.Json;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Tests;

public class OptionalTests
{
    private sealed record Patch(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int?> Count);

    [Fact]
    public void Absent_field_is_not_set()
    {
        var patch = JsonSerializer.Deserialize<Patch>("{}", JasonJson.Options)!;
        Assert.False(patch.Name.IsSet);
        Assert.False(patch.Count.IsSet);
    }

    [Fact]
    public void Present_null_is_set_with_a_null_value()
    {
        var patch = JsonSerializer.Deserialize<Patch>("{\"name\":null}", JasonJson.Options)!;
        Assert.True(patch.Name.IsSet);
        Assert.Null(patch.Name.Value);
    }

    [Fact]
    public void Present_value_is_set()
    {
        var patch = JsonSerializer.Deserialize<Patch>("{\"name\":\"x\",\"count\":3}", JasonJson.Options)!;
        Assert.Equal("x", patch.Name.Value);
        Assert.Equal(3, patch.Count.Value);
    }

    [Fact]
    public void Absent_fields_are_omitted_when_writing_and_set_fields_round_trip()
    {
        var json = JsonSerializer.Serialize(new Patch(Optional<string?>.Of(null), Optional<int?>.Absent), JasonJson.Options);
        Assert.Equal("{\"name\":null}", json);
    }
}
