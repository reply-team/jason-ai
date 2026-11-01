using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

public class RuntimeStatusCommandTests
{
    private static readonly RuntimeDescriptor Descriptor = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    /// <summary>A runtime from before the dispatcher and the plugin registry existed: the fields are simply not in the body.</summary>
    private const string InfoWithoutADispatcher =
        "{\"runtime_version\":\"0.1.0-dev\",\"api_version\":\"v1\",\"instance_id\":\"rt_LIVE\",\"pid\":77,"
        + "\"started_at\":\"1970-01-01T00:00:00+00:00\",\"data_dir\":\"/home/u/.jason\","
        + "\"database\":{\"applied_migrations\":[\"20260913225419_InitialCreate\"]}}";

    private static string InfoJson(string instanceId) =>
        InfoJson(instanceId, new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher) =>
        InfoJson(instanceId, dispatcher, new PluginsInfo(2, "snp_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), true));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher, PluginsInfo plugins) =>
        InfoJson(instanceId, dispatcher, plugins, new RoutesInfo("rts_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), "fake-provider", 1, 3));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher, PluginsInfo plugins, RoutesInfo routes) =>
        InfoJson(instanceId, dispatcher, plugins, routes, update: null);

    /// <summary>The defaults of the chain above, with only the update section chosen by the test.</summary>
    private static string InfoJson(string instanceId, UpdateInfo? update) =>
        InfoJson(
            instanceId,
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
            new PluginsInfo(2, "snp_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), true),
            new RoutesInfo("rts_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), "fake-provider", 1, 3),
            update);

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher, PluginsInfo plugins, RoutesInfo routes, UpdateInfo? update) => JsonSerializer.Serialize(
        new SystemInfoResponse(
            "0.1.0-dev",
            "v1",
            instanceId,
            77,
            DateTimeOffset.UnixEpoch,
            "/home/u/.jason",
            new DatabaseInfo(["20260913225419_InitialCreate"], [], null),
            dispatcher,
            plugins,
            routes,
            update),
        JasonJson.Options);

    private static readonly UpdateInfo Newer = new(true, "0.2.0", new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero), "https://example.test/notes");

    /// <summary>
    /// The reviews this runtime has summoned, beside the counters they belong with. A loop that is running and
    /// has summoned nothing is exactly what somebody checking on it wants to see, and the number is no use in a
    /// field nothing reads.
    /// </summary>
    [Fact]
    public async Task Human_status_shows_how_many_reviews_have_been_summoned()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 1, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41, 7);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("| 7 summoned", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And zero is the number somebody actually checks for. "The loop is running and has summoned nothing"
    /// is a state worth seeing; a field that disappears at zero answers that question by saying nothing at
    /// all, which reads as a runtime too old to have the counter.
    /// </summary>
    [Fact]
    public async Task Human_status_shows_a_summons_count_of_none()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 1, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41, 0);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("| 0 summoned", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The descriptor publisher some of these tests launch runs on a thread of its own, and an exception
    /// escaping a bare thread does not fail a test — it terminates the test host, taking every unrelated test
    /// in the process with it and naming none of them. This is the way it really fails: the CLI it serves
    /// polls the same file, and a reader holds it in a way that denies a writer while it reads, so a publish
    /// landing mid-poll is refused.
    /// </summary>
    [Fact]
    public void A_descriptor_refused_for_the_whole_window_is_survived_rather_than_thrown()
    {
        using var dir = new TempPaths();
        Blocked(dir);

        RuntimeVerbs.Publish(dir, Descriptor);

        // It gave up rather than throwing, which is what keeps three hundred unrelated tests alive, and wrote
        // nothing; the test that was waiting for a descriptor is left to fail in its own words.
        Assert.False(File.Exists(dir.Paths.DescriptorFile));
    }

    /// <summary>
    /// And a refusal that ends inside the window is waited out rather than given up on. The publisher exists
    /// to answer a CLI that is polling the same file, so being refused once is the ordinary case and not the
    /// failure — what would be a failure is a descriptor that never arrives because the first try lost a race.
    /// </summary>
    /// <remarks>
    /// The thing that frees the path is a thread of its own and not a pool work item. The publisher retries for
    /// about a second and blocks the test's own thread while it does, so the release has to happen inside that
    /// second — and on a machine running several test assemblies at once, a queued work item can wait longer
    /// than that for a pool thread. Then the publisher gives up, the descriptor never arrives, and a test about
    /// retrying fails for want of a scheduler rather than for want of a retry.
    /// </remarks>
    [Fact]
    public void A_descriptor_refused_and_then_released_lands_inside_the_retry_window()
    {
        using var dir = new TempPaths();
        Blocked(dir);

        var freed = new Thread(() =>
        {
            Thread.Sleep(200);
            Directory.Delete(dir.Paths.DescriptorFile);
        })
        {
            IsBackground = true,
            Name = "descriptor-releaser",
        };

        freed.Start();
        RuntimeVerbs.Publish(dir, Descriptor);
        Assert.True(freed.Join(TimeSpan.FromSeconds(10)), "the thread that frees the descriptor's path never finished");

        // The publish that landed was made after the way was clear, which only a retry can have made: a single
        // attempt would have been refused and given up while the path was still taken.
        Assert.Equal("rt_LIVE", Instance(dir));
    }

    /// <summary>
    /// The descriptor's path, taken by something that is not a file, which is a refusal every operating system
    /// agrees on. The race that really happened is a reader holding the file against a writer — but that is a
    /// rule only Windows enforces, so staging it that way tested the publisher on one platform and the
    /// filesystem on the others.
    /// </summary>
    private static void Blocked(TempPaths dir)
    {
        Directory.CreateDirectory(dir.Paths.RunDirectory);
        Directory.CreateDirectory(dir.Paths.DescriptorFile);
    }

    private static string? Instance(TempPaths dir) =>
        JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllText(dir.Paths.DescriptorFile), JasonJson.Options)?.InstanceId;

    [Fact]
    public async Task No_descriptor_exits_3_with_no_descriptor_error()
    {
        using var dir = new TempPaths();
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new InvalidOperationException("must not be called")));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"no_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreachable_runtime_exits_3_with_runtime_unreachable()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new HttpRequestException("connection refused")));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"runtime_unreachable\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_token_exits_3_with_unauthorized()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}")));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"unauthorized\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Healthy_runtime_prints_the_exact_response_and_exits_0()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        HttpRequestMessage? seen = null;
        var body = InfoJson("rt_LIVE");
        var (env, output, error) = Environment(dir, new FakeHandler(request => { seen = request; return Response(HttpStatusCode.OK, body); }));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("http://127.0.0.1:5000/v1/system.info", seen.RequestUri!.ToString());
        Assert.Equal("Bearer", seen.Headers.Authorization!.Scheme);
        Assert.Equal("the-token", seen.Headers.Authorization.Parameter);
    }

    [Fact]
    [Critical(Family.R2, "a stale descriptor is not this runtime")]
    public async Task Instance_mismatch_exits_3_with_stale_descriptor()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_OTHER"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"stale_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_business_error_is_printed_verbatim_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string envelope = "{\"error\":{\"code\":\"something_odd\",\"message\":\"nope\",\"retryable\":false}}";
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.InternalServerError, envelope)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(envelope + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Api_error_whose_body_is_not_json_is_printed_as_an_envelope_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(string.Empty) }));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = JsonSerializer.Deserialize<ErrorResponse>(output.ToString(), JasonJson.Options)!.Error;
        Assert.Equal(CliErrors.RuntimeError, envelope.Code);
        Assert.True(envelope.Retryable);
    }

    /// <summary>The defaults of the chain above, with only the notifications section chosen by the test.</summary>
    private static string InfoJson(NotificationsInfo? notifications, DateTimeOffset? startedAt = null)
    {
        var info = JsonSerializer.Deserialize<SystemInfoResponse>(InfoJson("rt_LIVE"), JasonJson.Options)!;
        return JsonSerializer.Serialize(info with { Notifications = notifications, StartedAt = startedAt ?? info.StartedAt }, JasonJson.Options);
    }

    private static readonly DateTimeOffset NineOhSix = new(2026, 10, 1, 9, 6, 0, TimeSpan.Zero);

    private static NotificationChannelInfo Toast(string? outcome = null, string? reason = null, string? detail = null, bool on = true, string presence = "available", string? presenceDetail = null) =>
        new("toast", "toast", on, outcome, reason, null, null, detail, outcome is null ? null : NineOhSix, presence, presenceDetail);

    /// <summary>What each notification channel did last, one line for a person, in every state a runtime can be in.</summary>
    [Theory]
    [MemberData(nameof(NotificationLines))]
    public async Task Human_mode_says_what_each_notification_channel_did_last(string json, string expected)
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, json)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains($"Notify:     {expected}{System.Environment.NewLine}", output.ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<string, string> NotificationLines() => new()
    {
        { InfoJson(new NotificationsInfo(false, [Toast(on: false)], null, null)), "off" },
        { InfoJson(new NotificationsInfo(true, [Toast()], null, null)), "toast on, nothing sent yet" },
        {
            InfoJson(new NotificationsInfo(
                true,
                [Toast("delivered"), new NotificationChannelInfo("ntfy", "plugin", true, "failed", null, "notification_unreachable", "transient", null, NineOhSix)],
                "ntf_01K",
                NineOhSix)),
            "toast delivered at 09:06 UTC | ntfy failed (notification_unreachable) at 09:06 UTC"
        },
        { InfoJson(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "session 0", presence: "no_desktop", presenceDetail: "session 0")], "ntf_01K", NineOhSix)), "toast has no desktop (session 0)" },

        // Whether there is a desktop is this runtime's own fact, decided when it started. An outcome a line from an
        // earlier runtime carried — one in session 0, or one in the person's session — never outweighs it.
        { InfoJson(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "session 0")], "ntf_01K", NineOhSix), NineOhSix.AddHours(1)), "toast on, nothing sent since this runtime started" },

        // What a restart actually hands over: no outcome at all, since the runtime does not carry an earlier one's "no
        // desktop", beside a last notification older than this runtime. Nothing sent since it started, which is not "yet"
        // — with a desktop, and without one.
        { InfoJson(new NotificationsInfo(true, [Toast()], "ntf_01K", NineOhSix), NineOhSix.AddHours(1)), "toast on, nothing sent since this runtime started" },
        { InfoJson(new NotificationsInfo(true, [Toast(presence: "no_desktop", presenceDetail: "session 0")], "ntf_01K", NineOhSix), NineOhSix.AddHours(1)), "toast has no desktop (session 0), nothing sent since this runtime started" },

        // This runtime's own send that found none — a Linux bus with no notification daemon — is said as it is.
        { InfoJson(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "no notification daemon")], "ntf_01K", NineOhSix)), "toast has no desktop (no notification daemon)" },

        // The person's own earlier "off", on a toast that is on again, is not a failure: nothing was sent since, which
        // is not "yet" — toasts may have been sent before it was turned off.
        { InfoJson(new NotificationsInfo(true, [Toast("not_attempted", "off")], "ntf_01K", NineOhSix)), "toast on again, nothing sent since" },
        { InfoJson(new NotificationsInfo(true, [Toast("delivered", presence: "no_desktop", presenceDetail: "session 0")], "ntf_01K", NineOhSix)), "toast has no desktop (session 0)" },
        { InfoJson(new NotificationsInfo(true, [Toast("silenced", "disabled_for_application")], "ntf_01K", NineOhSix)), "toast silenced (disabled_for_application) at 09:06 UTC" },

        // Decided when the runtime started, so it is said before anything has been sent: the one visible sign of a
        // runtime running where nothing it shows reaches a desktop.
        { InfoJson(new NotificationsInfo(true, [Toast(presence: "no_desktop", presenceDetail: "session 0")], null, null)), "toast has no desktop (session 0), nothing sent yet" },
        { InfoJson(notifications: null), "unknown" },
    };

    [Fact]
    public async Task Human_mode_renders_lines_for_people()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains("Runtime:", text, StringComparison.Ordinal);
        Assert.Contains("rt_LIVE", text, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_prints_what_the_dispatcher_is_doing()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 2, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41, 3);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Dispatcher: running | tick 10 s | 2/4 attempts | last scan 2026-09-14 10:00:00 UTC",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_the_dispatcher_has_not_scanned_yet()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Dispatcher: running | tick 10 s | 0/4 attempts | last scan never", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_tolerates_a_runtime_that_reports_no_dispatcher()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoWithoutADispatcher)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Dispatcher: unknown", output.ToString(), StringComparison.Ordinal);
    }

    private static string InfoJson(SkillsInfo skills) => JsonSerializer.Serialize(
        JsonSerializer.Deserialize<SystemInfoResponse>(InfoJson("rt_LIVE"), JasonJson.Options)! with { Skills = skills },
        JasonJson.Options);

    /// <summary>
    /// What the runtime will teach its roles, counted: a role a launch would refuse is not a role that teaches nothing —
    /// a directory with no SKILL.md is copied and the host learns nothing from it, and the launcher does not refuse it —
    /// so the two are counted apart, and the refusals are named by their codes.
    /// </summary>
    public static TheoryData<string, string> SkillLines => new()
    {
        {
            "refused and teaching nothing",
            "Skills:     1 role taught | 1 would be refused (skill_too_large) | 1 teaches nothing | cap 4096 bytes"
        },
        { "teaching nothing alone", "Skills:     0 roles taught | 2 teach nothing | cap 4096 bytes" },
        { "the directory unreadable", "Skills:     The role skills directory could not be read: denied [skills_directory_unreadable]" },
        { "a runtime without codes", "Skills:     1 role taught | 1 would be refused | cap 4096 bytes" },
    };

    [Theory]
    [MemberData(nameof(SkillLines))]
    public async Task Human_mode_counts_refused_roles_apart_from_roles_that_teach_nothing(string state, string expected)
    {
        var skills = state switch
        {
            "refused and teaching nothing" => new SkillsInfo("/r", 4096,
            [
                new DeployedRoleSkill("a", 10, null),
                new DeployedRoleSkill("b", 5000, "Too large.", SkillProblemCodes.SkillTooLarge),
                new DeployedRoleSkill("c", 3, "No SKILL.md.", SkillProblemCodes.SkillFileMissing),
            ]),
            "teaching nothing alone" => new SkillsInfo("/r", 4096,
            [
                new DeployedRoleSkill("a", 3, "No SKILL.md.", SkillProblemCodes.SkillFileMissing),
                new DeployedRoleSkill("b", 3, "No SKILL.md.", SkillProblemCodes.SkillFileMissing),
            ]),
            "the directory unreadable" => new SkillsInfo("/r", 4096, [], "The role skills directory could not be read: denied", SkillProblemCodes.SkillsDirectoryUnreadable, "UnauthorizedAccessException"),
            _ => new SkillsInfo("/r", 4096, [new DeployedRoleSkill("a", 10, null), new DeployedRoleSkill("b", 5000, "Too large.")]),
        };
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson(skills))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(expected, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_prints_what_the_plugin_registry_holds()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Plugins:    2 active | snapshot snp_01J4 | loaded 2026-09-14 12:00:00 UTC",
            output.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Where work is being sent, beside which packages are active: the two are one question, and a status that
    /// answered only half of it would send an operator to a second call to find the other half.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_where_work_is_being_sent()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Routes:     snapshot rts_01J4 | default fake-provider | 1 override | 3 campaign routes",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_when_nothing_is_routed_anywhere()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0);
        var plugins = new PluginsInfo(2, "snp_01J4", DateTimeOffset.UnixEpoch, true);
        var routes = new RoutesInfo("rts_EMPTY", DateTimeOffset.UnixEpoch, null, 0, 0);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher, plugins, routes))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Routes:     nothing is routed anywhere | snapshot rts_EMPTY", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_that_the_last_load_was_refused()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var plugins = new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, false);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher, plugins))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Plugins:    none active | last reload rejected", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_tolerates_a_runtime_that_reports_no_plugins()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoWithoutADispatcher)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Plugins:    unknown", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Routes:     unknown", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the runtime learned about newer versions, on the page an operator reads first. Three states, told
    /// apart: something newer, nothing newer, and never looked — the last being what a runtime younger than its
    /// own initial delay says, and which must not be dressed up as "up to date".
    /// </summary>
    [Fact]
    public async Task Human_mode_says_a_newer_version_is_available()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", Newer))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Update:     0.2.0 available (checked 2026-09-19 08:00:00 UTC)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_the_runtime_is_up_to_date()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", Newer with { Available = false, Version = "0.1.0-dev" }))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Update:     up to date (checked 2026-09-19 08:00:00 UTC)", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a runtime that has not looked yet")]
    [InlineData("a runtime from before the section existed")]
    public async Task Human_mode_says_when_no_check_has_happened(string which)
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var body = which.StartsWith("a runtime that", StringComparison.Ordinal) ? InfoJson("rt_LIVE", update: null) : InfoWithoutADispatcher;
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, body)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Update:     not checked yet", output.ToString(), StringComparison.Ordinal);
    }

    private static (CliEnvironment Env, StringWriter Out, StringWriter Error) Environment(TempPaths dir, HttpMessageHandler handler)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliEnvironment(output, error, dir.Paths, handler), output, error);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
