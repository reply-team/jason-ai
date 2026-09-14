using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Tests;

public class HostDiagnosticsTests
{
    private static readonly LogLimits Limits = new(LineBytes: 16_384, TotalBytes: 4_194_304);

    private static HostDiagnostics Diagnostics(TextWriter stderr, Redactor? redactor = null, LogLimits? limits = null) =>
        new(stderr, redactor ?? Redactor.None, limits ?? Limits, "fake-provider", "pin_1", TimeProvider.System);

    private static JsonObject[] Lines(StringWriter stderr) =>
        [.. stderr.ToString()
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())];

    [Fact]
    public void Every_line_is_one_json_object_naming_who_wrote_it()
    {
        using var stderr = new StringWriter();
        var diagnostics = Diagnostics(stderr);

        diagnostics.Host("info", "started");
        diagnostics.Plugin("warn", "careful", new JsonObject { ["attempt"] = 2 });

        var lines = Lines(stderr);
        Assert.Equal(2, lines.Length);
        Assert.Equal("host", lines[0]["source"]!.GetValue<string>());
        Assert.Equal("info", lines[0]["level"]!.GetValue<string>());
        Assert.Equal("started", lines[0]["message"]!.GetValue<string>());
        Assert.Equal("fake-provider", lines[0]["plugin"]!.GetValue<string>());
        Assert.Equal("pin_1", lines[0]["invocation_id"]!.GetValue<string>());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", lines[0]["ts"]!.GetValue<string>());
        Assert.Equal("plugin", lines[1]["source"]!.GetValue<string>());
        Assert.Equal(2, lines[1]["data"]!["attempt"]!.GetValue<int>());
    }

    [Fact]
    public void Only_the_plugins_own_lines_are_counted_for_the_outcome()
    {
        using var stderr = new StringWriter();
        var diagnostics = Diagnostics(stderr);

        diagnostics.Host("debug", "resolved an executable");
        diagnostics.Plugin("info", "one");
        diagnostics.Plugin("info", "two");

        Assert.Equal(2, diagnostics.LogLines);
    }

    [Fact]
    public void A_secret_the_host_knows_is_masked_in_the_message_and_in_the_data()
    {
        using var stderr = new StringWriter();
        var diagnostics = Diagnostics(stderr, new Redactor(["super-secret-token"]));

        diagnostics.Plugin("info", "sending super-secret-token upstream", new JsonObject { ["header"] = "Bearer super-secret-token" });

        var line = Lines(stderr)[0];
        Assert.DoesNotContain("super-secret-token", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, line["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, line["data"]!["header"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_or_a_payload_too_long_for_a_line_is_cut_and_flagged()
    {
        using var stderr = new StringWriter();
        var diagnostics = Diagnostics(stderr, limits: new LogLimits(LineBytes: 256, TotalBytes: 4_194_304));

        diagnostics.Plugin("info", new string('m', 8000), new JsonObject { ["blob"] = new string('d', 4000) });

        var line = Lines(stderr)[0];
        Assert.True(line["message"]!.GetValue<string>().Length <= 4096);
        Assert.True(line["truncated"]!.GetValue<bool>());
        Assert.True(line.ToJsonString().Length < 8000);
    }

    [Fact]
    public void After_its_budget_the_log_says_so_once_and_then_stays_quiet()
    {
        using var stderr = new StringWriter();
        var diagnostics = Diagnostics(stderr, limits: new LogLimits(LineBytes: 1024, TotalBytes: 2048));

        for (var index = 0; index < 100; index++)
        {
            diagnostics.Plugin("info", new string('x', 500));
        }

        var lines = Lines(stderr);
        Assert.Single(lines, line => line["message"]!.GetValue<string>() == "log_truncated");
        Assert.Equal("log_truncated", lines[^1]["message"]!.GetValue<string>());
        Assert.Equal("host", lines[^1]["source"]!.GetValue<string>());
        Assert.True(stderr.ToString().Length < 8192);
    }

    [Fact]
    public void A_line_is_valid_json_whatever_the_plugin_put_in_it()
    {
        using var stderr = new StringWriter();
        var diagnostics = Diagnostics(stderr);

        diagnostics.Plugin("error", "quotes \" newlines \n and {braces}", new JsonObject { ["nested"] = new JsonObject { ["x"] = "}" } });

        var line = Lines(stderr)[0];
        Assert.Equal("quotes \" newlines \n and {braces}", line["message"]!.GetValue<string>());
        Assert.Equal("}", line["data"]!["nested"]!["x"]!.GetValue<string>());
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(line.ToJsonString()).RootElement.ValueKind);
    }
}
