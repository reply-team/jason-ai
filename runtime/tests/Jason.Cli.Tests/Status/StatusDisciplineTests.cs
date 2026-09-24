using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Status;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// What keeps <c>jason status</c> from becoming <c>jason doctor</c>.
/// </summary>
/// <remarks>
/// <para>
/// A check may report a fact and the command that repairs it. It may not read a log, and it may not reach the
/// machine except through the one declared runner and with the one published command. Diagnostics — reading
/// logs, explaining failures, probing — is a story of its own, and these three facts are the whole of what
/// stands between the two.
/// </para>
/// <para>
/// The rule is stated the way the code can keep it. "No check runs a probe of its own" would already have been
/// false the day it was written, because one check runs a provider CLI; a rule a reader can see is broken
/// teaches them the rule is decorative. What is true, and worth holding, is that the only program any check
/// runs is the one the caller named, with one argument, through the one declared runner.
/// </para>
/// </remarks>
public partial class StatusDisciplineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void No_check_reads_a_log_or_reaches_the_machine_behind_the_runner()
    {
        var sources = Directory.EnumerateFiles(StatusSources(), "*.cs").ToList();
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            var name = Path.GetFileName(source);

            Assert.True(
                !text.Contains("LogsDirectory", StringComparison.Ordinal),
                $"'{name}' reads the log directory. A check reports a fact and the command that repairs it; "
                + "reading a log to explain a failure is what a diagnostics verb would be for.");

            Assert.True(
                !text.Contains("System.Diagnostics", StringComparison.Ordinal),
                $"'{name}' reaches the process table directly. The one way a check may start a program is "
                + $"{nameof(IProgramRunner)}, which a test can substitute and this guard can read.");
        }
    }

    /// <summary>
    /// And exactly one program is ever asked for — the one the caller named — with exactly one argument. The
    /// scan above cannot see this: a second probe added behind the same runner would pass it silently, which
    /// is precisely how a status verb grows into a diagnostics one.
    /// </summary>
    [Fact]
    public async Task The_only_program_any_check_runs_is_the_one_the_documentation_publishes()
    {
        using var dir = new TempPaths();
        var runner = new FakeProgramRunner(_ => new ProgramResult(1, string.Empty, "not found", false));

        await CliApp.RunAsync(["status", "--provider-cli", "acme-sdr"], Machine(dir, runner), Ct);

        var asked = Assert.Single(runner.Requested);
        Assert.True(
            asked is { Program: "acme-sdr", Arguments: ["--version"] },
            $"A check ran '{asked.Program} {string.Join(' ', asked.Arguments)}'. The only program this verb may "
            + "run is the one the caller named, with --version; anything else is a probe, and a probe is a "
            + "diagnostics verb.");
    }

    /// <summary>
    /// Every repair a check prints that is a <c>jason</c> command is one this program understands. A status
    /// verb whose advice is a usage error teaches the operator the tool is broken at the moment they most
    /// need it not to be.
    /// </summary>
    /// <remarks>
    /// This is one of the two kinds of repair, not all of them, and it says so now. It used to claim all of
    /// them while reading only the constants — which is how the PATH repair, composed in a method the reader
    /// below did not reach, shipped unread and green.
    /// </remarks>
    [Fact]
    public async Task Every_repair_a_check_prints_is_a_command_this_program_understands()
    {
        // Every jason repair this verb can print, read off the source rather than off one run of it. One
        // shape of machine prints three of them; the guard claimed it reached every one, which is how a
        // repair that is itself a usage error shipped green.
        var repairs = Printed();
        Assert.True(repairs.Count >= 5, $"Only {repairs.Count} repairs were found in the source, which is fewer than this verb prints.");

        foreach (var repair in repairs)
        {
            using var typing = new TempPaths();
            var error = new StringWriter();
            var exit = await CliApp.RunAsync(
                [.. repair.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)],
                new CliEnvironment(new StringWriter(), error, typing.Paths, Autostart: new RecordingRegistrar()),
                Ct);

            Assert.True(
                exit != ExitCodes.Usage,
                $"A check prints '{repair}' as the way to repair it, and this program does not understand it:"
                + $"{Environment.NewLine}{error}");
        }
    }

    /// <summary>
    /// And the repair that is not a <c>jason</c> command is a line this machine's own shell can run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Putting this executable on the PATH is the shell's act and no verb of this product's performs it, so
    /// this one cannot be typed the way the others are. It is held to what can be held about it: it is not
    /// empty, it names a directory that is really there, and it is written for the platform it was composed
    /// on. Growing the guard a second category is the honest move; the alternative was inventing a verb so
    /// that the first category could stay the only one.
    /// </para>
    /// <para>
    /// And it outlives the shell it is typed in. The Unix half was the bare <c>export</c> line — which
    /// <c>install.sh</c> appends to a login profile, and which typed at a prompt lasts exactly one shell and
    /// leaves the next <c>jason status</c> saying precisely what the last one said. The Windows half writes
    /// the registry and persists, so the two platforms' repairs differed in kind and neither said so.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_repair_that_is_not_a_jason_command_is_a_line_for_this_machines_own_shell()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var runner = new FakeProgramRunner(_ => new ProgramResult(1, string.Empty, "not found", false));

        await CliApp.RunAsync(["status"], Machine(dir, runner, output), Ct);

        var report = JsonSerializer.Deserialize<StatusReport>(output.ToString(), JasonJson.Options);
        Assert.NotNull(report);

        // One, and the guard says one out loud. A second repair of this kind is a decision somebody should
        // have to take deliberately rather than discover.
        var repair = Assert.Single(
            report.Checks
                .Where(check => check.Fix is not null && !check.Fix.StartsWith("jason ", StringComparison.Ordinal))
                .Select(check => check.Fix!)
                .Distinct(StringComparer.Ordinal));

        Assert.NotEmpty(repair.Trim());

        var directory = Path.GetDirectoryName(Installation(dir))!;
        Assert.True(Directory.Exists(directory), $"The repair names '{directory}', which is not a directory on this machine.");
        Assert.Contains(directory, repair, StringComparison.Ordinal);

        if (OperatingSystem.IsWindows())
        {
            // The account's own Path, read unexpanded and written back with its kind; never through the
            // [Environment] pair that turned an account's REG_EXPAND_SZ Path into fixed strings.
            Assert.Contains("CurrentUser.CreateSubKey('Environment')", repair, StringComparison.Ordinal);
            Assert.Contains("'DoNotExpandEnvironmentNames'", repair, StringComparison.Ordinal);
            Assert.DoesNotContain("SetEnvironmentVariable('Path'", repair, StringComparison.Ordinal);
            Assert.DoesNotContain("export PATH=", repair, StringComparison.Ordinal);
        }
        else
        {
            var profile = PathEntry.LoginProfile(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetEnvironmentVariable("SHELL"));

            Assert.Contains(profile, repair, StringComparison.Ordinal);
            Assert.Contains(PathEntry.ExportLine(directory), repair, StringComparison.Ordinal);
            Assert.DoesNotContain("[Environment]::", repair, StringComparison.Ordinal);
        }

        // And it names the installation rather than the process that happens to be answering. A repair that
        // pointed at the running image's directory on every machine would satisfy every rule above and repair
        // nothing on the one machine that needed it.
        var installed = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "elsewhere", "bin")).FullName;
        var moved = new StringWriter();
        await CliApp.RunAsync(
            ["status"],
            Machine(dir, runner, moved) with { InstallPath = Path.Combine(installed, OperatingSystem.IsWindows() ? "jason.exe" : "jason") },
            Ct);

        var elsewhere = JsonSerializer.Deserialize<StatusReport>(moved.ToString(), JasonJson.Options);
        Assert.NotNull(elsewhere);

        var there = elsewhere.Checks.Single(check => check.Name == "path").Fix;
        Assert.NotNull(there);
        Assert.Contains(installed, there, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every repair any check can print is composed in the one class that names repairs — which is what makes
    /// the two guards above exhaustive rather than merely true of whatever they happened to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This wave re-created the condition those guards exist to prevent. The PATH repair arrived as a method
    /// further down the checks' own file, outside the class the reader below slices, so the repair this verb
    /// had just gained was one no guard could see — and the count it asserts is a floor, so nothing went red.
    /// Three checks were also passing their line as a literal that happened to duplicate a constant beside
    /// it: the same hole, with the same absence of consequence.
    /// </para>
    /// <para>
    /// So this reads every <c>new StatusCheck</c> in the checks' own sources and takes the last argument of
    /// each. A repair is <c>null</c> or it comes from <c>Repair</c>, and there is no third spelling.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_repair_a_check_can_print_is_composed_in_the_class_that_names_them()
    {
        var sites = 0;
        foreach (var file in Directory.EnumerateFiles(StatusSources(), "*.cs"))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            if (!source.Contains("new StatusCheck(", StringComparison.Ordinal))
            {
                continue;
            }

            // The reader below understands ordinary and interpolated string literals, character literals and
            // comments, and nothing else. Saying so out loud means that the day somebody writes a raw or
            // verbatim literal in a file that builds checks this guard goes red, rather than mis-reading the
            // file and reporting nothing — which is this guard's own failure mode, pointed at itself.
            Assert.True(
                !source.Contains("\"\"\"", StringComparison.Ordinal) && !source.Contains("@\"", StringComparison.Ordinal),
                $"'{name}' builds checks and carries a raw or verbatim string literal, which the reader in "
                + "this guard does not understand. Teach it that shape before writing one here, or this guard "
                + "is reading the file wrongly and reporting nothing.");

            foreach (var (line, fix) in Constructed(source))
            {
                sites++;
                Assert.True(
                    fix is "null" || fix.StartsWith("Repair.", StringComparison.Ordinal),
                    $"{name}:{line} gives a check the repair `{fix}`. Every repair is composed in the Repair "
                    + "class, because that class is what the guards above read; a repair spelled anywhere "
                    + "else is one nothing types and nothing checks.");
            }
        }

        // A floor against the failure this repository has met before: a reader that matches nothing reports
        // no failures, and that is not evidence of absence. This verb composes eleven checks, so it builds at
        // least eleven of these.
        Assert.True(sites >= 11, $"Only {sites} checks were read out of the sources, which is fewer than this verb composes.");
    }

    /// <summary>
    /// Every <c>jason</c> repair string this verb can print, taken from the one class that names them. A run
    /// only ever prints the repairs its own machine's state calls for.
    /// </summary>
    private static IReadOnlyList<string> Printed()
    {
        var source = File.ReadAllText(Path.Combine(StatusSources(), "StatusChecks.cs"));
        var opens = source.IndexOf("private static class Repair", StringComparison.Ordinal);
        Assert.True(opens > 0, "The class that names every repair is not there any more, so this guard cannot read them.");

        var body = source[opens..];
        var closes = body.IndexOf("\n    }", StringComparison.Ordinal);
        Assert.True(closes > 0, "The class that names every repair has no end, so this guard cannot read it.");

        return [.. Repair().Matches(body[..closes])
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];
    }

    [System.Text.RegularExpressions.GeneratedRegex("\"(jason [^\"]*)\"")]
    private static partial System.Text.RegularExpressions.Regex Repair();

    /// <summary>Every <c>new StatusCheck(…)</c> in a file, as its line number and its last argument.</summary>
    private static IEnumerable<(int Line, string Fix)> Constructed(string source)
    {
        const string call = "new StatusCheck(";

        var at = source.IndexOf(call, StringComparison.Ordinal);
        while (at >= 0)
        {
            var open = at + call.Length;
            var (close, commas) = Arguments(source, open);

            Assert.True(close > open, $"A '{call}' at offset {at} never closes.");
            Assert.NotEmpty(commas);

            yield return (LineAt(source, at), source[(commas[^1] + 1)..close].Trim());
            at = source.IndexOf(call, close, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Walks an argument list from just after its opening parenthesis, and reports where it closes and where
    /// its top-level commas are.
    /// </summary>
    /// <remarks>
    /// Strings, interpolation holes, character literals and comments are stepped over whole, because every
    /// one of them can hold a comma or a parenthesis of its own. One message in these sources carries
    /// <c>string.Join(", ", …)</c> inside an interpolation, and a reader that did not know that would split
    /// the call in the wrong place and then check the wrong text — a guard reporting on something other than
    /// what it names.
    /// </remarks>
    private static (int Close, IReadOnlyList<int> Commas) Arguments(string source, int start)
    {
        var commas = new List<int>();
        var depth = 0;
        var at = start;

        while (at < source.Length)
        {
            var c = source[at];
            if (c == '"')
            {
                at = EndOfString(source, at);
            }
            else if (c == '\'')
            {
                at = EndOfChar(source, at);
            }
            else if (c == '/' && at + 1 < source.Length && source[at + 1] == '/')
            {
                var end = source.IndexOf('\n', at);
                at = end < 0 ? source.Length : end;
            }
            else if (c == '/' && at + 1 < source.Length && source[at + 1] == '*')
            {
                var end = source.IndexOf("*/", at, StringComparison.Ordinal);
                at = end < 0 ? source.Length : end + 2;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
                at++;
            }
            else if (c is ']' or '}')
            {
                depth--;
                at++;
            }
            else if (c == ')' && depth == 0)
            {
                return (at, commas);
            }
            else
            {
                if (c == ')')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    commas.Add(at);
                }

                at++;
            }
        }

        return (-1, commas);
    }

    /// <summary>Past the closing quote of the string literal that starts here, interpolation holes and all.</summary>
    private static int EndOfString(string source, int quote)
    {
        var interpolated = quote > 0 && source[quote - 1] == '$';
        var at = quote + 1;

        while (at < source.Length)
        {
            var c = source[at];
            if (c == '\\')
            {
                at += 2;
            }
            else if (interpolated && c == '{')
            {
                at = at + 1 < source.Length && source[at + 1] == '{' ? at + 2 : EndOfHole(source, at);
            }
            else if (c == '"')
            {
                return at + 1;
            }
            else
            {
                at++;
            }
        }

        return source.Length;
    }

    /// <summary>Past the closing brace of the interpolation hole that starts here.</summary>
    private static int EndOfHole(string source, int brace)
    {
        var depth = 0;
        var at = brace;

        while (at < source.Length)
        {
            var c = source[at];
            if (c == '"')
            {
                at = EndOfString(source, at);
            }
            else if (c == '\'')
            {
                at = EndOfChar(source, at);
            }
            else if (c == '{')
            {
                depth++;
                at++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return at + 1;
                }

                at++;
            }
            else
            {
                at++;
            }
        }

        return source.Length;
    }

    /// <summary>Past the closing quote of the character literal that starts here.</summary>
    private static int EndOfChar(string source, int quote)
    {
        var at = quote + 1;
        if (at < source.Length && source[at] == '\\')
        {
            at++;
        }

        return Math.Min(at + 2, source.Length);
    }

    private static int LineAt(string source, int index)
    {
        var line = 1;
        for (var at = 0; at < index; at++)
        {
            if (source[at] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <remarks>
    /// With an installation named, because this suite is not one: only a published single file is, and the test
    /// host is one file of many. Without a file to name, the check has no directory to print a repair for.
    /// </remarks>
    private static CliEnvironment Machine(TempPaths dir, IProgramRunner runner, StringWriter? output = null) =>
        new(
            output ?? new StringWriter(),
            new StringWriter(),
            dir.Paths,
            Processes: new FakeProcessControl(),
            InstallPath: Installation(dir),
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "no-harness-here")),
            Programs: runner,
            SearchPath: Path.Combine(dir.Paths.Root, "nowhere"));

    /// <summary>An installed executable of this test's own, in a directory that is really there.</summary>
    private static string Installation(TempPaths dir) =>
        Path.Combine(Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "install")).FullName, OperatingSystem.IsWindows() ? "jason.exe" : "jason");

    /// <summary>The checks' own directory, found from the repository root the way every guard here does.</summary>
    private static string StatusSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "runtime", "src", "Jason.Cli", "Status");
    }
}
