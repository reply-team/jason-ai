using System.Globalization;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Status;

/// <summary>
/// The checks themselves, one method each. Every one of them answers a fact and, where there is one, the
/// command that repairs it.
/// </summary>
/// <remarks>
/// <para>
/// Half of what this verb reports is not the runtime's to know — an executable on PATH, another vendor's CLI,
/// files in a folder in the operator's home directory — which is why no API operation can answer "am I ready
/// to work?" and why this is the one command that is not one-to-one with one.
/// </para>
/// <para>
/// Nothing here starts a process except through <see cref="IProgramRunner"/>, and nothing here reads a log.
/// Both are held by guards, because a status verb that grows a probe has become a diagnostics verb.
/// </para>
/// </remarks>
internal static class StatusChecks
{
    /// <summary>
    /// The repairs a check can print, named in one place.
    /// </summary>
    /// <remarks>
    /// They were null until the verb existed, deliberately: a check whose repair is a command this build does
    /// not answer teaches a person the tool is broken at the moment they most need it not to be. The guard
    /// beside this one types every repair printed here, so the commit that shipped the verb is the commit that
    /// turned these on and proved them.
    /// </remarks>
    private static class Repair
    {
        public const string Install = "jason skills install";

        public const string Reinstall = "jason skills install --force";
    }

    /// <summary>
    /// The one argument the provider check passes. The <em>program</em> is named by whoever runs this and is
    /// never named here: A7(a) makes vendor neutrality a property of the sources, and a guard reads it off
    /// them. Jason knowing the name of somebody's provider CLI is exactly the coupling that rule forbids.
    /// </summary>
    public static IReadOnlyList<string> ProviderArguments => ["--version"];

    /// <summary>
    /// How long the provider is given to answer. A check that hangs is a status verb that never answers, which
    /// is worse than one that says it could not tell.
    /// </summary>
    public static TimeSpan ProviderTimeout => TimeSpan.FromSeconds(10);

    public static async Task<StatusReport> ComposeAsync(CliEnvironment env, string? providerCli, CancellationToken cancellationToken)
    {
        var checks = new List<StatusCheck>();

        var descriptor = new DescriptorReader(env.Paths).Read();
        SystemInfoResponse? info = null;
        RuntimeClient? client = null;
        try
        {
            if (descriptor is not null)
            {
                client = new RuntimeClient(descriptor, env.HttpHandler);
                info = await ReadAsync<SystemInfoResponse>(client, Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
            }

            checks.Add(Runtime(descriptor, info));
            checks.Add(Migrations(info));
            checks.Add(Plugins(info));
            checks.Add(await RoleSkillsAsync(client, info, cancellationToken).ConfigureAwait(false));
            checks.Add(Path(env));

            checks.Add(ProviderPlugin(info));
            var routes = client is null ? null : await ReadAsync<RoutesDto>(client, Operations.RouteList, cancellationToken).ConfigureAwait(false);
            checks.Add(Route(routes));
            checks.Add(Binding(routes));
            checks.Add(await ProviderCliAsync(env, providerCli, cancellationToken).ConfigureAwait(false));
            checks.Add(Harnesses(env));
            checks.Add(Autostart(env));
        }
        finally
        {
            client?.Dispose();
        }

        return new StatusReport(checks.All(check => !check.Required || check.State == CheckState.Ok), checks);
    }

    private static StatusCheck Runtime(Contracts.Discovery.RuntimeDescriptor? descriptor, SystemInfoResponse? info)
    {
        if (descriptor is null)
        {
            return new StatusCheck("runtime", true, CheckState.Failed, "No runtime is running: there is no endpoint descriptor to ask.", "jason runtime start");
        }

        return info is null
            ? new StatusCheck("runtime", true, CheckState.Failed, $"A descriptor names {descriptor.BaseUrl} and nothing answered there.", "jason runtime start")
            : new StatusCheck("runtime", true, CheckState.Ok, $"Running: version {info.RuntimeVersion}, pid {info.Pid.ToString(CultureInfo.InvariantCulture)}, data directory {info.DataDir}.", null);
    }

    private static StatusCheck Migrations(SystemInfoResponse? info)
    {
        if (info is null)
        {
            return new StatusCheck("migrations", true, CheckState.Unknown, "The runtime did not answer, so its database was not asked about.", "jason runtime start");
        }

        var applied = info.Database.AppliedMigrations.Count;
        return applied == 0
            ? new StatusCheck("migrations", true, CheckState.Failed, "The database has no migrations applied.", "jason runtime restart")
            : new StatusCheck("migrations", true, CheckState.Ok, string.Create(CultureInfo.InvariantCulture, $"{applied} migrations applied."), null);
    }

    private static StatusCheck Plugins(SystemInfoResponse? info)
    {
        if (info is null)
        {
            return new StatusCheck("plugin_registry", true, CheckState.Unknown, "The runtime did not answer, so its plugin registry was not asked about.", "jason runtime start");
        }

        // Zero plugins is alive. A registry that refused its last load is not, and that is the state worth
        // telling apart: nothing is loaded, on purpose, and nothing will run until somebody looks at why.
        return info.Plugins.LastReloadActivated == false
            ? new StatusCheck("plugin_registry", true, CheckState.Failed, "The last plugin load was rejected, so nothing is active.", "jason plugin reload")
            : new StatusCheck(
                "plugin_registry",
                true,
                CheckState.Ok,
                string.Create(CultureInfo.InvariantCulture, $"Alive: {info.Plugins.ActiveCount} plugins active, snapshot {info.Plugins.SnapshotId}."),
                null);
    }

    /// <summary>
    /// The hole this wave exists to close, reported. A runtime that cannot teach its roles is not ready,
    /// whatever else is true — and the check is not only presence: a deployment the launcher would refuse is
    /// worse than none, because it refuses every launch of that role rather than running it untaught.
    /// </summary>
    private static async Task<StatusCheck> RoleSkillsAsync(RuntimeClient? client, SystemInfoResponse? info, CancellationToken cancellationToken)
    {
        if (info?.Skills is not { } skills)
        {
            return new StatusCheck("role_skills", true, CheckState.Unknown, "The runtime did not answer, so its role skills were not asked about.", "jason runtime start");
        }

        if (skills.Problem is { } problem)
        {
            return new StatusCheck("role_skills", true, CheckState.Failed, problem, null);
        }

        var roster = client is null
            ? []
            : (await ReadAsync<Page<RoleDto>>(client, Operations.RoleList, cancellationToken).ConfigureAwait(false))?.Items ?? [];
        var seeded = roster.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var deployed = skills.Roles.Select(role => role.Role).ToHashSet(StringComparer.Ordinal);

        var refused = skills.Roles.Where(role => role.Problem is not null).ToList();
        if (refused.Count > 0)
        {
            return new StatusCheck("role_skills", true, CheckState.Failed, string.Join(" ", refused.Select(role => role.Problem)), Repair.Reinstall);
        }

        var untaught = seeded.Except(deployed).Order(StringComparer.Ordinal).ToList();
        if (untaught.Count > 0)
        {
            return new StatusCheck(
                "role_skills",
                true,
                CheckState.Failed,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{untaught.Count} of {seeded.Count} seeded roles have no skill, so they would launch untaught: {string.Join(", ", untaught)}."),
                Repair.Install);
        }

        var stranger = deployed.Except(seeded).Order(StringComparer.Ordinal).ToList();
        var note = stranger.Count == 0
            ? string.Empty
            : $" {string.Join(", ", stranger.Select(name => $"'{name}'"))} is deployed and is not a role this runtime seeds, so nothing reads it.";

        return new StatusCheck(
            "role_skills",
            true,
            CheckState.Ok,
            string.Create(CultureInfo.InvariantCulture, $"{deployed.Count} roles taught, all within the {skills.MaxSkillBytes}-byte cap.{note}"),
            null);
    }

    /// <summary>
    /// Whether the product can be typed by name, and which file answers when it is. Both halves matter: a
    /// second installation earlier on PATH is why a person's <c>jason</c> is a version they did not install.
    /// </summary>
    private static StatusCheck Path(CliEnvironment env)
    {
        var resolved = Executables.OnPath("jason", env.SearchPath);
        if (resolved is null)
        {
            return new StatusCheck("path", true, CheckState.Failed, "'jason' does not resolve on PATH, so nothing can be typed by name.", null);
        }

        var running = env.InstallPath;
        if (running is not null && !string.Equals(System.IO.Path.GetFullPath(running), System.IO.Path.GetFullPath(resolved), StringComparison.OrdinalIgnoreCase))
        {
            return new StatusCheck(
                "path",
                true,
                CheckState.Ok,
                $"'jason' resolves to {resolved}, which is not the file answering here ({running}): two installations are on this machine.",
                null);
        }

        return new StatusCheck("path", true, CheckState.Ok, $"'jason' resolves to {resolved}.", null);
    }

    private static StatusCheck ProviderPlugin(SystemInfoResponse? info) =>
        info is null
            ? new StatusCheck("provider_plugin", false, CheckState.Unknown, "The runtime did not answer.", null)
            : info.Plugins.ActiveCount == 0
                ? new StatusCheck("provider_plugin", false, CheckState.Absent, "No provider plugin is installed, so no campaign can reach a provider yet.", null)
                : new StatusCheck("provider_plugin", false, CheckState.Ok, string.Create(CultureInfo.InvariantCulture, $"{info.Plugins.ActiveCount} installed."), null);

    private static StatusCheck Route(RoutesDto? routes)
    {
        if (routes is null)
        {
            return new StatusCheck("route", false, CheckState.Unknown, "The runtime did not answer.", null);
        }

        var configured = routes.Global.Default is not null || routes.Global.Operations.Count > 0 || routes.Campaigns.Count > 0;
        return configured
            ? new StatusCheck("route", false, CheckState.Ok, $"Routed: default {routes.Global.Default?.PluginId ?? "none"}.", null)
            : new StatusCheck("route", false, CheckState.Absent, "Nothing is routed anywhere yet.", "jason route set --plugin <id>");
    }

    private static StatusCheck Binding(RoutesDto? routes)
    {
        if (routes is null)
        {
            return new StatusCheck("binding", false, CheckState.Unknown, "The runtime did not answer.", null);
        }

        var bound = Every(routes).Any(route => route.Binding is not null);
        return bound
            ? new StatusCheck("binding", false, CheckState.Ok, "A route names an account.", null)
            : new StatusCheck("binding", false, CheckState.Absent, "No route names an account yet.", null);
    }

    private static IEnumerable<RouteDto> Every(RoutesDto routes)
    {
        if (routes.Global.Default is { } global)
        {
            yield return global;
        }

        foreach (var route in routes.Global.Operations.Values)
        {
            yield return route;
        }

        foreach (var campaign in routes.Campaigns)
        {
            if (campaign.Default is { } fallback)
            {
                yield return fallback;
            }

            foreach (var route in campaign.Operations.Values)
            {
                yield return route;
            }
        }
    }

    /// <summary>
    /// Whether the provider's own CLI is on this machine and answering. It names the command it runs, it is
    /// bounded, and it prints no credential: not the key, not a prefix of it, not its length.
    /// </summary>
    /// <remarks>
    /// The program is named by the caller, never by this build. Which provider somebody uses is theirs, and a
    /// readiness check that knew one vendor's command by heart would be this product naming a vendor in its
    /// own sources -- which A7(a) forbids and a guard enforces.
    /// </remarks>
    private static async Task<StatusCheck> ProviderCliAsync(CliEnvironment env, string? providerCli, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerCli))
        {
            return new StatusCheck(
                "provider_cli",
                false,
                CheckState.Absent,
                "No provider CLI was named, so none was checked. Name one with --provider-cli <program> and it will be run with --version.",
                null);
        }

        var command = $"{providerCli} {string.Join(' ', ProviderArguments)}";
        if (env.Programs is not { } runner)
        {
            return new StatusCheck("provider_cli", false, CheckState.Unknown, $"This environment cannot run programs, so '{command}' was not run.", null);
        }

        var result = await runner.RunAsync(providerCli, ProviderArguments, null, ProviderTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            return new StatusCheck("provider_cli", false, CheckState.Absent, $"'{command}' did not answer in {ProviderTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.", null);
        }

        return result.ExitCode == 0
            ? new StatusCheck("provider_cli", false, CheckState.Ok, $"'{command}' answered.", null)
            : new StatusCheck("provider_cli", false, CheckState.Absent, $"'{command}' is not on this machine, or did not answer.", null);
    }

    private static StatusCheck Harnesses(CliEnvironment env)
    {
        if (env.Harnesses is not { } locator)
        {
            return new StatusCheck("harness_skills", false, CheckState.Unknown, "This environment does not detect agent harnesses.", null);
        }

        var roots = locator.Detect();
        if (roots.Count == 0)
        {
            return new StatusCheck("harness_skills", false, CheckState.Absent, "No agent harness was found on this machine.", null);
        }

        var deployed = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                var record = SkillsRecord.Read(root.Directory);
                if (record is not null)
                {
                    deployed.AddRange(record.Packs.Select(pack => $"{pack.Pack} at {pack.Ref} in {root.Directory}"));
                }
            }
            catch (SkillsRecordUnreadable unreadable)
            {
                // Never absent: a root whose record cannot be read is one nothing may guess at.
                return new StatusCheck("harness_skills", false, CheckState.Unknown, unreadable.Message, null);
            }
        }

        return deployed.Count == 0
            ? new StatusCheck("harness_skills", false, CheckState.Absent, $"Nothing is deployed into {string.Join(", ", roots.Select(root => root.Directory))}.", Repair.Install)
            : new StatusCheck("harness_skills", false, CheckState.Ok, string.Join("; ", deployed) + ".", null);
    }

    private static StatusCheck Autostart(CliEnvironment env)
    {
        if (env.Autostart is not { } registrar)
        {
            return new StatusCheck("autostart", false, CheckState.Unknown, "This environment does not read autostart registrations.", null);
        }

        AutostartState state;
        try
        {
            state = registrar.Read();
        }
        catch (AutostartException failure)
        {
            return new StatusCheck("autostart", false, CheckState.Unknown, failure.Message, null);
        }

        return state.Registered
            ? new StatusCheck("autostart", false, CheckState.Ok, "The runtime is registered to start at logon.", null)
            : new StatusCheck("autostart", false, CheckState.Absent, "The runtime is not registered to start at logon.", "jason runtime autostart enable");
    }

    private static async Task<T?> ReadAsync<T>(RuntimeClient client, string operation, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await client.PostAsync(operation, cancellationToken).ConfigureAwait(false);
            return response.IsSuccess ? JsonSerializer.Deserialize<T>(response.Body, JasonJson.Options) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
