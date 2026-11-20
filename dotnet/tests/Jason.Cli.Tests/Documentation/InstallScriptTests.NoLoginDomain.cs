using System.Text.RegularExpressions;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The macOS proof's reading of launchd with no login session: what launchd answers an account that has no login domain,
/// asked about Jason Desktop's ShipIt job in that domain and about the domain itself, the two questions
/// <c>jason uninstall</c> asks there. It is kept for the record, the way the Windows proof reads ModulePath, and is never
/// a failure: no code in jason assumes what launchd answers, and the release's dry run records it.
/// </summary>
public partial class InstallScriptTests
{
    /// <summary>The words each line the reading says begins with.</summary>
    private const string NoLoginDomainSays = "launchd with no login session, a reading: ";

    /// <summary>
    /// The reading is made once in a job, as the first act of its first uninstall from a terminal, a step CI's union job
    /// and the release's proof job both run, and only after that step's own argument is checked. The account it asks as is
    /// daemon, which no login session has, and each question is asked of that account's own login domain, through
    /// <c>sudo -n</c>, with launchctl by its full path, bounded by <c>within</c> inside a subshell whose exit is kept as the
    /// answer's, so a refusal, an answer or a hang ends nothing; the function holds no failure and no exit of its own.
    /// </summary>
    [Fact]
    public void The_macOS_proof_reads_launchd_with_no_login_session_once_in_a_job_at_its_first_uninstall_from_a_terminal()
    {
        var root = RepositoryRoot();
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);

        var reading = MacProofFunction(macos, "read_no_login_domain");
        Assert.StartsWith("read_no_login_domain() {\n    [ ! -e \"$OUT/no-login-domain.txt\" ] || return 0\n", reading, StringComparison.Ordinal);
        Assert.Contains("\n    uid=\"$(id -u daemon 2>/dev/null)\" || uid=\"\"\n", reading, StringComparison.Ordinal);
        Assert.Contains("\n    for asked in \"job gui/$uid/ai.jasonharness.desktop.ShipIt\" \"domain gui/$uid\"; do\n", reading, StringComparison.Ordinal);
        Assert.Contains("\n        ( within 30 \"launchctl print $target as daemon\" sudo -n -u daemon /bin/launchctl print \"$target\" ) > \"$answer\" 2>&1 || status=$?\n", reading, StringComparison.Ordinal);
        Assert.EndsWith("\n    return 0\n}\n", reading, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(?:fail|fail_held|exit)\b", reading);

        var step = MacProofStep(macos, "uninstall-terminal");
        Assert.Contains("\n    case \"$by\" in ''|account-shell) ;; *) fail \"uninstall-terminal takes account-shell or nothing, not '$by'\" ;; esac\n    read_no_login_domain\n    placed=\"$(app)\"; carried=", step, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(macos, @"(?m)^\s*read_no_login_domain$"));
        foreach (var workflow in new[] { "ci.yml", "release.yml" })
        {
            Assert.Contains("bash .github/scripts/test-installation-macos.sh uninstall-terminal\n", File.ReadAllText(Path.Combine(root, ".github", "workflows", workflow)).Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The reading, run as the proof runs it, under the proof's own shell options: each answer's exit and first line are
    /// said, in the step's lines and its summary, and kept in the job's folder; a second call in the same job asks nothing;
    /// sudo refusing, an answer that says nothing, and no account daemon at all each end nothing, the last asking nothing.
    /// <c>sudo</c> and <c>id</c> are stand-ins here, so the row asks no launchd and runs nothing as another account; their
    /// codes are their own, and none stands for one of launchd's. It runs in bash: /bin/bash on Linux and macOS, Git Bash on
    /// a Windows that has it.
    /// </summary>
    [Fact]
    public void The_macOS_proof_s_reading_of_launchd_with_no_login_session_says_each_answer_and_ends_nothing()
    {
        var bash = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "/bin/bash";
        Assert.SkipUnless(File.Exists(bash), $"This machine has no {bash} to run the macOS proof's functions in.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        using var tree = new TempTree();
        string Quoted(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        var summary = Path.Combine(tree.Root, "summary.md");
        var first = tree.NewDirectory("first");
        var harness = Path.Combine(tree.Root, "harness.sh");
        File.WriteAllText(harness, NoLoginDomainHarness
            .Replace("@SETTINGS@", MacProofSettings(script), StringComparison.Ordinal)
            .Replace("@SUMMARY@", Quoted(summary), StringComparison.Ordinal)
            .Replace("@ASKED@", Quoted(Path.Combine(tree.Root, "asked.txt")), StringComparison.Ordinal)
            .Replace("@FIRST@", Quoted(first), StringComparison.Ordinal)
            .Replace("@REFUSED@", Quoted(tree.NewDirectory("refused")), StringComparison.Ordinal)
            .Replace("@SILENT@", Quoted(tree.NewDirectory("silent")), StringComparison.Ordinal)
            .Replace("@NO_DAEMON@", Quoted(tree.NewDirectory("no-daemon")), StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", MacProofFunction(script, "say") + MacProofFunction(script, "within") + MacProofFunction(script, "read_no_login_domain"), StringComparison.Ordinal));

        var (exit, stdout, stderr) = Run(bash, new Dictionary<string, string>(), "--noprofile", "--norc", harness.Replace('\\', '/'));
        var output = "\n" + stdout + stderr;
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        Assert.True(SaidLine(stdout, "end") == "reached", $"the reading ended the step:{output}");

        string Answer(string target, int code, string line) =>
            $"{NoLoginDomainSays}launchctl print {target}, asked as daemon (uid 1, which no login session has), ended {code}; its first line: {line}";
        const string Job = "gui/1/ai.jasonharness.desktop.ShipIt";
        string[] said =
        [
            Answer(Job, 7, "the stand-in, of the job"),
            Answer("gui/1", 9, "the stand-in, of the domain"),
            Answer(Job, 1, "sudo: a password is required"),
            Answer("gui/1", 1, "sudo: a password is required"),
            Answer(Job, 0, "none"),
            Answer("gui/1", 0, "none"),
            $"{NoLoginDomainSays}no account daemon with a user id was found here, so nothing was asked",
        ];
        var lines = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.StartsWith(NoLoginDomainSays, StringComparison.Ordinal)).ToList();
        Assert.True(lines.SequenceEqual(said), $"the reading did not say each answer's exit and first line, once:{output}");
        Assert.Equal(said.Select(line => "- " + line), File.ReadAllLines(summary));
        Assert.True(SaidLine(stdout, "asked once") == $"-n -u daemon /bin/launchctl print {Job}|-n -u daemon /bin/launchctl print gui/1|", $"the first call did not ask the job then the domain as daemon, or a second call in the same job asked again:{output}");
        Assert.True(SaidLine(stdout, "asked in all") == "6", $"the reading asked other than the job and the domain for each answered case, and nothing with no account daemon:{output}");
        Assert.Equal([$"{Job}\t7\tthe stand-in, of the job", "gui/1\t9\tthe stand-in, of the domain"], File.ReadAllLines(Path.Combine(first, "no-login-domain.txt")));
    }

    /// <summary>
    /// The bash the reading's row runs: the proof's own shell options, a summary and a job's folder of the row's own for
    /// each case, stand-ins for <c>id</c>, which knows daemon as the user id 1 until a case says there is none, and for
    /// <c>sudo</c>, which records what it was asked and answers as the case says, and the proof's own <c>say</c>,
    /// <c>within</c> and <c>read_no_login_domain</c>. One line per finding, its label and what was seen, apart by a colon.
    /// </summary>
    private const string NoLoginDomainHarness = """
        @SETTINGS@
        GITHUB_STEP_SUMMARY=@SUMMARY@
        ASKED=@ASKED@
        : > "$ASKED"
        jason_processes() { :; }
        DAEMON=1
        id() { [ "$*" = '-u daemon' ] || return 2; [ "$DAEMON" = 1 ] || return 1; echo 1; }
        sudo() {
            printf '%s\n' "$*" >> "$ASKED"
            case "$*" in
                '-n -u daemon /bin/launchctl print gui/1/ai.jasonharness.desktop.ShipIt') [ -z "$JOB_SAID" ] || printf '%s\n' "$JOB_SAID" >&2; return "$JOB_EXIT" ;;
                '-n -u daemon /bin/launchctl print gui/1') [ -z "$DOMAIN_SAID" ] || printf '%s\n' "$DOMAIN_SAID" >&2; return "$DOMAIN_EXIT" ;;
            esac
            return 99
        }

        @FUNCTIONS@

        OUT=@FIRST@
        JOB_EXIT=7; JOB_SAID='the stand-in, of the job'; DOMAIN_EXIT=9; DOMAIN_SAID='the stand-in, of the domain'
        read_no_login_domain
        read_no_login_domain
        echo "asked once: $(tr '\n' '|' < "$ASKED")"

        OUT=@REFUSED@
        JOB_EXIT=1; JOB_SAID='sudo: a password is required'; DOMAIN_EXIT=1; DOMAIN_SAID='sudo: a password is required'
        read_no_login_domain

        OUT=@SILENT@
        JOB_EXIT=0; JOB_SAID=''; DOMAIN_EXIT=0; DOMAIN_SAID=''
        read_no_login_domain

        OUT=@NO_DAEMON@
        DAEMON=0
        read_no_login_domain
        echo "asked in all: $(grep -c . "$ASKED")"
        echo "end: reached"
        """;
}
