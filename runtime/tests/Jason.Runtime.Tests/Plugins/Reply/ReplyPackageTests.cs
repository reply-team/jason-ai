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
        Assert.Equal("0.10.0", plugin.Version);
        Assert.StartsWith("sha256:", plugin.Digest, StringComparison.Ordinal);
        Assert.Equal(
            ["campaign.activate", "campaign.create", "campaign.enroll", "campaign.get", "campaign.pause", "contact.delete", "contact.update", "conversation.classify", "conversation.close", "conversation.get", "conversation.list", "conversation.reopen", "conversation.reply", "engagement.summarize", "enrollment.list", "enrollment.stop", "list_membership.add", "message.send", "optout.record", "schedule.list", "sender.list", "social_invitation.send"],
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
        var root = Publication.RepositoryRoot();
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
        Assert.Equal("0.10.0", result.Provenance.Version);
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
    /// The package reads named arguments only, so the provider's reserved slot is ignored, and the README says so where
    /// it says what the package does.
    /// </summary>
    [Fact]
    public void The_readme_says_the_provider_slot_is_ignored()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var section = Flat(readme[readme.IndexOf("## What it does", StringComparison.Ordinal)..readme.IndexOf("## What you need", StringComparison.Ordinal)]);

        Assert.Contains(
            "This package ignores the reserved `provider` object a work item may carry beside an operation's arguments: it reads none of it, passes none of it to the CLI, and an operation runs the same with it or without it.",
            section,
            StringComparison.Ordinal);
    }

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
        var entry = Flat(ChangelogSection(9));

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
    /// The release in which an email composed as a template goes in Reply's syntax and Reply fills it in: what is refused
    /// before anything is called, what Reply's own refusal is, and a final text sent as before, save the prompt marker.
    /// </summary>
    [Fact]
    public void The_changelog_opens_with_0_10_0_and_the_template_it_sends()
    {
        var section = Flat(ChangelogSection(0));

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
        var section = Flat(ChangelogSection(1));

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
        var section = Flat(ChangelogSection(2));

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
        var section = Flat(ChangelogSection(3));

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
        var section = Flat(ChangelogSection(4));

        Assert.StartsWith(" ## 0.8.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`schedule.list`", "`reply schedule list`", $"reply-cli {ReplyCliRelease.Version}", "the lowest first",
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
        var section = Flat(ChangelogSection(5));

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
        var section = Flat(ChangelogSection(6));

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
        var section = Flat(ChangelogSection(7));

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
    /// The plugin needs the person's own fields in what a write that may create them is handed, and the first Jason release
    /// that projects them is named where an operator reads what the package needs.
    /// </summary>
    [Fact]
    public void The_readme_names_the_jason_release_that_projects_the_person_s_fields()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("## What you need", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section on what the package needs.");
        var section = Flat(readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)]);

        Assert.Contains("**Jason 0.14.0 or newer.**", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Jason 0.13.0 or newer", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release that sends outside a sequence: the three operations it adds, the CLI it needs, and what a person reads
    /// in it about the stop Reply does not make on such a send, the note Reply refuses, the one write before a send, the
    /// email written as HTML, and the address keyed as the runtime keys it.
    /// </summary>
    [Fact]
    public void The_changelog_keeps_0_5_0_and_the_three_operations_it_adds()
    {
        var section = Flat(ChangelogSection(8));

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
        var section = Flat(ChangelogSection(9));

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
        var section = ChangelogSection(10);

        Assert.StartsWith("\n## 0.3.2\n", section, StringComparison.Ordinal);
        Assert.Contains("`campaign.create`", section, StringComparison.Ordinal);
        Assert.Contains("`campaign_name_taken`", section, StringComparison.Ordinal);
        Assert.Contains("`campaign_name_ambiguous`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_changelog_keeps_0_3_1_and_the_rule()
    {
        var section = ChangelogSection(11);

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
    /// which the package refuses before anything is called.
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
                "The refusals come in one order: a final text is read for a placeholder or a prompt, then its subject for a tag's start, then its body; a template is read part by part, its subject first, each for a construct the standard refuses, then a key Reply keeps, then a comparison Reply cannot read, then a prompt, then a tag's start.",
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
