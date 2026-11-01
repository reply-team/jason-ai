using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The stand-in plugin is the shape a plugin author copies, so it reports a failure the way the runtime accepts one:
/// under a code the operation it performs declares, and under the class the operation declares it with. Its table of
/// what it reports is read out of its own file, the way the Reply package's is, so one source holds both the plugin
/// and this check.
/// </summary>
public sealed partial class StandInReportsTests
{
    /// <summary>The canonical operations the stand-in's manifest lists, as against the ones that exist only to drive the host.</summary>
    public static TheoryData<string> Performed() => [.. StandIn.Performed().Where(operation => OperationCatalog.Find(operation) is not null)];

    [Fact]
    public void The_table_is_held_for_every_canonical_operation_the_stand_in_performs()
    {
        Assert.Equal(
            Performed().Select(row => row.Data).Order(StringComparer.Ordinal),
            StandIn.Reports["per_operation"]!.AsObject().Select(entry => entry.Key).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Performed))]
    public void Every_failure_the_stand_in_can_report_is_one_the_operation_declares_under_the_same_class(string operation)
    {
        var contract = OperationCatalog.Find(operation)!;
        var rows = StandIn.Reports["shared"]!.AsObject().Concat(StandIn.Reports["per_operation"]![operation]!.AsObject());

        foreach (var (key, row) in rows)
        {
            var code = (string)row!["code"]!;
            var reported = (string)row["class"]!;
            var declared = contract.FailureCodes.SingleOrDefault(failure => failure.Code == code);
            Assert.True(declared is not null, $"row '{key}' reports `{code}` for {operation}, which declares no such code.");
            Assert.True(
                Enum.TryParse<FailureClass>(reported, ignoreCase: true, out var mapped) && mapped == declared.Class,
                $"row '{key}' reports `{code}` for {operation} as `{reported}`; the operation declares it `{declared.Class}`.");
        }
    }

    /// <summary>
    /// Everything else the plugin fails with is outside every contract on purpose, and only where no contract applies:
    /// a provoked failure, whose operations exist to drive the host and are in no catalog, and an operation the manifest
    /// does not list, which no work item can name. Every other failure goes through the table.
    /// </summary>
    [Fact]
    public void A_failure_outside_the_table_is_written_only_where_no_contract_applies()
    {
        var module = File.ReadAllText(Path.Combine(TestPlugins.FakeProviderSource, "main.js"));

        var literal = LiteralFailure().Matches(module).Select(match => match.Groups["code"].Value);
        Assert.Equal(["unknown_operation"], literal);

        Assert.All(
            StandIn.Performed().Where(operation => operation.StartsWith("fail.", StringComparison.Ordinal)),
            operation => Assert.True(OperationCatalog.Find(operation) is null, $"{operation} provokes any failure it is given, and a contract now covers it."));
    }

    /// <summary>
    /// The events stand-in also performs <c>campaign.get</c>, and its shared call fails under a code of whichever contract
    /// it is answering: the events contract's words never stand in for the operation's.
    /// </summary>
    [Theory]
    [InlineData("unauthorized", FailureClass.Permanent)]
    [InlineData("unreachable", FailureClass.Transient)]
    public void The_events_stand_in_fails_campaign_get_under_codes_campaign_get_declares(string failure, FailureClass failureClass)
    {
        var module = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", "fake-events", "main.js");
        var codes = CampaignGetCodes().Match(File.ReadAllText(module));
        Assert.True(codes.Success, $"'{module}' names no CAMPAIGN_GET_CODES.");

        var code = (string)JsonNode.Parse(codes.Groups["codes"].Value)![failure]!;
        var declared = OperationCatalog.Find("campaign.get")!.FailureCodes.SingleOrDefault(entry => entry.Code == code);
        Assert.True(declared is not null && declared.Class == failureClass, $"the events stand-in fails campaign.get as `{code}`, which campaign.get does not declare `{failureClass}`.");
    }

    [GeneratedRegex(@"const CAMPAIGN_GET_CODES = (?<codes>\{[^}]*\});")]
    private static partial Regex CampaignGetCodes();

    [GeneratedRegex(@"host\.fail\(\{\s*class: ""[a-z]+"",\s*code: ""(?<code>[a-z_]+)""")]
    private static partial Regex LiteralFailure();
}

/// <summary>The stand-in's own table and manifest, read out of the package's own files.</summary>
internal static class StandIn
{
    /// <summary>What the stand-in reports, shared and per operation: <c>REPORTS</c> in its <c>main.js</c>.</summary>
    public static JsonObject Reports
    {
        get
        {
            var module = Path.Combine(TestPlugins.FakeProviderSource, "main.js");
            var text = File.ReadAllText(module);

            const string Marker = "export const REPORTS = ";
            var start = text.IndexOf(Marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"'{module}' declares no REPORTS.");

            var open = start + Marker.Length;
            var end = text.IndexOf("\n};", open, StringComparison.Ordinal);
            Assert.True(end > open, $"REPORTS in '{module}' does not end with a closing brace on a line of its own.");

            return JsonNode.Parse(text[open..(end + 2)])!.AsObject();
        }
    }

    /// <summary>The operations the stand-in's manifest lists, read from its one <c>operations:</c> line.</summary>
    public static IReadOnlyList<string> Performed()
    {
        var manifest = File.ReadAllLines(Path.Combine(TestPlugins.FakeProviderSource, "plugin.yaml"));
        var line = Assert.Single(manifest, entry => entry.StartsWith("operations: [", StringComparison.Ordinal));
        return [.. line["operations: [".Length..].TrimEnd(']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }
}
