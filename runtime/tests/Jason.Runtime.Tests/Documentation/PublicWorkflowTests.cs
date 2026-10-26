using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The public repository's own workflows, written here under <c>.github/publish/public/.github/workflows/</c> and
/// published to its root by every release. GitHub never runs them in this repository, so these tests are the only
/// place they run before they run there: parsed for what they are allowed, and their PowerShell executed with
/// <c>git</c> and <c>gh</c> standing in.
/// </summary>
public sealed class PublicWorkflowTests
{
    private static readonly string[] PublicRelease = [".github", "publish", "public", ".github", "workflows", "release.yml"];
    private static readonly string[] PrivateRelease = [".github", "workflows", "release.yml"];
    private const string Sha = "2222222222222222222222222222222222222222";
    private const string TapSecret = "${{ secrets.HOMEBREW_TAP_DISPATCH_TOKEN }}";
    private const string Token = "github_pat_00NOTAREALTOKEN";
    private const string Dispatch = "https://api.github.com/repos/reply-team/homebrew-tap/actions/workflows/bump.yml/dispatches";
    private const string Tail = "The release is published; the tap moves at its six-hour schedule until this job runs green.";

    /// <summary>The one request the tap's step sends, as the stand-in records it: the bump takes no input, so only the ref.</summary>
    private const string Request =
        $$"""POST {{Dispatch}} application/json {"ref":"main"} | Accept=application/vnd.github+json | Authorization=Bearer {{Token}} | X-GitHub-Api-Version=2022-11-28""";

    /// <summary>
    /// A merge into <c>main</c> that changes <c>VERSION</c> starts it, and so does the button, which takes nothing: what
    /// it publishes is what <c>VERSION</c> on <c>main</c> names, so nobody can type a version into it. It publishes with
    /// this repository's own token.
    /// </summary>
    [Fact]
    public void The_public_release_runs_on_a_merge_that_changes_VERSION_and_from_a_button_that_takes_nothing()
    {
        var text = Read(PublicRelease);
        var workflow = Parse(text);
        var triggers = Mapping(workflow, "on");

        var push = Mapping(triggers, "push");
        Assert.Equal(["main"], Assert.IsType<YamlSequenceNode>(Node(push, "branches")).Children.Select(node => node.ToString()));
        Assert.Equal(["VERSION"], Assert.IsType<YamlSequenceNode>(Node(push, "paths")).Children.Select(node => node.ToString()));
        Assert.Equal(["push", "workflow_dispatch"], triggers.Children.Keys.Select(key => key.ToString()).Order(StringComparer.Ordinal));
        var button = Node(triggers, "workflow_dispatch");
        Assert.True(
            button is YamlScalarNode { Value: null or "" } || (button is YamlMappingNode mapping && Node(mapping, "inputs") is null),
            "The button takes an input; it must publish only what VERSION names.");

        var permissions = Mapping(workflow, "permissions");
        Assert.Equal(["contents"], permissions.Children.Keys.Select(key => key.ToString()));
        Assert.Equal("write", Scalar(permissions, "contents"));

        var checkout = Steps().Single(step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true);
        Assert.Equal("0", Scalar(Mapping(checkout, "with"), "fetch-depth"));
    }

    /// <summary>
    /// The one secret the public release holds is the tap's token, handed to the one step that sends it and to nothing
    /// that publishes: the twin of the private release's rule that only its publishing job holds the publication token.
    /// The publication itself needs nothing but this repository's own token.
    /// </summary>
    [Fact]
    [Critical(Family.B4, "the tap token's scope")]
    public void Only_the_tap_step_holds_a_secret_and_it_is_the_tap_token()
    {
        var text = Read(PublicRelease);

        Assert.Single(Regex.Matches(text, @"secrets\."));
        var env = Mapping(Assert.Single(Steps("tap")), "env");
        Assert.Equal(["TOKEN"], env.Children.Keys.Select(key => key.ToString()));
        Assert.Equal(TapSecret, Scalar(env, "TOKEN"));
        Assert.DoesNotContain(Scalars(PublicJob("publish")), scalar => scalar.Contains("secrets.", StringComparison.Ordinal));
    }

    /// <summary>A version one repository builds is a version the other publishes: the two read it with one pattern.</summary>
    [Fact]
    public void The_public_release_reads_a_version_exactly_as_the_private_release_does()
    {
        Assert.Equal(VersionRule(Read(PrivateRelease)), VersionRule(Read(PublicRelease)));
    }

    /// <summary>
    /// What it does in each state the release and its tag can be in, run: the step's own PowerShell, with <c>git</c>
    /// and <c>gh</c> replaced by functions that record what they were asked and answer as the state says.
    /// </summary>
    [Theory]
    [InlineData("draft", false, 0, "tag,push,publish")]
    [InlineData("draft", true, 0, "publish")]
    [InlineData("published", true, 0, "")]
    [InlineData("published", false, 1, "")]
    [InlineData("none", false, 1, "")]
    [InlineData("none", true, 1, "")]
    public void The_public_release_tags_the_merge_and_publishes_its_draft_in_every_state_it_can_meet(string release, bool tagged, int exit, string acts)
    {
        var run = RunTheStep("0.6.0", release, tagged);

        Assert.True(run.Exit == exit, $"The step exited {run.Exit}: {run.Stderr}");
        Assert.Equal(acts, string.Join(',', Acts(run.Calls)));

        if (acts.Contains("tag", StringComparison.Ordinal))
        {
            // The commit that last changed VERSION, which is the merge of the release's pull request, and not
            // whatever main holds when the run happens; annotated, by the workflow's own identity.
            var tag = Assert.Single(run.Calls, call => call.StartsWith("git ", StringComparison.Ordinal) && call.Contains(" tag ", StringComparison.Ordinal));
            Assert.Contains($"--annotate v0.6.0 {Sha} -m Jason 0.6.0", tag, StringComparison.Ordinal);
            Assert.Contains("user.name=github-actions[bot]", tag, StringComparison.Ordinal);
            Assert.Contains(run.Calls, call => call == "git push origin refs/tags/v0.6.0");
        }

        if (acts.Contains("publish", StringComparison.Ordinal))
        {
            var publish = Assert.Single(run.Calls, call => call.StartsWith("gh release edit", StringComparison.Ordinal));
            Assert.Equal("gh release edit v0.6.0 --draft=false --latest", publish);
        }

        if (release == "none")
        {
            Assert.Contains("there is no draft of v0.6.0", run.Stderr, StringComparison.OrdinalIgnoreCase);
        }

        if (release == "published" && tagged)
        {
            Assert.Contains("v0.6.0 is already published", run.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>A candidate is published as a pre-release and never becomes the latest release.</summary>
    [Fact]
    public void A_candidate_is_published_as_a_pre_release_and_never_as_latest()
    {
        var run = RunTheStep("1.0.0-rc.1", "draft", tagged: false);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal("gh release edit v1.0.0-rc.1 --draft=false --prerelease", Assert.Single(run.Calls, call => call.StartsWith("gh release edit", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A release becomes latest unless a newer one already is: a fix to an older line, published after a newer
    /// release, must not take latest from it — that is what every install one-liner and the default feed download.
    /// </summary>
    [Theory]
    [InlineData("v0.7.0", "--latest=false")]
    [InlineData("v0.10.0", "--latest=false")]
    [InlineData("v0.5.3", "--latest")]
    [InlineData("", "--latest")]
    public void A_release_older_than_the_latest_is_published_beside_it(string latest, string flag)
    {
        var run = RunTheStep("0.6.0", "draft", tagged: true, latest);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal($"gh release edit v0.6.0 --draft=false {flag}", Assert.Single(run.Calls, call => call.StartsWith("gh release edit", StringComparison.Ordinal)));
        if (flag == "--latest=false")
        {
            Assert.Contains($"{latest} is newer and stays the latest release", run.Stdout, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("0.6")]
    [InlineData("v0.6.0")]
    [InlineData("0.6.0+build.1")]
    public void A_VERSION_that_is_not_a_version_is_refused_before_anything_is_asked(string version)
    {
        var run = RunTheStep(version, "draft", tagged: false);

        Assert.NotEqual(0, run.Exit);
        Assert.Empty(run.Calls);
    }

    /// <summary>
    /// Once the release is published the tap is told, by a job after <c>publish</c> that runs whenever it ended 0 — its
    /// "already published" answer included — and never after it failed. It holds no permission of this repository's,
    /// checks nothing out and uses no action: its one step sends one request with the tap's token.
    /// </summary>
    [Fact]
    public void The_tap_is_told_after_the_publication_by_a_job_that_holds_nothing_but_its_token()
    {
        var jobs = Mapping(Parse(Read(PublicRelease)), "jobs");
        Assert.Equal(["publish", "tap"], jobs.Children.Keys.Select(key => key.ToString()));

        var tap = PublicJob("tap");
        Assert.Equal("publish", Scalar(tap, "needs"));
        Assert.Null(Node(tap, "if"));
        Assert.Empty(Assert.IsType<YamlMappingNode>(Node(tap, "permissions")).Children);
        Assert.Equal("ubuntu-latest", Scalar(tap, "runs-on"));
        Assert.Equal("5", Scalar(tap, "timeout-minutes"));

        var step = Assert.Single(Steps("tap"));
        Assert.Null(Node(step, "uses"));
        Assert.Equal("pwsh", Scalar(step, "shell"));
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
        var run = RunTheTapStep(Token, status, body: body);

        Assert.True(run.Exit == 0, $"The step exited {run.Exit}: {run.Stdout}{run.Stderr}");
        Assert.Contains(said, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("::", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([Request], run.Calls);
    }

    /// <summary>
    /// What the tap's step does with each answer GitHub can give, run with <c>Invoke-WebRequest</c> standing in: a 2xx
    /// is a bump queued, and every refusal is a red job that says what to do about it — the release stays published,
    /// since the job runs after it and undoes nothing. The request is the same every time: the bump takes no input and
    /// reads the latest release itself. <c>-SkipHttpErrorCheck</c>, which hands a refusal back as a response to read
    /// rather than throwing it, is PowerShell 7's; <c>ubuntu-latest</c> has it, and the stand-in throws without it.
    /// </summary>
    [Theory]
    [InlineData(401, 1, "::error::HOMEBREW_TAP_DISPATCH_TOKEN was refused (expired or revoked): make a new one, store it, re-run this job.")]
    [InlineData(403, 1, "::error::HOMEBREW_TAP_DISPATCH_TOKEN may not dispatch there: it needs Actions read and write on reply-team/homebrew-tap, and the organisation may still have to approve it.")]
    [InlineData(404, 1, "::error::HOMEBREW_TAP_DISPATCH_TOKEN does not reach reply-team/homebrew-tap's bump.yml: the repository is not selected for it, or the workflow moved.")]
    [InlineData(422, 1, "::error::GitHub answered 422: Workflow does not have 'workflow_dispatch' trigger.")]
    [InlineData(500, 1, "::error::GitHub answered 500: Server Error.")]
    public void The_tap_is_dispatched_once_and_every_answer_but_a_queued_bump_turns_the_job_red(int status, int exit, string said)
    {
        var message = status switch { 422 => "Workflow does not have 'workflow_dispatch' trigger", 500 => "Server Error", _ => "Bad credentials" };
        var run = RunTheTapStep(Token, status, body: $$"""{"message":"{{message}}"}""");

        Assert.True(run.Exit == exit, $"The step exited {run.Exit}: {run.Stdout}{run.Stderr}");
        Assert.Contains(said, run.Stdout, StringComparison.Ordinal);
        Assert.Equal([Request], run.Calls);
        Assert.Contains(Tail, run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// A request that never gets an answer — no address, no connection — throws even with <c>-SkipHttpErrorCheck</c>;
    /// the job says so in its own words, with what a refusal says after it, and is red.
    /// </summary>
    [Fact]
    public void A_request_that_gets_no_answer_turns_the_job_red_in_its_own_words()
    {
        var run = RunTheTapStep(Token, 204, throws: "No such host is known. (api.github.com:443)");

        Assert.Equal(1, run.Exit);
        Assert.Contains($"::error::GitHub could not be reached: No such host is known. (api.github.com:443). {Tail}", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, run.Stderr, StringComparison.Ordinal);
    }

    /// <summary>A secret that is not there sends nothing and is red, saying what to store.</summary>
    [Fact]
    public void A_missing_token_sends_nothing_and_turns_the_job_red()
    {
        var run = RunTheTapStep(string.Empty, 204);

        Assert.Equal(1, run.Exit);
        Assert.Empty(run.Calls);
        Assert.Contains(
            "::error::HOMEBREW_TAP_DISPATCH_TOKEN is not set: the tap moves at its six-hour schedule instead. Store a fine-grained token with Actions read and write on reply-team/homebrew-tap, then re-run this job.",
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
    public void A_token_that_expires_inside_two_weeks_is_warned_of_and_the_job_stays_green(int days, string zone, bool warned)
    {
        var offset = zone == "UTC" ? TimeSpan.Zero : TimeSpan.ParseExact(zone.TrimStart('+', '-').Replace(":", string.Empty, StringComparison.Ordinal), "hhmm", null) * (zone[0] == '-' ? -1 : 1);
        var at = DateTimeOffset.UtcNow.AddDays(days).ToOffset(offset);
        var run = RunTheTapStep(Token, 204, $"{at:yyyy-MM-dd HH:mm:ss} {zone}");

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        var date = $"{at.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
        var warning = days switch
        {
            <= 0 => $"::warning title=Tap token::HOMEBREW_TAP_DISPATCH_TOKEN expired {date}: make a new one and store it.",
            1 => $"::warning title=Tap token::HOMEBREW_TAP_DISPATCH_TOKEN expires {date}, in 1 day: make a new one and store it before then.",
            _ => $"::warning title=Tap token::HOMEBREW_TAP_DISPATCH_TOKEN expires {date}, in {days} days: make a new one and store it before then.",
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
    public void An_expiry_it_cannot_read_is_warned_of_and_a_token_without_one_is_not(string? expiration, bool warned)
    {
        var run = RunTheTapStep(Token, 204, expiration);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        if (warned)
        {
            Assert.Contains($"::warning title=Tap token::GitHub says HOMEBREW_TAP_DISPATCH_TOKEN expires '{expiration}'", run.Stdout, StringComparison.Ordinal);
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
    public void The_token_reaches_the_request_and_never_the_log(int status)
    {
        var run = RunTheTapStep(Token, status, "not a date", """{"message":"Bad credentials"}""");

        Assert.Contains(run.Calls, call => call.Contains($"Bearer {Token}", StringComparison.Ordinal));
        Assert.DoesNotContain(Token, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, run.Stderr, StringComparison.Ordinal);
    }

    private static YamlMappingNode PublicJob(string name) => Mapping(Mapping(Parse(Read(PublicRelease)), "jobs"), name);

    private static IEnumerable<YamlMappingNode> Steps(string job = "publish") =>
        Assert.IsType<YamlSequenceNode>(Node(PublicJob(job), "steps")).Children.Cast<YamlMappingNode>();

    /// <summary>Every scalar under a node, keys included, in document order.</summary>
    private static IEnumerable<string> Scalars(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value is { } value ? [value] : [],
        YamlSequenceNode sequence => sequence.Children.SelectMany(Scalars),
        YamlMappingNode mapping => mapping.Children.SelectMany(pair => Scalars(pair.Key).Concat(Scalars(pair.Value))),
        _ => [],
    };

    /// <summary>The identifier and the whole pattern a workflow checks a version with.</summary>
    private static (string Identifier, string Pattern) VersionRule(string workflow) =>
        (Assert.Single(Regex.Matches(workflow, @"\$identifier = '(?<rule>[^']+)'")).Groups["rule"].Value,
         Assert.Single(Regex.Matches(workflow, @"-cnotmatch ""(?<rule>[^""]+)""")).Groups["rule"].Value);

    /// <summary>What the recorded calls did, in order: tagged, pushed the tag, published.</summary>
    private static IEnumerable<string> Acts(IReadOnlyList<string> calls) => calls
        .Select(call => call.StartsWith("git ", StringComparison.Ordinal) && call.Contains(" tag ", StringComparison.Ordinal) ? "tag"
            : call.StartsWith("git push", StringComparison.Ordinal) ? "push"
            : call.StartsWith("gh release edit", StringComparison.Ordinal) ? "publish"
            : null)
        .OfType<string>();

    private static (int Exit, string Stdout, string Stderr, IReadOnlyList<string> Calls) RunTheStep(string version, string release, bool tagged, string latest = "")
    {
        var step = Steps().Single(step => Scalar(step, "run") is not null);

        using var tree = new TempTree();
        var work = tree.NewDirectory("public");
        File.WriteAllText(Path.Combine(work, "VERSION"), version + "\n");
        var script = Path.Combine(tree.Root, "step.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, StandIns.Replace("@STEP@", script, StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["WORK"] = work,
                ["CALLS"] = calls,
                ["RELEASE"] = release,
                ["TAGGED"] = tagged ? "yes" : "no",
                ["LATEST"] = latest,
                ["SHA"] = Sha,
                ["GH_TOKEN"] = "not-a-token",
            },
            "-File", harness);

        return (exit, stdout, stderr, File.ReadAllLines(calls));
    }

    private static (int Exit, string Stdout, string Stderr, IReadOnlyList<string> Calls) RunTheTapStep(string token, int status, string? expiration = null, string body = "", string? throws = null)
    {
        var step = Assert.Single(Steps("tap"));

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

    /// <summary>
    /// <c>git</c> and <c>gh</c> for the length of one step, every call recorded as one line. <c>git log</c> answers
    /// the commit, <c>git ls-remote</c> answers by whether the tag exists; <c>gh release view</c> answers the
    /// release's state, or the latest release's tag when it names none.
    /// </summary>
    private const string StandIns = """
        $ErrorActionPreference = 'Stop'
        Set-Location $env:WORK

        function git {
            Add-Content -LiteralPath $env:CALLS -Value ('git ' + ($args -join ' '))
            $global:LASTEXITCODE = 0
            if ($args -contains 'log') { $env:SHA; return }
            if ($args -contains 'ls-remote') { if ($env:TAGGED -ne 'yes') { $global:LASTEXITCODE = 2 }; return }
        }

        function gh {
            Add-Content -LiteralPath $env:CALLS -Value ('gh ' + ($args -join ' '))
            $global:LASTEXITCODE = 0
            if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
                if ($args[2].StartsWith('--')) {
                    if ($env:LATEST) { $env:LATEST } else { [Console]::Error.WriteLine('release not found'); $global:LASTEXITCODE = 1 }
                    return
                }

                switch ($env:RELEASE) {
                    'draft'     { 'true'; return }
                    'published' { 'false'; return }
                    default     { [Console]::Error.WriteLine('release not found'); $global:LASTEXITCODE = 1; return }
                }
            }
        }

        try { & '@STEP@'; if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;
}
