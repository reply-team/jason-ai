using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

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
        Assert.Equal("0.3.0", plugin.Version);
        Assert.StartsWith("sha256:", plugin.Digest, StringComparison.Ordinal);
        Assert.Equal(
            ["campaign.activate", "campaign.enroll", "campaign.get", "campaign.pause", "conversation.get", "conversation.list", "engagement.summarize", "enrollment.list", "enrollment.stop", "list_membership.add", "optout.record"],
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
        Assert.Equal("0.11.0", executable.MinVersion);
        Assert.NotNull(executable.Version);
        Assert.NotNull(executable.MinVersion);
        Assert.True(Version.Parse(executable.Version) >= Version.Parse(executable.MinVersion));

        // And what answered is ours. A machine with a real CLI installed must never be the one that answered.
        ReplyPlugins.AssertInsideTheTestTree(executable.Path);
        Assert.Equal(ReplyPlugins.ExecutablePath, executable.Path);
    }

    /// <summary>
    /// Reply's endpoints are reply-cli's to know. The package names one, the contact search it makes for a person Reply
    /// will not import, and no other: an endpoint named anywhere else in it would be a call that none of the CLI's own
    /// guards — the read before a write, the retries, the one answer for an outcome it could not see — ever reach.
    /// </summary>
    [Fact]
    public void The_package_names_no_reply_endpoint_but_the_contact_search()
    {
        var named = Directory.GetFiles(ReplyPlugins.PackageSource, "*.js", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"/v3/[A-Za-z0-9_\-/{}.]*").Select(match => match.Value.TrimEnd('.')))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["/v3/contacts/filter"], named);
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
        Assert.Equal("0.3.0", result.Provenance.Version);
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

    [Fact]
    public async Task A_route_to_this_package_may_name_an_account_and_nothing_else()
    {
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        // The binding is what an operator writes, so it is closed on purpose: a route names which account to act
        // as and which team to act in, and a key or a person to impersonate has no field here and never will.
        var binding = Assert.Single(registry.Plugins).BindingSchema;
        Assert.NotNull(binding);
        Assert.False(binding["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["profile", "team_id"], binding["properties"]!.AsObject().Select(property => property.Key).Order());
        Assert.Null(binding["required"]);
    }
}
