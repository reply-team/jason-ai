using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;
using Jason.Runtime.Tests;

namespace Jason.PluginHost.Tests;

/// <summary>
/// A provider that puts its credential in the request's path, as a bot API does: the host's own line about the call names
/// the path, and a plugin may quote it in a log line or a failure. Whichever way the secret reached the plugin, a
/// configuration value or a granted variable, it is masked in every place the invocation writes.
/// </summary>
[Collection(ModeCollection.Name)]
public sealed class SecretInRequestPathTests : IDisposable
{
    // Made up. Shaped like a bot token: digits, a colon, letters and hyphens, and long enough to be masked.
    private const string Secret = "7000000001:AAF-made-up-0123456789-abcdefghij";

    private const string Variable = "PROOF_BOT_TOKEN";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-plugin-host-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task Answer(HttpListenerContext context)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("""{"ok":true}""");
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    /// <summary>
    /// The secret rides the path of a request to the loopback, once through configuration and once through a granted
    /// variable; the request is answered or refused at the connection. Then the plugin logs the path and fails quoting it.
    /// </summary>
    [Critical(Family.S3, "a secret in a request path is masked in every line and in the failure")]
    [Theory]
    [InlineData("config", true)]
    [InlineData("config", false)]
    [InlineData("env", true)]
    [InlineData("env", false)]
    public async Task A_secret_in_a_request_path_is_masked_in_every_line_and_in_the_failure(string source, bool answered)
    {
        using var server = new LoopbackHttpServer(Answer);
        var hostKey = answered ? server.HostKey : $"127.0.0.1:{LoopbackHttpServer.FreePort()}";
        InvocationFactory.WritePackage(
            _root,
            """
            export function invoke(operation, input) {
              const token = input.source === "config" ? host.config("token") : host.env("PROOF_BOT_TOKEN");
              const url = input.base + "/bot" + token + "/sendMessage";
              const response = host.http({ method: "POST", url, headers: { "Content-Type": "application/json" }, body: "{}" });
              host.log("info", "sent to " + url, { url, status: response.status });
              throw host.fail({ class: "permanent", code: "refused", message: "refused " + url, details: { path: "/bot" + token + "/sendMessage" } });
            }
            """);
        var invocation = InvocationFactory.Create(
            _root,
            "echo.run",
            new JsonObject { ["source"] = source, ["base"] = "http://" + hostKey },
            builder =>
            {
                builder.Grants = new InvocationGrants(null, new HttpGrants([hostKey]), source == "env" ? new EnvGrants([Variable]) : null);
                builder.Config = source == "config" ? new Dictionary<string, string> { ["token"] = Secret } : null;
            });

        string stdout;
        IReadOnlyList<string> stderr;
        var previous = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, source == "env" ? Secret : null);
        try
        {
            (var exit, stdout, stderr) = await ModeRun.RunAsync(invocation, Ct);
            Assert.Equal(HostExitCodes.Completed, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, previous);
        }

        var written = string.Join('\n', stderr);
        Assert.DoesNotContain(Secret, written, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("AAF-made-up", written + stdout, StringComparison.Ordinal);

        var records = ModeRun.Records(stderr);
        var call = Assert.Single(records, record => record["message"]?.GetValue<string>() == (answered ? "http" : "http_failed"));
        Assert.Equal("/bot" + Redactor.Mask + "/sendMessage", call["data"]!["path"]!.GetValue<string>());
        var said = Assert.Single(records, record => record["message"]?.GetValue<string>()?.StartsWith("sent to ", StringComparison.Ordinal) == true);
        Assert.Contains(Redactor.Mask, said["message"]!.GetValue<string>(), StringComparison.Ordinal);

        var error = ModeRun.Outcome(stdout).Error!;
        Assert.Contains(Redactor.Mask, error.Message, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, error.Details!.ToJsonString(), StringComparison.Ordinal);
    }
}
