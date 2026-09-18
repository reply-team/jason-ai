using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// One role, end to end, through the shipped executable: a profile names a host, the runtime launches it, and
/// the child that comes up reads its skill out of the directory it was started in, reads its own note through
/// the API, answers in the shape it was asked for and writes down what it learned.
/// <para>
/// The host here is the repository's stand-in rather than an installed agent, and it is the only pretend thing
/// in the run. The process, the work directory, the skill file, the callbacks, the note and the validation of
/// the answer are all real, so what this proves is the mechanism a real agent will meet.
/// </para>
/// </summary>
public class ResearcherRoleTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private const string Settings = """
        {"Dispatcher":{"TickSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":1}}}
        """;

    /// <summary>
    /// What the brief asks for back. Small on purpose: the point is that the runtime holds the answer to it,
    /// and that a role which answers with a sentence instead is told so.
    /// </summary>
    private const string Shape = """
        {"type":"object","properties":{"findings":{"type":"array"},"taught_by":{"type":"string"}},"required":["findings","taught_by"]}
        """;

    /// <summary>
    /// The skill this attempt is taught with. Written by the test rather than taken from the repository's own
    /// pack, so what is being proved is that the file put in the work directory is the file the child reads —
    /// a claim a shipped skill, present for other reasons, would not pin down.
    /// </summary>
    private const string Skill = """
        ---
        name: researcher
        description: Find and verify facts about accounts and people.
        ---

        Read your note before you start. It is your own memory and not the truth.
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string StandInHost => Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "Jason.FakeAgentHost.exe" : "Jason.FakeAgentHost");

    [Fact]
    public async Task A_role_reads_its_skill_and_its_note_answers_in_shape_and_remembers_what_it_learned()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-researcher", Guid.NewGuid().ToString("N"));
        var paths = new JasonPaths(root);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        await File.WriteAllTextAsync(paths.UserSettingsFile, Settings, Ct);

        // What the operator composed: one directory per role under the data directory's own skills tree.
        var skillDirectory = Path.Combine(paths.RoleSkillsDirectory, "researcher");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), Skill, Ct);

        try
        {
            AssertOk(await JasonAsync(root, "runtime", "start"));

            AssertOk(await JasonAsync(root, "profile", "create", "local-host", "--host", "claude_code", "--program", StandInHost));
            var campaign = (string)Json(await JasonAsync(root, "campaign", "create", "--name", "Researched"))["id"]!;
            AssertOk(await JasonAsync(root, "campaign", "start", campaign));

            // An earlier pass already wrote something down. Whether it comes back is how "the note was read"
            // is proved: the envelope carries the address of this document and never the document.
            AssertOk(await JasonAsync(
                root, "rolenote", "set", campaign, "researcher", "--note", """{"gatekeeper":"the switchboard hangs up after six"}"""));

            var item = Json(await JasonAsync(
                root,
                "workitem",
                "create",
                campaign,
                "--kind",
                "ai_role",
                "--role",
                "researcher",
                "--execution-profile",
                "local-host",
                "--result-format",
                Shape,
                "--context",
                """{"behaviour":"researcher","brief":"who signs off on this"}"""));

            var done = await FinishedAsync(root, (string)item["id"]!, "succeeded");

            // 1. The skill reached the work directory, and the child read it there: the name comes out of the
            //    file's own front matter, which nothing else in this run could have supplied.
            var result = done["result"]!.AsObject();
            Assert.Equal("researcher", (string?)result["taught_by"]);

            // 2. The note was read: the key an earlier pass wrote is what the run says it recalled.
            Assert.Contains("gatekeeper", result["recalled"]!.AsArray().Select(key => (string?)key));

            // 3. The answer satisfied the shape that was asked for, which is why the item is finished at all.
            Assert.Equal(2, result["findings"]!.AsArray().Count);

            // 4. And the note is the earlier pass's document plus what this one learned — replaced whole, by
            //    the attempt that ran, with the runtime's own count of how many passes have written.
            var attempt = done["attempts"]!.AsArray()[0]!.AsObject();
            var note = Json(await JasonAsync(root, "rolenote", "get", campaign, "researcher"));
            Assert.Equal("the switchboard hangs up after six", (string?)note["note"]!["gatekeeper"]);
            Assert.Equal(1, (int?)note["note"]!["passes"]);
            Assert.Equal((string?)attempt["id"], (string?)note["note"]!["last_attempt"]);
            Assert.Equal("attempt", (string?)note["updated_by"]!["type"]);
            Assert.Equal((string?)attempt["id"], (string?)note["updated_by"]!["id"]);

            // 5. And the attempt says what ran it, for as long as the attempt exists.
            var agent = attempt["provenance"]!["agent"]!.AsObject();
            Assert.Equal("local-host", (string?)agent["profile_name"]);
            Assert.Equal(1, (int?)agent["profile_revision"]);
            Assert.Equal("work_item_override", (string?)agent["resolution_source"]);
            Assert.True(Guid.TryParse((string?)agent["session_id"], out _));
            var skill = attempt["launch"]!["role_skill"]!;
            Assert.Equal(true, (bool?)skill["copied"]);
            Assert.Equal("researcher", (string?)skill["name"]);
        }
        finally
        {
            await JasonAsync(root, "runtime", "stop");
            Delete(root);
        }
    }

    /// <summary>
    /// The sibling case: the same role, taught the same way, reading the same note, refused for the shape of
    /// its answer alone. A model that has been asked for a structure and produces a paragraph is the ordinary
    /// failure of this whole design, and it has to end accountably rather than quietly becoming the result.
    /// </summary>
    [Fact]
    public async Task A_role_that_answers_with_prose_is_refused_and_what_it_remembered_still_stands()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-researcher-prose", Guid.NewGuid().ToString("N"));
        var paths = new JasonPaths(root);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        await File.WriteAllTextAsync(paths.UserSettingsFile, Settings, Ct);
        var skillDirectory = Path.Combine(paths.RoleSkillsDirectory, "researcher");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), Skill, Ct);

        try
        {
            AssertOk(await JasonAsync(root, "runtime", "start"));
            AssertOk(await JasonAsync(root, "profile", "create", "local-host", "--host", "claude_code", "--program", StandInHost));
            var campaign = (string)Json(await JasonAsync(root, "campaign", "create", "--name", "Refused"))["id"]!;
            AssertOk(await JasonAsync(root, "campaign", "start", campaign));

            var item = Json(await JasonAsync(
                root,
                "workitem",
                "create",
                campaign,
                "--kind",
                "ai_role",
                "--role",
                "researcher",
                "--execution-profile",
                "local-host",
                "--result-format",
                Shape,
                "--context",
                """{"behaviour":"researcher --prose","brief":"who signs off on this"}"""));

            var failed = await FinishedAsync(root, (string)item["id"]!, "failed");

            // The sentence never became the result, and the attempt ended for a reason somebody can read.
            Assert.Equal("executor_exited", (string?)failed["last_error"]!["code"]);
            Assert.DoesNotContain("thorough look", failed.ToJsonString(), StringComparison.OrdinalIgnoreCase);

            // And the refusal reached the child while it still held the attempt, rather than being decided
            // after it: the host says on its own stderr what it was told, and the trace of a failed attempt
            // carries the tail of that.
            var error = failed["attempts"]!.AsArray()[0]!["error"]!;
            Assert.Contains("result_invalid", (string?)error["trace"] ?? string.Empty, StringComparison.Ordinal);

            // What the run learned is still written down. A note is not an answer, and it survives one.
            var note = Json(await JasonAsync(root, "rolenote", "get", campaign, "researcher"));
            Assert.Equal(1, (int?)note["note"]!["passes"]);
        }
        finally
        {
            await JasonAsync(root, "runtime", "stop");
            Delete(root);
        }
    }

    private static async Task<JsonObject> FinishedAsync(string root, string workItemId, string expected)
    {
        var deadline = DateTime.UtcNow.Add(StepTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var read = Json(await JasonAsync(root, "workitem", "get", workItemId));
            if ((string?)read["status"] is { } status && status is "succeeded" or "failed")
            {
                Assert.Equal(expected, status);
                return read;
            }

            await Task.Delay(200, Ct);
        }

        throw new InvalidOperationException($"Work item {workItemId} never finished.");
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

    private static async Task<CliResult> JasonAsync(string root, params string[] args)
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

        start.Environment[JasonPaths.DataDirectoryVariable] = root;

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
