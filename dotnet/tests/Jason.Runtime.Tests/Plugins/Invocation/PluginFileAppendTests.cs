using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// A journal subscription runs one chain per kind, so several plugin hosts append to one file at the same moment. What
/// holds them apart is the share the door opens the file with, one open per line, and only real processes can say whether
/// it does: a process that keeps a line to itself on one platform tears it on another.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PluginFileAppendTests
{
    private const int Hosts = 4;
    private const int LinesPerHost = 250;

    /// <summary>About as long as a journal line: long enough that a line torn by another host cannot pass for a whole one.</summary>
    private const int LineLength = 300;

    private static readonly TimeSpan ReaderPause = TimeSpan.FromMilliseconds(20);

    [Fact]
    [Critical(Family.R1, "several hosts appending to one file keep every line whole, on every platform")]
    public async Task Four_plugin_hosts_appending_to_one_file_keep_every_line_whole_and_every_host_succeeds_while_a_reader_keeps_opening_the_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-file-append-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "lines");
        var file = Path.Combine(folder, "lines.jsonl");
        Directory.CreateDirectory(folder);
        try
        {
            await using var api = await PluginInvokerTests.StartAsync(
                paths =>
                {
                    TestPlugins.Write(paths, "appender", Manifest(), Script(file));
                    Grant(paths, folder);
                });

            var invocations = Enumerable.Range(0, Hosts)
                .Select(host => PluginInvokerTests.InvokeAsync(
                    api,
                    PluginInvokerTests.Request("echo.run", new JsonObject { ["host"] = host }, plugin: "appender", timeout: TimeSpan.FromMinutes(4)),
                    TestContext.Current.CancellationToken))
                .ToArray();
            var all = Task.WhenAll(invocations);

            // A reader that must not stop the appends, and that opens, reads and closes between its reads: on Linux and macOS a
            // .NET program that keeps the file open holds a shared lock for as long as it does. Whether a read lands between two
            // appends is not the row's to claim: four hosts in a tight loop hold the lock nearly all the time, and on a fast
            // runner every line is down before one try finds a gap. What the row holds is what the reader cannot take away.
            while (!all.IsCompleted)
            {
                _ = LengthRead(file);
                await Task.WhenAny(all, Task.Delay(ReaderPause, TestContext.Current.CancellationToken));
            }

            var outcomes = await all;

            Assert.All(outcomes, outcome => Assert.IsType<InvocationOutcome.Succeeded>(outcome.Outcome));

            var whole = File.ReadAllLines(file, new UTF8Encoding(false));
            Assert.Equal(Hosts * LinesPerHost, whole.Length);
            for (var host = 0; host < Hosts; host++)
            {
                var prefix = string.Create(CultureInfo.InvariantCulture, $"{host}:");
                var own = whole.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                Assert.Equal(LinesPerHost, own.Length);
                Assert.All(own, line => Assert.Equal(LineLength, line.Length));
                Assert.Equal(
                    Enumerable.Range(0, LinesPerHost),
                    own.Select(line => int.Parse(line.Split(':')[1], CultureInfo.InvariantCulture)));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// What one read found: nothing when the file is not there yet or a writer has it for the length of a line, and then
    /// the read only has to have been tried.
    /// </summary>
    private static long LengthRead(string file)
    {
        try
        {
            using var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            reader.CopyTo(Stream.Null);
            return reader.Position;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static string Manifest() =>
        TestPlugins.Manifest("appender", extra: "capabilities:\n  files:\n    append: true\n");

    private static string Script(string file) =>
        "export function invoke(operation, input) {\n"
        + "  const prefix = input.host + \":\";\n"
        + "  for (let line = 0; line < " + LinesPerHost.ToString(CultureInfo.InvariantCulture) + "; line++) {\n"
        + "    const head = prefix + line + \":\";\n"
        + "    host.appendLine(" + System.Text.Json.JsonSerializer.Serialize(file) + ", head + \"x\".repeat(" + LineLength.ToString(CultureInfo.InvariantCulture) + " - head.length));\n"
        + "  }\n"
        + "  return { result: {} };\n"
        + "}\n";

    private static void Grant(JasonPaths paths, string folder)
    {
        TestPlugins.Grant(paths, "appender");
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        var plugins = settings["Plugins"]!.AsObject();
        plugins["Grants"]!["appender"]!["Files"] = new JsonArray(folder);
        plugins["Files"] = new JsonObject { ["MaxCalls"] = LinesPerHost };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }
}
