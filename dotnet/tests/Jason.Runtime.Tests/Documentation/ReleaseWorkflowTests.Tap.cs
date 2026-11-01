using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The Homebrew tap: once a tag's release is published, a job of its own asks the tap to bump its formula, with the one
/// token it holds. Its PowerShell is run here with <c>Invoke-WebRequest</c> standing in.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private const string TapRequestToken = "github_pat_00NOTAREALTOKEN";

    private const string TapDispatch = "https://api.github.com/repos/reply-team/homebrew-tap/actions/workflows/bump.yml/dispatches";

    private const string TapTail = "The release is published; the tap moves at its six-hour schedule until this job runs green.";

    /// <summary>The one request the tap's step sends, as the stand-in records it: the bump takes no input, so only the ref.</summary>
    private const string TapRequest =
        $$"""POST {{TapDispatch}} application/json {"ref":"main"} | Accept=application/vnd.github+json | Authorization=Bearer {{TapRequestToken}} | X-GitHub-Api-Version=2022-11-28""";

    /// <summary>
    /// The tap is told once the release is published, by a job after the release job that runs only when that job
    /// succeeded, only for a tag and only in the public repository. It is a job of its own so that "Re-run failed jobs"
    /// after a refused dispatch runs the dispatch alone, never the publication again, which would refuse a release that is
    /// already published. It holds no permission of this repository's, checks nothing out and uses no action: its one step
    /// sends one request with the tap's token, which only this job holds, in the environment that admits release tags.
    /// </summary>
    [Fact]
    public void The_tap_is_told_after_the_release_by_a_job_that_holds_nothing_but_its_token()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var tap = TapJob();
        Assert.Equal(PublishingJobName(jobs), Scalar(tap, "needs"));
        Assert.Equal(TagHere, Scalar(tap, "if"));
        Assert.Equal(SigningEnvironment, Scalar(tap, "environment"));
        Assert.Empty(Assert.IsType<YamlMappingNode>(Node(tap, "permissions")).Children);
        Assert.Equal("ubuntu-latest", Scalar(tap, "runs-on"));
        Assert.Equal("5", Scalar(tap, "timeout-minutes"));

        var step = Assert.Single(JobSteps(tap));
        Assert.Null(Node(step, "uses"));
        Assert.Null(Node(step, "if"));
        Assert.Equal("pwsh", Scalar(step, "shell"));
        var env = Mapping(step, "env");
        Assert.Equal(["TOKEN"], env.Children.Keys.Select(key => key.ToString()));
        Assert.Equal(TapToken, Scalar(env, "TOKEN"));
    }

    /// <summary>
    /// A 2xx is a bump queued. GitHub documents the answer as 200 with the run it queued, and the run's address is
    /// printed so the green line leads to what the dispatch does not prove; an answer without one — a 204, or a body
    /// that cannot be read — is queued all the same.
    /// </summary>
    [Theory]
    [InlineData(200, """{"workflow_run_id":1,"run_url":"https://api.github.com/repos/reply-team/homebrew-tap/actions/runs/1","html_url":"https://github.com/reply-team/homebrew-tap/actions/runs/1"}""", "The tap's bump is queued at https://github.com/reply-team/homebrew-tap/actions/runs/1; it reads the latest release itself.")]
    [InlineData(204, "", "The tap's bump is queued; it reads the latest release itself.")]
    [InlineData(200, "<html>", "The tap's bump is queued; it reads the latest release itself.")]
    public void A_queued_bump_is_green_and_names_its_run_when_GitHub_says_which(int status, string body, string said)
    {
        var run = RunTheTapStep(TapRequestToken, status, body: body);

        Assert.True(run.Exit == 0, $"The step exited {run.Exit}: {run.Stdout}{run.Stderr}");
        Assert.Contains(said, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("::", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([TapRequest], run.Calls);
    }

    /// <summary>
    /// What the tap's step does with each answer GitHub can give: a 2xx is a bump queued, and every refusal is a red job
    /// that says what to do about it — the release stays published, since the job runs after it and undoes nothing. The
    /// request is the same every time: the bump takes no input and reads the latest release itself.
    /// <c>-SkipHttpErrorCheck</c>, which hands a refusal back as a response to read rather than throwing it, is
    /// PowerShell 7's; <c>ubuntu-latest</c> has it, and the stand-in throws without it.
    /// </summary>
    [Theory]
    [InlineData(401, "::error::HOMEBREW_TAP_DISPATCH_TOKEN was refused (expired or revoked): make a new one, store it in the signing environment, re-run this job.")]
    [InlineData(403, "::error::HOMEBREW_TAP_DISPATCH_TOKEN may not dispatch there: it needs Actions read and write on reply-team/homebrew-tap, and the organisation may still have to approve it.")]
    [InlineData(404, "::error::HOMEBREW_TAP_DISPATCH_TOKEN does not reach reply-team/homebrew-tap's bump.yml: the repository is not selected for it, or the workflow moved.")]
    [InlineData(422, "::error::GitHub answered 422: Workflow does not have 'workflow_dispatch' trigger.")]
    [InlineData(500, "::error::GitHub answered 500: Server Error.")]
    public void The_tap_is_dispatched_once_and_every_answer_but_a_queued_bump_turns_the_job_red(int status, string said)
    {
        var message = status switch { 422 => "Workflow does not have 'workflow_dispatch' trigger", 500 => "Server Error", _ => "Bad credentials" };
        var run = RunTheTapStep(TapRequestToken, status, body: $$"""{"message":"{{message}}"}""");

        Assert.True(run.Exit == 1, $"The step exited {run.Exit}: {run.Stdout}{run.Stderr}");
        Assert.Contains($"{said} {TapTail}", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([TapRequest], run.Calls);
    }

    /// <summary>
    /// A request that never gets an answer — no address, no connection — throws even with <c>-SkipHttpErrorCheck</c>;
    /// the job says so in its own words, with what a refusal says after it, and is red.
    /// </summary>
    [Fact]
    public void A_dispatch_that_gets_no_answer_turns_the_job_red_in_its_own_words()
    {
        var run = RunTheTapStep(TapRequestToken, 204, throws: "No such host is known. (api.github.com:443)");

        Assert.Equal(1, run.Exit);
        Assert.Contains($"::error::GitHub could not be reached: No such host is known. (api.github.com:443). {TapTail}", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(TapRequestToken, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(TapRequestToken, run.Stderr, StringComparison.Ordinal);
    }

    /// <summary>A token that is not there sends nothing and is red, saying what to store and where.</summary>
    [Fact]
    public void A_missing_tap_token_sends_nothing_and_turns_the_job_red()
    {
        var run = RunTheTapStep(string.Empty, 204);

        Assert.Equal(1, run.Exit);
        Assert.Empty(run.Calls);
        Assert.Contains(
            "::error::HOMEBREW_TAP_DISPATCH_TOKEN is not set: the tap moves at its six-hour schedule instead. Store a fine-grained token with Actions read and write on reply-team/homebrew-tap in the signing environment, then re-run this job.",
            run.Stdout,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A token that expires says when in a header of the response, as GitHub sent it when this was written, for example
    /// <c>2026-11-25 13:50:00 UTC</c>; inside two weeks the run warns, a token whose moment has passed is said to have
    /// expired, and a warning never turns the job red. The moment is read in UTC or at the offset it names.
    /// </summary>
    [Theory]
    [InlineData(10, "UTC", true)]
    [InlineData(13, "UTC", true)]
    [InlineData(15, "UTC", false)]
    [InlineData(30, "UTC", false)]
    [InlineData(1, "UTC", true)]
    [InlineData(0, "UTC", true)]
    [InlineData(-3, "UTC", true)]
    [InlineData(10, "+02:00", true)]
    [InlineData(10, "-0700", true)]
    [InlineData(30, "-0700", false)]
    public void A_tap_token_that_expires_inside_two_weeks_is_warned_of_and_the_job_stays_green(int days, string zone, bool warned)
    {
        var offset = zone == "UTC" ? TimeSpan.Zero : TimeSpan.ParseExact(zone.TrimStart('+', '-').Replace(":", string.Empty, StringComparison.Ordinal), "hhmm", null) * (zone[0] == '-' ? -1 : 1);
        var at = DateTimeOffset.UtcNow.AddDays(days).ToOffset(offset);
        var run = RunTheTapStep(TapRequestToken, 204, $"{at:yyyy-MM-dd HH:mm:ss} {zone}");

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        var date = $"{at.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
        var warning = days switch
        {
            <= 0 => $"::warning title=Tap token::HOMEBREW_TAP_DISPATCH_TOKEN expired {date}: make a new one and store it in the signing environment.",
            1 => $"::warning title=Tap token::HOMEBREW_TAP_DISPATCH_TOKEN expires {date}, in 1 day: make a new one and store it in the signing environment before then.",
            _ => $"::warning title=Tap token::HOMEBREW_TAP_DISPATCH_TOKEN expires {date}, in {days} days: make a new one and store it in the signing environment before then.",
        };
        if (warned)
        {
            Assert.Contains(warning, run.Stdout, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("::warning", run.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>An expiry it cannot read is named as it came, as a warning; a token without one sends no header and hears nothing.</summary>
    [Theory]
    [InlineData("next Tuesday", true)]
    [InlineData(null, false)]
    public void An_expiry_the_tap_step_cannot_read_is_warned_of_and_a_token_without_one_is_not(string? expiration, bool warned)
    {
        var run = RunTheTapStep(TapRequestToken, 204, expiration);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        if (warned)
        {
            Assert.Contains($"::warning title=Tap token::GitHub says HOMEBREW_TAP_DISPATCH_TOKEN expires '{expiration}', which is not a date this job reads: look at the token's expiry yourself.", run.Stdout, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("::warning", run.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>The token goes into the request and nowhere else: no answer, warning or refusal prints it.</summary>
    [Theory]
    [InlineData(204)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    public void The_tap_token_reaches_the_request_and_never_the_log(int status)
    {
        var run = RunTheTapStep(TapRequestToken, status, "not a date", """{"message":"Bad credentials"}""");

        Assert.Contains(run.Calls, call => call.Contains($"Bearer {TapRequestToken}", StringComparison.Ordinal));
        Assert.DoesNotContain(TapRequestToken, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(TapRequestToken, run.Stderr, StringComparison.Ordinal);
    }

    private static (int Exit, string Stdout, string Stderr, IReadOnlyList<string> Calls) RunTheTapStep(string token, int status, string? expiration = null, string body = "", string? throws = null)
    {
        var step = Assert.Single(JobSteps(TapJob()));

        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "step.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, TapStandIn.Replace("@STEP@", script, StringComparison.Ordinal));

        var environment = new Dictionary<string, string>
        {
            ["CALLS"] = calls,
            ["TAP_STATUS"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TAP_BODY"] = body,
            ["TOKEN"] = token,
        };
        if (expiration is not null)
        {
            environment["TAP_EXPIRATION"] = expiration;
        }

        if (throws is not null)
        {
            environment["TAP_THROW"] = throws;
        }

        var (exit, stdout, stderr) = Pwsh(environment, "-File", harness);
        return (exit, stdout, stderr, File.ReadAllLines(calls));
    }

    /// <summary>
    /// <c>Invoke-WebRequest</c> for the length of the tap's step, taking only the parameters the real one is given and
    /// recording each request as one line: method, address, content type, body, then the headers by name. It answers
    /// the status it is told, with the expiry header only when there is one to send, and — as the real one does
    /// without <c>-SkipHttpErrorCheck</c> — throws on a refusal it was not told to hand back. Told to throw, it throws
    /// whatever the status, as a request that never got an answer does.
    /// </summary>
    private const string TapStandIn = """
        $ErrorActionPreference = 'Stop'

        function Invoke-WebRequest {
            param([string] $Method, [string] $Uri, [hashtable] $Headers, [string] $ContentType, [string] $Body, [switch] $SkipHttpErrorCheck)
            $named = ($Headers.Keys | Sort-Object | ForEach-Object { "$_=$($Headers[$_])" }) -join ' | '
            Add-Content -LiteralPath $env:CALLS -Value "$($Method.ToUpperInvariant()) $Uri $ContentType $Body | $named"
            if ($env:TAP_THROW) { throw $env:TAP_THROW }
            $status = [int] $env:TAP_STATUS
            if ($status -ge 400 -and -not $SkipHttpErrorCheck) { throw "Response status code does not indicate success: $status." }
            $answered = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.IEnumerable[string]]]::new([StringComparer]::OrdinalIgnoreCase)
            if ($null -ne $env:TAP_EXPIRATION) { $answered['github-authentication-token-expiration'] = [string[]] @($env:TAP_EXPIRATION) }
            [pscustomobject] @{ StatusCode = $status; Headers = $answered; Content = $env:TAP_BODY }
        }

        try { & '@STEP@'; if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;
}
