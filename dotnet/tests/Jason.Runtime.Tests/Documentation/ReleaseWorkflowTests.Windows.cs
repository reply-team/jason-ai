using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The Windows signature: the job that alone holds the Azure identity and alone may ask for an OIDC token, the steps of
/// the package action that sign, timestamp and verify the executable between the publish and the staging, and the
/// script that verifies a signature the way a person's machine does.
/// </summary>
public partial class ReleaseWorkflowTests
{
    /// <summary>The five Azure secrets, each under the package action's input of its own name.</summary>
    private static readonly (string Input, string Secret)[] AzureSecrets =
    [
        ("azure-tenant-id", "AZURE_TENANT_ID"),
        ("azure-client-id", "AZURE_CLIENT_ID"),
        ("azure-signing-account", "AZURE_SIGNING_ACCOUNT"),
        ("azure-signing-endpoint", "AZURE_SIGNING_ENDPOINT"),
        ("azure-signing-profile", "AZURE_SIGNING_PROFILE"),
    ];

    /// <summary>What every step that signs in, signs or verifies runs on: win-x64, and only when it is asked to.</summary>
    private const string WindowsSigning = "inputs.rid == 'win-x64' && inputs.sign-windows == 'true'";

    private const string SignIn = "Sign in to Azure with this job's OIDC token";

    private const string FetchToken = "Fetch the signing service's token while the sign-in is fresh";

    private const string SignWindows = "Sign the Windows executable with Artifact Signing";

    private const string VerifyWindows = "Verify the signature of the Windows executable";

    private const string ForgetAzure = "Forget the Azure sign-in";

    private const string ArchivedWindows = "Verify the signature of the archived Windows executable";

    private const string WindowsLibraries = "Read who signed each native library the bundle unpacked";

    private const string WindowsSignature = ".github/scripts/Assert-WindowsSignature.ps1";

    /// <summary>
    /// The publisher both verifications hold the signature to: the simple name of the subject Reply's certificate is
    /// issued to.
    /// </summary>
    private const string Publisher = "'ReplyApp Inc.'";

    /// <summary>The GitHub environment the jobs that hold a secret or may write run in, the one the federated credential is matched on.</summary>
    private const string SigningEnvironment = "signing";

    /// <summary>
    /// The two actions that hold the Azure identity, each pinned to the commit of a release, with that release's tag
    /// beside it; every other action keeps its major tag.
    /// </summary>
    private static readonly (string Action, string Commit, string Tag)[] AzurePins =
    [
        ("azure/login", "a641126d1b8aa4d1fa005f4f92df94a3a4c4c906", "v3.1.0"),
        ("azure/artifact-signing-action", "c7ab2a863ab5f9a846ddb8265964877ef296ee82", "v2.0.0"),
    ];

    private static string Pinned(string action)
    {
        var pin = Assert.Single(AzurePins, pin => pin.Action == action);
        return $"{pin.Action}@{pin.Commit}";
    }

    /// <summary>The release's job that builds win-x64: the one whose package call names it.</summary>
    private static YamlMappingNode WindowsJob() =>
        Assert.Single(
            Mapping(Parse(Read(Release)), "jobs").Children.Values.Cast<YamlMappingNode>(),
            job => JobSteps(job).Any(step => Scalar(step, "uses") == "./.github/actions/package" && Scalar(Mapping(step, "with"), "rid") == "win-x64"));

    private static YamlMappingNode WindowsStep(string name) => Assert.Single(JobSteps(WindowsJob()), step => Scalar(step, "name") == name);

    /// <summary>
    /// The place of each Azure secret, held against release.yml as it is and against copies of it that move one: a sixth
    /// name beside the five, two of them swapped, an Apple secret in the Windows package call and an Azure one in the
    /// macOS call, the five in another step of the Windows job or in the matrix's package call, a test run in the Windows
    /// job, the tap's token in it, the whole context read there, and one of the five reached through a YAML alias of
    /// the node in the call; and a job that calls another workflow, a job that hands every secret on, and the two at once,
    /// which name no secret anywhere an expression could be read. Each copy is refused, and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("a sixth name", true)]
    [InlineData("two swapped", true)]
    [InlineData("an Apple secret in the windows call", true)]
    [InlineData("an Azure secret in the macOS call", true)]
    [InlineData("another step of the windows job", true)]
    [InlineData("the matrix's package call", true)]
    [InlineData("a test in the windows job", true)]
    [InlineData("the token in the windows job", true)]
    [InlineData("the whole context in the windows job", true)]
    [InlineData("an alias of the call's client id", true)]
    [InlineData("a job that calls a workflow", true)]
    [InlineData("a job that hands every secret on", true)]
    [InlineData("a called workflow handed every secret", true)]
    public void Only_the_package_call_that_builds_win_x64_holds_the_azure_secrets(string copy, bool refused)
    {
        var text = Read(Release).ReplaceLineEndings("\n");
        const string ClientId = "          azure-client-id: ${{ secrets.AZURE_CLIENT_ID }}\n";
        const string TeamId = "          apple-team-id: ${{ secrets.APPLE_TEAM_ID }}\n";
        // The osx-arm64 call's team id, told from the osx-x64 call's by the step that follows it.
        const string MacosTail = "\n      # The archive is what a person downloads";
        const string Archive = "      - name: " + ArchivedWindows + "\n        shell: pwsh\n        env:\n          ARCHIVE: ${{ steps.package.outputs.archive }}\n";
        const string PluginSteps = "  plugin:\n    name: The plugins' packages\n    needs: version\n    runs-on: ubuntu-latest\n";
        const string WindowsPackage = "      - name: Package\n        id: package\n        uses: ./.github/actions/package\n        with:\n          rid: win-x64\n";
        const string MatrixPackage = "          rid: ${{ matrix.rid }}\n          version: ${{ needs.version.outputs.version }}\n";
        const string Called = "  called:\n    needs: version\n    uses: ./.github/workflows/made-up.yml\n";
        var five = string.Concat(AzureSecrets.Select(secret => $"          {secret.Input}: ${{{{ secrets.{secret.Secret} }}}}\n"));

        string Moved(string from, string to, string? within = null)
        {
            within ??= text;
            Assert.Equal(1, Regex.Count(within, Regex.Escape(from)));
            return within.Replace(from, to, StringComparison.Ordinal);
        }

        var moved = copy switch
        {
            "as it is" => text,
            "a sixth name" => Moved(ClientId, ClientId + "          azure-client-secret: ${{ secrets.AZURE_CLIENT_SECRET }}\n"),
            "two swapped" => Moved(ClientId, "          azure-client-id: ${{ secrets.AZURE_TENANT_ID }}\n"),
            "an Apple secret in the windows call" => Moved(ClientId, ClientId + TeamId),
            "an Azure secret in the macOS call" => Moved(TeamId + MacosTail, TeamId + ClientId + MacosTail),
            "another step of the windows job" => Moved(Archive, Archive + "          CLIENT: ${{ secrets.AZURE_CLIENT_ID }}\n"),
            "the matrix's package call" => Moved(MatrixPackage, MatrixPackage + five, Moved(five, string.Empty)),
            "a test in the windows job" => Moved(WindowsPackage, "      - run: dotnet test --solution dotnet/Jason.slnx\n\n" + WindowsPackage),
            "the token in the windows job" => Moved(Archive, Archive + $"          TOKEN: {TapToken}\n"),
            "the whole context in the windows job" => Moved(Archive, Archive + "          ALL: ${{ toJSON(secrets) }}\n"),
            "an alias of the call's client id" => Moved(
                PluginSteps,
                PluginSteps + "    env:\n      CLIENT: *client\n",
                Moved(ClientId, "          azure-client-id: &client ${{ secrets.AZURE_CLIENT_ID }}\n")),
            "a job that calls a workflow" => Moved(PluginSteps, Called + "\n" + PluginSteps),
            "a job that hands every secret on" => Moved(PluginSteps, PluginSteps + "    secrets: inherit\n"),
            "a called workflow handed every secret" => Moved(PluginSteps, Called + "    secrets: inherit\n\n" + PluginSteps),
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };

        var problems = SecretsOutOfPlace(moved);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// An OIDC token is what the federated credential trades for an Azure sign-in, so the job that may ask for one is the
    /// job that may sign. That is the Windows job alone: no other job, the matrix that runs the tests included, and not
    /// the workflow, which would hand the permission to every job.
    /// </summary>
    [Fact]
    [Critical(Family.B4, "the OIDC token's scope")]
    public void Only_the_windows_job_may_ask_for_an_oidc_token()
    {
        var text = Read(Release);
        Assert.Empty(IdTokenOutOfPlace(text));
        Assert.Equal("read", Scalar(Mapping(Parse(text), "permissions"), "contents"));
        var permissions = Mapping(WindowsJob(), "permissions");
        Assert.Equal(["contents=read", "id-token=write"], permissions.Children.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>
    /// Each place an OIDC token could be asked for outside the Windows job, or that job's own permission gone, as a line
    /// saying where; empty when the Windows job alone asks for one. <c>write-all</c> grants it as surely as the key, and a
    /// YAML alias of the Windows job's permissions is that job's own node seen a second time.
    /// </summary>
    private static List<string> IdTokenOutOfPlace(string text)
    {
        var problems = new List<string>();
        var release = Parse(text);
        var jobs = Mapping(release, "jobs");

        var holders = PackageCallsWithoutTests(jobs, "win-x64").Select(call => call.Job).Distinct(StringComparer.Ordinal).ToList();
        if (holders.Count != 1)
        {
            problems.Add($"{holders.Count} jobs that run no test package win-x64, where exactly one may ask for an OIDC token");
            return problems;
        }

        var own = Node(Mapping(jobs, holders[0]), "permissions") as YamlMappingNode;
        if (own is null || Scalar(own, "id-token") != "write")
        {
            problems.Add($"the {holders[0]} job does not ask for id-token: write");
        }

        var sightings = 0;
        foreach (var (parent, key, value) in Pairs(release))
        {
            if (key == "permissions" && value is YamlScalarNode { Value: "write-all" })
            {
                problems.Add("permissions: write-all grants an OIDC token");
            }

            if (own is not null && ReferenceEquals(value, own) && ++sightings > 1)
            {
                problems.Add($"the {holders[0]} job's permissions stand a second time, through an alias");
            }

            if (key == "id-token" && !ReferenceEquals(parent, own))
            {
                problems.Add($"an id-token permission stands outside the {holders[0]} job");
            }
        }

        return problems;
    }

    /// <summary>Every key and value of every mapping under a node, with the mapping that holds them.</summary>
    private static IEnumerable<(YamlMappingNode Parent, string Key, YamlNode Value)> Pairs(YamlNode node) => node switch
    {
        YamlMappingNode mapping => mapping.Children.SelectMany(pair => Pairs(pair.Value).Prepend((mapping, pair.Key.ToString(), pair.Value))),
        YamlSequenceNode sequence => sequence.Children.SelectMany(Pairs),
        _ => [],
    };

    /// <summary>
    /// The OIDC permission held against release.yml as it is and against copies of it that ask for one elsewhere: on the
    /// workflow, in the matrix job, in the macOS job, in the release job, through <c>write-all</c> on a job or on the
    /// workflow, through an alias of the Windows job's permissions, and the Windows job without it. Each copy is refused,
    /// and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("on the workflow", true)]
    [InlineData("in the matrix job", true)]
    [InlineData("in the macOS job", true)]
    [InlineData("in the release job", true)]
    [InlineData("write-all on a job", true)]
    [InlineData("write-all on the workflow", true)]
    [InlineData("an alias of its permissions elsewhere", true)]
    [InlineData("the windows job without it", true)]
    public void Only_the_windows_job_holds_an_id_token(string copy, bool refused)
    {
        var text = Read(Release).ReplaceLineEndings("\n");
        const string Workflow = "permissions:\n  contents: read\n\njobs:\n";
        const string Reads = "    permissions:\n      contents: read\n";
        const string MatrixRuns = "    runs-on: ${{ matrix.os }}\n";
        const string Matrix = MatrixRuns + Reads;
        const string MacosRuns = "    runs-on: macos-latest\n";
        const string Macos = MacosRuns + Reads;
        const string Publishing = "    permissions:\n      contents: write\n";
        const string Own = "    permissions:\n      contents: read\n      id-token: write\n";

        string Moved(string from, string to, string? within = null)
        {
            within ??= text;
            Assert.Equal(1, Regex.Count(within, Regex.Escape(from)));
            return within.Replace(from, to, StringComparison.Ordinal);
        }

        var moved = copy switch
        {
            "as it is" => text,
            "on the workflow" => Moved(Workflow, "permissions:\n  contents: read\n  id-token: write\n\njobs:\n"),
            "in the matrix job" => Moved(Matrix, MatrixRuns + Own),
            "in the macOS job" => Moved(Macos, MacosRuns + Own),
            "in the release job" => Moved(Publishing, "    permissions:\n      contents: write\n      id-token: write\n"),
            "write-all on a job" => Moved(Macos, MacosRuns + "    permissions: write-all\n"),
            "write-all on the workflow" => Moved(Workflow, "permissions: write-all\n\njobs:\n"),
            "an alias of its permissions elsewhere" => Moved(
                Macos,
                MacosRuns + "    permissions: *oidc\n",
                Moved(Own, "    permissions: &oidc\n      contents: read\n      id-token: write\n")),
            "the windows job without it" => Moved(Own, "    permissions:\n      contents: read\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };

        var problems = IdTokenOutOfPlace(moved);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// A release's Windows executable carries a signature made through Artifact Signing; a pull request's carries none.
    /// The package action signs only when it is told to, and <c>release.yml</c> tells it so in one call alone, the one
    /// that names win-x64 in a job of its own, with the five Azure secrets each under the input of its own name; no other
    /// call tells it anything of Azure. That job runs on Windows, builds that platform alone and runs no test, and the
    /// matrix no longer builds win-x64. <c>ci.yml</c> tells the action nothing, names no Azure secret, signs in nowhere and
    /// asks for no OIDC token: a pull request's build never holds the identity.
    /// </summary>
    [Fact]
    public void A_release_signs_the_windows_executable_and_ci_never_does()
    {
        var inputs = Mapping(Parse(Read(Action)), "inputs");
        Assert.Equal("false", Scalar(Mapping(inputs, "sign-windows"), "default"));
        foreach (var (input, _) in AzureSecrets)
        {
            Assert.Equal("false", Scalar(Mapping(inputs, input), "required"));
            Assert.Equal(string.Empty, Scalar(Mapping(inputs, input), "default"));
        }

        var release = PackagingCalls(Release);
        var signing = Assert.Single(release, call => Node(Mapping(call, "with"), "sign-windows") is not null);
        var with = Mapping(signing, "with");
        Assert.Equal("win-x64", Scalar(with, "rid"));
        Assert.Equal("true", Scalar(with, "sign-windows"));
        foreach (var (input, secret) in AzureSecrets)
        {
            Assert.Equal($"${{{{ secrets.{secret} }}}}", Scalar(with, input));
        }

        Assert.Null(Node(signing, "continue-on-error"));
        Assert.All(release.Where(call => call != signing), call => Assert.DoesNotContain(
            Mapping(call, "with").Children.Keys.Select(key => key.ToString()),
            key => key == "sign-windows" || key.StartsWith("azure-", StringComparison.Ordinal)));

        var windows = WindowsJob();
        Assert.Equal("windows-latest", Scalar(windows, "runs-on"));
        Assert.Null(Node(windows, "strategy"));
        Assert.DoesNotContain(JobSteps(windows), step => Scalar(step, "run") is { } run && Regex.IsMatch(run, @"\bdotnet\s+(test|build|restore)\b"));
        var matrix = Scalars(Mapping(MatrixJob(), "strategy")).ToList();
        Assert.DoesNotContain("win-x64", matrix);
        Assert.DoesNotContain("windows-latest", matrix);

        var ci = PackagingCalls(Ci);
        Assert.NotEmpty(ci);
        Assert.All(ci, call => Assert.DoesNotContain(
            Mapping(call, "with").Children.Keys.Select(key => key.ToString()),
            key => key == "sign-windows" || key.StartsWith("azure-", StringComparison.Ordinal)));
        foreach (var never in (string[])["AZURE_", "azure/login", "artifact-signing", "id-token", "sign-windows"])
        {
            Assert.DoesNotContain(never, Read(Ci), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The executable is signed after the publish and before the staging, so that the staged copy, the archive and the
    /// caller's smoke test all carry the one signed file: the inputs checked by name, the sign-in with the job's own OIDC
    /// token, the signing service's own token fetched at once, while the assertion the sign-in was made with is still
    /// fresh, the signature with its SHA-256 timestamp, the verification with the runner's own signtool, and the sign-in
    /// forgotten whatever happened; the application manifest is read after them, from the file that will be archived. The
    /// sign-in takes no subscription and no client secret, and the signing action is told to try the Azure CLI's sign-in
    /// and nothing else, and to restore nothing a previous run cached.
    /// </summary>
    [Fact]
    public void The_windows_executable_is_signed_between_the_publish_and_the_staging_and_verified()
    {
        var steps = ActionSteps();
        int At(Func<YamlMappingNode, bool> which) => steps.FindIndex(step => which(step));

        var published = At(step => Scalar(step, "id") == "publish");
        var checkedAt = At(step => Scalar(step, "id") == "signable");
        var signedIn = At(step => Scalar(step, "name") == SignIn);
        var fetched = At(step => Scalar(step, "name") == FetchToken);
        var signed = At(step => Scalar(step, "name") == SignWindows);
        var verified = At(step => Scalar(step, "name") == VerifyWindows);
        var forgotten = At(step => Scalar(step, "name") == ForgetAzure);
        var manifest = At(step => Scalar(step, "name") == "Read the application manifest out of the packaged executable");
        var staged = At(step => Scalar(step, "name") == "Put LICENSE and THIRD-PARTY-NOTICES beside the executable");

        Assert.True(published >= 0 && checkedAt > published, "The Azure inputs are not checked after the publish.");
        Assert.True(signedIn > checkedAt, "The sign-in comes before the inputs are checked.");
        Assert.True(fetched == signedIn + 1, "The signing service's token is not fetched right after the sign-in.");
        Assert.True(signed == fetched + 1, "The executable is not signed right after its token is fetched.");
        Assert.True(verified > signed, "The signature is not verified after it is made.");
        Assert.True(forgotten > verified, "The sign-in is not forgotten after the last step that needs it.");
        Assert.True(manifest > verified, "The manifest is read from a file that was not yet signed.");
        Assert.True(staged > forgotten && staged > manifest, "The executable is staged before it is signed and read.");

        foreach (var step in new[] { steps[checkedAt], steps[signedIn], steps[fetched], steps[signed], steps[verified] })
        {
            Assert.Equal(WindowsSigning, Scalar(step, "if"));
            Assert.Null(Node(step, "continue-on-error"));
        }

        Assert.Equal("always() && " + WindowsSigning, Scalar(steps[forgotten], "if"));

        var signIn = steps[signedIn];
        Assert.Equal(Pinned("azure/login"), Scalar(signIn, "uses"));
        Assert.Equal(
            ["client-id=${{ inputs.azure-client-id }}", "tenant-id=${{ inputs.azure-tenant-id }}", "allow-no-subscriptions=true"],
            Mapping(signIn, "with").Children.Select(pair => $"{pair.Key}={pair.Value}"));

        // The GitHub assertion the sign-in traded lives for minutes, and the sign-in fetched a token for Azure Resource
        // Manager alone; the signing service's token is asked for here, before the signing action spends those minutes
        // installing what it needs, and is then the Azure CLI's to hand out for its hour. The token proves no role: a role
        // missing on the signing account fails the signing step, and with it the job. Nothing is printed: the token never
        // reaches the log.
        var fetch = steps[fetched];
        Assert.Equal("pwsh", Scalar(fetch, "shell"));
        Assert.Null(Node(fetch, "env"));
        Assert.Equal("az account get-access-token --resource https://codesigning.azure.net --output none", Scalar(fetch, "run")!.Trim());

        var sign = steps[signed];
        Assert.Equal(Pinned("azure/artifact-signing-action"), Scalar(sign, "uses"));
        string[] excluded =
        [
            "environment", "workload-identity", "managed-identity", "shared-token-cache", "visual-studio", "visual-studio-code",
            "azure-powershell", "azure-developer-cli", "interactive-browser",
        ];
        Assert.Equal(
            [
                "endpoint=${{ inputs.azure-signing-endpoint }}",
                "signing-account-name=${{ inputs.azure-signing-account }}",
                "certificate-profile-name=${{ inputs.azure-signing-profile }}",
                "files=${{ steps.signable.outputs.file }}",
                "file-digest=SHA256",
                "timestamp-rfc3161=http://timestamp.acs.microsoft.com",
                "timestamp-digest=SHA256",
                "exclude-azure-cli-credential=false",
                .. excluded.Select(source => $"exclude-{source}-credential=true"),
                "cache-dependencies=false",
                "trace=false",
            ],
            Mapping(sign, "with").Children.Select(pair => $"{pair.Key}={pair.Value}"));

        var check = steps[checkedAt];
        Assert.Equal(["EXECUTABLE", "MISSING"], Mapping(check, "env").Children.Keys.Select(key => key.ToString()));
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(check, "env"), "EXECUTABLE"));

        // Exactly this, and nothing after it: the script refuses with exit 1, which fails a pwsh step only as its last
        // command.
        var verify = steps[verified];
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(verify, "env"), "EXECUTABLE"));
        Assert.Equal("./" + WindowsSignature + " -Executable $env:EXECUTABLE -Publisher " + Publisher, Scalar(verify, "run")!.Trim());
        Assert.True(File.Exists(Path.Combine([RepositoryRoot(), .. WindowsSignature.Split('/')])));

        var forget = Scalar(steps[forgotten], "run")!;
        Assert.Contains("az account clear", forget, StringComparison.Ordinal);
        Assert.EndsWith("exit 0", forget.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The five inputs reach the two steps that take them, the sign-in and the signature, as their inputs and nowhere
    /// else in the action. The check before them is handed only an expression that names the inputs that are empty, so
    /// it never holds a value at all. The desktop's signing client is told the endpoint, the account and the profile, none
    /// of them a credential, through its step's environment alone. <c>release.yml</c> names each secret once, where it
    /// calls the action, and no script of its names one.
    /// </summary>
    [Fact]
    public void The_azure_secrets_reach_only_the_sign_in_and_the_signing_step()
    {
        var steps = ActionSteps();
        var signIn = Assert.Single(steps, step => Scalar(step, "name") == SignIn);
        var sign = Assert.Single(steps, step => Scalar(step, "name") == SignWindows);
        var check = Assert.Single(steps, step => Scalar(step, "id") == "signable");
        var desktop = Assert.Single(steps, step => Scalar(step, "id") == "desktop-signing");

        // The desktop's signing client is told the account it signs with, the three names that are not credentials, as
        // its step's environment and nowhere else; its one credential is the sign-in above.
        Assert.Equal(
            ["SIGNING_ENDPOINT=${{ inputs.azure-signing-endpoint }}", "SIGNING_ACCOUNT=${{ inputs.azure-signing-account }}", "SIGNING_PROFILE=${{ inputs.azure-signing-profile }}"],
            Mapping(desktop, "env").Children.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain("inputs.azure-", Scalar(desktop, "run"), StringComparison.Ordinal);

        var given = new List<string>();
        foreach (var step in steps)
        {
            if (ReferenceEquals(step, desktop))
            {
                continue;
            }

            if (ReferenceEquals(step, signIn) || ReferenceEquals(step, sign))
            {
                Assert.All(
                    step.Children.Where(pair => pair.Key.ToString() != "with").SelectMany(pair => Scalars(pair.Value)),
                    value => Assert.DoesNotContain("inputs.azure-", value, StringComparison.Ordinal));
                given.AddRange(Mapping(step, "with").Children.Values.Select(value => value.ToString()).Where(value => value.Contains("inputs.azure-", StringComparison.Ordinal)));
                continue;
            }

            foreach (var value in Scalars(step).Where(value => value.Contains("inputs.azure-", StringComparison.Ordinal)))
            {
                Assert.True(ReferenceEquals(step, check), $"'{Scalar(step, "name")}' reads {value}");
                Assert.Equal(Regex.Count(value, @"inputs\.azure-"), Regex.Count(value, @"inputs\.azure-[a-z-]+ == ''"));
            }
        }

        Assert.Equal(AzureSecrets.Select(secret => $"${{{{ inputs.{secret.Input} }}}}").Order(StringComparer.Ordinal), given.Order(StringComparer.Ordinal));
        var missing = Scalar(Mapping(check, "env"), "MISSING")!;
        Assert.All(AzureSecrets, secret => Assert.Contains($"inputs.{secret.Input} == '' && '{secret.Input}'", missing, StringComparison.Ordinal));

        var release = Read(Release);
        foreach (var (_, secret) in AzureSecrets)
        {
            Assert.Equal(1, Regex.Count(release, $@"\bsecrets\.{secret}\b"));
        }

        var runs = Mapping(Parse(release), "jobs").Children.Values.Cast<YamlMappingNode>().SelectMany(JobSteps).Select(step => Scalar(step, "run") ?? string.Empty);
        Assert.All(runs, run => Assert.DoesNotContain("AZURE_", run, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// No step of the release, in its workflow or in the package action, prints its environment or traces its commands,
    /// in bash or in PowerShell, and none reads a variable named for Azure: the sign-in leaves its own in the job's
    /// environment, and a step that printed it would print what the sign-in holds.
    /// </summary>
    [Fact]
    public void No_step_prints_its_environment_or_reads_an_azure_variable()
    {
        var steps = ActionSteps().Concat(Mapping(Parse(Read(Release)), "jobs").Children.Values.Cast<YamlMappingNode>().SelectMany(JobSteps)).ToList();
        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            var name = Scalar(step, "name") ?? Scalar(step, "uses");
            var lines = (Scalar(step, "run") ?? string.Empty).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();
            Assert.DoesNotContain(lines, line => PrintsTheEnvironment().IsMatch(line) || PowershellPrintsTheEnvironment().IsMatch(line));
            Assert.All(lines, line => Assert.True(!line.Contains("$env:AZURE", StringComparison.OrdinalIgnoreCase), $"'{name}' reads an Azure variable: {line}"));
            if (Node(step, "env") is YamlMappingNode env)
            {
                Assert.DoesNotContain(env.Children.Keys.Select(key => key.ToString()), key => key.StartsWith("AZURE", StringComparison.OrdinalIgnoreCase));
            }
        }

        Assert.DoesNotContain("set -x", Read(Action) + Read(Release), StringComparison.Ordinal);
    }

    /// <summary>A PowerShell line that prints every variable of its environment, or traces its commands.</summary>
    [GeneratedRegex(@"(?i)(^|[\s;|(&])(Get-ChildItem|gci|dir|ls|Get-Item|gi)\s+(-(Literal)?Path\s+)?['""]?env:(\*|\\|/)?['""]?\s*($|[;|)])|\[(System\.)?Environment\]::GetEnvironmentVariables\(|Set-PSDebug\s+-Trace\s+[12]")]
    private static partial Regex PowershellPrintsTheEnvironment();

    /// <summary>
    /// The PowerShell lines that print a step's environment, and the lines that only read one variable of it.
    /// </summary>
    [Theory]
    [InlineData("Get-ChildItem env:", true)]
    [InlineData("Get-ChildItem Env: | Sort-Object Name", true)]
    [InlineData("gci env:", true)]
    [InlineData("dir env:", true)]
    [InlineData("ls env:\\", true)]
    [InlineData("Get-Item env:*", true)]
    [InlineData("Get-ChildItem -Path env:", true)]
    [InlineData("Get-ChildItem -LiteralPath 'env:'", true)]
    [InlineData("[Environment]::GetEnvironmentVariables()", true)]
    [InlineData("[System.Environment]::GetEnvironmentVariables() | Out-String", true)]
    [InlineData("Set-PSDebug -Trace 1", true)]
    [InlineData("$exe = $env:EXECUTABLE", false)]
    [InlineData("Get-Item env:RUNNER_TEMP", false)]
    [InlineData("Get-ChildItem -LiteralPath $unpacked -Recurse -File", false)]
    [InlineData("\"file=$path\" >> $env:GITHUB_OUTPUT", false)]
    [InlineData("Set-PSDebug -Off", false)]
    public void A_powershell_line_that_prints_the_environment_is_told_apart(string line, bool prints) =>
        Assert.Equal(prints, PowershellPrintsTheEnvironment().IsMatch(line));

    /// <summary>
    /// The check before the sign-in, run as GitHub runs a pwsh step: handed the names of the inputs that are empty, it
    /// names them in one error line and fails; handed none, it gives the signing step the executable's absolute path,
    /// which the signing action requires.
    /// </summary>
    [Theory]
    [InlineData("two empty", "azure-client-id   azure-signing-profile ", 1)]
    [InlineData("none empty", "    ", 0)]
    public void The_check_before_the_sign_in_names_the_empty_inputs_and_nothing_else(string row, string missing, int exit)
    {
        using var tree = new TempTree();
        var executable = Path.Combine(tree.Root, "artifacts", "publish", "win-x64", "jason.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "made up");
        var output = Path.Combine(tree.Root, "github-output");
        File.WriteAllText(output, string.Empty);
        var script = Path.Combine(tree.Root, "step.ps1");
        File.WriteAllText(script, Scalar(Assert.Single(ActionSteps(), step => Scalar(step, "id") == "signable"), "run"));

        var ran = Script(new Dictionary<string, string> { ["MISSING"] = missing, ["EXECUTABLE"] = executable, ["GITHUB_OUTPUT"] = output }, script);

        Assert.True(ran.Exit == exit, $"{row}: exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        var written = File.ReadAllText(output);
        if (exit == 0)
        {
            Assert.Equal($"file={Path.GetFullPath(executable)}", written.Trim());
            Assert.Equal(string.Empty, ran.Stdout.Trim());
        }
        else
        {
            Assert.Equal(string.Empty, written);
            Assert.Equal(
                "::error title=Signing::Signing was asked for without every Azure input; empty: azure-client-id, azure-signing-profile.",
                ran.Stdout.Trim());
        }
    }

    /// <summary>
    /// The archive is what a person downloads, so its executable is the one whose signature counts: unpacked in the job
    /// and verified by the same script, rather than trusted to be the file the action signed. The smoke test runs the
    /// file the action signed. And after it, a step lists who signed each native library the bundle unpacked, by file
    /// name, status and signer alone: a reading that decides whether those libraries need signing too, which never fails
    /// the job.
    /// </summary>
    [Fact]
    public void The_archived_windows_executable_is_verified_and_the_smoke_test_runs_the_signed_file()
    {
        var steps = JobSteps(WindowsJob()).ToList();
        var package = Assert.Single(steps, step => Scalar(step, "uses") == "./.github/actions/package");
        var archived = WindowsStep(ArchivedWindows);
        var smoke = Assert.Single(steps, step => Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true);
        var libraries = WindowsStep(WindowsLibraries);
        Assert.True(steps.IndexOf(package) < steps.IndexOf(archived), "The archive is verified before it is made.");
        Assert.True(steps.IndexOf(archived) < steps.IndexOf(smoke), "The smoke test runs before the archived copy is verified.");
        Assert.True(steps.IndexOf(smoke) < steps.IndexOf(libraries), "The native libraries are read before the smoke test unpacks them.");

        Assert.Null(Node(archived, "if"));
        Assert.Null(Node(archived, "continue-on-error"));
        Assert.Equal("pwsh", Scalar(archived, "shell"));
        Assert.Equal("${{ steps.package.outputs.archive }}", Scalar(Mapping(archived, "env"), "ARCHIVE"));
        // The copy the script reads is the one unpacked from the archive, never the publish directory's.
        var run = Scalar(archived, "run")!.TrimEnd().Split('\n').Select(line => line.Trim()).ToList();
        Assert.Equal("$unpacked = Join-Path $env:RUNNER_TEMP 'archived'", run[0]);
        Assert.Contains("Expand-Archive -LiteralPath $env:ARCHIVE -DestinationPath $unpacked", run);
        Assert.Equal("./" + WindowsSignature + " -Executable (Join-Path $unpacked 'jason.exe') -Publisher " + Publisher, run[^1]);

        Assert.Equal("${{ steps.package.outputs.executable }}", Scalar(Mapping(smoke, "env"), "EXECUTABLE"));

        var listed = Scalar(libraries, "run")!;
        Assert.Contains("'.net\\jason'", listed, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", listed, StringComparison.Ordinal);
        Assert.Contains("catch", listed, StringComparison.Ordinal);
        Assert.DoesNotContain(
            listed.Split('\n'),
            line => line.Contains("Write-Host", StringComparison.Ordinal) && (line.Contains("FullName", StringComparison.Ordinal) || line.Contains("$unpacked", StringComparison.Ordinal)));
        Assert.DoesNotContain("Exception.Message", listed, StringComparison.Ordinal);
        Assert.EndsWith("exit 0", listed.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every job that holds a secret or may write runs in the GitHub environment `signing`, which admits only release tags
    /// and release branches: the Windows job, whose OIDC token the federated credential accepts from that environment
    /// alone; the macOS job, which holds the Apple identity; the release job, which may write a release; and the tap's
    /// job, which holds the tap's token. No other job runs in an environment. The rule reads every job of the workflow, so
    /// a job added later that names a secret or asks for a write without the environment is refused. Held against
    /// release.yml as it is and against copies of it: the environment gone from each of the four jobs, a new job that
    /// reads the tap's token without it, the plugin's job in it, and on the Windows job another name, the same name as a
    /// mapping, and the name as an expression. Each copy is refused, and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("gone from the macos job", true)]
    [InlineData("gone from the windows job", true)]
    [InlineData("gone from the release job", true)]
    [InlineData("gone from the tap job", true)]
    [InlineData("a new job that reads the tap's token", true)]
    [InlineData("on the plugin job", true)]
    [InlineData("another name on the windows job", true)]
    [InlineData("a mapping on the windows job", true)]
    [InlineData("an expression on the windows job", true)]
    public void Every_job_that_holds_a_secret_or_may_write_runs_in_the_signing_environment_and_no_other_does(string copy, bool refused)
    {
        var release = Parse(Read(Release));
        var jobs = Mapping(release, "jobs");
        YamlMappingNode Job(string name) => Mapping(jobs, name);
        void Set(string job, YamlNode? environment)
        {
            Job(job).Children.Remove(new YamlScalarNode("environment"));
            if (environment is not null)
            {
                Job(job).Children.Add(new YamlScalarNode("environment"), environment);
            }
        }

        switch (copy)
        {
            case "as it is":
                break;
            case "gone from the macos job":
            case "gone from the windows job":
            case "gone from the release job":
            case "gone from the tap job":
                Set(copy.Split(' ')[3], null);
                break;
            case "a new job that reads the tap's token":
                jobs.Children.Add(
                    new YamlScalarNode("extra"),
                    Parse($"runs-on: ubuntu-latest\nsteps:\n  - run: echo asked\n    env:\n      TOKEN: {TapToken}\n"));
                break;
            case "on the plugin job":
                Set("plugin", new YamlScalarNode(SigningEnvironment));
                break;
            case "another name on the windows job":
                Set("windows", new YamlScalarNode("release"));
                break;
            case "a mapping on the windows job":
                Set("windows", Parse("name: " + SigningEnvironment + "\n"));
                break;
            case "an expression on the windows job":
                Set("windows", new YamlScalarNode("${{ 'signing' }}"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(copy));
        }

        var problems = EnvironmentsOutOfPlace(release);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Each job that runs in an environment it should not, or holds a secret or a write outside its own, as a line saying
    /// which; empty when exactly the jobs that name the secrets context or ask for a write name the environment, as the
    /// plain word.
    /// </summary>
    private static List<string> EnvironmentsOutOfPlace(YamlMappingNode release)
    {
        var problems = new List<string>();
        foreach (var (name, job) in Mapping(release, "jobs").Children.Select(pair => (pair.Key.ToString(), (YamlMappingNode)pair.Value)))
        {
            var environment = Node(job, "environment");
            var holds = ScalarNodes(job).Any(scalar => scalar.Value is { } value && NamesSecrets().IsMatch(value));
            var writes = Node(job, "permissions") switch
            {
                YamlScalarNode scalar => scalar.Value == "write-all",
                YamlMappingNode permissions => permissions.Children.Values.Any(value => value is YamlScalarNode { Value: "write" }),
                _ => false,
            };
            if ((holds || writes) && environment is not YamlScalarNode { Value: SigningEnvironment })
            {
                problems.Add($"the {name} job {(holds ? "holds a secret" : "may write")} outside the {SigningEnvironment} environment named as that word alone");
            }
            else if (!holds && !writes && environment is not null)
            {
                problems.Add($"the {name} job runs in an environment, though it holds no secret and may write nothing");
            }
        }

        return problems;
    }

    /// <summary>
    /// The two actions that hold the Azure identity are each used once, at the full commit of the release whose tag
    /// stands beside it in a comment, so that the tag moving cannot change what runs with the identity; every other
    /// action, in every workflow and action of the repository, keeps its major tag, or is the repository's own. Held
    /// against the files as they are and against copies that loosen a pin: the sign-in back on its major tag, a shortened
    /// commit, no tag beside the commit, another release's tag beside it, the sign-in used a second time, and another
    /// action pinned to a commit. Each copy is refused, and the files themselves are not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("the sign-in on its major tag", true)]
    [InlineData("a shortened commit", true)]
    [InlineData("no tag beside the commit", true)]
    [InlineData("another release's tag beside it", true)]
    [InlineData("the sign-in a second time", true)]
    [InlineData("another action on a commit", true)]
    public void The_azure_actions_are_pinned_to_a_commit_and_every_other_action_to_its_major_tag(string copy, bool refused)
    {
        var root = Path.Combine(RepositoryRoot(), ".github");
        var files = Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/'),
            path => File.ReadAllText(path).ReplaceLineEndings("\n"),
            StringComparer.Ordinal);
        const string ActionFile = ".github/actions/package/action.yml";
        const string ReleaseFile = ".github/workflows/release.yml";
        var (login, commit, tag) = AzurePins[0];
        var pinned = $"{login}@{commit} # {tag}";

        string Moved(string file, string from, string to)
        {
            Assert.Equal(1, Regex.Count(files[file], Regex.Escape(from)));
            return files[file].Replace(from, to, StringComparison.Ordinal);
        }

        switch (copy)
        {
            case "as it is":
                break;
            case "the sign-in on its major tag":
                files[ActionFile] = Moved(ActionFile, pinned, $"{login}@v3");
                break;
            case "a shortened commit":
                files[ActionFile] = Moved(ActionFile, pinned, $"{login}@{commit[..12]} # {tag}");
                break;
            case "no tag beside the commit":
                files[ActionFile] = Moved(ActionFile, pinned, $"{login}@{commit}");
                break;
            case "another release's tag beside it":
                files[ActionFile] = Moved(ActionFile, pinned, $"{login}@{commit} # v3.0.2");
                break;
            case "the sign-in a second time":
                files[ActionFile] = Moved(ActionFile, pinned, pinned + "\n    - uses: " + pinned);
                break;
            case "another action on a commit":
                files[ReleaseFile] = new Regex(Regex.Escape("actions/checkout@v7")).Replace(
                    files[ReleaseFile], "actions/checkout@0123456789abcdef0123456789abcdef01234567 # v7.0.0", 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(copy));
        }

        var problems = PinsOutOfPlace(files);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>Each <c>uses:</c> line that does not keep its convention, and each Azure action not used exactly once.</summary>
    private static List<string> PinsOutOfPlace(IReadOnlyDictionary<string, string> files)
    {
        var problems = new List<string>();
        var sightings = AzurePins.ToDictionary(pin => pin.Action, _ => 0, StringComparer.Ordinal);
        foreach (var (file, text) in files)
        {
            foreach (Match line in UsesLine().Matches(text))
            {
                var reference = line.Groups["reference"].Value;
                var comment = line.Groups["comment"].Value.Trim();
                var at = Array.FindIndex(AzurePins, pin => reference.StartsWith(pin.Action + "@", StringComparison.Ordinal));
                if (at >= 0)
                {
                    var (action, commit, tag) = AzurePins[at];
                    sightings[action]++;
                    if (reference != $"{action}@{commit}" || comment != tag)
                    {
                        problems.Add($"{file}: '{line.Value.Trim()}' is not {action}@{commit} # {tag}");
                    }
                }
                else if (comment.Length > 0 || !(reference.StartsWith("./", StringComparison.Ordinal) || MajorTag().IsMatch(reference)))
                {
                    problems.Add($"{file}: '{line.Value.Trim()}' does not keep its major tag");
                }
            }
        }

        problems.AddRange(sightings.Where(pair => pair.Value != 1).Select(pair => $"{pair.Key} is used {pair.Value} times, where it is used once"));
        return problems;
    }

    /// <summary>A step's <c>uses:</c> line: what it names, and the comment after it, if any.</summary>
    [GeneratedRegex(@"^[ \t]*(?:-[ \t]+)?uses:[ \t]*(?<reference>\S+)(?:[ \t]+#(?<comment>[^\n]*))?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    /// <summary>An action of another repository named by its major tag alone.</summary>
    [GeneratedRegex(@"^[\w.-]+/[\w.-]+@v\d+$")]
    private static partial Regex MajorTag();

    /// <summary>
    /// Every step of the Windows job runs where the identity is held, so the job's steps are these, in this order, and a
    /// step added there is a change this row makes visible.
    /// </summary>
    [Fact]
    public void The_windows_job_runs_these_steps_and_no_other() =>
        Assert.Equal(
            ["actions/checkout@v7", "./.github/actions/setup-dotnet", "Package", ArchivedWindows, "Smoke-test the published executable", WindowsLibraries],
            JobSteps(WindowsJob()).Select(step => Scalar(step, "name") ?? Scalar(step, "uses")));

    /// <summary>
    /// A job that continues on error satisfies the <c>needs</c> of the job that publishes even when a step of it failed,
    /// so a signature that did not verify, or a smoke test that did not pass, would still be released. Neither signing
    /// job, nor any step of either, continues on error.
    /// </summary>
    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    public void A_signing_job_never_continues_on_error(string name)
    {
        var job = Mapping(Mapping(Parse(Read(Release)), "jobs"), name);
        Assert.Null(Node(job, "continue-on-error"));
        Assert.All(JobSteps(job), step => Assert.Null(Node(step, "continue-on-error")));
    }

    /// <summary>
    /// The reading of the native libraries, run as GitHub runs a pwsh step, over an extraction directory of the test's
    /// own: it ends 0 whether the directory holds libraries or is not there, and what it writes names a library by its
    /// file name and says where it looked as <c>.net\jason</c>, never by a path under the profile.
    /// </summary>
    [Theory]
    [InlineData("libraries there")]
    [InlineData("no directory")]
    public void The_reading_of_the_native_libraries_never_fails_and_writes_no_path(string row)
    {
        using var tree = new TempTree();
        if (row == "libraries there")
        {
            var unpacked = Path.Combine(tree.Root, ".net", "jason", "made-up-bundle-id");
            Directory.CreateDirectory(unpacked);
            File.WriteAllText(Path.Combine(unpacked, "made_up_native.dll"), "made up");
        }

        // The step's own file lives in a tree of its own, so that a path the harness writes is never the one looked for.
        using var scripts = new TempTree();
        var step = Path.Combine(scripts.Root, "step.ps1");
        File.WriteAllText(step, Scalar(WindowsStep(WindowsLibraries), "run"));

        var ran = Script(new Dictionary<string, string> { ["TEMP"] = tree.Root }, step);

        Assert.True(ran.Exit == 0, $"{row}: exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.DoesNotContain(tree.Root, ran.Stdout + ran.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(tree.Root), ran.Stdout + ran.Stderr, StringComparison.OrdinalIgnoreCase);
        if (row == "libraries there" && OperatingSystem.IsWindows())
        {
            Assert.Contains("1 native libraries unpacked under .net\\jason", ran.Stdout, StringComparison.Ordinal);
            // A made-up file is no image Windows can read, so its status is whatever Windows calls that; the line's
            // shape is what the step promises.
            Assert.Matches(@"(?m)^made_up_native\.dll: \w+, signed by nobody\r?$", ran.Stdout);
        }
        else
        {
            Assert.Contains("::warning title=Native libraries::", ran.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The script that verifies a Windows signature, run against a stand-in for signtool that records how it was asked
    /// and answers as this row says, over a file Microsoft signed and timestamped as the publisher <c>.NET</c>, over a
    /// copy of it with one byte changed and over a file nobody signed. It passes a file its named publisher signed and
    /// timestamped that signtool accepts, and refuses, saying so, a file signtool refuses, an unsigned file, a changed
    /// file, a file Windows reads as validly signed but without a timestamp, a valid timestamped signature by any
    /// publisher but the one named — a name that only contains it, or differs from it in case, included — a call that
    /// names no publisher, a signtool that is not there and a runner without the Windows SDK's signtool; signtool is
    /// asked with the Authenticode policy and verbosely, and only by the script.
    /// </summary>
    [Theory]
    [InlineData("signed, signtool clean", 0)]
    [InlineData("signed, signtool refuses", 1)]
    [InlineData("unsigned, signtool clean", 1)]
    [InlineData("tampered, signtool clean", 1)]
    [InlineData("signed without a timestamp, signtool clean", 1)]
    [InlineData("signed by another publisher, signtool clean", 1)]
    [InlineData("signed by a publisher whose name only contains the one named, signtool clean", 1)]
    [InlineData("signed by the publisher named in another case, signtool clean", 1)]
    [InlineData("no publisher named", 1)]
    [InlineData("no signtool", 1)]
    [InlineData("no Windows SDK", 1)]
    public void The_signature_script_passes_a_file_its_publisher_signed_and_timestamped_alone(string row, int exit)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Authenticode is read on Windows; CI's Windows leg runs this row.");

        using var tree = new TempTree();
        var signed = typeof(object).Assembly.Location;
        var unsigned = Path.Combine(tree.Root, "jason.exe");
        File.WriteAllText(unsigned, "made up");
        // One byte in the middle of the file, far from the signature at its end, so the signature is still there and its
        // timestamp with it, and only the file's hash no longer matches what was signed.
        var tampered = Path.Combine(tree.Root, "tampered.dll");
        var bytes = File.ReadAllBytes(signed);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(tampered, bytes);
        var calls = Path.Combine(tree.Root, "calls.txt");
        var signtool = Path.Combine(tree.Root, "signtool.cmd");
        File.WriteAllText(signtool, $"@echo %*> \"{calls}\"\r\n@exit /b {(row == "signed, signtool refuses" ? 1 : 0)}\r\n");

        var executable = row.StartsWith("unsigned", StringComparison.Ordinal) ? unsigned
            : row.StartsWith("tampered", StringComparison.Ordinal) ? tampered
            : signed;
        // The publisher Microsoft signs this file as is the simple name of its signer's certificate, CN=.NET.
        var publisher = row switch
        {
            "signed by another publisher, signtool clean" => "ReplyApp Inc.",
            "signed by a publisher whose name only contains the one named, signtool clean" => "NET",
            "signed by the publisher named in another case, signtool clean" => ".net",
            _ => ".NET",
        };
        string[] arguments = row switch
        {
            "no signtool" => ["-Executable", executable, "-Publisher", publisher, "-SignTool", Path.Combine(tree.Root, "missing", "signtool.exe")],
            "no Windows SDK" => ["-Executable", executable, "-Publisher", publisher],
            "no publisher named" => ["-Executable", executable, "-SignTool", signtool],
            _ => ["-Executable", executable, "-Publisher", publisher, "-SignTool", signtool],
        };
        var environment = new Dictionary<string, string> { ["ProgramFiles(x86)"] = tree.Root };
        var script = Path.Combine([RepositoryRoot(), .. WindowsSignature.Split('/')]);

        // No file Microsoft ships is signed without a timestamp, so the row reads a signed one through a stand-in for
        // Windows' own reading that answers Valid and leaves the timestamp out; a function shadows the cmdlet of its name
        // in the script it calls.
        if (row.StartsWith("signed without a timestamp", StringComparison.Ordinal))
        {
            var shadowed = Path.Combine(tree.Root, "untimestamped.ps1");
            File.WriteAllText(shadowed, $$"""
                function Get-AuthenticodeSignature([string] $LiteralPath) {
                    $real = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $LiteralPath
                    [pscustomobject]@{ Status = 'Valid'; SignerCertificate = $real.SignerCertificate; TimeStamperCertificate = $null }
                }
                & '{{script.Replace("'", "''", StringComparison.Ordinal)}}' @args
                exit $LASTEXITCODE
                """);
            script = shadowed;
        }

        var ran = Script(environment, script, arguments);

        Assert.True(ran.Exit == exit, $"{row}: exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        if (row.EndsWith("signtool clean", StringComparison.Ordinal) || row == "signed, signtool refuses")
        {
            Assert.Equal($"verify /pa /v {executable}", File.ReadAllText(calls).Trim().Replace("\"", string.Empty, StringComparison.Ordinal));
        }

        if (exit == 0)
        {
            Assert.Contains("is signed by CN=", ran.Stdout, StringComparison.Ordinal);
            Assert.Contains("timestamped by CN=", ran.Stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, ran.Stderr.Trim());
        }
        else
        {
            Assert.NotEqual(string.Empty, ran.Stderr.Trim());
        }

        if (row.StartsWith("signed without a timestamp", StringComparison.Ordinal))
        {
            Assert.Contains("without a timestamp", ran.Stderr, StringComparison.Ordinal);
        }

        if (row.StartsWith("tampered", StringComparison.Ordinal))
        {
            Assert.Contains("does not carry a valid Authenticode signature: HashMismatch", ran.Stderr, StringComparison.Ordinal);
        }

        if (row.StartsWith("signed by", StringComparison.Ordinal))
        {
            Assert.Contains($"is signed by '.NET', not by '{publisher}'", ran.Stderr, StringComparison.Ordinal);
        }

        if (row == "no publisher named")
        {
            Assert.Contains("Publisher", ran.Stderr, StringComparison.Ordinal);
        }
    }
}
