using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// What a launched executor is, when it reports through the shipped command line and says nothing about itself.
/// <para>
/// The runtime tells a child which attempt it is by putting the attempt's id in its environment, and the CLI
/// reads it when no <c>--actor</c> was given. That is what keeps the work an agent creates traceable to the run
/// that asked for it — and therefore what the execution-profile lineage is read from. Left to the agent to
/// remember, it would be lost the first time one forgot, and nothing about the loss would be visible afterwards.
/// </para>
/// <para>
/// The launcher's half — that the variables really are set on the child — is proved where the launcher lives.
/// This is the other half, through the real executable: given those variables, work created through the CLI is
/// its attempt's, and inherits its attempt's profile.
/// </para>
/// </summary>
public class LaunchedActorTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private const string Settings = """
        {"Dispatcher":{"TickSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":1}}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The stand-in host as a program a profile can name. The executable rather than the assembly: a profile
    /// names something this machine can start, and the runtime puts no interpreter in front of it.
    /// </summary>
    private static string StandInHost => Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "Jason.FakeAgentHost.exe" : "Jason.FakeAgentHost");

    [Fact]
    public async Task Work_created_from_inside_an_attempt_is_that_attempts_and_inherits_its_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-launched-actor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var paths = new JasonPaths(root);
        await File.WriteAllTextAsync(paths.UserSettingsFile, Settings, Ct);

        try
        {
            AssertOk(await JasonAsync(root, null, "runtime", "start"));

            var profile = Json(await JasonAsync(
                root,
                null,
                "profile",
                "create",
                "local-host",
                "--host",
                "claude_code",
                "--program",
                StandInHost));
            Assert.Equal("local-host", (string?)profile["name"]);

            var campaign = (string)Json(await JasonAsync(root, null, "campaign", "create", "--name", "Work"))["id"]!;
            AssertOk(await JasonAsync(root, null, "campaign", "start", campaign));

            // An attempt has to exist before anything can claim to be one: the runtime verifies that claim, so a
            // variable naming an attempt nobody ran buys nothing. This one is a real, finished attempt.
            var ancestor = await AttemptOfAsync(root, campaign, "local-host");

            // The child's environment, exactly as the launcher writes it — and no --actor on the command line.
            var created = Json(await JasonAsync(
                root,
                ancestor.AttemptId,
                "workitem",
                "create",
                campaign,
                "--kind",
                "ai_role",
                "--role",
                "researcher",
                "--context",
                """{"brief":"what the run asked for next"}"""));

            var createdBy = created["created_by"]!.AsObject();
            Assert.Equal("attempt", (string?)createdBy["type"]);
            Assert.Equal(ancestor.AttemptId, (string?)createdBy["id"]);

            // And therefore the work runs where the run that asked for it ran.
            var lineage = created["lineage"]!.AsObject();
            Assert.Equal("inherited", (string?)lineage["state"]);
            Assert.Equal("local-host", (string?)lineage["profile_name"]);
            Assert.Equal(ancestor.AttemptId, (string?)lineage["from_attempt_id"]);
        }
        finally
        {
            await JasonAsync(root, null, "runtime", "stop");
            Delete(root);
        }
    }

    /// <summary>
    /// One finished agent attempt that ran under the named profile, so there is a real run for the next piece of
    /// work to have been created by.
    /// </summary>
    private static async Task<(string WorkItemId, string AttemptId)> AttemptOfAsync(string root, string campaign, string profile)
    {
        var item = Json(await JasonAsync(
            root,
            null,
            "workitem",
            "create",
            campaign,
            "--kind",
            "ai_role",
            "--role",
            "researcher",
            "--execution-profile",
            profile,
            "--context",
            """{"behaviour":"succeed"}"""));

        var id = (string)item["id"]!;
        var deadline = DateTime.UtcNow.Add(StepTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var read = Json(await JasonAsync(root, null, "workitem", "get", id));
            if ((string?)read["status"] is { } status && status is "succeeded" or "failed")
            {
                // The ancestor has to have really run under the profile, or what the next item inherits would
                // be a record of a launch that never happened.
                Assert.Equal("succeeded", status);
                var attempts = read["attempts"]!.AsArray();
                var attempt = attempts[0]!.AsObject();
                Assert.Equal(profile, (string?)attempt["provenance"]!["agent"]!["profile_name"]);
                return (id, (string)attempt["id"]!);
            }

            await Task.Delay(200, Ct);
        }

        throw new InvalidOperationException($"Work item {id} never finished.");
    }

    private static JsonObject Json(CliResult result)
    {
        AssertOk(result);
        return JsonNode.Parse(result.Output)!.AsObject();
    }

    private static void AssertOk(CliResult result) =>
        Assert.True(
            result.ExitCode == 0,
            $"'jason {result.Command}' exited with {result.ExitCode}.{Environment.NewLine}stdout: {result.Output}{Environment.NewLine}stderr: {result.Error}");

    /// <summary>One CLI step, optionally run as a launched executor would be: the attempt in the environment.</summary>
    private static async Task<CliResult> JasonAsync(string root, string? attemptId, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "jason.dll"));
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        TestRuntimeEnvironment.Offline(start.Environment, root);
        if (attemptId is not null)
        {
            start.Environment[ExecutionEnvironment.AttemptIdVariable] = attemptId;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The jason executable could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);

        await process.WaitForExitAsync(Ct).WaitAsync(StepTimeout, Ct);
        return new CliResult(
            process.ExitCode,
            (await output.WaitAsync(StepTimeout, Ct)).Trim(),
            (await error.WaitAsync(StepTimeout, Ct)).Trim(),
            string.Join(' ', args));
    }

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A child that outlived the runtime may still hold a file; the directory is a temporary one.
        }
        catch (UnauthorizedAccessException)
        {
            // Same story, seen from the other side of a permission check.
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error, string Command);
}
