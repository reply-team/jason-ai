using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// The mode's own seam is one static property, so every test that drives a whole invocation through
/// <c>PluginHostMode</c> shares it and they must not run at the same time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ModeCollection
{
    public const string Name = "plugin-host mode";
}

/// <summary>
/// Runs a complete invocation the way the process does — argv, an envelope on stdin, one outcome on stdout,
/// JSON Lines on stderr — without starting a process, so a conformance test asserts on the real protocol
/// rather than on a runner called directly.
/// </summary>
public static class ModeRun
{
    /// <summary>The canonical package, as every test project copies it next to itself.</summary>
    public static string FakeProviderRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", "fake-provider");

    public static async Task<(int Exit, string Stdout, IReadOnlyList<string> Stderr)> RunAsync(
        PluginInvocation invocation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // The mode's seam is shared state; a test that drives a real invocation wants the real runner whatever
        // a test before it left behind.
        PluginHostMode.Runner = new InvocationRunner();

        using var stdin = new StringReader(JsonSerializer.Serialize(invocation, JasonJson.Options));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        string[] argv =
        [
            "--protocol", invocation.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            "--plugin", invocation.Plugin.Id,
            "--operation", invocation.Operation,
            "--correlation", invocation.CorrelationId,
        ];

        var exit = await PluginHostMode.RunAsync(argv, stdin, stdout, stderr, ct);
        return (exit, stdout.ToString(), Lines(stderr.ToString()));
    }

    /// <summary>The outcome, insisting on what the protocol promises: exactly one JSON document on stdout.</summary>
    public static PluginOutcome Outcome(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(stdout));
        var outcome = JsonSerializer.Deserialize<PluginOutcome>(ref reader, JasonJson.Options)
            ?? throw new InvalidOperationException("stdout carried no outcome.");

        Assert.False(reader.Read(), "stdout carries exactly one JSON document");
        return outcome;
    }

    public static IReadOnlyList<string> Lines(string stderr) =>
        [.. stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static IReadOnlyList<JsonObject> Records(IEnumerable<string> stderr) =>
        [.. stderr.Select(line => JsonNode.Parse(line)).OfType<JsonObject>()];
}
