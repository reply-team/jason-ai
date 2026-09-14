using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jason.Contracts.Api;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The log files are read by agents and pasted into bug reports, so what a caller sent must never reach them:
/// not a contact's address, not what the caller keeps in the contact's custom fields, not the value a
/// validation error complained about, and not the capability token. Distinctive canary strings are used so a
/// match cannot be a coincidence.
/// </summary>
public class PrivacyTests
{
    private const string Address = "privacy-canary-7f3a@example.test";
    private const string Secret = "canary-9c1d";
    private const string Rejected = "bad-canary-2b8e";

    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Contact_data_and_the_token_never_reach_the_log_files()
    {
        using var dir = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(dir.Paths, Quiet, Ct);
        var token = runtime.Token;
        try
        {
            using var http = new HttpClient { BaseAddress = runtime.BaseUrl };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var accepted = await http.PostAsync(
                Operations.Route(Operations.ContactCreate),
                Json($$$"""
                      {"first_name":"Ada","channels":[{"channel":"email","value":"{{{Address}}}"}],"custom":{"secret":"{{{Secret}}}"}}
                      """),
                Ct);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

            // A rejected value is the one a naive implementation would quote back into the log line.
            using var refused = await http.PostAsync(
                Operations.Route(Operations.ContactCreate),
                Json($$$"""
                      {"channels":[{"channel":"email","value":"{{{Rejected}}}"}]}
                      """),
                Ct);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var body = await refused.Content.ReadAsStringAsync(Ct);
            Assert.Contains("validation_failed", body, StringComparison.Ordinal);
            Assert.DoesNotContain(Rejected, body, StringComparison.Ordinal);
        }
        finally
        {
            // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
            await runtime.StopAsync();
        }

        var files = Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var logs = string.Concat(await Task.WhenAll(files.Select(file => File.ReadAllTextAsync(file, Ct))));
        Assert.Contains("Runtime API listening on", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Address, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Rejected, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
