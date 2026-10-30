using System.Diagnostics;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// Reading the repository's workflows and running the PowerShell they hold: the parsed document, one of its jobs as
/// text, and <c>pwsh</c> with a given environment. Shared by every test of the release pipeline and of the public
/// repository's own workflows.
/// </summary>
internal static class Workflows
{
    internal static (int Exit, string Stdout, string Stderr) Pwsh(IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it, and the release is only ever built there.");

        var start = new ProcessStartInfo(pwsh)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // PowerShell reports telemetry over the network unless told not to, and no test in this repository
        // reaches the network.
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("pwsh did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// git in <paramref name="directory"/>, its output returned and its failure a failed test. The machine's own settings
    /// are kept out — no system configuration, and the line endings, the signing and the identity set here — so a test
    /// repository holds exactly the bytes a test wrote, whatever this machine's git would have done to them. Automatic
    /// maintenance is off as well: a newer git starts it in the background after a commit, and it writes into
    /// <c>.git</c> while a test is reading what is there.
    /// </summary>
    internal static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory,
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var setting in (string[])["core.autocrlf=false", "commit.gpgsign=false", "tag.gpgsign=false", "init.defaultBranch=main", "user.name=Jason Tests", "user.email=tests@example.invalid", "maintenance.auto=false", "gc.auto=0"])
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(setting);
        }

        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var git = Process.Start(start)!;
        var stdout = git.StandardOutput.ReadToEndAsync();
        var stderr = git.StandardError.ReadToEndAsync();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr.Result}");
        return stdout.Result;
    }

    /// <summary>
    /// A script run as a workflow step runs it, with its arguments, and a failure reported in its own words. PowerShell's
    /// error view wraps a message at the console's width, and a runner without a terminal splits a long one across
    /// lines, so what a test reads on stderr is the exception's message, printed whole by <see cref="PlainErrors"/>.
    /// </summary>
    internal static (int Exit, string Stdout, string Stderr) Script(IReadOnlyDictionary<string, string> environment, string script, params string[] arguments)
    {
        var harness = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N") + ".ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(harness)!);
        File.WriteAllText(harness, PlainErrors.Replace("@SCRIPT@", script.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal));
        try
        {
            return Pwsh(environment, ["-File", harness, .. arguments]);
        }
        finally
        {
            File.Delete(harness);
        }
    }

    /// <summary>
    /// The PowerShell that runs one script or one workflow step, <c>@SCRIPT@</c>, passing on its own arguments, and ends
    /// as GitHub's <c>shell: pwsh</c> wrapper ends: with the exit code the script left — the line after the call is the
    /// one GitHub appends to every step — or, when it threw, with its message whole on stderr and exit code 1. The step
    /// harnesses of the release tests end the same way.
    /// </summary>
    internal const string PlainErrors = """
        $ErrorActionPreference = 'Stop'
        try { & '@SCRIPT@' @args; if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;

    internal static string Read(string[] relative) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. relative]));

    /// <summary>Every step of a workflow that packages through the shared action.</summary>
    internal static List<YamlMappingNode> PackagingCalls(string[] workflow) =>
        [.. Mapping(Parse(Read(workflow)), "jobs").Children.Values.Cast<YamlMappingNode>()
            .SelectMany(job => Node(job, "steps") is YamlSequenceNode steps ? steps.Children.Cast<YamlMappingNode>() : [])
            .Where(step => Scalar(step, "uses") == "./.github/actions/package")];

    /// <summary>
    /// One job of a workflow, as the text between its name and the next job at the same indentation. Read as
    /// text rather than through the parser because what these guards are about is what the job's script says.
    /// </summary>
    internal static string Job(string workflow, string name)
    {
        var start = workflow.IndexOf($"\n  {name}:", StringComparison.Ordinal);
        Assert.True(start >= 0, $"There is no '{name}' job in this workflow.");

        var next = workflow.IndexOf("\n  ", start + 3, StringComparison.Ordinal);
        while (next >= 0 && workflow.Length > next + 3 && char.IsWhiteSpace(workflow[next + 3]))
        {
            next = workflow.IndexOf("\n  ", next + 3, StringComparison.Ordinal);
        }

        return next < 0 ? workflow[start..] : workflow[start..next];
    }

    internal static YamlMappingNode Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    internal static YamlNode? Node(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child : null;

    internal static string? Scalar(YamlMappingNode node, string key) => (Node(node, key) as YamlScalarNode)?.Value;

    internal static YamlMappingNode Mapping(YamlMappingNode node, string key)
    {
        var child = Node(node, key);
        Assert.True(child is not null, $"There is no '{key}' mapping.");
        return Assert.IsType<YamlMappingNode>(child);
    }

    /// <summary>The repository root, found by walking up from the test binaries to the directory holding <c>global.json</c>.</summary>
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
