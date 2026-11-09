using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.ReplyCliStub;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.Tests.Plugins.Invocation;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The first marketplace package, held to the mechanism every community package is held to: it loads from the
/// same loader, it is granted the same way, and what it declares is exactly what it uses. Nothing about it is
/// privileged, and these tests are how that stays true.
/// </summary>
public class ReplyPackageTests
{
    /// <summary>The list of the package's versions and the digest each was recorded with, beside the package and outside it.</summary>
    private const string DigestList = "plugins/reply.digests";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.R5, "the shipped package's grants")]
    public async Task The_official_package_loads_and_asks_for_exactly_what_it_uses()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        var plugin = Assert.Single(registry.Plugins, listed => listed.Id == ReplyPlugins.PluginId);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Empty(plugin.Problems);
        Assert.Equal(PluginKind.Provider, plugin.Kind);
        Assert.Equal("0.14.7", plugin.Version);
        Assert.StartsWith("sha256:", plugin.Digest, StringComparison.Ordinal);
        Assert.Equal(
            ["campaign.activate", "campaign.create", "campaign.enroll", "campaign.get", "campaign.pause", "contact.delete", "contact.update", "conversation.classify", "conversation.close", "conversation.get", "conversation.list", "conversation.reopen", "conversation.reply", "engagement.summarize", "enrollment.list", "enrollment.stop", "list_membership.add", "message.cancel", "message.send", "message.status", "optout.record", "schedule.list", "sender.list", "social_invitation.send"],
            plugin.Operations.Order());

        // Declaration is not permission, and this package declares nothing it does not use: one program, no
        // environment variable it could hold a secret in, and no way to reach the provider except through it. A
        // capability that was never requested is absent rather than empty, which is a different sentence.
        var executable = Assert.Single(plugin.Capabilities.Exec!.Requested);
        Assert.Equal(ReplyPlugins.ExecutableName, executable.Name);
        Assert.Equal([ReplyPlugins.ExecutableName], plugin.Capabilities.Exec.Granted);
        Assert.Null(plugin.Capabilities.Env);
        Assert.Null(plugin.Capabilities.Http);
    }

    /// <summary>
    /// The package lists under <c>settles_by_key</c> exactly the two sends it makes under the work item's key and reads
    /// back by it, so the runtime repeats them after a lost answer, and nothing else on that word.
    /// </summary>
    [Fact]
    public void The_package_settles_the_two_sends_by_the_key_and_nothing_else() =>
        Assert.Equal(["message.send", "social_invitation.send"], ReplyPlugins.KeyedOperations.Order(StringComparer.Ordinal));

    /// <summary>
    /// The package lists a message written as a template on an email and on LinkedIn, the channels Reply fills one in on,
    /// so the claim lets such a template through to it there, and nothing else on that word.
    /// </summary>
    [Fact]
    public void The_package_writes_a_message_as_a_template_on_email_and_on_linkedin_and_nothing_else()
    {
        var templates = ReplyPlugins.Templates.ByOperation;

        Assert.Equal(["message.send"], templates.Keys);
        Assert.Equal(["email", "linkedin"], templates["message.send"]);
    }

    /// <summary>
    /// A package's version is its content. The digest of the whole package — the number <c>plugin list</c> shows, and the
    /// digest of the package a release publishes, which that release's runtime reports in <c>system.info</c>'s
    /// <c>released_plugins</c>; it is not the archive's checksum in <c>checksums.txt</c> — has one line on the list beside the
    /// package, <c>plugins/reply.digests</c>, for the version the manifest names, and that line is the last. Content changed
    /// without a new version is the digest of a version no longer being what the list says; a new version without its line is
    /// the list not yet saying what the version is; a version on the list twice is the content of a version given a second
    /// line; the version's line above a later one is the list out of order. Each failure says what to do.
    /// </summary>
    /// <remarks>
    /// A line that changes or disappears in a diff is what this test cannot see, since the list it reads is the changed list:
    /// a line already on it is refused in review, as a line of MigrationDigests.txt is.
    /// </remarks>
    [Fact]
    [Critical(Family.R1, "a package's version is its content")]
    public void The_package_is_the_digest_its_version_has_on_the_list()
    {
        var root = RepositoryTree.Root();
        var package = Path.Combine(root, "plugins", ReplyPlugins.PluginId);
        var version = YamlToJson.Convert(File.ReadAllText(Path.Combine(package, "plugin.yaml")))!["version"]!.GetValue<string>();
        var digest = PackageDigest.Compute(package).Digest;
        var line = $"{version} {digest}";

        var path = Path.Combine(root, DigestList);
        Assert.True(
            File.Exists(path),
            $"{DigestList} is missing. It records the digest of each version of the package, one line per version, beside the "
            + "package and outside it: create it with a header comment and this line, the manifest's version and the package's digest:"
            + Environment.NewLine
            + line);

        var listed = new List<(string Version, string Digest)>();
        foreach (var entry in File.ReadAllLines(path))
        {
            if (entry.Length == 0 || entry.StartsWith('#'))
            {
                continue;
            }

            Assert.True(
                IsListLine(entry),
                $"Not a line of {DigestList}: '{entry}'. A line is a bare version, X.Y.Z with no pre-release or build metadata, a "
                + "space and the package's sha256:digest.");
            var parts = entry.Split(' ');
            listed.Add((parts[0], parts[1]));
        }

        var problems = new List<string>();
        foreach (var twice in listed.GroupBy(entry => entry.Version, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"{DigestList} names version {twice.Key} {twice.Count()} times. A version has one line and a line is never changed: "
                + "delete the line that was added or edited, put the package's content under a new version in plugin.yaml, and append "
                + "the line for that version.");
        }

        var own = listed.Where(entry => entry.Version == version).ToList();
        if (!IsListLine(line))
        {
            problems.Add(
                $"plugin.yaml says version {version}, and a version on {DigestList} is a bare X.Y.Z, as the release's manifest writer "
                + "reads one: give the package a version with no pre-release or build metadata.");
        }
        else if (own.Count == 0)
        {
            problems.Add(
                $"plugin.yaml says version {version} and {DigestList} has no line for it. A version is its content, recorded once: "
                + $"append this line to the end of {DigestList}, after the edits to the package are done, since every edit changes it:"
                + Environment.NewLine
                + line);
        }
        else if (own.Count == 1)
        {
            if (own[0].Digest != digest)
            {
                problems.Add(
                    $"The package is not the content version {version} was recorded with: {DigestList} says {own[0].Digest}, the package "
                    + $"is {digest}. A version is its content, so the version changes whenever any file of plugins/{ReplyPlugins.PluginId} does: "
                    + "bump the version in plugin.yaml and write the changelog entry, then run this test again, which prints the line "
                    + $"to append to the end of {DigestList}. The line already there is left as it is.");
            }

            if (listed[^1].Version != version)
            {
                problems.Add(
                    $"The last line of {DigestList} is for version {listed[^1].Version} and plugin.yaml says version {version}. A line is "
                    + "appended at the end of the list, after the lines already there: put the line for version "
                    + $"{version} at the end of {DigestList}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A pre-release or a build's metadata is no line of the list: the release's manifest writer takes a bare X.Y.Z, so the
    /// list is read by one rule on both sides, and this test never prints a line the release would refuse.
    /// </summary>
    [Theory]
    [InlineData("0.9.1-beta.1")]
    [InlineData("0.9.1+b")]
    public void A_line_of_the_list_is_a_bare_version_as_the_release_reads_one(string version)
    {
        Assert.False(IsListLine($"{version} sha256:{new string('b', 64)}"));
    }

    /// <summary>A line of the list by the rule <c>.github/scripts/Write-Manifest.ps1</c> reads it with, character for character.</summary>
    private static bool IsListLine(string entry) =>
        Regex.IsMatch(entry, "^((0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)) (sha256:[0-9a-f]{64})$", RegexOptions.CultureInvariant);

    [Fact]
    public async Task The_program_it_declares_resolves_to_the_stand_in_and_answers_the_version_it_asks_for()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        var executable = Assert.Single(Assert.Single(registry.Plugins).Capabilities.Exec!.Requested);

        // The manifest asks for a minimum version, so the reload ran the version command on whatever it resolved.
        // That it answered, and that the answer satisfied the minimum, is why the package is valid at all.
        Assert.Equal(ReplyCliRelease.Version, executable.MinVersion);
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md"));
        Assert.Contains($"**Version {ReplyCliRelease.Version} or newer.**", readme, StringComparison.Ordinal);
        Assert.NotNull(executable.Version);
        Assert.NotNull(executable.MinVersion);
        Assert.True(Version.Parse(executable.Version) >= Version.Parse(executable.MinVersion));

        // And what answered is ours. A machine with a real CLI installed must never be the one that answered.
        ReplyPlugins.AssertInsideTheTestTree(executable.Path);
        Assert.Equal(ReplyPlugins.ExecutablePath, executable.Path);
    }

    /// <summary>
    /// Reply's endpoints are reply-cli's to know, and every request this package makes has a command made for it, so the
    /// package names no endpoint at all. One named anywhere in it would be a raw call that none of the CLI's own guards —
    /// the read before a write, the retries, the one answer for an outcome it could not see — ever reach.
    /// </summary>
    [Fact]
    public void The_package_names_no_reply_endpoint()
    {
        var named = Directory.GetFiles(ReplyPlugins.PackageSource, "*.js", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"/v3/[A-Za-z0-9_\-/{}.]*").Select(match => Path.GetFileName(file) + ": " + match.Value))
            .ToList();

        Assert.True(named.Count == 0, $"The package names a Reply endpoint: {string.Join(", ", named)}.");
    }

    [Fact]
    public async Task Declaring_no_limits_is_what_lets_the_slowest_operation_finish()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        // A package's own limit is a ceiling on the operation's budget and can only lower it. `campaign.enroll`
        // is allowed 300 s, so a smaller ceiling here would kill invocations the contract says are still running
        // — and a killed invocation is ambiguous, because the provider may already have enrolled someone.
        var plugin = Assert.Single(registry.Plugins);
        var slowest = plugin.Operations
            .Select(OperationCatalog.Find)
            .Where(contract => contract is not null)
            .MaxBy(contract => contract!.TimeoutMs);
        Assert.NotNull(slowest);
        Assert.True(
            plugin.Limits.TimeoutMs >= slowest!.TimeoutMs,
            $"the package's ceiling of {plugin.Limits.TimeoutMs} ms is below {slowest.Id}'s own budget of {slowest.TimeoutMs} ms.");
    }

    /// <summary>
    /// The memory budget counts what an invocation allocates, every answer it reads included, and an answer can be as
    /// large as the exec capture. The package keeps 256 MiB for that, as its manifest says why, and asks for memory
    /// alone: the timeout stays the installation's, which is what lets the slowest operation finish.
    /// </summary>
    [Fact]
    public async Task The_package_asks_for_its_memory_and_no_timeout_of_its_own()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        var plugin = Assert.Single(registry.Plugins);
        Assert.Equal(256, plugin.Limits.MemoryMb);
        Assert.Equal(OperationCatalog.Slowest!.TimeoutMs, plugin.Limits.TimeoutMs);
    }

    [Fact]
    [Critical(Family.R1, "the shipped package in a real host")]
    public async Task The_entry_module_is_loaded_by_a_real_child_and_answers_for_the_operation_it_was_given()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(ReplyPlugins.PluginId, "campaign.get", new JsonObject(), null, "att_01K0REPLYPACKAGE"),
            Ct);

        // The package is JavaScript, so "it loads" is not a property of the file sitting there: a child process
        // has to compile the entry module and the modules it imports, find the exported function the manifest
        // names, and hand back one outcome. An input naming no campaign at all is answered by the module the
        // entry dispatched to, in that operation's own word and before any provider is reached — which is the
        // shortest route through the whole mechanism that still proves every part of it ran.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("campaign_not_found", failed.Error.Code);
        Assert.Equal(ReplyPlugins.PluginId, result.Provenance.PluginId);
        Assert.Equal("0.14.7", result.Provenance.Version);
    }

    /// <summary>
    /// The sign-in step is the first command a person types, and in Windows PowerShell the name as written is refused
    /// by the default execution policy — so the step says what that shell takes, and what the others take.
    /// </summary>
    [Fact]
    public void The_sign_in_step_says_what_windows_powershell_takes()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var step = readme[readme.IndexOf("**2. Sign in**", StringComparison.Ordinal)..readme.IndexOf("**3. Grant the program**", StringComparison.Ordinal)];

        Assert.Contains("In Windows PowerShell, type `reply.cmd auth login`", step, StringComparison.Ordinal);
        Assert.Contains("PowerShell 7 and Command Prompt take `reply auth login` as written", step, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reply cannot say whether a repeat's effect was there already, so the package answers the plain word. The
    /// contracts allow that where the provider cannot tell, and the README says so rather than calling it a departure.
    /// </summary>
    [Fact]
    public void The_readme_says_the_contracts_allow_the_plain_answer()
    {
        var readme = Regex.Replace(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")), @"\s+", " ");

        Assert.Contains(
            "The contracts let a repeat answer `already_member`, `already_recorded` or `already_paused` where the provider can tell.",
            readme,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Three answers the contracts publish are never given", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_route_to_this_package_may_name_an_account_and_whether_to_derive_a_first_name_and_nothing_else()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        // The binding is what an operator writes, so it is closed on purpose: a route names which account to act
        // as, which team to act in, and whether a person with no first name may be imported under a derived one. A
        // key or a person to impersonate has no field here and never will.
        var binding = Assert.Single(registry.Plugins).BindingSchema;
        Assert.NotNull(binding);
        Assert.False(binding["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["derive_first_name", "profile", "team_id"], binding["properties"]!.AsObject().Select(property => property.Key).Order());
        Assert.Equal("boolean", (string?)binding["properties"]!["derive_first_name"]!["type"]);
        Assert.Null(binding["required"]);
        Assert.Empty(SchemaValidator.Validate(JsonNode.Parse("""{"derive_first_name":false}"""), binding));
        Assert.Equal("type", Assert.Single(SchemaValidator.Validate(JsonNode.Parse("""{"derive_first_name":"no"}"""), binding)).Reason);
        Assert.Equal("additional_properties", Assert.Single(SchemaValidator.Validate(JsonNode.Parse("""{"derive_first_names":true}"""), binding)).Reason);
    }

    /// <summary>
    /// The switch as an operator writes it by hand in <c>settings.json</c>: the configuration store reads <c>false</c>
    /// and the string <c>"false"</c> alike, and the route the runtime resolves carries the boolean the plugin is given.
    /// </summary>
    [Theory]
    [InlineData("false")]
    [InlineData("\"false\"")]
    public async Task A_global_route_in_settings_can_turn_the_derivation_off(string written)
    {
        await using var api = await ReplyPlugins.StartAsync(
            Ct,
            prepare: paths => TestRoutes.WriteGlobal(
                paths,
                new JsonObject
                {
                    ["Default"] = new JsonObject
                    {
                        ["Plugin"] = ReplyPlugins.PluginId,
                        ["Binding"] = new JsonObject { ["derive_first_name"] = JsonNode.Parse(written) },
                    },
                }.ToJsonString()));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

        var resolved = await api.PostOkAsync<RouteResolutionDto>(
            Operations.RouteResolve, new { campaign_id = campaign, operation = "list_membership.add" }, Ct);

        Assert.True(resolved.Usable, string.Join("; ", resolved.Problems.Select(problem => problem.Code + " " + problem.Message)));
        Assert.Equal(JsonValueKind.False, resolved.Binding!["derive_first_name"]!.GetValueKind());
    }

    /// <summary>
    /// A switch spelled as anything but a boolean is refused where an operator sets it, and so is never read as on.
    /// </summary>
    [Fact]
    public async Task A_switch_spelled_as_anything_but_a_boolean_is_refused_as_the_route_is_set()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, plugin = ReplyPlugins.PluginId, binding = new { derive_first_name = "no" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(RouteProblemCodes.BindingInvalid, Assert.Single(error.Details!).Code);
    }

    /// <summary>
    /// Every field of this package's binding is optional, so a campaign route with no binding at all is a valid
    /// route: it is written, frozen again by a reload, and resolves usable with no binding and no identity. An empty
    /// binding is a route of its own, and a field the schema does not declare is still refused.
    /// </summary>
    [Fact]
    public async Task A_campaign_route_to_this_package_needs_no_binding()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

        await TestRoutes.SetCampaignAsync(api, campaign, operation: null, ReplyPlugins.PluginId, binding: null, Ct);
        var written = await ResolveAsync(api, campaign);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        var reloaded = await ResolveAsync(api, campaign);

        foreach (var resolved in new[] { written, reloaded })
        {
            Assert.True(resolved.Usable, string.Join("; ", resolved.Problems.Select(problem => problem.Code + " " + problem.Message)));
            Assert.Equal(RouteScope.CampaignDefault, resolved.Scope);
            Assert.True(resolved.Binding is null, $"a route written with no binding resolved with {resolved.Binding?.ToJsonString()}");
            Assert.True(resolved.BindingIdentity is null, $"a route with no binding resolved with the identity {resolved.BindingIdentity}");
        }

        await TestRoutes.SetCampaignAsync(api, campaign, operation: null, ReplyPlugins.PluginId, [], Ct);
        var empty = await ResolveAsync(api, campaign);

        Assert.True(empty.Usable, string.Join("; ", empty.Problems.Select(problem => problem.Code + " " + problem.Message)));
        Assert.True(empty.Binding is { Count: 0 }, $"a route written with an empty binding resolved with {empty.Binding?.ToJsonString() ?? "none"}");
        Assert.NotNull(empty.BindingIdentity);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, plugin = ReplyPlugins.PluginId, binding = new { profiles = "outbound" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(RouteProblemCodes.BindingInvalid, Assert.Single(error.Details!).Code);
    }

    /// <summary>
    /// The same in <c>settings.json</c>: a global route naming this package and no <c>Binding</c> is activated by the
    /// reload that freezes it, rather than refusing the whole reload.
    /// </summary>
    [Fact]
    public async Task A_global_route_to_this_package_needs_no_binding()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(ReplyPlugins.PluginId), Ct);

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        var resolved = await ResolveAsync(api, campaign);

        Assert.True(reloaded.Activated, $"the reload was not activated: {string.Join("; ", reloaded.LastReload?.Routes.Select(route => route.Code + " " + route.Message) ?? [])}");
        Assert.True(resolved.Usable, string.Join("; ", resolved.Problems.Select(problem => problem.Code + " " + problem.Message)));
        Assert.Equal(RouteScope.GlobalDefault, resolved.Scope);
        Assert.True(resolved.Binding is null, $"a global route written with no binding resolved with {resolved.Binding?.ToJsonString()}");
    }

    private static Task<RouteResolutionDto> ResolveAsync(RuntimeApiFixture api, string campaign) =>
        api.PostOkAsync<RouteResolutionDto>(
            Operations.RouteResolve, new { campaign_id = campaign, operation = "list_membership.add" }, Ct);

    /// <summary>
    /// The schedule's flag is named wherever the package lists what it passes the CLI: the manifest's account of the
    /// release it needs, the README's paragraph on that release, and the command table's rows for an email and for the
    /// three LinkedIn messages.
    /// </summary>
    [Fact]
    public void The_manifest_and_the_readme_name_the_schedule_flag_among_the_cli_s()
    {
        var manifest = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "plugin.yaml")).Replace("#", " ", StringComparison.Ordinal));
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.Contains("the sending schedule a send is timed by (`--schedule-id`)", manifest, StringComparison.Ordinal);
        Assert.Contains("are timed by a sending schedule (`--schedule-id`)", readme, StringComparison.Ordinal);
        Assert.Contains("| `contact send-email --contact-id --email-account-id --idempotency-key [--send-after] [--schedule-id] --message -` |", readme, StringComparison.Ordinal);
        Assert.Contains(
            "| `contact linkedin-message`, `contact linkedin-inmail`, `contact linkedin-voice`, each `--contact-id --linkedin-account-id --idempotency-key [--send-after] [--schedule-id] --message -` |",
            readme,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reply's direct-send routes are on every account, so the README's row for the read of a direct send names no read
    /// before a send: not of <c>email-1</c>, not as a guard of the routes, the key or a schedule. It names the two reads
    /// left, of the send an earlier attempt returned and of the send <c>message.status</c> answers from. A LinkedIn route
    /// that refuses a schedule at 400 naming <c>scheduleId</c> keeps its own row.
    /// </summary>
    [Fact]
    public void The_readme_s_row_for_the_direct_send_read_names_no_read_before_a_send()
    {
        var readme = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "README.md"));
        var row = Assert.Single(readme, line => line.StartsWith("| `direct-send get <reference>` | read |", StringComparison.Ordinal));

        Assert.All(
            new[] { "email-1", "guards", "bridge the days", "keeps direct sends", "a 404 with no code of Reply's own says it does not" },
            word => Assert.DoesNotContain(word, row, StringComparison.Ordinal));
        Assert.Contains("First on a repeat, inside the window and past it, of the send an earlier attempt", row, StringComparison.Ordinal);
        Assert.Contains("And of the send the runtime names under `send.reference`: what `message.status` answers from.", row, StringComparison.Ordinal);
        Assert.Contains(readme, line => line.Contains("naming the field `scheduleId`", StringComparison.Ordinal) && line.Contains("| `capability_not_offered` | permanent |", StringComparison.Ordinal));
    }

    /// <summary>
    /// The package reads one member of the provider's reserved object, on <c>message.send</c> alone, and ignores it
    /// everywhere else; the README says so where it says what the package does, and the section of the send states the
    /// shape it reads, the sentence a refusal of another says, what a schedule and a moment do together, the two ways
    /// Reply answers an email timed by a schedule, and what the package does not read first.
    /// </summary>
    [Fact]
    public void The_readme_says_which_operation_reads_the_provider_object_and_the_shape_it_reads()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var section = Flat(readme[readme.IndexOf("## What it does", StringComparison.Ordinal)..readme.IndexOf("## What you need", StringComparison.Ordinal)]);
        var send = Flat(readme[readme.IndexOf("### `message.send`", StringComparison.Ordinal)..readme.IndexOf("## What it cannot do", StringComparison.Ordinal)]);

        Assert.Contains(
            "This package reads the reserved `provider` object a work item may carry beside an operation's arguments on `message.send` alone, where it names the sending schedule a message is timed by ([`message.send`](#messagesend)). Every other operation ignores it: it reads none of it, passes none of it to the CLI, and runs the same with it or without it.",
            section,
            StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "```json { \"schedule_id\": \"4021\" } ```",
                "> " + ProviderShape,
                "Reply holds the email for the schedule's next opening, at or after `send_after` where a moment still ahead is named beside it.",
                "Reply answers such an email in one of two ways: one whose schedule is open now, or opens within ten minutes, is `queued`, with the moment under `send_after`; one whose schedule opens later is `waitingForSchedule`, with only an estimate of the moment, `estimated_send_at`, null where the schedule does not open within 30 days. Either way the answer is `accepted` with `cancelable` true, and `accepted_for` is absent: the email goes when the schedule lets it, and an estimate is no moment Reply holds it for, on the send or on `message.status`'s read of it.",
                "The package does not read `reply schedule list` before the send",
                "A LinkedIn message, InMail or voice message names a schedule the same way, and it goes on the format's own command as `--schedule-id`. Reply holds the message for the schedule's next opening",
            },
            word => Assert.Contains(word, send, StringComparison.Ordinal));
    }

    /// <summary>The sentence every refusal of the provider's object on <c>message.send</c> says, as the error table writes it.</summary>
    private const string ProviderShape =
        "`args.provider` is read on message.send as `{\"schedule_id\": \"<id>\"}` and nothing else, the id of a Reply sending schedule written as a string of its digits; the object given is not that shape.";

    /// <summary>
    /// The rule a person reads before they route a campaign to Reply: how a first name is derived, the switch that
    /// forbids it, and that an opt-out derives whatever the switch says. It lives where the binding is described.
    /// </summary>
    [Fact]
    public void The_readme_states_the_first_name_rule_before_a_campaign_is_routed()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var section = Flat(readme[readme.IndexOf("## The account a route names", StringComparison.Ordinal)..readme.IndexOf("### What does not reach it", StringComparison.Ordinal)]);

        Assert.Contains("| `derive_first_name` |", section, StringComparison.Ordinal);
        Assert.Contains("derived from their last name", section, StringComparison.Ordinal);
        Assert.Contains("the text of their address before the `@`", section, StringComparison.Ordinal);
        Assert.Contains("the route forbids deriving a first name", section, StringComparison.Ordinal);
        Assert.Contains("a first name could not be derived", section, StringComparison.Ordinal);
        Assert.Contains("The switch does not apply to an opt-out", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// How a person is found at Reply is the contact proxy rule as this provider performs it, and an operator reads it in
    /// one place: the exact lookup, what a person Reply holds is named by, which operations import, and that a LinkedIn
    /// URL identifies a person by its lowercased slug.
    /// </summary>
    [Fact]
    public void The_readme_says_how_a_contact_is_found_at_reply()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("## Contacts at Reply", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section on contacts at Reply.");
        var section = Flat(readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)]);

        Assert.All(
            new[]
            {
                "the contact proxy rule", "`reply contact find`",
                "LinkedIn URL identifies a person by the profile's name after `/in/`, decoded once, composed and lowercased",
                "`host.channelKey`",
                "A person Reply holds is named by its identifier", "Only `find_or_create` imports.", "`contact_not_unique`",
                "Fields and lists travel with the operation that needs them.",
            },
            words => Assert.Contains(words, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// Jason always asks Reply for a LinkedIn profile by its key, the profile's name lowercased and in Unicode, and whether
    /// Reply's lookup ignores the name's case is not known. What an operator is owed is that limit, why the name is in
    /// Unicode, and what follows from it, where the README says what the package cannot do and in the release that
    /// brought the slug: a person Reply holds under a mixed-case profile link is not found, a second contact is created,
    /// and the opt-out lands on that one.
    /// </summary>
    [Fact]
    public void The_readme_and_the_changelog_say_a_mixed_case_profile_link_may_not_be_found()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("## What it cannot do", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section on what the package cannot do.");
        var section = Flat(readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)]);
        var entry = Flat(ChangelogSection(20));

        Assert.All(
            new[]
            {
                "Jason always asks Reply for a LinkedIn profile by its key: the profile's name, lowercased, in Unicode.",
                "Reply's own key decodes percent-escapes and stores the name in Unicode",
                "Whether Reply's lookup ignores the name's case is not verified.",
                "a person Reply holds under a mixed-case profile link is not found", "creates a second contact",
                "`optout.record` lands on that one",
            },
            words => Assert.Contains(words, section, StringComparison.Ordinal));
        Assert.DoesNotContain("not checked against a live account", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "whether Reply's lookup ignores the slug's case is not verified",
                "a person Reply holds under a mixed-case profile link is not found", "creates a second contact",
                "`optout.record` lands on that one",
            },
            words => Assert.Contains(words, entry, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reply takes a LinkedIn send without consulting its blacklist and consults it when it makes the send, so a person the
    /// blacklist holds is accepted and the send fails later, which only Jason's own register stops before it is made. And a
    /// read or a cancel of a direct send that Reply answers with a 404 carrying no code of its own ends as every command
    /// does: the README's row for that 404 does not leave either command out, and the two operations' tables carry no row
    /// of their own for it, since Reply's direct-send routes are on every account.
    /// </summary>
    [Fact]
    public void The_readme_says_when_reply_consults_its_blacklist_on_linkedin_and_what_a_read_or_cancel_meeting_a_404_without_reply_s_code_answers()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("## What it cannot do", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section on what the package cannot do.");
        var section = Flat(readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)]);

        Assert.All(
            new[]
            {
                "nor consult its blacklist when it takes a LinkedIn send", "On LinkedIn it consults it when it makes the send",
                "is accepted, and fails when Reply makes it, which `message.status` reads as `failed` with Reply's reason",
                "record the stop with `jason suppression add` as well, so the send is refused at the claim",
            },
            words => Assert.Contains(words, section, StringComparison.Ordinal));
        Assert.DoesNotContain("blacklist on an email alone", section, StringComparison.Ordinal);

        var route = Assert.Single(
            readme.Split('\n'), row => row.Contains("which reply-cli passes through as it is on every command but", StringComparison.Ordinal));
        Assert.DoesNotContain("`direct-send get`", route, StringComparison.Ordinal);
        Assert.DoesNotContain("`direct-send cancel`", route, StringComparison.Ordinal);
        foreach (var (operation, command) in new[] { ("message.status", "get"), ("message.cancel", "cancel") })
        {
            var table = readme.IndexOf($"### `{operation}`", StringComparison.Ordinal);
            Assert.True(table >= 0, $"The README has no section for {operation}.");
            var rows = readme[table..readme.IndexOf("\n\n", readme.IndexOf("| When |", table, StringComparison.Ordinal), StringComparison.Ordinal)];
            Assert.DoesNotContain(
                rows.Split('\n'),
                row => row.StartsWith($"| `reply direct-send {command}` answered that Reply's API has no route for it", StringComparison.Ordinal)
                    || row.Contains("| `capability_not_offered` |", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The release that reads Reply's deferred LinkedIn sends: a cancel of a send Reply is sending now, which Reply refuses
    /// at 409 and reply-cli 0.18.0 names by a code of its own, answered as a send that already went; the README saying
    /// what such a send answers and how it can end; a send read and cancelled by its scheduled reference; and the package
    /// needing reply-cli 0.18.0, an older one leaving it <c>unavailable</c>.
    /// </summary>
    [Fact]
    public void The_changelog_opens_with_0_14_3_a_cancel_of_a_send_reply_is_sending_and_reply_s_deferred_linkedin_sends()
    {
        var section = Flat(ChangelogSection(4));

        Assert.StartsWith(" ## 0.14.3 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`direct_send.already_sending`", "`directSend.alreadySending`", "409", "`send_not_pending`", "`linkedin-scheduled-<n>`", "`sending`",
                "`directLinkedIn.missedWindow`", "`directLinkedIn.weeklyLimitReached`", "`executable_incompatible`", "Jason 0.22.0",
                $"reply-cli {ReplyCliRelease.Version} or newer",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reply refuses a cancel of a send it is sending now at 409 under <c>directSend.alreadySending</c>, which reply-cli
    /// names <c>direct_send.already_sending</c>, since Reply times deferred LinkedIn sends itself on every account. Every
    /// text of the package that names the CLI's code tells it as an answer Reply gives now: the README's rows and the error
    /// table's. The changelog's entry for 0.14.3, which named it as a coming answer, is that version's history and keeps
    /// its words, and the entry for 0.14.5 names the code among the texts told in the present tense.
    /// </summary>
    [Fact]
    public void Reply_s_refusal_of_a_cancel_of_a_send_it_is_sending_is_told_as_an_answer_reply_gives_now_wherever_it_is_named()
    {
        const string Code = "`direct_send.already_sending`";
        const string Coming = "once Reply times deferred LinkedIn sends itself";
        var readme = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "README.md")).Where(line => line.Contains(Code, StringComparison.Ordinal)).ToList();
        var table = ReplyErrorTable.AllRows().Select(entry => (string?)entry.Row["when"] ?? "").Where(when => when.Contains(Code, StringComparison.Ordinal)).ToList();
        var history = Items(ChangelogSection(4)).Where(item => item.Contains(Code, StringComparison.Ordinal)).ToList();

        Assert.Equal((2, 1, 1), (readme.Count, table.Count, history.Count));
        Assert.All(readme.Concat(table), text => Assert.DoesNotContain("once Reply times", Flat(text), StringComparison.Ordinal));
        Assert.All(history, text => Assert.Contains(Coming, Flat(text), StringComparison.Ordinal));
        Assert.Contains(Items(ChangelogSection(2)), item => item.Contains(Code, StringComparison.Ordinal) && item.Contains("present", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reply answers a cancel it could not confirm at 503 for a send still queued or waiting for its schedule, since it
    /// times deferred LinkedIn sends itself on every account. Every text that names the CLI's code, the README's rows and
    /// the error table's, says so in the present tense; the changelog's entry for 0.14.3, which named the schedule's wait
    /// as coming, is that version's history and keeps its words.
    /// </summary>
    [Fact]
    public void A_cancel_reply_could_not_confirm_is_named_as_an_answer_reply_gives_now()
    {
        const string Code = "`direct_send.cancel_not_confirmed`";
        const string Queued = "still queued or waiting for its schedule";
        var readme = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "README.md")).Where(line => line.Contains(Code, StringComparison.Ordinal)).ToList();
        var table = ReplyErrorTable.AllRows().Where(entry => entry.Key == "cancel_not_confirmed").SelectMany(entry => new[] { (string?)entry.Row["when"] ?? "", (string?)entry.Row["note"] ?? "" }).ToList();
        var history = Items(ChangelogSection(4)).Where(item => item.Contains(Code, StringComparison.Ordinal)).ToList();

        Assert.Equal((2, 2, 1), (readme.Count, table.Count, history.Count));
        Assert.All(
            readme.Concat(table).Select(Flat),
            text =>
            {
                Assert.Contains(Queued, text, StringComparison.Ordinal);
                Assert.DoesNotContain("once Reply times", text, StringComparison.Ordinal);
                Assert.DoesNotContain("once it times", text, StringComparison.Ordinal);
            });
        Assert.All(history.Select(Flat), text => Assert.Contains("once Reply times deferred LinkedIn sends itself, waiting for its schedule", text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reply times deferred LinkedIn sends itself on every account, so neither the README nor the error table tells it as
    /// something Reply will do: no "once Reply times" and no "Before then" about a deferred send remain in them.
    /// </summary>
    [Fact]
    public void The_readme_and_the_table_tell_reply_s_deferred_linkedin_sends_in_the_present_tense()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n"));
        var table = ReplyErrorTable.AllRows().SelectMany(entry => new[] { (string?)entry.Row["when"] ?? "", (string?)entry.Row["note"] ?? "" }).Select(Flat).ToList();

        foreach (var phrase in new[] { "once Reply times", "once it times", "Before then a deferred send", "the second once", "a schedule yet", "until Reply times" })
        {
            Assert.DoesNotContain(phrase, readme, StringComparison.Ordinal);
            Assert.All(table, text => Assert.DoesNotContain(phrase, text, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A LinkedIn message may name a Reply sending schedule from 0.14.3, so the texts say what it meets: the changelog's
    /// entry names the flag, Reply's refusals of a schedule and the cancel Reply could not confirm; the README's paragraph
    /// says Reply answers such a message as it answers an email under the scheduled reference. What the release's notes
    /// no longer say is held with the notes, by <see cref="ReleaseNotesTests"/>.
    /// </summary>
    [Fact]
    public void The_texts_say_a_linkedin_message_may_name_a_sending_schedule()
    {
        var changelog = Flat(ChangelogSection(4));
        Assert.All(
            new[]
            {
                "`--schedule-id`", "`provider_input_invalid`", "`capability_not_offered`", "`schedule.not_found`",
                "`contact.time_zone_missing`", "`schedule.no_slot`", "`contact.scheduleNotFound`", "`directLinkedIn.scheduleNotFound`",
                "an email and a LinkedIn message alike", "`waitingForSchedule`", "`estimated_send_at`",
                "`direct_send.cancel_not_confirmed`", "`provider_unavailable`",
            },
            word => Assert.Contains(word, changelog, StringComparison.Ordinal));

        var readme = Items(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));
        var paragraph = Flat(Assert.Single(readme, item => item.StartsWith("A LinkedIn message, InMail or voice message names a schedule", StringComparison.Ordinal)));
        Assert.Contains("answers it as it answers such an email", paragraph, StringComparison.Ordinal);
        Assert.Contains("`linkedin-scheduled-<n>`", paragraph, StringComparison.Ordinal);
    }

    /// <summary>A page's items: each list item and each paragraph, whole, however it is wrapped.</summary>
    private static List<string> Items(string text) =>
        [.. Regex.Split(text.ReplaceLineEndings("\n"), @"\n(?=- |\n)").Select(item => item.Trim()).Where(item => item.Length > 0)];

    /// <summary>
    /// The release that names what the package already did: four commands on which reply-cli passes Reply's bare 404
    /// through, added to a list that still left others out, Reply's two codes for a LinkedIn allowance spent, in either
    /// shape, and the two ways Reply answers an email timed by a schedule; nothing the package does changes, and it needs
    /// what 0.14.1 needs. It never claims the list was every such command, which 0.14.3's entry corrects.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_14_2_what_the_package_does_named_and_nothing_it_does_changed()
    {
        var section = Flat(ChangelogSection(5));

        Assert.StartsWith(" ## 0.14.2 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`route_not_offered`", "`reply direct-send get` and `reply direct-send cancel`", "`directLinkedIn.dailyLimitReached`",
                "`directLinkedIn.weeklyLimitReached`", "`linkedin.limit_reached`", "the state `failed`", "`provider_blocked`", "`rate_limited`",
                "`queued`", "within ten minutes", "`waitingForSchedule`", "No change in behaviour", "Jason 0.22.0",
                "reply-cli 0.17.2 or newer", "still left others out",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("names every command", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release that corrects the README alone: what it says of a send a Reply without the route answers, and of
    /// Reply's blacklist on a LinkedIn send; nothing the package does changes, and it needs what 0.14.0 needs.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_14_1_the_readme_corrected_and_nothing_the_package_does_changed()
    {
        var section = Flat(ChangelogSection(6));

        Assert.StartsWith(" ## 0.14.1 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "The README is corrected", "`message.status` and `message.cancel`", "`capability_not_offered`",
                "`reply direct-send get` and `reply direct-send cancel`", "its blacklist", "No change in behaviour",
                "Jason 0.22.0", "reply-cli 0.17.2 or newer",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The release in which a LinkedIn message and an InMail may be templates: the names Reply gives the sender there and
    /// the two it lacks, the voice message still refused, the manifest's list and what the claim makes of a package older
    /// than it, reply-cli's own codes for a template Reply could not fill in with the names it lists, and the send held for
    /// later that Reply fills in when it goes.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_14_0_templates_on_linkedin_and_the_codes_for_one_reply_could_not_fill_in()
    {
        var section = Flat(ChangelogSection(7));

        Assert.StartsWith(" ## 0.14.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "A LinkedIn message and an InMail may be composed as a template", "`SendingLinkedIn.SenderFirstName`",
                "`SendingLinkedIn.SenderName`", "which Reply converts from HTML to text after filling it in", "`templates` lists `message.send` on `email` and `linkedin`",
                "a Reply package older than 0.14.0 under runtime 0.22.0 has its email templates refused `template_not_offered` at the claim until the package is updated",
                "`unknown_field`", "the package writes in its place the profile the arguments hold", "`template_unsupported` (validation)",
                "A template in a voice message", "`contact.missing_variables`", "`contact.rendering_failed`",
                "`linkedin.missing_variables`", "`template_unrendered`", "`message_invalid`", "`missing_variables`",
                "the first twenty, each to 64 characters", "filled in by Reply when it goes",
                "reply-cli 0.17.2 or newer",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The release in which a send the runtime keeps a reference for is read and taken back by it: the two commands, the
    /// state in the document's five words, what a cancel that cannot stop the send answers, the repeat after a lost answer,
    /// and the endings both share; in which <c>message.send</c> is timed by the provider's own schedule, and a schedule on
    /// LinkedIn is refused for Reply's reason; and in which every call the package makes names the harness and the runtime
    /// it came through, with the CLI it needs for that.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_13_0_the_send_read_and_taken_back_the_schedule_and_the_calls_marked_as_the_harness_s()
    {
        var section = Flat(ChangelogSection(8));

        Assert.StartsWith(" ## 0.13.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`message.status`", "`message.cancel`", "`send.reference`", "never by anything the caller wrote",
                "`reply direct-send get`", "`reply direct-send cancel`", "`queued`", "`cancelable` unless Reply is sending it now",
                "One cancelled before answers the same", "the cancel carries no key", "`send_not_pending` (permanent)",
                "never reported as cancelled", "`provider_answer_lost`", "`send_not_found` (permanent)",
                "`capability_not_offered` (permanent)", "Nothing is pinned.",
                "`REPLY_CLIENT`", "`jason-harness/<runtime version>`", "the runtime version without its build metadata",
                "`REPLY_NO_UPDATE_CHECK`",
                "reply-cli 0.17.2 or newer", "Under an older CLI the package is `unavailable`",
                "`provider.schedule_id`", "`--schedule-id`", "`provider_input_invalid`", "`capability_not_offered`",
                "since Reply refuses a schedule on every LinkedIn direct send",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("ignores the variable", section, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped unread", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every vendor-named variable the package sets on the CLI is named where the README says what reaches the child:
    /// the update check it turns off, and the name that tells Reply the call came through the harness, with the runtime
    /// version it carries.
    /// </summary>
    [Fact]
    public void The_readme_names_the_two_variables_the_package_sets_on_the_cli()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.Contains("The two variables this package sets are `REPLY_NO_UPDATE_CHECK=1` and `REPLY_CLIENT`", readme, StringComparison.Ordinal);
        Assert.Contains("`REPLY_CLIENT=jason-harness/<runtime version>`", readme, StringComparison.Ordinal);
        Assert.Contains("the runtime version without its build metadata", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("The one variable this package sets", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release in which the package answers for the account's events and a direct send says when it should go out
    /// and ends with what Reply took. The events: the contract it declares, the feed it reads, the one kind and who moved
    /// it, the lines a move says, the three cursors and the hour read again, the window a read by date keeps inside, and
    /// the endings a reader acts on. The sends: the moment and the key the send carries, the read before a send that
    /// names a moment and before a repeat after an attempt that may have sent, the window a repeat goes under the key in,
    /// the result it answers and the lost answer repeated under the key. Then the CLI it needs, and what a runtime before
    /// the one that admits the sends' result makes of it.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_12_0_the_event_feed_it_reads_and_the_send_reply_takes()
    {
        var section = Flat(ChangelogSection(9));

        Assert.StartsWith(" ## 0.12.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`contracts.events: [1]`", "`events.read`", "`reply inbox changes`", "Reply's own cursor", "one `thread_moved`",
                "`other_side` where the last message is the other side's", "on a thread that holds one", "`provider` is never said", "`unread` is `null`", "the last 30 days", "an hour inside it",
                "`events_cursor_unknown`", "`c|<Reply's cursor>`", "`m|<moment>`", "`u|<moment>|<Reply's cursor>`",
                "an hour before that moment", "counts as held", "`inbox.cursor_expired`", "`inbox.cursor_refused`",
                "A move by the account is read and not kept", "a source of its own", "`contact_pin`", "`events_unreachable`", "`events_unsupported`",
                "page by page", "reply-cli 0.17.0 or newer",
                "`send_after`", "`--send-after`", "`--idempotency-key`", "`reply direct-send get`", "`accepted`", "`accepted_for`",
                "`reference`", "`cancelable`", "`capability_not_offered` (permanent)", "`send_not_settled` (permanent)",
                "`idempotency_key.in_use`", "`provider_unavailable` (transient)", "`idempotency_key.reused`",
                "repeated under the same key", "a runtime from 0.22.0", "`email-1`",
                "`context.unsettled_since`", "within 23 hours",
                "the key travels only on the send command, and at most one send command per attempt reaches Reply's send",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("sendAfter", section, StringComparison.Ordinal);
        Assert.DoesNotContain("subject", section, StringComparison.Ordinal);
        Assert.DoesNotContain("without the command", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release in which a route Reply's API does not have is said as such: the answer it reads that way, the commands
    /// that answer can reach it on, the code and its class, the 404 that keeps its meaning, and what a runtime before the
    /// one that admits the code makes of it.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_11_0_and_the_route_reply_does_not_have()
    {
        var section = Flat(ChangelogSection(10));

        Assert.StartsWith(" ## 0.11.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`capability_not_offered` (permanent)", "naming the operation", "nothing is repeated",
                "`reply.refused` at status 404 with no code of Reply's own", "the direct sends, `inbox categorize`",
                "A 404 Reply explains with a code of its own keeps the meaning it had.", "a runtime from 0.22.0",
                "`result_invalid`, ambiguous and never repeated",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("sendAfter", section, StringComparison.Ordinal);
        Assert.DoesNotContain(".notSupported", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release in which an email composed as a template goes in Reply's syntax and Reply fills it in: what is refused
    /// before anything is called, what Reply's own refusal is, and a final text sent as before, save the prompt marker.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_10_0_and_the_template_it_sends()
    {
        var section = Flat(ChangelogSection(11));

        Assert.StartsWith(" ## 0.10.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`message.send`", "`template`", "Reply's own syntax", "`reply contact send-email`", "`template_unsupported`",
                "Before anything is called", "a key Reply keeps", "a prompt to a model", "`template_unrendered`",
                "Reply's own sentence", "`message_invalid`",
                "a template that compares a value with a text holding a character Reply's comparison does not read",
                "and so, from this release, is `@prompt[[`, which Reply reads as a prompt to a model",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("never reports", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release that changes no behaviour and whose README installs the package with the verb that checks it against the
    /// release before placing it, with the fetched tree as the fallback.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_9_1_and_the_install_verb_its_readme_names()
    {
        var section = Flat(ChangelogSection(12));

        Assert.StartsWith(" ## 0.9.1 ", section, StringComparison.Ordinal);
        Assert.All(
            new[] { "No behaviour changes.", "`jason plugin install reply`", "`checksums.txt`", "content digest", "`jason skills install`" },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The release in which the six writes that may create the person say whether Reply found the contact or created it:
    /// the operations, the CLI's word it reads, the two words it says, what it does where the CLI says nothing, and the
    /// runtime release that writes it down.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_9_0_and_whether_the_contact_was_found_or_created()
    {
        var section = Flat(ChangelogSection(13));

        Assert.StartsWith(" ## 0.9.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`list_membership.add`", "`campaign.enroll`", "`optout.record`", "`contact.update`", "`social_invitation.send`",
                "`message.send`", "found or created", "`contact_created`", "`created`", "`found`", "`external_id_origins.contact`",
                "nothing is guessed", "0.19.0", "`external_id_origin`",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The package's list of the person's own fields is a copy of what the documents project, since the plugin host does
    /// not hand a plugin the documents; this holds the copy to every document that may create the provider's contact.
    /// </summary>
    [Fact]
    public void The_list_of_the_person_s_own_fields_is_what_every_creating_document_projects_beside_the_identifier()
    {
        var source = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "modules", "contacts.js"));
        var copy = Regex.Match(source, @"const PROJECTED = \[(?<fields>[^\]]*)\];").Groups["fields"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(field => field.Trim('"'))
            .ToList();
        var creating = OperationCatalog.All.Where(contract => contract.Preflight.ProviderContact == ProviderContact.FindOrCreate).ToList();

        Assert.NotEmpty(copy);
        Assert.NotEmpty(creating);
        Assert.All(creating, contract =>
            Assert.True(
                contract.ContactProjection!.Fields.Where(field => field != "id").SequenceEqual(copy),
                $"{contract.Id} projects {string.Join(", ", contract.ContactProjection.Fields)}; contacts.js lists {string.Join(", ", copy)}."));
    }

    /// <summary>
    /// The release that changes no behaviour and says, beside the list of the person's own fields the package writes
    /// with, where that list comes from, so a field added to the documents is not left for a reader to find missing here.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_8_1_and_where_the_projected_fields_come_from()
    {
        var section = Flat(ChangelogSection(14));

        Assert.StartsWith(" ## 0.8.1 ", section, StringComparison.Ordinal);
        Assert.All(
            new[] { "`contact_projection`", "No behaviour changes" },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The release that lists the sending schedules: the operation it adds, the command and the CLI release it needs, and
    /// what a person reads in it about the order a page counts in, the time zone carried as Reply spells it and the
    /// calendar it does not work out.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_8_0_and_the_schedules_it_lists()
    {
        var section = Flat(ChangelogSection(15));

        Assert.StartsWith(" ## 0.8.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`schedule.list`", "`reply schedule list`", "reply-cli 0.16.0", "the lowest first",
                "Windows time zone identifier", "`windows`", "never converted", "no calendar", "`unauthorized`",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The release in which the two sends say a sending account Reply refused for its own state is that sender's block:
    /// the codes that become it, the reasons, the moment it clears where reply-cli says how long to wait, the codes that
    /// stay as they were, and the runtime release that holds work on a block.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_7_0_and_the_sender_s_block()
    {
        var section = Flat(ChangelogSection(16));

        Assert.StartsWith(" ## 0.7.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`message.send`", "`social_invitation.send`", "`provider_blocked`", "`blocked`", "`channel`", "`limit`",
                "`linkedin.account_inactive`", "`linkedin.invalid_cookie`", "`linkedin.limit_reached`", "`clears_at`",
                "`retry_after`", "`sender_unavailable`", "`sender_limit_reached`", "`no_inmail_credits`", "a runtime from 0.18.0",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// The release that names the provider block in the error table and changes nothing a Reply answer reaches, so one
    /// version never has two contents.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_6_1_and_says_no_reply_answer_reaches_the_block_row()
    {
        var section = Flat(ChangelogSection(17));

        Assert.StartsWith(" ## 0.6.1 ", section, StringComparison.Ordinal);
        Assert.Contains(
            "## 0.6.1 - the error table names `provider_blocked`, the runtime's class `blocked` from 0.18.0; no Reply answer reaches it until reply-cli reports a block; no behaviour change.",
            section,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The release in which the sends and the update create the person Reply holds nobody for: the three operations it
    /// changes, the CLI release and the Jason release it needs, and what a person reads in it about the read that still
    /// comes first — for a contact the command's own import matched too — the one command that imports, the route's word
    /// on deriving and the new refusals. No send reaches a contact Reply held without that read, so nothing says one does.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_6_0_and_the_three_operations_that_now_create_the_person()
    {
        var section = Flat(ChangelogSection(18));

        Assert.StartsWith(" ## 0.6.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`message.send`", "`social_invitation.send`", "`contact.update`", "`find_or_create`", "reply-cli 0.15.0",
                "Jason 0.14.0", "read first", "`--contact -`", "`contact.already_held`", "`derive_first_name`", "`limit_reached`",
                "`contact_channel_conflict`",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("unread", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Jason release the package needs is the one that reads its manifest's lists and admits what its sends answer, and
    /// it is named where an operator reads what the package needs, with what an older runtime does with the package, and
    /// with the person's own fields the package is handed, which only a runtime older than 0.14.0 leaves out.
    /// </summary>
    [Fact]
    public void The_readme_names_the_jason_release_that_reads_the_package_s_manifest()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("## What you need", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section on what the package needs.");
        var section = Flat(readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)]);

        Assert.All(
            new[]
            {
                "**Jason 0.22.0 or newer.**", "(`arguments`)", "(`templates`)", "`unknown_field`", "reads the list at the claim",
                "the person's own fields", "only a runtime older than 0.14.0 composes",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.DoesNotContain("Jason 0.14.0 or newer", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release that sends outside a sequence: the three operations it adds, the CLI it needs, and what a person reads
    /// in it about the stop Reply does not make on such a send, the note Reply refuses, the one write before a send, the
    /// email written as HTML, and the address keyed as the runtime keys it.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_5_0_and_the_three_operations_it_adds()
    {
        var section = Flat(ChangelogSection(19));

        Assert.StartsWith(" ## 0.5.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`sender.list`", "`social_invitation.send`", "reply-cli 0.14.0", "`never`", "Reply refuses notes over 200 characters",
                "opted out", "a teammate", "never changed", "outside ASCII", "`message.send`", "written as HTML",
                "an address is never written", "`not_connected`", "JavaScript's lowercasing", "never cut to 500",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    [Fact]
    public void The_changelog_keeps_0_4_0_and_the_four_inbox_operations_it_adds()
    {
        var section = Flat(ChangelogSection(20));

        Assert.StartsWith(" ## 0.4.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[] { "`conversation.reply`", "`conversation.classify`", "`conversation.close`", "`conversation.reopen`", "`never`", "0.13.0" },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
        Assert.Contains("three requests to Reply before its send", section, StringComparison.Ordinal);

        // And what a live account showed the inbox does beyond the operations themselves.
        Assert.All(
            new[] { "`category_opts_out`", "Do not contact", "a connected CRM", "`sent_at` is null", "no identifier on email", "quote", "`RenderingFail`" },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    [Fact]
    public void The_changelog_keeps_0_3_2_and_the_operation_it_added()
    {
        var section = ChangelogSection(21);

        Assert.StartsWith("\n## 0.3.2\n", section, StringComparison.Ordinal);
        Assert.Contains("`campaign.create`", section, StringComparison.Ordinal);
        Assert.Contains("`campaign_name_taken`", section, StringComparison.Ordinal);
        Assert.Contains("`campaign_name_ambiguous`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_changelog_keeps_0_3_1_and_the_rule()
    {
        var section = ChangelogSection(22);

        Assert.StartsWith("\n## 0.3.1\n", section, StringComparison.Ordinal);
        Assert.Contains("`derive_first_name`", section, StringComparison.Ordinal);
        Assert.Contains("`last_name`", section, StringComparison.Ordinal);
        Assert.Contains("`email_local_part`", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The switch never applies to an opt-out, which is protective and greets nobody, so the opt-out's own rows hold
    /// the floor's refusal and never the switch's.
    /// </summary>
    [Fact]
    public void The_opt_out_has_no_row_for_a_forbidden_derivation()
    {
        Assert.True(ReplyErrorTable.Own("optout.record").ContainsKey("first_name_not_derivable"));
        Assert.False(ReplyErrorTable.Own("optout.record").ContainsKey("first_name_derivation_forbidden"));
        Assert.True(ReplyErrorTable.Own("list_membership.add").ContainsKey("first_name_derivation_forbidden"));
        Assert.True(ReplyErrorTable.Own("campaign.enroll").ContainsKey("first_name_derivation_forbidden"));
        Assert.All(
            new[] { "message.send", "social_invitation.send", "contact.update" },
            operation => Assert.True(ReplyErrorTable.Own(operation).ContainsKey("first_name_derivation_forbidden"), $"{operation} has no row for a forbidden derivation."));
    }

    /// <summary>
    /// Reply gives its reserved categories a meaning of its own, and an operator choosing a category has to know it: the
    /// events Reply fires, and the re-engagement it may start by its own hand. The classify section says so, says where
    /// it was read from, and says what a campaign that must own every send does instead.
    /// </summary>
    [Fact]
    public void The_classify_section_says_what_reply_does_on_its_reserved_names()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("### `conversation.classify`", StringComparison.Ordinal);
        var section = Flat(readme[start..readme.IndexOf("\n### ", start + 1, StringComparison.Ordinal)]);

        Assert.All(
            new[]
            {
                "read from the provider's own implementation", "a webhook", "a connected CRM", "a positive event for Interested",
                "a negative one for Not interested and Not now", "re-engagement", "by Reply's hand", "categories of its own",
            },
            words => Assert.Contains(words, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// What a reply's result leaves out, and what goes out beside the text, are things an operator reading the result or
    /// approving a reply has to be told: no moment, because Reply's answer stamps a local time as UTC; no identifier on
    /// email; and Reply's own quote of the thread after the text, which the approval does not show.
    /// </summary>
    [Fact]
    public void The_reply_section_says_what_the_answer_leaves_out_and_what_goes_out_beside_the_text()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("### `conversation.reply`", StringComparison.Ordinal);
        var section = Flat(readme[start..readme.IndexOf("\n### ", start + 1, StringComparison.Ordinal)]);

        Assert.All(
            new[]
            {
                "the provider's send answer stamps a local time as UTC, so it is not passed on", "`message.sent_at` is null",
                "`message.external_id` is null on email", "its own quote of the thread's earlier messages", "the approval shows the text only",
            },
            words => Assert.Contains(words, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reply's list keeps a thread's last activity where the person's latest message put it after a reply Jason sends, so
    /// that reply raises no line and wakes nobody. The page says so outright, and no longer says the thread moves.
    /// </summary>
    [Fact]
    public void The_readme_says_a_reply_jason_sends_does_not_move_the_thread_or_wake_the_manager()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.Contains("A reply Jason sends does not wake the manager at all.", readme, StringComparison.Ordinal);
        Assert.Contains("inbound sync says nothing for it", readme, StringComparison.Ordinal);
        Assert.Contains("A line for that thread is the person, or somebody else, moving it.", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("moves the thread", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// What Reply counts as no value and how it compares are Reply's, and the README states them as Reply's rendering reads
    /// a template, beside the sentence the package reads Reply's refusal by and the tag Reply drops with what follows it,
    /// which the package refuses before anything is called; a LinkedIn template goes as written, without the sender's
    /// two names Reply lacks there, and one Reply does not send within the call is filled in when it goes.
    /// </summary>
    [Fact]
    public void The_readme_states_how_reply_reads_a_template_as_reply_s()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.All(
            new[]
            {
                "What counts as no value, and how two values compare, is Reply's.",
                "a fallback is written in place of a value that is null, empty, or exactly `false` or `0`",
                "an `#if` takes its other branch on those, and on `false` written in any case",
                "white space is a value",
                "two numbers as numbers",
                "that sentence starts `RenderingFail`",
                "Reply decodes the body before it reads it, and drops an unknown tag with everything after it, in the body and in the subject alike, in a final text as in a template",
                "The subject goes as written, and a character reference in it is not read.",
                "**A `<` that starts a tag is refused in a direct email, `message.send`.**",
                "A reply on a thread, `conversation.reply`, is not held to this yet, and its text goes as written.",
                "The refusals come in one order: a final text is read for a placeholder or a prompt, then its subject for a tag's start, then its body; a template is read part by part, its subject first, each for a construct the standard refuses, then, on LinkedIn, the sending account's address named with neither a value nor a fallback, then a name Reply holds no field for on the channel, then a key Reply keeps, then a comparison Reply cannot read, then a prompt, then, on an email, a tag's start.",
                "**A LinkedIn message or InMail composed as a template goes in Reply's syntax as written.**",
                "Reply has no variable on LinkedIn for the sending account's last name or address. The arguments hold the address, the account's profile, so where a template names `sender.address` the package writes the profile they hold in its place, as the example a person approves shows it: its fallback where they hold none, and a condition on it as the branch the profile takes, compared as the example compares. With no profile and no fallback it is refused `template_unsupported` before anything is called, and so is a template naming `sender.last_name`, which neither Reply nor the arguments hold.",
                "The package writes the template's text as written around the tags and inside them",
                "On LinkedIn the package writes a text as written, and Reply converts it from HTML to text after filling it in; an invitation's note goes as written.",
                "After filling a LinkedIn text in, Reply converts it from HTML to text, so markup written in it does not reach the person as written, and what it makes of a `<` that starts a tag has not been seen; the package does not refuse one on LinkedIn.",
                "the package reads those codes as a template's only when the message is one: for a final text they are `message_invalid`.",
                "On an email reply-cli answers one as `contact.missing_variables` or `contact.rendering_failed`, which are `message_invalid` too; an answer under `contact.unknown`, as a reply-cli older than 0.17.2 gave it, says nothing of it, so one that got past this check there would be reported as `provider_call_failed`, Reply's code in the trace.",
                "**A template Reply does not send within the call is filled in when it goes.**",
                "A send held for a later moment, or one that goes now which Reply queues behind the account's other sends and answers as accepted, is taken as written and filled in at the moment Reply sends it",
                "which fills in a template it sends within the call before it sends anything",
                "its text, the text of a fallback and the text a comparison compares reach Reply escaped as a final text's are",
                "so a comparison compares the value with the text as the template wrote it",
                "So the package refuses, before anything is called, an email whose subject or body holds a `<` followed by an ASCII letter, `/`, `!` or `?`, as `message_invalid` in a final text and as `template_unsupported` in a template, and names the character without quoting the text.",
                "In a template its text, a fallback and the text a comparison compares are held alike, and so is a `<` right before a value, which the package does not see and which may start with a letter.",
                "A comparison's text is held because whether Reply sanitizes a template before or after it fills it in has not been seen.",
            },
            words => Assert.Contains(words, readme, StringComparison.Ordinal));
    }

    /// <summary>
    /// On Linux the runtime started at logon is a <c>systemd --user</c> unit, whose environment reads no shell profile, so
    /// the README says where a directory on the terminal's PATH reaches it, in the terms the release page uses.
    /// </summary>
    [Fact]
    public void The_readme_says_where_a_directory_reaches_a_runtime_started_at_logon_on_linux()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.Contains(
            "On Linux, a program on the PATH your terminal has is not necessarily on the runtime's: a runtime started at logon is a `systemd --user` unit, whose environment is the user manager's and reads no shell profile — neither `~/.profile` nor `~/.bashrc`, where npm's global directory or a version manager most often adds itself — so that directory reaches it only through `~/.config/environment.d/` or the unit's own `Environment=`.",
            readme,
            StringComparison.Ordinal);
        Assert.DoesNotContain("reads the login PATH", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The README's row and the error table's row for a comparison Reply cannot read name every kind of character Reply's
    /// comparison reads, the joining punctuation <c>_</c> among them, as the package's check holds them.
    /// </summary>
    [Fact]
    public void The_comparison_rows_name_every_kind_of_character_reply_s_comparison_reads()
    {
        const string classes = "letters, the marks that combine with them, digits, `_` and the other joining punctuation, white space and the punctuation";
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));
        var row = Assert.Single(ReplyErrorTable.AllRows(), entry => entry.Key == "template_compares_what_reply_cannot_read").Row;

        Assert.Contains("Reply reads " + classes + " `` $@\"`~!#%^&*()+,-./:;<= `` there", readme, StringComparison.Ordinal);
        Assert.Contains("Reply reads " + classes + " $@\"`~!#%^&*()+,-./:;<= there", (string?)row["when"], StringComparison.Ordinal);
    }

    /// <summary>
    /// The release that drops the read before a send: no <c>direct-send get email-1</c> before a send that names a moment
    /// ahead, a schedule or a repeat, since Reply's direct-send routes are on every account; a 404 with no code of Reply's
    /// own is <c>provider_call_failed</c>, on a repeat of a send <c>send_not_settled</c> and on the change feed
    /// <c>events_unsupported</c>; the two rows that said a moment or a schedule was not offered gone; one call less before
    /// such a send and the same endings for every answer that carries Reply's code; and Reply's deferred LinkedIn sends
    /// told in the present tense. It needs what 0.14.4 needs.
    /// </summary>
    [Fact]
    public void The_changelog_opens_with_0_14_5_no_read_before_a_send_and_what_a_404_without_reply_s_code_ends_in()
    {
        var section = Flat(ChangelogSection(2));

        Assert.StartsWith(" ## 0.14.5 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`reply direct-send get email-1`", "`provider_call_failed`", "`reply.refused`", "404", "`send_not_settled`",
                "`events_unsupported`", "`send_after_not_offered`", "`schedule_not_offered`", "one call less",
                "the same endings", "`jason update apply`", "deferred LinkedIn sends", "reply-cli 0.18.0",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// What Reply detects of a contact's later replies is stated as a Jason user meets it: the first, direct-email
    /// statement said to be observed on an account, the rest said to be read from how Reply is built and not observed,
    /// and a later reply said to be detected when it answers a message this side sent from the inbox. The earlier
    /// sentences, that an answer keeps the conversation visible and that only a first reply is read, are gone.
    /// </summary>
    [Fact]
    public void The_readme_says_which_later_email_replies_are_detected_and_which_statements_were_observed()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.DoesNotContain("Only a contact's first reply is read.", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("stops detecting that contact's later replies", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("the contact's next replies on it are stored and read as usual", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Answering a reply is therefore what keeps the conversation visible to Jason", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Both statements are read from how Reply is built", readme, StringComparison.Ordinal);
        Assert.Contains("A reply that is lost reaches none of `conversation.list`, `conversation.get` and the change feed `events.read` follows, and Jason's inbound sync says nothing of it.", readme, StringComparison.Ordinal);
        Assert.Contains("A direct email's replies are all detected, any number, a reply after an answer sent from Reply's inbox included.", readme, StringComparison.Ordinal);
        Assert.Contains("A second reply to a sequence email is not detected", readme, StringComparison.Ordinal);
        Assert.Contains("only when the reply's own headers reference that answer", readme, StringComparison.Ordinal);
        Assert.Contains("No auto-reply or out-of-office is stored on a direct thread.", readme, StringComparison.Ordinal);
        Assert.Contains("`In-Reply-To` and `References` are both empty is dropped", readme, StringComparison.Ordinal);
        Assert.Contains("older than 40 days is not matched unless a newer send to that address exists", readme, StringComparison.Ordinal);
        Assert.Contains("On a sequence thread, a reply after an answer sent from Reply's inbox is detected only when its own headers reference that answer; a second reply to the sequence email itself is not.", readme, StringComparison.Ordinal);
        Assert.Contains("A LinkedIn thread keeps later messages, so they are read.", readme, StringComparison.Ordinal);
        Assert.Contains("That a direct email's replies are all detected, any number, was observed on an account. The others, the one about LinkedIn among them, are read from how Reply is built and have not been observed; an observation on one account confirms or corrects them.", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The changelog's 0.14.6, after the newest, says what the README states of later replies, only the first statement
    /// observed and the rest read from how Reply is built, and keeps 0.14.4, the version that changed the earlier
    /// sentence that only a contact's first reply is read, as history.
    /// </summary>
    [Fact]
    public void The_changelog_s_0_14_6_says_what_the_readme_says_of_later_replies()
    {
        var section = Flat(ChangelogSection(1));

        Assert.StartsWith(" ## 0.14.6 ", section, StringComparison.Ordinal);
        Assert.Contains("later replies", section, StringComparison.Ordinal);
        Assert.Contains("observed", section, StringComparison.Ordinal);
        Assert.DoesNotContain("The first two statements were observed", section, StringComparison.Ordinal);
        Assert.Contains("That a direct email's replies are all detected, any number, was observed on an account;", section, StringComparison.Ordinal);
        Assert.Contains("On a sequence thread, a reply after an answer sent from Reply's inbox is detected only when its own headers reference that answer; a second reply to the sequence email itself is not.", section, StringComparison.Ordinal);
        Assert.Contains("`jason update apply`", section, StringComparison.Ordinal);
        Assert.StartsWith(" ## 0.14.4 ", Flat(ChangelogSection(3)), StringComparison.Ordinal);
    }

    /// <summary>
    /// A global npm install refused with EACCES and a Mac's logon start on launchd's search path are the two ways a fresh
    /// installation's runtime does not find <c>reply</c>; the prerequisites say both, each with its repair.
    /// </summary>
    [Fact]
    public void The_prerequisites_say_what_to_do_when_npm_refuses_a_global_install_and_what_a_mac_s_logon_start_searches()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")));

        Assert.Contains(
            "Where `npm install -g` is refused with `EACCES`, npm's global prefix is a directory this account does not own: install it under a prefix of the account's own, `npm install -g --prefix ~/.local reply-cli`, and put `~/.local/bin` on the search path the runtime sees, as \"A runtime that can find it\" below says.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "On macOS, a runtime started at logon is a LaunchAgent, and launchd's default search path is `/usr/bin:/bin:/usr/sbin:/sbin`, which holds neither `~/.local/bin` nor a package manager's directory; the registration records the `PATH` of the terminal it was made from (`jason runtime autostart status` names it). The repair is to register again from a terminal whose `PATH` has `reply`, with `jason runtime autostart enable`, then `jason runtime restart`.",
            readme,
            StringComparison.Ordinal);
    }

    /// <summary>The changelog opens with 0.14.7, which says what the README now says of both, and that nothing else changed.</summary>
    [Fact]
    public void The_changelog_opens_with_0_14_7_the_refused_global_install_and_the_mac_s_logon_search_path()
    {
        var section = Flat(ChangelogSection(0));

        Assert.StartsWith(" ## 0.14.7 ", section, StringComparison.Ordinal);
        Assert.Contains("`npm install -g --prefix ~/.local reply-cli`", section, StringComparison.Ordinal);
        Assert.Contains("`/usr/bin:/bin:/usr/sbin:/sbin`", section, StringComparison.Ordinal);
        Assert.Contains("Nothing the package does has changed", section, StringComparison.Ordinal);
        Assert.StartsWith(" ## 0.14.6 ", Flat(ChangelogSection(1)), StringComparison.Ordinal);
    }

    /// <summary>The changelog's section at this position, newest first, from its heading up to the next one.</summary>
    private static string ChangelogSection(int position)
    {
        var changelog = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "CHANGELOG.md")).ReplaceLineEndings("\n");
        var start = changelog.IndexOf("\n## ", StringComparison.Ordinal);
        for (var skipped = 0; skipped < position; skipped++)
        {
            start = changelog.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        }

        var next = changelog.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return changelog[start..next];
    }

    /// <summary>A page's text with every run of white space one space, so a sentence is found however it is wrapped.</summary>
    private static string Flat(string text) => Regex.Replace(text, @"\s+", " ");
}
