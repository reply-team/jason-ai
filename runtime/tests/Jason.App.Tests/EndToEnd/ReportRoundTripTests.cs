using System.Text.Json.Nodes;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// What a reporter said, through the shipped program and back again. A report is somebody's words about
/// something that happened where Jason could not see it, so the one thing the whole path has to guarantee is
/// that the words do not change on the way through — including the parts that say what the reporter did not
/// know.
/// </summary>
/// <remarks>
/// This runs at the process boundary rather than against a service, because the claim is about the whole path:
/// a real CLI process composing a body, a real runtime storing it, and a second CLI process reading it back. No
/// plugin is installed and no route exists — admitting a report asks nothing of any provider.
/// </remarks>
public class ReportRoundTripTests
{
    [Fact]
    public async Task Uncertainty_and_unknown_fields_come_back_through_the_cli_exactly_as_they_were_sent()
    {
        using var it = GoldenPath.Create("report-round-trip");
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: false));
        await GoldenPath.StartAsync(it);

        var submitted = await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "--actor", "human:person-1",
            "report", "submit",
            "--effect", "email_sent",
            "--tool", "some-other-cli 1.2.3",
            "--summary", "A follow-up was sent by hand while the runtime was not involved.",
            "--occurred-at", "2026-09-17T11:04:00Z",
            "--unknown", "observed_at",
            "--uncertainty", "The send was queued; nobody watched it leave.",
            "--evidence", """{"subject":"Following up"}""",
            "--reason", "Reporting it the moment I noticed."));

        var admitted = GoldenPath.Json(submitted);
        var id = (string)admitted["id"]!;
        Assert.StartsWith("rpt_", id, StringComparison.Ordinal);

        var read = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "report", "get", id)));

        // Built from the flags above rather than from the first answer, so the two answers agreeing with each
        // other is not mistaken for either of them agreeing with what was typed.
        var expected = new JsonObject
        {
            ["effect"] = "email_sent",
            ["tool"] = "some-other-cli 1.2.3",
            ["summary"] = "A follow-up was sent by hand while the runtime was not involved.",
            ["occurred_at"] = "2026-09-17T11:04:00Z",
            ["unknown_fields"] = new JsonArray("observed_at"),
            ["uncertainty"] = "The send was queued; nobody watched it leave.",
            ["evidence"] = new JsonObject { ["subject"] = "Following up" },
        };

        Assert.True(
            JsonNode.DeepEquals(expected, read["assertion"]),
            $"The assertion came back as {read["assertion"]?.ToJsonString()}");
        Assert.True(JsonNode.DeepEquals(admitted["assertion"], read["assertion"]));

        // The envelope is not part of what was asserted about the world, and the receipt is the runtime's.
        var assertion = read["assertion"]!.AsObject();
        Assert.False(assertion.ContainsKey("actor"));
        Assert.False(assertion.ContainsKey("reason"));
        Assert.Equal("Reporting it the moment I noticed.", (string?)read["reason"]);
        Assert.Equal("human", (string?)read["reporter"]!["type"]);
        Assert.Equal("person-1", (string?)read["reporter"]!["id"]);
        Assert.False((bool)read["correlation"]!["verified"]!);

        // Nothing about admitting a report involves a provider, so the one installed beside these tests was
        // never asked anything.
        Assert.Empty(it.Account.Calls);

        await GoldenPath.StopAsync(it);
    }
}
