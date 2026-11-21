using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A campaign folder's leaf in a recorded exchange is one stable word, whatever the run's clock and ids: the fixed moment
/// and the end of the id it was scrubbed to, so a file recorded again is the same file.
/// </summary>
public sealed class FixtureScrubberLeafTests
{
    private const string Data = "/data-of-the-run";

    [Fact]
    public void A_leaf_is_written_with_the_fixed_moment_and_the_end_of_its_campaign_s_scrubbed_id()
    {
        var scrubber = new FixtureScrubber(Data, "/home/someone");
        var answer = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["folder_path"] = "/home/someone/Jason/projects/default/campaigns/2026-11-21-084507-7h5nrd",
            ["other"] = "/home/someone/Jason/projects/default/campaigns/2026-11-21-084507-7h5nrd-2",
        };

        var scrubbed = scrubber.Scrub(answer)!.AsObject();

        Assert.Equal("cmp_1", scrubbed["id"]!.GetValue<string>());
        Assert.Equal($"~/Jason/projects/default/campaigns/{FixtureScrubber.FixedLeafMoment}-cmp001", scrubbed["folder_path"]!.GetValue<string>());
        Assert.Equal($"~/Jason/projects/default/campaigns/{FixtureScrubber.FixedLeafMoment}-cmp001-2", scrubbed["other"]!.GetValue<string>());
    }

    [Fact]
    public void Two_runs_of_different_moments_and_ids_write_the_same_words()
    {
        JsonNode Run(string id, string leaf) => new FixtureScrubber(Data, "/home/someone").Scrub(new JsonObject
        {
            ["id"] = id,
            ["folder_path"] = "/home/someone/Jason/projects/acme/campaigns/" + leaf,
        })!;

        var first = Run("cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", "2026-11-21-084507-7h5nrd");
        var second = Run("cmp_01JC00000000000000000ABCDE", "2027-03-02-231501-0abcde");

        Assert.True(JsonNode.DeepEquals(first, second), $"{first.ToJsonString()} differs from {second.ToJsonString()}");
    }

    [Fact]
    public void A_leaf_whose_id_was_not_seen_is_numbered_by_first_appearance_and_a_name_leaf_is_left_alone()
    {
        var scrubber = new FixtureScrubber(Data);
        var answer = new JsonArray(
            "projects/default/campaigns/2026-11-21-084507-zzzzzz",
            "projects/default/campaigns/2026-11-21-084508-yyyyyy",
            "projects/default/campaigns/2026-11-21-084507-zzzzzz",
            "projects/default/campaigns/q3-marketing-leads");

        var scrubbed = scrubber.Scrub(answer)!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Equal(
            [
                $"projects/default/campaigns/{FixtureScrubber.FixedLeafMoment}-leaf01",
                $"projects/default/campaigns/{FixtureScrubber.FixedLeafMoment}-leaf02",
                $"projects/default/campaigns/{FixtureScrubber.FixedLeafMoment}-leaf01",
                "projects/default/campaigns/q3-marketing-leads",
            ],
            scrubbed);
    }
}
