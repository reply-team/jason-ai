using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jason.Contracts.Api;
using Jason.Runtime.Execution;
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


    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Contact_data_and_the_token_never_reach_the_log_files()
    {
        using var dir = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
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

    [Fact]
    public async Task Nothing_a_work_item_carries_reaches_the_logs_and_the_token_reaches_nothing_at_all()
    {
        const string Brief = "ctx-canary-5e1b";
        const string Shape = "fmt-canary-77aa";
        const string Outcome = "res-canary-31c0";

        var host = await FakeHostRuntime.StartAsync(Ct);
        var token = host.Fixture.Runtime.Token;
        var paths = host.Paths;
        try
        {
            var campaign = await host.CampaignAsync(Ct);
            await host.RoleAsync("fake-succeed", Ct, "succeed", "--result", $$"""{"secret":"{{Outcome}}"}""");

            // A host that spills its credentials into its own output is the case redaction exists for.
            await host.RoleAsync("fake-leak", Ct, "leak");

            var work = await host.CreateAsync(
                new
                {
                    campaign_id = campaign,
                    kind = "ai_role",
                    role = "fake-succeed",
                    context = new { brief = Brief },
                    result_format = new { type = "object", description = Shape },
                },
                Ct);
            await host.ScanAsync(Ct);
            var done = await host.WaitForStatusAsync(work.Id, WorkItemStatus.Succeeded, Ct);
            Assert.Equal(Outcome, (string?)done.Result!["secret"]);

            var leaky = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "fake-leak" }, Ct);
            await host.ScanAsync(Ct);
            var leaked = await host.WaitForStatusAsync(leaky.Id, WorkItemStatus.Succeeded, Ct);

            // What the child said about the token is kept, with the token itself taken out of it.
            var stderr = FakeHostRuntime.StderrOf(paths, leaked.Id, leaked.Attempts![0]);
            Assert.Contains($"token={TokenRedactor.Mask}", stderr, StringComparison.Ordinal);

            // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
            await host.Fixture.Runtime.StopAsync();

            var logs = ReadAll(paths.LogsDirectory);
            Assert.Contains("Runtime API listening on", logs, StringComparison.Ordinal);
            Assert.DoesNotContain(Brief, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(Shape, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(Outcome, logs, StringComparison.Ordinal);
            Assert.DoesNotContain("context_snapshot", logs, StringComparison.Ordinal);
            Assert.DoesNotContain(token, logs, StringComparison.Ordinal);

            // The work directory keeps everything the children wrote, minus the one thing they never had.
            var artifacts = ReadAll(paths.WorkDirectory);
            Assert.Contains(TokenRedactor.Mask, artifacts, StringComparison.Ordinal);
            Assert.DoesNotContain(token, artifacts, StringComparison.Ordinal);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static string ReadAll(string directory)
    {
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        return string.Concat(files.Select(FakeHostRuntime.ReadShared));
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
