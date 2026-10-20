using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
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
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_official_package_loads_and_asks_for_exactly_what_it_uses()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        var plugin = Assert.Single(registry.Plugins, listed => listed.Id == ReplyPlugins.PluginId);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Empty(plugin.Problems);
        Assert.Equal(PluginKind.Provider, plugin.Kind);
        Assert.Equal("0.5.0", plugin.Version);
        Assert.StartsWith("sha256:", plugin.Digest, StringComparison.Ordinal);
        Assert.Equal(
            ["campaign.activate", "campaign.create", "campaign.enroll", "campaign.get", "campaign.pause", "contact.delete", "contact.update", "conversation.classify", "conversation.close", "conversation.get", "conversation.list", "conversation.reopen", "conversation.reply", "engagement.summarize", "enrollment.list", "enrollment.stop", "list_membership.add", "optout.record", "sender.list", "social_invitation.send"],
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

    [Fact]
    public async Task The_program_it_declares_resolves_to_the_stand_in_and_answers_the_version_it_asks_for()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        var executable = Assert.Single(Assert.Single(registry.Plugins).Capabilities.Exec!.Requested);

        // The manifest asks for a minimum version, so the reload ran the version command on whatever it resolved.
        // That it answered, and that the answer satisfied the minimum, is why the package is valid at all.
        Assert.Equal("0.14.0", executable.MinVersion);
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
        Assert.Equal("0.5.0", result.Provenance.Version);
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
                "the contact proxy rule", "`reply contact find`", "LinkedIn URL identifies a person by its lowercased slug",
                "A person Reply holds is named by its identifier", "Only `find_or_create` imports.", "`contact_not_unique`",
                "Fields and lists travel with the operation that needs them.",
            },
            words => Assert.Contains(words, section, StringComparison.Ordinal));
    }

    /// <summary>
    /// Jason always asks Reply for a LinkedIn profile by its lowercased slug, and whether Reply's lookup ignores the slug's
    /// case is not known. What an operator is owed is that limit and what follows from it, where the README says what the
    /// package cannot do and in the release that brought the slug: a person Reply holds under a mixed-case profile link is
    /// not found, a second contact is created, and the opt-out lands on that one.
    /// </summary>
    [Fact]
    public void The_readme_and_the_changelog_say_a_mixed_case_profile_link_may_not_be_found()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("## What it cannot do", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section on what the package cannot do.");
        var section = Flat(readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)]);
        var entry = Flat(ChangelogSection(1));

        Assert.All(
            new[]
            {
                "Jason always asks Reply for a LinkedIn profile by its lowercased slug.",
                "Whether Reply's lookup ignores the slug's case is not verified.",
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
    /// The release that sends outside a sequence: the two operations it adds, the CLI it needs, and what a person reads in
    /// it about the stop Reply does not make on such a send, the note Reply refuses, and the one write before a send.
    /// </summary>
    [Fact]
    public void The_changelog_opens_with_0_5_0_and_the_two_operations_it_adds()
    {
        var section = Flat(ChangelogSection(0));

        Assert.StartsWith(" ## 0.5.0 ", section, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "`sender.list`", "`social_invitation.send`", "reply-cli 0.14.0", "`never`", "Reply refuses notes over 200 characters",
                "opted out", "a teammate", "never changed", "outside ASCII",
            },
            word => Assert.Contains(word, section, StringComparison.Ordinal));
    }

    [Fact]
    public void The_changelog_keeps_0_4_0_and_the_four_inbox_operations_it_adds()
    {
        var section = Flat(ChangelogSection(1));

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
        var section = ChangelogSection(2);

        Assert.StartsWith("\n## 0.3.2\n", section, StringComparison.Ordinal);
        Assert.Contains("`campaign.create`", section, StringComparison.Ordinal);
        Assert.Contains("`campaign_name_taken`", section, StringComparison.Ordinal);
        Assert.Contains("`campaign_name_ambiguous`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_changelog_keeps_0_3_1_and_the_rule()
    {
        var section = ChangelogSection(3);

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
