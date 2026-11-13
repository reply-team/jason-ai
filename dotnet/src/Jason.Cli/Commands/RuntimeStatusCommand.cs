using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Skills;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime status</c>: read the descriptor, call <c>system.info</c>, print the exact response.
/// Any client-side failure is reported in the error envelope with exit code 3.
/// </summary>
public static class RuntimeStatusCommand
{
    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var reader = new DescriptorReader(env.Paths);
        var look = reader.Look();
        if (look.Descriptor is not { } descriptor)
        {
            return Fail(env, CliErrors.NoDescriptor, reader.NoRuntime(look), retryable: !look.Denied);
        }

        using var client = new RuntimeClient(descriptor, env.AttemptFromEnvironment(), env.Error, env.HttpHandler, env.Clock);
        RuntimeResponse response;
        try
        {
            response = await client.PostAsync(Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer ({ex.Message}). The descriptor may be stale.", retryable: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer in time.", retryable: true);
        }

        if (response.StatusCode == 401)
        {
            return Fail(env, CliErrors.Unauthorized, "The runtime rejected the capability token from the descriptor; the descriptor is stale.", retryable: true);
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(OperationRunner.ErrorText(response));
            return ExitCodes.ApiError;
        }

        SystemInfoResponse? info;
        try
        {
            info = JsonSerializer.Deserialize<SystemInfoResponse>(response.Body, JasonJson.Options);
        }
        catch (JsonException)
        {
            info = null;
        }

        if (info is null || !string.Equals(info.InstanceId, descriptor.InstanceId, StringComparison.Ordinal))
        {
            return Fail(env, CliErrors.StaleDescriptor, $"The descriptor names instance '{descriptor.InstanceId}' but the runtime answering at {descriptor.BaseUrl} is '{info?.InstanceId ?? "unknown"}'.", retryable: true);
        }

        if (human)
        {
            RenderHuman(env.Out, info, descriptor);
        }
        else
        {
            env.Out.WriteLine(response.Body);
        }

        return ExitCodes.Success;
    }

    private static int Fail(CliEnvironment env, string code, string message, bool retryable)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable));
        return ExitCodes.RuntimeUnavailable;
    }

    /// <summary>Shared with <c>runtime start</c>, which reports a running runtime exactly the way status does.</summary>
    internal static void RenderHuman(TextWriter output, SystemInfoResponse info, RuntimeDescriptor descriptor)
    {
        output.WriteLine($"Runtime:    running (pid {info.Pid})");
        output.WriteLine($"Version:    {info.RuntimeVersion}");
        output.WriteLine($"Executable: {descriptor.InstalledImage ?? "none recorded: a build run from source, or a runtime older than this CLI"}");
        output.WriteLine($"API:        {info.ApiVersion} at {descriptor.BaseUrl}");
        output.WriteLine($"Port:       {Port(info.Listening)}");
        output.WriteLine($"Instance:   {info.InstanceId}");
        output.WriteLine($"Started:    {info.StartedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
        output.WriteLine($"Data dir:   {info.DataDir}");
        output.WriteLine($"Harness:    {Harness(info.Harness)}");
        output.WriteLine($"Migrations: {info.Database.AppliedMigrations.Count} applied");
        output.WriteLine($"Dispatcher: {Dispatcher(info.Dispatcher)}");
        output.WriteLine($"Plugins:    {Plugins(info.Plugins)}");
        output.WriteLine($"Packages:   {Packages(info.ReleasedPlugins)}");
        output.WriteLine($"Routes:     {Routes(info.Routes)}");
        output.WriteLine($"Skills:     {Skills(info.Skills)}");
        output.WriteLine($"UI:         {Ui(info.Ui)}");
        output.WriteLine($"Update:     {Update(info.Update)}");
        output.WriteLine($"Notify:     {Notifications(info.Notifications, info.StartedAt)}");
    }

    private static string Port(ListeningInfo? listening) => listening switch
    {
        null => "unknown",
        { ConfiguredPort: 0 } => $"{listening.Port} (chosen by the OS; set Runtime:Port for a stable one)",
        { Port: var port, ConfiguredPort: var configured } when port == configured => port.ToString(CultureInfo.InvariantCulture),
        _ => $"{listening.Port} ({listening.ConfiguredPort} was taken)",
    };

    /// <summary>The harness root the runtime reports and what stands there; a runtime older than the report says nothing of it.</summary>
    private static string Harness(HarnessInfo? harness) => harness?.RootState switch
    {
        null => "unknown: the runtime is older than this CLI",
        HarnessRootState.Absent => $"{harness.Root}, made at the first project or campaign",
        HarnessRootState.Marked => $"{harness.Root}, with its marker",
        HarnessRootState.Unmarked => $"{harness.Root}, a folder with no marker yet, taken as the root at the first project or campaign",
        HarnessRootState.Taken => $"{harness.Root}, marked as something else: the first project or campaign is refused until Harness:Root names another folder or that one is moved away",
        HarnessRootState.NotAFolder => $"{harness.Root}, a file: the first project or campaign is refused until Harness:Root names a folder or the file is moved away",
        HarnessRootState.Unreadable => $"{harness.Root}, which this account cannot list or read: the first project or campaign is refused until this account may read and write it or Harness:Root names a folder it can",

        // Every state the runtime says has its arm above; the words are read by name, never as numbers, so no other value
        // is ever read from an answer.
        _ => throw new ArgumentOutOfRangeException(nameof(harness), harness.RootState, "There is no such harness root state."),
    };

    /// <summary>How each plugin package this runtime's release publishes stands against the installed one.</summary>
    private static string Packages(IReadOnlyList<ReleasedPluginInfo>? packages) => packages switch
    {
        null => "unknown",
        [] => "none carried",
        _ => string.Join("; ", packages.Select(package => package.State switch
        {
            ReleasedPluginState.Current => $"{package.Id} {package.Installed?.Version}, this release's",
            ReleasedPluginState.Behind => $"{package.Id} {package.Installed?.Version}, behind this release's {package.Released.Version}",
            ReleasedPluginState.Differs => $"{package.Id} {package.Installed?.Version}, not this release's {package.Released.Version} (a later release's, or built or edited here)",
            ReleasedPluginState.NotInstalled => $"{package.Id} not installed, this release's is {package.Released.Version}",

            // Every state the runtime says has its arm above; the words are read by name, never as numbers.
            _ => throw new ArgumentOutOfRangeException(nameof(packages), package.State, "There is no such package state."),
        })),
    };

    private static string Ui(UiModuleInfo? ui) => ui switch
    {
        null => "not installed (`jason ui open` says how)",
        { Problem: { } problem } => $"{ui.Source} from {ui.Directory}, with a problem: {problem}",
        _ => $"{ui.Source} {ui.Version} from {ui.Directory}, API {ui.ApiVersion} ({(ui.ApiVersionMatches ? "this runtime's" : "not this runtime's")}), {ui.Digest}",
    };

    /// <summary>
    /// What each notification channel did last. Off is said as off; a channel that has sent nothing yet says so; and a
    /// toast with no desktop says it before anything has been sent, because that is decided when the runtime starts —
    /// a runtime in session 0 is the one place nothing it shows can reach a person — and says it again when one of this
    /// runtime's own sends found none.
    /// </summary>
    private static string Notifications(NotificationsInfo? notifications, DateTimeOffset startedAt)
    {
        if (notifications is null)
        {
            return "unknown";
        }

        var on = notifications.Channels.Where(channel => channel.On).ToList();
        if (!notifications.Enabled || on.Count == 0)
        {
            return "off";
        }

        return string.Join(" | ", on.Select(channel => Channel(channel, startedAt, notifications.LastAt)));

        static string Channel(NotificationChannelInfo channel, DateTimeOffset startedAt, DateTimeOffset? lastAt)
        {
            var at = channel.At is { } when ? " at " + when.ToUniversalTime().ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture) : string.Empty;
            var hidden = Status.NotificationReading.Hidden(channel, startedAt, lastAt);

            // This runtime's own desktop, before any outcome a line from an earlier one carried.
            if (Status.NotificationReading.NoDesktop(channel, startedAt) is { } noDesktop)
            {
                return channel.Outcome is not null ? $"{channel.Channel} has no desktop ({noDesktop})"
                    : hidden ? $"{channel.Channel} has no desktop ({noDesktop}), nothing sent since this runtime started"
                    : $"{channel.Channel} has no desktop ({noDesktop}), nothing sent yet";
            }

            // The person's own earlier "off" on a channel that is on again: nothing sent since, which is not "yet".
            if (Status.NotificationReading.OnAgain(channel))
            {
                return $"{channel.Channel} on again, nothing sent since";
            }

            return Status.NotificationReading.Outcome(channel, startedAt) switch
            {
                null when hidden => $"{channel.Channel} on, nothing sent since this runtime started",
                null => $"{channel.Channel} on, nothing sent yet",
                ChannelResults.Delivered => $"{channel.Channel} delivered{at}",
                ChannelResults.Silenced => $"{channel.Channel} silenced ({channel.Reason}){at}",
                ChannelResults.Failed => $"{channel.Channel} failed ({channel.Code}){at}",
                ChannelResults.NotAttempted => channel.Detail is { } detail
                    ? $"{channel.Channel} not attempted ({channel.Reason}: {detail})"
                    : $"{channel.Channel} not attempted ({channel.Reason})",
                var other => $"{channel.Channel} {other}{at}",
            };
        }
    }

    /// <summary>
    /// What the runtime will teach its roles from. A runtime older than the section reports none at all, and
    /// an empty root is worth spelling out: until something deploys there, every role this runtime launches
    /// runs untaught, quietly and by omission.
    /// </summary>
    private static string Skills(SkillsInfo? skills)
    {
        if (skills is null)
        {
            return "unknown";
        }

        if (skills.Problem is { } problem)
        {
            return skills.ProblemCode is { } code ? $"{problem} [{code}]" : problem;
        }

        var cap = string.Create(CultureInfo.InvariantCulture, $"cap {skills.MaxSkillBytes} bytes");
        if (skills.Roles.Count == 0)
        {
            return $"no role skills deployed | every role would launch untaught | {cap}";
        }

        // A directory with no SKILL.md is copied and teaches the host nothing; the launcher does not refuse it, so it is
        // not counted with the roles a launch would refuse. A runtime from before the codes cannot tell them apart, and
        // every problem it reports is counted as a refusal, as it always was.
        var nothing = skills.Roles.Count(role => role.ProblemCode == SkillProblemCodes.SkillFileMissing);
        var refused = skills.Roles.Where(role => role.Problem is not null && role.ProblemCode != SkillProblemCodes.SkillFileMissing).ToList();
        var parts = new List<string> { $"{Plural(skills.Roles.Count - refused.Count - nothing, "role")} taught" };
        if (refused.Count > 0)
        {
            var codes = refused.Select(role => role.ProblemCode).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{refused.Count} would be refused{(codes.Count > 0 ? $" ({string.Join(", ", codes)})" : string.Empty)}"));
        }

        if (nothing > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{nothing} {(nothing == 1 ? "teaches" : "teach")} nothing"));
        }

        parts.Add(cap);
        return string.Join(" | ", parts);
    }

    /// <summary>
    /// What the runtime learned about newer versions. Three states, told apart: a runtime that has not looked
    /// yet — younger than its own initial delay, or with the check turned off — is not "up to date" and says
    /// so, and a runtime from before the section existed reports none at all, which reads the same way.
    /// </summary>
    private static string Update(UpdateInfo? update)
    {
        if (update is null)
        {
            return "not checked yet";
        }

        var checkedAt = update.CheckedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        return update.Available
            ? $"{update.Version} available (checked {checkedAt})"
            : $"up to date (checked {checkedAt})";
    }

    /// <summary>
    /// Where work is being sent. A runtime older than routing reports no section at all, and a runtime that
    /// routes nothing anywhere is the state worth spelling out: no campaign will reach a provider until
    /// somebody writes a route.
    /// </summary>
    private static string Routes(RoutesInfo? routes)
    {
        if (routes is null)
        {
            return "unknown";
        }

        if (routes is { GlobalDefaultPlugin: null, GlobalOverrideCount: 0, CampaignRouteCount: 0 })
        {
            return $"nothing is routed anywhere | snapshot {routes.SnapshotId}";
        }

        // The counts above include the channel routes; a runtime that has any says so beside them, since a channel route
        // is one operation's route for one channel and reads as no other.
        var byChannel = routes.ChannelRouteCount is > 0 and var channels ? $" | {channels} of them by channel" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"snapshot {routes.SnapshotId} | default {routes.GlobalDefaultPlugin ?? "none"} | {Plural(routes.GlobalOverrideCount, "override")} | {Plural(routes.CampaignRouteCount, "campaign route")}{byChannel}");
    }

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    /// <summary>A runtime older than the dispatcher reports no section at all; say so rather than invent a state.</summary>
    private static string Dispatcher(DispatcherInfo? dispatcher)
    {
        if (dispatcher is null)
        {
            return "unknown";
        }

        var state = JsonNamingPolicy.SnakeCaseLower.ConvertName(dispatcher.State.ToString());
        var lastScan = dispatcher.LastScanAt is null
            ? "never"
            : dispatcher.LastScanAt.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        // The reviews this runtime has put on the queue since it started, beside the rest of the scan's
        // counters — and printed at zero too, because a loop that is running and has summoned nothing is
        // exactly the thing somebody runs this to see. A field that disappeared at zero would answer that
        // question by saying nothing, which is what a runtime too old to have the counter looks like.
        var summons = string.Create(CultureInfo.InvariantCulture, $" | {dispatcher.Summons} summoned");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{state} | tick {dispatcher.TickSeconds} s | {dispatcher.RunningAttempts}/{dispatcher.MaxParallel} attempts | last scan {lastScan}{summons}");
    }

    /// <summary>
    /// What the plugin registry holds. A runtime older than the registry reports no section at all, and an
    /// empty registry after a refused load is the one state worth spelling out: nothing is loaded on purpose.
    /// </summary>
    private static string Plugins(PluginsInfo? plugins)
    {
        if (plugins is null)
        {
            return "unknown";
        }

        if (plugins is { ActiveCount: 0, LastReloadActivated: false })
        {
            return "none active | last reload rejected";
        }

        var loadedAt = plugins.LoadedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{plugins.ActiveCount} active | snapshot {plugins.SnapshotId} | loaded {loadedAt}");
    }
}
