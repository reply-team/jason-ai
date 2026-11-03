using Jason.Cli.Skills;
using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The <c>agent_hosts</c> check: which agent hosts this machine has, and whether every enabled profile names one of them.
/// It never decides readiness — a profile naming a host that is not here fails the work it launches, not the
/// installation — and it reads a profile's host as the word the runtime wrote, so a runtime that knows a host this CLI
/// does not is named rather than thrown on.
/// </summary>
public sealed class AgentHostsCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Claude = "claude_code";

    private static readonly string ClaudeSkills = Path.Combine("home", ".claude", "skills");

    private static HarnessRoot[] Detected => [new([AgentHostKind.ClaudeCode], ClaudeSkills)];

    [Fact]
    public void Every_enabled_profile_naming_a_detected_host_is_ok_and_names_both()
    {
        var check = StatusChecks.AgentHostsCheck(Detected, [new("local-claude", Claude), new("night-run", Claude)], names: true);

        Assert.Equal("agent_hosts", check.Name);
        Assert.False(check.Required);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal($"Detected: claude_code ({ClaudeSkills}). Profiles: local-claude names claude_code; night-run names claude_code.", check.Fact);
        Assert.Null(check.Fix);
    }

    [Fact]
    public void A_detected_host_and_no_profile_is_ok()
    {
        var check = StatusChecks.AgentHostsCheck(Detected, [], names: true);

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal($"Detected: claude_code ({ClaudeSkills}). No enabled profile names a host.", check.Fact);
    }

    /// <summary>
    /// A profile whose host is not on this machine is named with the host. With no host here there is no command to give:
    /// installing a host is the person's, and the repair that moves a profile names a host that is here.
    /// </summary>
    [Fact]
    public void A_profile_naming_a_host_not_detected_fails_naming_it_with_no_repair_where_no_host_is_here()
    {
        var hosts = new HarnessRoot[] { new([], Path.Combine("somewhere", "else")) };
        var noClaude = StatusChecks.AgentHostsCheck(hosts, [new("night-run", Claude)], names: true);

        Assert.Equal(CheckState.Failed, noClaude.State);
        Assert.Equal("No agent host was detected on this machine. Profiles: night-run names claude_code, which was not detected on this machine.", noClaude.Fact);
        Assert.Null(noClaude.Fix);
    }

    /// <summary>
    /// A host word the runtime wrote and this build of the CLI does not know — a newer runtime's — is named as given, and
    /// fails the check as a host that is not here does: nothing this CLI detects can stand for it.
    /// </summary>
    [Fact]
    public void A_host_word_this_cli_does_not_know_is_named_as_given_and_fails()
    {
        var check = StatusChecks.AgentHostsCheck(Detected, [new("local-claude", Claude), new("later", "future_host")], names: true);

        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal($"Detected: claude_code ({ClaudeSkills}). Profiles: local-claude names claude_code; later names future_host, a host this build of the CLI does not know.", check.Fact);
        Assert.Equal("jason profile update later --host claude_code --program claude", check.Fix);
    }

    [Fact]
    public void No_host_detected_and_no_profile_is_absent()
    {
        var check = StatusChecks.AgentHostsCheck([], [], names: true);

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal("No agent host was detected on this machine. No enabled profile names a host.", check.Fact);
        Assert.Null(check.Fix);
    }

    /// <summary>Profiles the runtime did not answer for are unknown, never none: the hosts are still said.</summary>
    [Fact]
    public void Profiles_that_could_not_be_read_are_unknown_and_the_hosts_are_still_said()
    {
        var check = StatusChecks.AgentHostsCheck(Detected, profiles: null, names: true);

        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal($"Detected: claude_code ({ClaudeSkills}). The profiles could not be read: the runtime did not answer.", check.Fact);
    }

    [Fact]
    public void An_environment_that_detects_no_hosts_is_unknown()
    {
        var check = StatusChecks.AgentHostsCheck(roots: null, [], names: true);

        Assert.Equal(CheckState.Unknown, check.State);
        Assert.StartsWith("This environment does not detect agent hosts.", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A profile's name is the person's. Without names the check counts the profiles by host, in the table's order and
    /// then the words this CLI does not know, and its repair names no profile: the doctor's bundle carries it so.
    /// </summary>
    [Fact]
    public void Without_names_the_profiles_are_counted_by_host_and_no_name_is_written()
    {
        var check = StatusChecks.AgentHostsCheck(
            Detected,
            [new("sentinel-later", "future_host"), new("sentinel-one", Claude), new("sentinel-two", Claude)],
            names: false);

        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal($"Detected: claude_code ({ClaudeSkills}). Profiles by host: claude_code 2; future_host 1, a host this build of the CLI does not know.", check.Fact);
        Assert.Equal("jason profile update <name> --host claude_code --program claude", check.Fix);
        Assert.DoesNotContain("sentinel", check.Fact + check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_names_a_host_not_detected_is_said_beside_its_count()
    {
        var check = StatusChecks.AgentHostsCheck([], [new("sentinel-one", Claude)], names: false);

        Assert.Equal("No agent host was detected on this machine. Profiles by host: claude_code 1, not detected.", check.Fact);
    }

    /// <summary>
    /// Through the verb: every page of the enabled profiles is read, with the host as the runtime wrote it, and the check
    /// stands right after the skills deployed to the agents' folders.
    /// </summary>
    [Fact]
    public async Task Status_reads_every_page_of_the_enabled_profiles_and_says_the_check_after_the_harness_skills()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Profiles(dir, ("local-claude", Claude), ("later", "future_host"));

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var checks = StatusCommandTests.Read(output).Checks;
        var at = checks.ToList().FindIndex(check => check.Name == "agent_hosts");
        Assert.Equal("harness_skills", checks[at - 1].Name);
        Assert.Equal(CheckState.Failed, checks[at].State);
        Assert.Equal("No agent host was detected on this machine. Profiles: local-claude names claude_code, which was not detected on this machine; later names future_host, a host this build of the CLI does not know.", checks[at].Fact);
        Assert.All(StatusCommandTests.ProfileReads(dir), body => Assert.DoesNotContain("include_disabled\":true", body, StringComparison.Ordinal));
        Assert.Equal(2, StatusCommandTests.ProfileReads(dir).Count);
    }

    [Fact]
    public async Task Status_with_no_runtime_says_the_profiles_are_unknown()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "agent_hosts");
        Assert.Equal(CheckState.Unknown, check.State);
    }

    /// <summary>A host the runtime wrote that is no word at all is not repeated: the answer is not one this check can read.</summary>
    [Fact]
    public async Task A_host_that_is_no_word_makes_the_profiles_unreadable()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Profiles(dir, ("local-claude", "Not a word, an address@example.com"));

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "agent_hosts");
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.DoesNotContain("example.com", check.Fact, StringComparison.Ordinal);
        Assert.EndsWith("The profiles could not be read: a profile names its host by something that is not a host word.", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A runtime that answered with more pages than the check reads did answer: the fact says what happened, never that it
    /// was silent.
    /// </summary>
    [Fact]
    public async Task More_pages_than_the_check_reads_are_said_as_such_and_not_as_silence()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Profiles(dir, [.. Enumerable.Range(0, 101).Select(index => ($"p{index}", Claude))]);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "agent_hosts");
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.EndsWith("The profiles could not be read: the runtime answered with more than 100 pages of them, more than this check reads.", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>Why the profiles were not read is the check's to say in its own words, whatever it was.</summary>
    [Fact]
    public void Profiles_that_could_not_be_read_say_why_where_the_runtime_answered()
    {
        var check = StatusChecks.AgentHostsCheck(Detected, profiles: null, names: true, unread: "the runtime refused to list them");

        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal($"Detected: claude_code ({ClaudeSkills}). The profiles could not be read: the runtime refused to list them.", check.Fact);
    }

    /// <summary>The same rule over the CLI's own table: a profile naming any host this build knows reads as that host.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_host_of_the_table_is_ok_where_it_is_detected(AgentHostKind kind)
    {
        var word = AgentHosts.Word(kind);

        var check = StatusChecks.AgentHostsCheck([new([kind], "skills")], [new("p", word)], names: true);

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal($"Detected: {word} (skills). Profiles: p names {word}.", check.Fact);
    }

    /// <summary>
    /// The skills deployed to the agents' folders keep their own check, and say beside each folder the hosts that read it;
    /// a folder named with <c>--root</c> and no host says none.
    /// </summary>
    [Fact]
    public async Task The_harness_skills_fact_names_the_hosts_that_read_each_folder()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        StatusCommandTests.Deploy(harness);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, harnesses: HarnessLocators.At(harness, AgentHostKind.ClaudeCode)), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Contains($"in {harness} (claude_code)", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where a path is case-sensitive, two folders whose names differ only in case are two roots, each read by its own
    /// hosts, and one named with no host says none: the check reads them by the comparer detection reads them by, so it
    /// neither folds them into one nor lends one folder's hosts to the other.
    /// </summary>
    [Fact]
    public async Task The_harness_skills_fact_keeps_two_folders_whose_names_differ_only_in_case_apart_where_paths_are_case_sensitive()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only a case-sensitive file system holds two folders whose names differ only in case.");
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var lower = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "skills")).FullName;
        var upper = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "Skills")).FullName;
        StatusCommandTests.Deploy(lower);
        StatusCommandTests.Deploy(upper);
        var locator = new Roots([new([AgentHostKind.ClaudeCode], lower), new([], upper)]);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, harnesses: locator), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Contains($"in {lower} (claude_code)", check.Fact, StringComparison.Ordinal);
        Assert.Contains($"in {upper}", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain($"in {upper} (", check.Fact, StringComparison.Ordinal);
    }

    private sealed class Roots(IReadOnlyList<HarnessRoot> roots) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => roots;
    }

    public static TheoryData<AgentHostKind> Kinds() => [.. Enum.GetValues<AgentHostKind>()];
}
