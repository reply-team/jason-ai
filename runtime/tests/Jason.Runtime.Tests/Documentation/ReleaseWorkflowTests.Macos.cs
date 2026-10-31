using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The native libraries a macOS release bundles, signed by the team that signs the executable before they are bundled:
/// the project target that copies them out of the bundle and bundles them again from the signed copies, the script that
/// signs them, and the package action's steps that run the two in order.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private static readonly string[] AppProject = ["runtime", "src", "Jason.App", "Jason.App.csproj"];
    private const string SignNatives = "Sign the native libraries the bundle carries";
    private const string BundleAgain = "Bundle the signed native libraries into the executable";

    /// <summary>
    /// The publish copies the bundle's native libraries out when it is asked to, and the second publish bundles them from
    /// the signed copies, everything else from where the publish found it. A library the directory does not hold stops
    /// the second publish, and so does a relative directory, which MSBuild would read against the project. The target is
    /// run as MSBuild runs it, taken out of <c>Jason.App.csproj</c> into a project of its own whose
    /// <c>PrepareForBundle</c> makes the items the SDK's would: two native libraries, one of them under a directory,
    /// and an assembly.
    /// </summary>
    [Theory]
    [InlineData("neither")]
    [InlineData("copied out")]
    [InlineData("bundled from")]
    [InlineData("one missing")]
    [InlineData("a relative directory to copy to")]
    [InlineData("a relative directory to bundle from")]
    public void The_project_bundles_each_native_library_from_the_signed_copies_and_refuses_one_it_lacks(string asked)
    {
        using var tree = new TempTree();
        var found = Path.Combine(tree.Root, "found");
        var copies = Path.Combine(tree.Root, "copies");
        string[] natives = ["libexample.dylib", "sub/libother.dylib"];
        foreach (var file in (string[])[.. natives, "Example.dll"])
        {
            Write(found, file, "found " + file);
        }

        if (asked is "bundled from" or "one missing")
        {
            foreach (var native in asked == "one missing" ? natives[..1] : natives)
            {
                Write(copies, native, "signed " + native);
            }
        }

        var listed = Path.Combine(tree.Root, "listed.txt");
        var project = Path.Combine(tree.Root, "bundle.proj");
        var target = XDocument.Load(Path.Combine([RepositoryRoot(), .. AppProject])).Root!
            .Elements("Target").Single(element => (string?)element.Attribute("Name") == "ExchangeBundledNativeLibraries");
        new XDocument(new XElement(
            "Project",
            new XElement("PropertyGroup", new XElement("PublishSingleFile", "true")),
            XElement.Parse("""
                <Target Name="PrepareForBundle">
                  <ItemGroup>
                    <FilesToBundle Include="$(Found)libexample.dylib" RelativePath="libexample.dylib" AssetType="native" />
                    <FilesToBundle Include="$(Found)sub/libother.dylib" RelativePath="sub/libother.dylib" AssetType="native" />
                    <FilesToBundle Include="$(Found)Example.dll" RelativePath="Example.dll" AssetType="runtime" />
                  </ItemGroup>
                </Target>
                """),
            target,
            XElement.Parse("""
                <Target Name="ListBundle" DependsOnTargets="PrepareForBundle">
                  <WriteLinesToFile File="$(Listed)" Lines="@(FilesToBundle->'%(RelativePath)|%(AssetType)|%(FullPath)')" Overwrite="true" />
                </Target>
                """))).Save(project);

        var properties = new List<string> { $"-p:Found={found}{Path.DirectorySeparatorChar}", $"-p:Listed={listed}" };
        properties.AddRange(asked switch
        {
            "copied out" => [$"-p:CopyBundledNativeLibrariesTo={copies}"],
            "bundled from" or "one missing" => [$"-p:BundleNativeLibrariesFrom={copies}"],
            "a relative directory to copy to" => ["-p:CopyBundledNativeLibrariesTo=relative/copies"],
            "a relative directory to bundle from" => ["-p:BundleNativeLibrariesFrom=relative/copies"],
            _ => [],
        });

        var (exit, output) = Msbuild(project, "ListBundle", properties);

        var refusal = asked switch
        {
            "one missing" => $"The bundle carries sub/libother.dylib, and {copies} does not hold it.",
            "a relative directory to copy to" => "CopyBundledNativeLibrariesTo is 'relative/copies', which is not an absolute path.",
            "a relative directory to bundle from" => "BundleNativeLibrariesFrom is 'relative/copies', which is not an absolute path.",
            _ => null,
        };
        if (refusal is not null)
        {
            Assert.True(exit != 0, output);
            Assert.Contains(refusal, output, StringComparison.Ordinal);
            Assert.False(File.Exists(listed), "The publish went on to bundle what the directory does not hold.");
            return;
        }

        Assert.True(exit == 0, output);
        string Entry(string root, string file, string asset) => $"{file}|{asset}|{Path.GetFullPath(Path.Combine(root, file))}";
        var from = asked == "bundled from" ? copies : found;
        Assert.Equal(
            natives.Select(native => Entry(from, native, "native")).Append(Entry(found, "Example.dll", "runtime")).Order(StringComparer.Ordinal),
            File.ReadAllLines(listed).Select(line => line.Split('|') is [var file, var asset, var path] ? $"{file}|{asset}|{Path.GetFullPath(path)}" : line).Order(StringComparer.Ordinal));

        if (asked == "copied out")
        {
            Assert.Equal(
                natives.Select(native => Path.GetFullPath(Path.Combine(copies, native))).Order(StringComparer.Ordinal),
                Directory.GetFiles(copies, "*", SearchOption.AllDirectories).Select(Path.GetFullPath).Order(StringComparer.Ordinal));
            Assert.All(natives, native => Assert.Equal("found " + native, File.ReadAllText(Path.Combine(copies, native))));
        }
        else if (asked == "neither")
        {
            Assert.False(Directory.Exists(copies), "The publish copied the native libraries out unasked.");
        }

        static void Write(string root, string file, string text)
        {
            var path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    /// <summary>
    /// The signing step itself, run as GitHub runs a pwsh step, against a codesign that signs nothing, records every call
    /// and says whom a library is signed by. Each library is signed with the identity from the keychain, under the
    /// hardened runtime and with a timestamp, then verified and read back; the step fails, saying so, when codesign
    /// refuses to sign, when what it reads back is not a Developer ID Application signature, and when the directory holds
    /// no library at all. Every call is held to codesign's own grammar.
    /// </summary>
    [Theory]
    [InlineData("signed by our team", 0, "2 native libraries signed.")]
    [InlineData("refused", 1, "codesign exited with 1 signing ")]
    [InlineData("ad hoc", 1, "does not carry a Developer ID Application signature: what codesign said is above.")]
    [InlineData("none copied", 1, "holds no native library: the first publish copied none out of the bundle")]
    public void The_native_libraries_are_each_signed_verified_and_read_back(string signed, int exit, string said)
    {
        var step = ActionStep(SignNatives);
        Assert.Equal("pwsh", Scalar(step, "shell"));

        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, ".github", "scripts", "Sign-NativeLibraries.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.Copy(Path.Combine(RepositoryRoot(), ".github", "scripts", "Sign-NativeLibraries.ps1"), script);

        var natives = Path.Combine(tree.Root, "natives");
        Directory.CreateDirectory(Path.Combine(natives, "sub"));
        // The script names each library by its full path, as PowerShell writes it.
        string[] libraries = signed == "none copied" ? [] : [.. new[] { "libexample.dylib", Path.Combine("sub", "libother.dylib") }.Select(file => Path.GetFullPath(Path.Combine(natives, file)))];
        foreach (var library in libraries)
        {
            File.WriteAllText(library, "a made-up library");
        }

        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);
        var run = Path.Combine(tree.Root, "sign-the-native-libraries.ps1");
        File.WriteAllText(run, Scalar(step, "run"));
        var keychain = Path.Combine(tree.Root, "jason-signing.keychain-db");

        var (code, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["NATIVES"] = natives,
                ["IDENTITY"] = TeamIdentity,
                ["KEYCHAIN"] = keychain,
                ["CALLS"] = calls,
                ["SIGNED"] = signed,
            },
            "-Command",
            $"Set-Location -LiteralPath '{tree.Root}'\n"
                + StandInForCodesign + "\n"
                + Workflows.PlainErrors.Replace("@SCRIPT@", run.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal));

        var made = File.ReadAllLines(calls).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToList();
        foreach (var call in made)
        {
            var problem = CodesignGrammar(call, out _, out _);
            Assert.True(problem is null, $"codesign {string.Join(' ', call)}: {problem}");
        }

        if (exit != 0)
        {
            Assert.True(code != 0, stdout + stderr);
            Assert.Contains(said, stderr, StringComparison.Ordinal);
            Assert.Equal(signed == "none copied" ? 0 : signed == "refused" ? 1 : 3, made.Count);
            return;
        }

        Assert.True(code == 0, stdout + stderr);
        Assert.Contains(said, stdout, StringComparison.Ordinal);
        Assert.Equal(libraries.Length * 3, made.Count);
        // PowerShell writes a path in the case the file system keeps, which on Windows need not be the case it was given in.
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var library in libraries)
        {
            var own = made.Where(call => { CodesignGrammar(call, out _, out var operands); return operands is [var path] && paths.Equals(path, library); }).ToList();
            Assert.Equal(["--sign", "--verify", "--display"], own.Select(call => { CodesignGrammar(call, out var operation, out _); return operation; }));
            Assert.Equal(["--force", "--sign", TeamIdentity, "--keychain", keychain, "--options", "runtime", "--timestamp"], own[0][..^1]);
            Assert.Contains("--strict", own[1]);
        }
    }

    /// <summary>
    /// A codesign that signs nothing. It records each call as the JSON array of its arguments and leaves exit code 0,
    /// or 1 where <c>SIGNED</c> says it refuses to sign; asked to display a signature it says what codesign says of one
    /// signed by a Developer ID Application identity, or of an ad hoc one. codesign writes that on standard error, which
    /// the script reads together with its output; this one writes it on its output.
    /// </summary>
    private const string StandInForCodesign = """
        function codesign {
            Add-Content -LiteralPath $env:CALLS -Value (ConvertTo-Json -Compress -InputObject @($args))
            $global:LASTEXITCODE = 0
            if ($args -contains '--sign' -and $env:SIGNED -eq 'refused') { $global:LASTEXITCODE = 1; return }
            if ($args -contains '--display') {
                "Executable=$($args[-1])"
                if ($env:SIGNED -eq 'ad hoc') {
                    'Signature=adhoc'
                    'TeamIdentifier=not set'
                } else {
                    'Authority=Developer ID Application: Example Software Inc (0A1B2C3D4E)'
                    'Authority=Developer ID Certification Authority'
                    'Authority=Apple Root CA'
                    'TeamIdentifier=0A1B2C3D4E'
                }
            }
        }
        """;

    /// <summary>
    /// One codesign call held to the grammar of codesign(1), the macOS manual page (SYNOPSIS, OPTIONS and OPTION FLAGS):
    /// exactly one operation of <c>--sign</c>, <c>--verify</c> and <c>--display</c>; options before the operands, since
    /// codesign reads its options as getopt_long does on macOS, which stops at the first operand; <c>--sign</c>,
    /// <c>--keychain</c> and <c>--options</c> take the next argument as their value, and the flags <c>--options</c> may
    /// name are kill, hard, host, expires, library, runtime and linker-signed; <c>--timestamp</c>, <c>--strict</c> and
    /// <c>--verbose</c> take a value only after <c>=</c>, so an argument after them is an operand. Returns what is wrong
    /// with the call, or null.
    /// </summary>
    private static string? CodesignGrammar(IReadOnlyList<string> call, out string operation, out List<string> operands)
    {
        string[] valued = ["--sign", "--keychain", "--options"];
        string[] flags = ["--force", "--verify", "--display", "--timestamp", "--strict", "--verbose"];
        string[] optionFlags = ["kill", "hard", "host", "expires", "library", "runtime", "linker-signed"];
        string[] operations = ["--sign", "--verify", "--display"];

        operation = string.Empty;
        operands = [];
        var named = new List<string>();
        for (var at = 0; at < call.Count; at++)
        {
            var argument = call[at];
            if (operands.Count > 0 || !argument.StartsWith('-'))
            {
                operands.Add(argument);
                continue;
            }

            if (valued.Contains(argument))
            {
                if (at + 1 == call.Count)
                {
                    return $"{argument} takes a value, and none follows it.";
                }

                var value = call[++at];
                if (argument == "--options" && value.Split(',').FirstOrDefault(flag => !optionFlags.Contains(flag)) is { } unknown)
                {
                    return $"--options names '{unknown}', which is not one of its flags.";
                }
            }
            else if (!flags.Contains(argument) && !(argument.Split('=', 2) is [var option, _] && option is "--timestamp" or "--strict" or "--verbose"))
            {
                return $"{argument} is not an option codesign takes.";
            }

            named.Add(argument.Split('=', 2)[0]);
        }

        var asked = named.Where(operations.Contains).ToList();
        if (asked.Count != 1)
        {
            return $"it asks for {asked.Count} operations of --sign, --verify and --display, not one.";
        }

        operation = asked[0];
        if (named.Count != named.Distinct().Count())
        {
            return "it names an option twice.";
        }

        return operands.Count == 1 ? null : $"it names {operands.Count} paths, not one.";
    }

    /// <summary>
    /// A signing publish copies the native libraries out, the next steps sign them with the identity just imported and
    /// bundle them again, and only then is the executable signed. The second publish is the first, building and restoring
    /// nothing, with the signed copies given where the first was told to copy them; a publish that signs nothing copies
    /// nothing out.
    /// </summary>
    [Fact]
    public void The_native_libraries_are_copied_out_signed_and_bundled_again_before_the_executable_is_signed()
    {
        var steps = ActionSteps();
        var published = steps.FindIndex(step => Scalar(step, "id") == "publish");
        var imported = steps.FindIndex(step => Scalar(step, "id") == "identity");
        var natives = steps.FindIndex(step => Scalar(step, "name") == SignNatives);
        var bundled = steps.FindIndex(step => Scalar(step, "name") == BundleAgain);
        var signed = steps.FindIndex(step => Scalar(step, "name") == "Sign the macOS executable with the hardened runtime");
        Assert.True(published >= 0 && imported > published, "The identity is not imported after the publish.");
        Assert.True(natives > imported, "The native libraries are signed before the identity is imported.");
        Assert.True(bundled > natives, "The native libraries are bundled again before they are signed.");
        Assert.True(signed > bundled, "The executable is signed before the signed native libraries are bundled into it.");
        foreach (var step in new[] { steps[natives], steps[bundled] })
        {
            Assert.Equal(Signing, Scalar(step, "if"));
            Assert.Null(Node(step, "continue-on-error"));
            Assert.Equal("pwsh", Scalar(step, "shell"));
        }

        var publish = steps[published];
        Assert.Equal("${{ " + Signing + " }}", Scalar(Mapping(publish, "env"), "SIGN_MACOS"));
        var first = Scalar(publish, "run")!;
        foreach (var line in (string[])
                 [
                     "$natives = Join-Path $PWD \"artifacts/natives/$env:RID\"",
                     "$copied = @(if ($env:SIGN_MACOS -eq 'true') { \"-p:CopyBundledNativeLibrariesTo=$natives\" })",
                     "if ($copied) { \"natives=$natives\" >> $env:GITHUB_OUTPUT }",
                 ])
        {
            Assert.Contains(line, first.Split('\n'));
        }

        static string Publish(string run) => Assert.Single(run.Split('\n'), line => line.TrimStart().StartsWith("dotnet publish ", StringComparison.Ordinal));
        Assert.Contains(" @runtime @copied --output $publish", Publish(first), StringComparison.Ordinal);
        var again = Scalar(steps[bundled], "run")!;
        Assert.Equal(
            Publish(first).Replace(" @copied --output ", " --no-restore --no-build \"-p:BundleNativeLibrariesFrom=$env:NATIVES\" --output ", StringComparison.Ordinal),
            Publish(again));
        foreach (var line in first.Split('\n').Where(line => line.StartsWith("$publish = ", StringComparison.Ordinal) || line.StartsWith("$runtime = ", StringComparison.Ordinal)))
        {
            Assert.Contains(line, again.Split('\n'));
        }

        var env = Mapping(steps[bundled], "env");
        foreach (var name in (string[])["RID", "VERSION", "RUNTIME_VERSION"])
        {
            Assert.Equal(Scalar(Mapping(publish, "env"), name), Scalar(env, name));
        }

        Assert.Equal("${{ steps.publish.outputs.natives }}", Scalar(env, "NATIVES"));

        var sign = Mapping(steps[natives], "env");
        Assert.Equal("${{ steps.publish.outputs.natives }}", Scalar(sign, "NATIVES"));
        Assert.Equal("${{ steps.identity.outputs.identity }}", Scalar(sign, "IDENTITY"));
        Assert.Equal("${{ steps.identity.outputs.keychain }}", Scalar(sign, "KEYCHAIN"));
        Assert.Equal(
            "./.github/scripts/Sign-NativeLibraries.ps1 -Directory $env:NATIVES -Identity $env:IDENTITY -Keychain $env:KEYCHAIN",
            Scalar(steps[natives], "run")!.Trim());
    }

    /// <summary>
    /// MSBuild, through the dotnet on PATH, in the repository root so that its pinned SDK runs it, building one target of
    /// <paramref name="project"/> with the given properties; the output is what it said, both streams.
    /// </summary>
    private static (int Exit, string Output) Msbuild(string project, string target, IEnumerable<string> properties)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in (string[])["msbuild", project, "-nologo", "-noAutoResponse", "-nodeReuse:false", "-tl:off", $"-t:{target}", .. properties])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("MSBuild did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private const string QuarantinedStart = "Start a quarantined copy of the archived executable";

    /// <summary>
    /// After the smoke test, the release starts a copy of the archived executable marked as a browser marks a download, so
    /// that the notes' sentence that such a copy starts is a reading of every release. The step reads the archive the
    /// package action made and the tag's version, bounds the run, has the copy unpack its native libraries under the
    /// runner's temporary directory rather than find the smoke test's, and ends with exit 0 whatever happened.
    /// </summary>
    [Fact]
    public void The_release_starts_a_quarantined_copy_of_the_archive_after_the_smoke_test()
    {
        var steps = JobSteps(MacosJob()).ToList();
        var smoke = steps.FindIndex(step => Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true);
        var quarantined = steps.FindIndex(step => Scalar(step, "name") == QuarantinedStart);
        Assert.True(smoke >= 0 && quarantined > smoke, "The quarantined copy is not started after the smoke test.");

        var step = steps[quarantined];
        Assert.Null(Node(step, "if"));
        Assert.Equal("pwsh", Scalar(step, "shell"));
        var env = Mapping(step, "env");
        Assert.Equal("${{ steps.package.outputs.archive }}", Scalar(env, "ARCHIVE"));
        Assert.Equal("${{ needs.version.outputs.version }}", Scalar(env, "VERSION"));
        Assert.True(int.TryParse(Scalar(env, "TIMEOUT_SECONDS"), out var seconds) && seconds is > 0 and <= 300, "The run is not bounded by a number of seconds.");
        Assert.Equal("false", Scalar(env, "JASON_Update__CheckEnabled"));
        Assert.Matches(@"(?m)^\s*\$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = \$extracted\r?$", Scalar(step, "run")!);
        Assert.Matches(@"(?m)^\s*\$extracted = Join-Path \$env:RUNNER_TEMP '[^']+'\r?$", Scalar(step, "run")!);
        Assert.EndsWith("exit 0", Scalar(step, "run")!.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The quarantined start itself, run as GitHub runs a pwsh step, against a tar that unpacks the program this row
    /// writes, an xattr that keeps the attribute it is given and an spctl that answers. The step marks the unpacked
    /// executable with a quarantine value of the form a browser writes and every xattr call is held to xattr's own
    /// grammar; the value says the file was downloaded and not that a person approved it, which would spare it the
    /// assessment. A copy that says the tag's version, starts its runtime, reports its migrations and stops it is reported
    /// as started, and as assessed only where spctl says assessments are on and the first run moved the quarantine flags,
    /// which this row's program does as Gatekeeper does. One that says another version, one the system kills, as it kills
    /// what Gatekeeper refuses, one that never exits, one whose runtime does not start or reports no migrations, an
    /// attribute that cannot be written, assessments that are off and flags the first run left as they were are each a
    /// warning, a runtime the copy was asked to start is stopped whatever came after, a failed start included, since it
    /// may have left one running, and the step passes all the same. Whatever the copy ran, it unpacked its
    /// native library under the runner's temporary directory, and the step shows that library's attributes and signature,
    /// each call held to its program's grammar.
    /// </summary>
    [Theory]
    [InlineData("says the tag's version", null)]
    [InlineData("says another version", "The quarantined copy says '0.22.0+0123abc', and the tag says 0.23.0.")]
    [InlineData("is killed", "jason --version exited with 137.")]
    [InlineData("hangs", "jason --version had not finished after 2 seconds, and was stopped.")]
    [InlineData("cannot start", "jason runtime start exited with 1.")]
    [InlineData("cannot open its database", "The quarantined copy's runtime reported no applied migrations: {\"applied_migrations\":[]}")]
    [InlineData("is not assessed", "The quarantine flags read 0081 before the first run and after it: nothing shows that Gatekeeper assessed the copy.")]
    [InlineData("cannot be marked", "xattr exited with 1 marking ")]
    [InlineData("cannot be marked, and nothing assesses", "spctl says 'assessments disabled': Gatekeeper assesses nothing on this runner")]
    public void The_quarantined_copy_is_started_under_a_bound_and_never_fails_the_job(string program, string? warned)
    {
        Assert.SkipUnless(
            program.StartsWith("cannot be marked", StringComparison.Ordinal) || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            "The program the step starts is a shell script here; CI's Linux and macOS legs run this case.");

        using var tree = new TempTree();
        var written = Path.Combine(tree.Root, "program", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(written)!);
        var version = program switch
        {
            "says another version" => "echo '0.22.0+0123abc'",
            "is killed" => "kill -KILL $$",
            "hangs" => "exec sleep 30",
            _ => "echo '0.23.0+0123abc'",
        };
        var migrations = program == "cannot open its database" ? "[]" : "[\"Initial\"]";
        var start = program == "cannot start" ? "exit 1" : ":";
        // Gatekeeper, having assessed a quarantined file it lets run, records that in the attribute's flags.
        var assessed = program == "is not assessed" ? string.Empty : "v=$(cat \"$ATTRIBUTE\"); printf '%s' \"00c1${v#????}\" > \"$ATTRIBUTE\"";
        File.WriteAllText(written, $$"""
            #!/bin/sh
            echo "$*" >> "$PROGRAM_CALLS"
            {{assessed}}
            if [ -n "$DOTNET_BUNDLE_EXTRACT_BASE_DIR" ]; then
              mkdir -p "$DOTNET_BUNDLE_EXTRACT_BASE_DIR/jason/0a1b2c" && : > "$DOTNET_BUNDLE_EXTRACT_BASE_DIR/jason/0a1b2c/libe_sqlite3.dylib"
            fi
            case "$*" in
              --version) {{version}} ;;
              'runtime start') {{start}} ;;
              'runtime status') echo '{"applied_migrations":{{migrations}}}' ;;
            esac

            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(written, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);
        var asked = Path.Combine(tree.Root, "asked.txt");
        var archive = Path.Combine(tree.Root, "jason-osx-arm64.tar.gz");
        var run = Path.Combine(tree.Root, "start-a-quarantined-copy.ps1");
        File.WriteAllText(run, Scalar(JobSteps(MacosJob()).Single(step => Scalar(step, "name") == QuarantinedStart), "run"));

        var clock = Stopwatch.StartNew();
        var (code, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["RUNNER_TEMP"] = tree.Root,
                ["ARCHIVE"] = archive,
                ["VERSION"] = "0.23.0",
                ["TIMEOUT_SECONDS"] = "2",
                ["JASON_Update__CheckEnabled"] = "false",
                ["CALLS"] = calls,
                ["PROGRAM"] = written,
                ["PROGRAM_CALLS"] = asked,
                ["ATTRIBUTE"] = Path.Combine(tree.Root, "attribute.txt"),
                ["XATTR"] = program.StartsWith("cannot be marked", StringComparison.Ordinal) ? "refuses" : "writes",
                ["SPCTL"] = program.EndsWith("nothing assesses", StringComparison.Ordinal) ? "disabled" : "enabled",
            },
            "-Command",
            $"Set-Location -LiteralPath '{tree.Root}'\n"
                + StandInsForTheQuarantinedStart + "\n"
                + Workflows.PlainErrors.Replace("@SCRIPT@", run.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal));
        clock.Stop();

        Assert.True(code == 0, stdout + stderr);
        var warnings = stdout.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("::warning title=Quarantined start::", StringComparison.Ordinal)).ToList();
        if (warned is null)
        {
            Assert.Empty(warnings);
        }
        else
        {
            Assert.Contains(warnings, warning => warning.Contains(warned, StringComparison.Ordinal));
            Assert.Equal(program == "cannot be marked, and nothing assesses" ? 2 : 1, warnings.Count);
        }

        if (program == "says the tag's version")
        {
            Assert.Contains("The quarantined copy was assessed, started, said 0.23.0 and opened its database.", stdout, StringComparison.Ordinal);
        }
        else if (program == "is not assessed")
        {
            Assert.Contains("The quarantined copy started, said 0.23.0 and opened its database, and nothing above shows that Gatekeeper assessed it.", stdout, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("The quarantined copy was assessed", stdout, StringComparison.Ordinal);
        }

        if (program == "hangs")
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(25), $"The run was not stopped at its bound: it took {clock.Elapsed}.");
        }

        // What the copy was asked, in order: a runtime it started is stopped, and one it never started is not.
        string[] questions = program switch
        {
            "says the tag's version" or "is not assessed" or "cannot open its database" => ["--version", "runtime start", "runtime status", "runtime stop"],
            "cannot start" => ["--version", "runtime start", "runtime stop"],
            "cannot be marked" or "cannot be marked, and nothing assesses" => [],
            _ => ["--version"],
        };
        Assert.Equal(questions, File.Exists(asked) ? File.ReadAllLines(asked) : []);

        var made = File.ReadAllLines(calls).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToList();
        var unpacked = Path.GetFullPath(Path.Combine(tree.Root, "quarantined"));
        var exe = Path.Combine(unpacked, "jason");
        Assert.Equal(["spctl", "--status"], made[0]);
        Assert.Equal(["tar", "-xzf", archive, "-C"], made[1][..^1]);
        Assert.Equal(unpacked, Path.GetFullPath(made[1][^1]));

        var xattr = made.Skip(2).Where(call => call[0] == "xattr").ToList();
        var codesign = made.Skip(2).Where(call => call[0] == "codesign").ToList();
        Assert.Equal(made.Count - 2, xattr.Count + codesign.Count);
        foreach (var call in xattr)
        {
            var problem = XattrGrammar(call[1..], out _, out _);
            Assert.True(problem is null, $"xattr {string.Join(' ', call[1..])}: {problem}");
        }

        // The attribute is read before the first run and after it, however the run ended; then the library the copy
        // unpacked is listed, which in xattr's grammar is no operation at all.
        var ran = !program.StartsWith("cannot be marked", StringComparison.Ordinal);
        string[] operations = ran ? ["-w", "-p", "-p", ""] : ["-w"];
        Assert.Equal(operations, xattr.Select(call => { XattrGrammar(call[1..], out var operation, out _); return operation; }));
        Assert.All(xattr.Take(ran ? 3 : 1), call =>
        {
            XattrGrammar(call[1..], out _, out var operands);
            Assert.Equal("com.apple.quarantine", operands[0]);
            Assert.Equal(exe, Path.GetFullPath(operands[^1]));
        });

        var library = Path.GetFullPath(Path.Combine(tree.Root, "jason-quarantined-extract", "jason", "0a1b2c", "libe_sqlite3.dylib"));
        if (ran)
        {
            Assert.Equal(library, Path.GetFullPath(xattr[^1][^1]));
            var display = Assert.Single(codesign);
            var problem = CodesignGrammar(display[1..], out var operation, out var operands);
            Assert.True(problem is null, $"codesign {string.Join(' ', display[1..])}: {problem}");
            Assert.Equal("--display", operation);
            Assert.Equal(library, Path.GetFullPath(Assert.Single(operands)));
            Assert.Contains("TeamIdentifier=0A1B2C3D4E", stdout, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(codesign);
        }

        // flags;time;agent;id: four hexadecimal digits of flags, the moment in hexadecimal seconds since 1970, the agent that
        // downloaded the file, and the download's id in the upper-case form the system writes.
        XattrGrammar(xattr[0][1..], out _, out var marked);
        var value = Assert.Single(marked[1..^1]);
        var match = System.Text.RegularExpressions.Regex.Match(value, "^[0-9a-f]{4};(?<time>[0-9a-f]{8});[^;]+;[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$");
        Assert.True(match.Success, $"'{value}' is not a quarantine value of the form a browser writes.");
        // The flags of qtn_file_flags: 0x0001 says the file was downloaded; 0x0040 says a person approved it, and a file
        // marked approved is not assessed at all.
        var flags = Convert.ToInt32(value[..4], 16);
        Assert.True((flags & 0x0001) != 0, $"'{value}' does not say the file was downloaded.");
        Assert.True((flags & 0x0040) == 0, $"'{value}' says a person already approved the file, so Gatekeeper would not assess it.");
        var moment = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(match.Groups["time"].Value, 16));
        Assert.True((DateTimeOffset.UtcNow - moment).Duration() < TimeSpan.FromHours(1), $"The value names {moment:O}, not the moment it was written.");
    }

    /// <summary>
    /// A tar, an xattr, an spctl and a codesign that do nothing to the system. Each records its call as the JSON array of its name
    /// and its arguments. tar unpacks the program the row wrote into the directory after <c>-C</c>; xattr keeps the
    /// value <c>-w</c> gives it in a file and gives it back to <c>-p</c>, or refuses to write it where <c>XATTR</c> says
    /// so, and lists no attribute; spctl says assessments are enabled, or disabled where <c>SPCTL</c> says so; codesign
    /// says what it says of a library signed by a Developer ID Application identity.
    /// </summary>
    private const string StandInsForTheQuarantinedStart = """
        function spctl {
            Add-Content -LiteralPath $env:CALLS -Value (ConvertTo-Json -Compress -InputObject (@('spctl') + @($args)))
            $global:LASTEXITCODE = 0
            "assessments $env:SPCTL"
        }

        function tar {
            Add-Content -LiteralPath $env:CALLS -Value (ConvertTo-Json -Compress -InputObject (@('tar') + @($args)))
            Copy-Item -LiteralPath $env:PROGRAM -Destination (Join-Path $args[3] 'jason')
            $global:LASTEXITCODE = 0
        }

        function xattr {
            Add-Content -LiteralPath $env:CALLS -Value (ConvertTo-Json -Compress -InputObject (@('xattr') + @($args)))
            $global:LASTEXITCODE = 0
            if ($args[0] -eq '-w') {
                if ($env:XATTR -eq 'refuses') { $global:LASTEXITCODE = 1; return }
                Set-Content -LiteralPath $env:ATTRIBUTE -Value $args[2] -NoNewline
            } elseif ($args[0] -eq '-p') {
                if (Test-Path -LiteralPath $env:ATTRIBUTE) { Get-Content -LiteralPath $env:ATTRIBUTE -Raw } else { $global:LASTEXITCODE = 1 }
            }
        }

        function codesign {
            Add-Content -LiteralPath $env:CALLS -Value (ConvertTo-Json -Compress -InputObject (@('codesign') + @($args)))
            $global:LASTEXITCODE = 0
            "Executable=$($args[-1])"
            'Authority=Developer ID Application: Example Software Inc (0A1B2C3D4E)'
            'TeamIdentifier=0A1B2C3D4E'
        }
        """;

    /// <summary>
    /// One xattr call, without the program's name, held to the grammar of xattr(1), the macOS manual page (SYNOPSIS):
    /// <code>
    /// xattr [-lrsvx] file ...
    /// xattr -p [-lrsvx] attr_name file ...
    /// xattr -w [-rsx] attr_name attr_value file ...
    /// xattr -d [-rsv] attr_name file ...
    /// xattr -c [-rsv] file ...
    /// </code>
    /// Options come before the operands, since xattr stops reading options at the first argument that is not one, so a
    /// value written after the name is an operand whatever it holds. Returns what is wrong with the call, or null.
    /// </summary>
    private static string? XattrGrammar(IReadOnlyList<string> call, out string operation, out List<string> operands)
    {
        string? asked = null;
        var flags = new List<char>();
        var at = 0;
        for (; at < call.Count && call[at].Length > 1 && call[at][0] == '-'; at++)
        {
            foreach (var letter in call[at][1..])
            {
                if ("pwdc".Contains(letter))
                {
                    if (asked is not null)
                    {
                        operation = string.Empty;
                        operands = [];
                        return "it asks for two operations.";
                    }

                    asked = "-" + letter;
                }
                else
                {
                    flags.Add(letter);
                }
            }
        }

        operation = asked ?? string.Empty;
        operands = [.. call.Skip(at)];
        var (allowed, named) = operation switch
        {
            "-p" => ("lrsvx", 1),
            "-w" => ("rsx", 2),
            "-d" => ("rsv", 1),
            "-c" => ("rsv", 0),
            _ => ("lrsvx", 0),
        };
        if (flags.FirstOrDefault(flag => !allowed.Contains(flag)) is var unknown && unknown != default)
        {
            return $"-{unknown} is not an option of xattr {operation}.";
        }

        return operands.Count > named ? null : $"xattr {operation} takes {named} operands before its files and at least one file, and it was given {operands.Count}.";
    }
}
