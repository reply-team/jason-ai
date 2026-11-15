using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Tests.Integration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// <c>profile.set_default</c> and <c>profile.unset_default</c>: the configured default execution profile,
/// <c>Roles:DefaultExecutionProfile</c>, named and taken away through the runtime. Each writes the settings file through
/// the runtime's one writer of it, which keeps every byte it does not own, and is in force when the request answers: the
/// next claim resolves to it, with no restart. A name must answer a profile when it is written; what the profile then
/// means for work is the claim's, as at every other level.
/// </summary>
public class DefaultProfileTests
{
    /// <summary>The settings file the runtime starts from: layout, a comment and a setting of the same section the writer must keep.</summary>
    private const string Original =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        "    \"Roles\": { \"MaxStdoutBytes\": 2097152 }\n" +
        "}\n";

    /// <summary>What the writer makes of <see cref="Original"/> for the default <c>house</c>: one member more, in the file's own layout.</summary>
    private const string WithHouse =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        "    \"Roles\": { \"MaxStdoutBytes\": 2097152, \"DefaultExecutionProfile\": \"house\" }\n" +
        "}\n";

    private static readonly object Ada = new { type = "human", id = "ada" };

    /// <summary>The recordings' moment, so a recorded profile's timestamps are the same on every run.</summary>
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Naming_a_default_writes_the_setting_keeps_every_other_byte_and_is_in_force_at_once()
    {
        await using var api = await StartAsync();
        await ProfileAsync(api, "house");

        var answer = await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house", actor = Ada, reason = "the roles run on the house host" }, Ct);

        Assert.Equal("house", answer.ExecutionProfile);
        Assert.Equal(WithHouse, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal("house", InForce(api));
        Assert.Equal(2_097_152, api.Resolve<LiveSettings<RolesOptions>>().Current.MaxStdoutBytes);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Equal(RolesOptions.Section + ":" + nameof(RolesOptions.DefaultExecutionProfile), line.Key);
        Assert.Null(line.Old);
        Assert.Equal("house", line.New!.GetValue<string>());
        Assert.Equal("the roles run on the house host", line.Reason);
        Assert.Equal(("human", "ada"), (line.Actor.Type.ToString().ToLowerInvariant(), line.Actor.Id));
        Assert.Null(line.CampaignId);
    }

    [Fact]
    public async Task A_file_with_no_roles_section_is_given_one()
    {
        await using var api = await StartAsync("{\n    \"Dispatcher\": { \"Enabled\": false }\n}\n");
        await ProfileAsync(api, "house");

        await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house" }, Ct);

        var roles = JsonNode.Parse(File.ReadAllText(api.Paths.UserSettingsFile))!["Roles"]!.AsObject();
        Assert.Equal("house", Assert.Single(roles).Value!.GetValue<string>());
        Assert.Equal("house", InForce(api));
    }

    [Fact]
    public async Task Naming_another_default_replaces_the_one_in_force_and_says_both()
    {
        await using var api = await StartAsync();
        await ProfileAsync(api, "house");
        await ProfileAsync(api, "night-shift");
        await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house" }, Ct);

        var answer = await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "night-shift" }, Ct);

        Assert.Equal("night-shift", answer.ExecutionProfile);
        Assert.Equal("night-shift", InForce(api));
        var lines = (await LinesAsync(api)).Items;
        Assert.Equal(2, lines.Count);
        var replaced = lines.Single(line => line.Old is not null);
        Assert.Equal(("house", "night-shift"), (replaced.Old!.GetValue<string>(), replaced.New!.GetValue<string>()));
    }

    /// <summary>The default already named is the state asked for: nothing is written and the chronicle says nothing.</summary>
    [Fact]
    public async Task Naming_the_default_already_in_force_writes_nothing_and_journals_nothing()
    {
        await using var api = await StartAsync(WithHouse);
        await ProfileAsync(api, "house");
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var answer = await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house" }, Ct);

        Assert.Equal("house", answer.ExecutionProfile);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>A disabled profile is still a profile: it may be the default, and work meets it at the claim.</summary>
    [Fact]
    public async Task A_disabled_profile_may_be_named_the_default()
    {
        await using var api = await StartAsync();
        await ProfileAsync(api, "house");
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "house" }, Ct);

        var answer = await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house" }, Ct);

        Assert.Equal("house", answer.ExecutionProfile);
        Assert.Equal("house", InForce(api));
    }

    [Fact]
    public async Task Taking_the_default_away_removes_the_setting_and_the_section_it_leaves_empty()
    {
        await using var api = await StartAsync("{\n    \"Dispatcher\": { \"Enabled\": false },\n    \"Roles\": { \"DefaultExecutionProfile\": \"house\" }\n}\n");
        await ProfileAsync(api, "house");

        var answer = await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileUnsetDefault, new { actor = Ada, reason = "no default any more" }, Ct);

        Assert.Null(answer.ExecutionProfile);
        Assert.Null(InForce(api));
        Assert.Null(JsonNode.Parse(File.ReadAllText(api.Paths.UserSettingsFile))!["Roles"]);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Equal("house", line.Old!.GetValue<string>());
        Assert.Null(line.New);
        Assert.Equal("no default any more", line.Reason);
    }

    [Fact]
    public async Task Taking_the_default_away_keeps_the_rest_of_its_section()
    {
        await using var api = await StartAsync(WithHouse);
        await ProfileAsync(api, "house");

        await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileUnsetDefault, new { }, Ct);

        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Null(InForce(api));
    }

    [Fact]
    public async Task Taking_away_a_default_nobody_named_writes_nothing_and_answers_none()
    {
        await using var api = await StartAsync();
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var answer = await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileUnsetDefault, new { }, Ct);

        Assert.Null(answer.ExecutionProfile);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>A name nothing answers would refuse every claim afterwards: it is refused when it is written, and nothing is.</summary>
    [Fact]
    public async Task A_name_no_profile_answers_is_refused_and_nothing_is_written()
    {
        await using var api = await StartAsync();
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(Operations.ProfileSetDefault, new { name = "never-registered" }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("profile_not_found", error.Code);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("  ", "required")]
    [InlineData("Not A Name", "invalid")]
    public async Task A_missing_or_misshapen_name_is_refused_on_the_field(string? name, string code)
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.ProfileSetDefault, new { name }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("name", code), (detail.Field, detail.Code));
    }

    [Fact]
    public async Task A_reason_too_long_is_refused_on_the_field()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.ProfileUnsetDefault, new { reason = new string('x', 2001) }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal(("reason", "too_long"), (error.Details![0].Field, error.Details[0].Code));
    }

    /// <summary>The default is Jason's own setting: from inside a run it is refused by the header, before the body is read, and nothing is written.</summary>
    [Theory]
    [InlineData(Operations.ProfileSetDefault)]
    [InlineData(Operations.ProfileUnsetDefault)]
    public async Task From_inside_a_run_it_is_refused_and_writes_nothing(string operation)
    {
        await using var api = await StartAsync(WithHouse);
        await ProfileAsync(api, "night-shift");
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);
        var inside = new Dictionary<string, string> { ["Jason-Attempt"] = "att_01K7QN0000000000000000000R" };

        var (status, text) = await api.PostAsync(operation, new { name = "night-shift", actor = Ada }, inside, Ct);

        Assert.True(status == HttpStatusCode.Forbidden, $"{operation} answered {(int)status}: {text}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        Assert.Equal("decided_inside_a_run", error.Code);
        Assert.Equal(
            "This request comes from inside attempt `att_01K7QN0000000000000000000R`. Nothing inside a run changes Jason itself, and which profile agent work runs on by default is part of it: leave it to the person, or raise it as a decision.",
            error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Equal("house", InForce(api));
    }

    /// <summary>A <c>JASON_</c> variable that sets the default is what is in force, whatever the file says: both acts refuse it, and nothing is written.</summary>
    [Theory]
    [InlineData(Operations.ProfileSetDefault)]
    [InlineData(Operations.ProfileUnsetDefault)]
    public async Task A_default_an_environment_variable_sets_is_refused_as_shadowed(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, Original, new UTF8Encoding(false)),
            configurationBuilt: configuration => configuration.Providers.OfType<EnvironmentVariablesConfigurationProvider>().Single()
                .Set("Roles:DefaultExecutionProfile", "house"));
        await ProfileAsync(api, "house");
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(operation, new { name = "house" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("setting_shadowed", error.Code);
        Assert.Equal(
            "Roles:DefaultExecutionProfile is set by the environment variable JASON_Roles__DefaultExecutionProfile, which overrides the settings file, so nothing was written; remove the variable or change it, then restart the runtime.",
            error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// A Roles section a person broke by hand keeps the runtime on the last Roles settings that validated, so a default
    /// written beside it would not be in force: the act is refused, the file is put back as the person left it, and the
    /// answer says so.
    /// </summary>
    [Fact]
    public async Task A_default_the_runtime_would_not_put_in_force_is_refused_and_the_file_is_put_back()
    {
        await using var api = await StartAsync();
        await ProfileAsync(api, "house");
        const string Broken = "{\n    \"Dispatcher\": { \"Enabled\": false },\n    \"Roles\": { \"MaxStdoutBytes\": 1 }\n}\n";
        File.WriteAllText(api.Paths.UserSettingsFile, Broken, new UTF8Encoding(false));
        Assert.Null(api.Resolve<SettingsFile>().Read());
        Assert.Equal(2_097_152, api.Resolve<LiveSettings<RolesOptions>>().Current.MaxStdoutBytes);

        var error = await api.PostErrorAsync(Operations.ProfileSetDefault, new { name = "house" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("setting_not_in_force", error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(
            "Roles:DefaultExecutionProfile cannot be put in force: the Roles settings in the file are refused, so the runtime keeps the last ones that validated; its log names the setting it refused. Mend the Roles section, then run the command again.",
            error.Message);
        var last = error.Details![^1];
        Assert.Equal(("settings.json", "settings_file_put_back"), (last.Field, last.Code));
        Assert.Equal(Broken, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Null(InForce(api));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// The point of the verb: named through the runtime, the default is what the next claim resolves a role's work to, with
    /// no restart; taken away, the work that follows falls back to the role's own command again. The house profile names a
    /// program nobody installed, so the claim that resolves to it refuses before a child exists and says so.
    /// </summary>
    [Fact]
    public async Task A_role_resolves_to_the_default_named_through_the_runtime_with_no_restart()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "house", host = "claude_code", program = "a-host-nobody-installed", deny = new[] { "Write" }, host_version_verified = "0.0.0-stand-in" },
            Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "succeed");

        var before = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "profiled", context = new { brief = "before" } }, Ct);
        await host.ScanAsync(Ct);
        var ranOwn = await host.WaitForStatusAsync(before.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal(ProfileResolutionSource.RoleEntryCommand, ranOwn.Attempts![0].Provenance!.Agent!.ResolutionSource);

        await host.Fixture.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house" }, Ct);
        var named = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "profiled", context = new { brief = "named" } }, Ct);
        await host.ScanAsync(Ct);
        var refused = await host.WaitForStatusAsync(named.Id, WorkItemStatus.Failed, Ct);
        var agent = refused.Attempts![0].Provenance!.Agent!;
        Assert.Equal(ProfileResolutionSource.GlobalDefault, agent.ResolutionSource);
        Assert.Equal("house", agent.ProfileName);
        Assert.Equal(AttemptErrors.HostNotAvailable, refused.LastError!.Code);

        await host.Fixture.PostOkAsync<ProfileDefaultDto>(Operations.ProfileUnsetDefault, new { }, Ct);
        var after = await host.CreateAsync(new { campaign_id = campaign, kind = "ai_role", role = "profiled", context = new { brief = "after" } }, Ct);
        await host.ScanAsync(Ct);
        var ranOwnAgain = await host.WaitForStatusAsync(after.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal(ProfileResolutionSource.RoleEntryCommand, ranOwnAgain.Attempts![0].Provenance!.Agent!.ResolutionSource);
    }

    /// <summary>
    /// <c>profile.list</c> says which name is the default in force, on every page and whatever the listing leaves out, and
    /// says a name that answers no profile too: written by hand, it is what every claim that falls back to it will refuse on.
    /// </summary>
    [Fact]
    public async Task The_listing_says_the_default_in_force_even_where_it_names_no_profile()
    {
        await using var api = await StartAsync();
        await ProfileAsync(api, "house");
        await ProfileAsync(api, "night-shift");

        var none = await api.PostOkAsync<ProfilePageDto>(Operations.ProfileList, new { }, Ct);
        Assert.Null(none.DefaultExecutionProfile);

        await api.PostOkAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, new { name = "house" }, Ct);
        var named = await api.PostOkAsync<ProfilePageDto>(Operations.ProfileList, new { limit = 1, cursor = (await api.PostOkAsync<ProfilePageDto>(Operations.ProfileList, new { limit = 1 }, Ct)).NextCursor }, Ct);
        Assert.Equal(["night-shift"], named.Items.Select(profile => profile.Name));
        Assert.Equal("house", named.DefaultExecutionProfile);

        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "house" }, Ct);
        Assert.Equal("house", (await api.PostOkAsync<ProfilePageDto>(Operations.ProfileList, new { }, Ct)).DefaultExecutionProfile);

        File.WriteAllText(api.Paths.UserSettingsFile, WithHouse.Replace("\"house\"", "\"nobody-made-this\"", StringComparison.Ordinal), new UTF8Encoding(false));
        Assert.Null(api.Resolve<SettingsFile>().Read());
        var dangling = await api.PostOkAsync<ProfilePageDto>(Operations.ProfileList, new { include_disabled = true }, Ct);
        Assert.Equal("nobody-made-this", dangling.DefaultExecutionProfile);
        Assert.DoesNotContain(dangling.Items, profile => profile.Name == "nobody-made-this");
    }

    [Fact]
    public async Task The_exchanges_are_recorded()
    {
        await using var api = await StartAsync(clock: new FixedClock(Nine));
        await ProfileAsync(api, "house");

        await api.RecordAsync<ProfileDefaultDto>(Operations.ProfileSetDefault, "set", new { name = "house", reason = "the roles run on the house host" }, Ct);
        await api.RecordErrorAsync(Operations.ProfileSetDefault, "profile-not-found", new { name = "never-registered" }, HttpStatusCode.NotFound, Ct);
        await api.RecordAsync<ProfileDefaultDto>(Operations.ProfileUnsetDefault, "unset", new { reason = "no default any more" }, Ct);
        await api.RecordAsync<ProfilePageDto>(Operations.ProfileList, "no-default", new { }, Ct);
    }

    [Fact]
    public async Task The_listing_exchanges_are_recorded()
    {
        await using var api = await StartAsync(WithHouse, new FixedClock(Nine));
        await ProfileAsync(api, "house");

        await api.RecordAsync<ProfilePageDto>(Operations.ProfileList, "with-default", new { }, Ct);

        File.WriteAllText(api.Paths.UserSettingsFile, WithHouse.Replace("\"house\"", "\"nobody-made-this\"", StringComparison.Ordinal), new UTF8Encoding(false));
        Assert.Null(api.Resolve<SettingsFile>().Read());
        await api.RecordAsync<ProfilePageDto>(Operations.ProfileList, "default-names-no-profile", new { }, Ct);
    }

    private static string? InForce(RuntimeApiFixture api) => api.Resolve<LiveSettings<RolesOptions>>().Current.DefaultExecutionProfile;

    private static Task<ExecutionProfileDto> ProfileAsync(RuntimeApiFixture api, string name) =>
        api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal(name), Ct);

    private static Task<Page<JournalEntryDto>> LinesAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.DefaultProfileUpdated }, Ct);

    private static Task<RuntimeApiFixture> StartAsync(string settings = Original, TimeProvider? clock = null) =>
        RuntimeApiFixture.StartAsync(Ct, prepare: paths => File.WriteAllText(paths.UserSettingsFile, settings, new UTF8Encoding(false)), clock: clock);
}
