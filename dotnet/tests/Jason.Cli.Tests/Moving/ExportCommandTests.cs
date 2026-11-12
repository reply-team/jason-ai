using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Moving;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Uninstall;
using Jason.Contracts.Harness;

namespace Jason.Cli.Tests.Moving;

/// <summary>
/// <c>jason export</c>: one archive of this installation, written only while the runtime is stopped, holding exactly the
/// data directory's carried parts and the harness root's tree, with <c>export.json</c> naming where it came from and the
/// digest of every file, and never over a file that is there.
/// </summary>
public sealed class ExportCommandTests
{
    /// <summary>Noon two hours east of Greenwich is ten in UTC: the name and the moment are written in UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 5, TimeSpan.FromHours(2));

    private const string DefaultName = "jason-export-20261109T100005Z.zip";

    private static readonly ExportOptions Plain = new(File: null, Stop: false, Human: false)
    {
        StopTimeout = RuntimeVerbs.Timeout,
        StartTimeout = RuntimeVerbs.Timeout,
        Poll = RuntimeVerbs.Poll,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- the runtime -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Refuses_while_the_runtime_runs_naming_runtime_stop_and_writes_nothing()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.StartRuntime();

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("runtime_running", error.Code);
        Assert.Contains("'jason runtime stop'", error.Message, StringComparison.Ordinal);
        Assert.Contains("--stop", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
        Assert.True(machine.Running);
        Assert.DoesNotContain(machine.Calls, call => call.Operation == "system.shutdown");
    }

    [Fact]
    public async Task With_stop_it_stops_the_runtime_and_starts_it_again_because_it_was_running()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.StartRuntime();

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(machine.Calls, call => call.Operation == "system.shutdown");
        Assert.Single(machine.Processes.Launches);
        Assert.True(machine.Running);
        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.True(answer.RootElement.GetProperty("stopped").GetBoolean());
        Assert.True(answer.RootElement.GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task With_stop_and_no_runtime_running_it_starts_none()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(machine.Processes.Launches);
        Assert.False(machine.Running);
        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.False(answer.RootElement.GetProperty("stopped").GetBoolean());
        Assert.False(answer.RootElement.GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task A_runtime_that_does_not_start_again_is_exit_1_and_the_archive_is_written()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.StartRuntime();
        machine.StartsOnLaunch = false;

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(Path.Combine(machine.Here, DefaultName)));
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("runtime_start_failed", error.Code);
        Assert.Contains(Path.Combine(machine.Here, DefaultName), error.Message, StringComparison.Ordinal);
        Assert.Contains("'jason runtime start'", error.Message, StringComparison.Ordinal);
    }

    // ---- the help ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Its_help_publishes_what_is_carried_never_carried_and_the_secrets_line()
    {
        using var machine = new MovingMachine();

        var exit = await CliApp.RunAsync(["export", "--help"], machine.Env(), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var help = machine.Out.ToString().ReplaceLineEndings(" ");
        Assert.Contains("Carried: data/state/jason.db", help, StringComparison.Ordinal);
        Assert.Contains("data/logs/", help, StringComparison.Ordinal);
        Assert.Contains("Never carried: data/state/backups/", help, StringComparison.Ordinal);
        Assert.Contains("a replaced-<stamp>/ directory at the data directory's top, which an import moved aside", help, StringComparison.Ordinal);
        Assert.Contains("data/desktop/, the desktop application's own files, the conversations of its chat among them", help, StringComparison.Ordinal);
        Assert.Contains("every folder in it whole, a .git included", help, StringComparison.Ordinal);
        Assert.Contains("The harness root: JASON_Harness__Root, else the Harness:Root the settings file names, else Jason in the home directory", help, StringComparison.Ordinal);
        Assert.Contains("a link at the top of a carried place, or a harness root that is one, is followed and judged where it leads", help, StringComparison.Ordinal);
        Assert.Contains("a link inside is refused", help, StringComparison.Ordinal);
        Assert.Contains("two names apart only by the case of their letters", help, StringComparison.Ordinal);
        Assert.Contains(".partial already there is refused as yours to remove", help, StringComparison.Ordinal);
        Assert.Contains("'jason import'", help, StringComparison.Ordinal);
        Assert.Contains("Secrets travel whole.", help, StringComparison.Ordinal);
        Assert.Contains("Exit codes: 0 written", help, StringComparison.Ordinal);
    }

    // ---- what it carries ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_archive_holds_exactly_the_carried_parts_the_logs_included_and_none_of_the_rest()
    {
        using var machine = new MovingMachine();
        string[] carried =
        [
            "state/jason.db", "state/jason.db-wal", "state/jason.db-shm", "config/settings.json", "plugins/reply/plugin.yaml",
            "plugins/reply/main.js", "skills/roles/.jason-skills.json", "skills/roles/manager/SKILL.md", "ui/custom/index.html",
            "logs/jason-20261108.log", "logs/doctor/jason-doctor-20261108T000000Z.json",
        ];
        string[] left =
        [
            "state/backups/jason-20261101T000000Z.db", "run/runtime.lock", "work/wi_01/att_01/input.json",
            "skills/.staging/pack/SKILL.md", "skills/staged/pack/SKILL.md", "ui/default/index.html",
            "replaced-20261101T000000Z/state/jason.db", "autostart/jason-logon.xml", "update/ledger.json",
            "desktop/settings.json", "desktop/conversations/conv_01JD7Q6V2N0000000000000000.json",
            "desktop/claude-keyed/claude-api-key/projects/chat/4f1c2b3a-0000-4000-8000-000000000001.jsonl",
            "desktop/codex-keyed/sessions/2026/11/11/rollout-2026-11-11T10-00-00.jsonl",
        ];
        foreach (var file in carried.Concat(left))
        {
            machine.PlantData(file);
        }

        machine.Mark(string.Empty, HarnessMarker.Root, null);
        machine.Mark("projects/acme", HarnessMarker.Project, "prj_01ACME");
        machine.PlantHarness("projects/acme/AGENTS.md", "# Acme");
        machine.Mark("projects/acme/campaigns/q4", HarnessMarker.Campaign, "cmp_01Q4");
        Directory.CreateDirectory(Path.Combine(machine.Harness, "scratch"));
        MovingMachine.Plant(machine.Root, "beside/notes.txt", "outside both");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        using var archive = ZipFile.OpenRead(Path.Combine(machine.Here, DefaultName));
        var files = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToList();
        string[] expected =
        [
            .. carried.Select(file => "data/" + file),
            "harness/.jason/marker.json",
            "harness/projects/acme/.jason/marker.json",
            "harness/projects/acme/AGENTS.md",
            "harness/projects/acme/campaigns/q4/.jason/marker.json",
            "export.json",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), files);
        Assert.Contains(archive.Entries, entry => entry.FullName == "harness/scratch/");

        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.Equal(carried.Length, answer.RootElement.GetProperty("data_files").GetInt32());
        Assert.Equal(4, answer.RootElement.GetProperty("harness_files").GetInt32());
        Assert.Equal(1, answer.RootElement.GetProperty("projects").GetInt32());
        Assert.Equal(1, answer.RootElement.GetProperty("campaigns").GetInt32());
    }

    [Fact]
    public async Task Export_json_names_the_version_the_moment_the_system_the_places_and_the_registration()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.Registrar.State = new AutostartState(true, ["C:/Programs/jason.exe", "runtime", "run", "--data-dir", machine.Data], null);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        using var manifest = Manifest(Path.Combine(machine.Here, DefaultName));
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal(MovingMachine.Version, root.GetProperty("jason_version").GetString());
        Assert.Equal(Now, root.GetProperty("exported_at").GetDateTimeOffset());
        Assert.EndsWith("Z", root.GetProperty("exported_at").GetString(), StringComparison.Ordinal);
        Assert.Equal(ExportManifest.ThisSystem(), root.GetProperty("operating_system").GetString());
        Assert.Equal(machine.Data, root.GetProperty("data_directory").GetString());
        Assert.Equal(machine.Harness, root.GetProperty("harness_root").GetString());
        Assert.Equal(machine.Home, root.GetProperty("home").GetString());
        Assert.True(root.GetProperty("autostart").GetProperty("registered").GetBoolean());
        Assert.Equal("windows", root.GetProperty("autostart").GetProperty("platform").GetString());
    }

    [Fact]
    public async Task A_registration_that_starts_another_data_directory_is_not_this_installation_s()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.Registrar.State = new AutostartState(true, ["C:/Programs/jason.exe", "runtime", "run", "--data-dir", Path.Combine(machine.Root, "other")], null);

        await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        using var manifest = Manifest(Path.Combine(machine.Here, DefaultName));
        Assert.False(manifest.RootElement.GetProperty("autostart").GetProperty("registered").GetBoolean());
    }

    [Fact]
    public async Task Every_file_carried_is_listed_with_its_size_and_digest()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db", "a database, made up");
        machine.PlantData("config/settings.json", """{ "Plugins": { "Config": { "reply": { "token": "MADE-UP-TOKEN" } } } }""");
        machine.PlantHarness("projects/acme/AGENTS.md", "# Acme");

        await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        var path = Path.Combine(machine.Here, DefaultName);
        using var manifest = Manifest(path);
        using var archive = ZipFile.OpenRead(path);
        var listed = manifest.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(archive.Entries.Count(entry => !entry.FullName.EndsWith('/')) - 1, listed.Count);
        foreach (var entry in listed)
        {
            var name = entry.GetProperty("path").GetString()!;
            using var stream = archive.GetEntry(name)!.Open();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, Ct);
            Assert.Equal(copy.Length, entry.GetProperty("bytes").GetInt64());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())), entry.GetProperty("sha256").GetString());
        }

        // Secrets travel whole: the settings file is carried as it is.
        Assert.Contains(listed, entry => entry.GetProperty("path").GetString() == "data/config/settings.json");
    }

    [Fact]
    public async Task It_says_the_archive_carries_the_settings_file_with_its_secrets()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantData("config/settings.json", """{ "Plugins": { "Config": { "acme-mail": { "token": "made-up-token" } } } }""");

        await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(
            "The archive carries the settings file with everything in it, a token under Plugins:Config included: keep the archive as you keep that file.",
            ExportCommand.SecretsLine);
        Assert.Contains(ExportCommand.SecretsLine, machine.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_settings_file_it_says_nothing_of_one()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantHarness("projects/acme/AGENTS.md", "# Acme");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(ExportCommand.SecretsLine, machine.Error.ToString(), StringComparison.Ordinal);
        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.Empty(answer.RootElement.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task The_harness_root_is_the_one_the_settings_file_names_when_no_variable_does()
    {
        using var machine = new MovingMachine();
        var named = Path.Combine(machine.Root, "elsewhere");
        machine.PlantData("config/settings.json", "// the person's own note\n{ \"harness\": { \"root\": " + JsonSerializer.Serialize(named) + ", }, }");
        MovingMachine.Plant(named, "projects/acme/AGENTS.md", "# Acme");
        var env = machine.Env();
        machine.Variables.Remove("JASON_Harness__Root");

        var exit = await ExportCommand.RunAsync(env, Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        using var manifest = Manifest(Path.Combine(machine.Here, DefaultName));
        Assert.Equal(named, manifest.RootElement.GetProperty("harness_root").GetString());
        using var archive = ZipFile.OpenRead(Path.Combine(machine.Here, DefaultName));
        Assert.NotNull(archive.GetEntry("harness/projects/acme/AGENTS.md"));
    }

    [Fact]
    public async Task The_harness_root_is_Jason_in_the_home_when_nothing_names_one()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        MovingMachine.Plant(machine.Home, "Jason/projects/acme/AGENTS.md", "# Acme");
        var env = machine.Env();
        machine.Variables.Remove("JASON_Harness__Root");

        await ExportCommand.RunAsync(env, Plain, new StoppedClock(Now), Ct);

        using var manifest = Manifest(Path.Combine(machine.Here, DefaultName));
        Assert.Equal(Path.Combine(machine.Home, "Jason"), manifest.RootElement.GetProperty("harness_root").GetString());
    }

    [Fact]
    public async Task A_harness_root_that_holds_the_data_directory_is_refused_by_the_runtime_s_rule_and_nothing_is_written()
    {
        using var machine = new MovingMachine(dataFolder: "box/data");
        machine.PlantData("state/jason.db");
        var box = Path.GetDirectoryName(machine.Data)!;
        MovingMachine.Plant(box, "projects/acme/AGENTS.md", "# Acme");
        var env = machine.Env();
        machine.Variables["JASON_Harness__Root"] = box;

        var exit = await ExportCommand.RunAsync(env, Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains(HarnessRootRule.Refusal(box, machine.Data, machine.Home)!, error.Message, StringComparison.Ordinal);
        Assert.Contains("must not hold the data directory", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    [Fact]
    public async Task A_harness_root_the_settings_file_writes_with_a_tilde_is_refused_and_nothing_is_written()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantData("config/settings.json", "{ \"Harness\": { \"Root\": \"~/Jason\" } }");
        var env = machine.Env();
        machine.Variables.Remove("JASON_Harness__Root");

        var exit = await ExportCommand.RunAsync(env, Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains("must be an absolute path", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    // ---- where it is written -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_default_name_is_the_moment_in_UTC_in_the_folder_the_command_runs_in()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var path = Path.Combine(machine.Here, DefaultName);
        Assert.True(File.Exists(path));
        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.Equal(path, answer.RootElement.GetProperty("file").GetString());
        Assert.Equal(new FileInfo(path).Length, answer.RootElement.GetProperty("bytes").GetInt64());
        Assert.Equal(MovingMachine.Version, answer.RootElement.GetProperty("jason_version").GetString());
        Assert.Equal("20261109T100005Z", ExportCommand.Stamp(Now));
    }

    [Fact]
    public async Task A_file_named_relative_is_written_in_the_folder_the_command_runs_in()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { File = "moving.zip" }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(Path.Combine(machine.Here, "moving.zip")));
    }

    [Fact]
    public async Task Never_writes_over_a_file_that_is_there()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        var there = MovingMachine.Plant(machine.Here, DefaultName, "the person's own");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("export_output_exists", RuntimeVerbs.Envelope(machine.Out.ToString()).Code);
        Assert.Equal("the person's own", File.ReadAllText(there));
    }

    // ---- what cannot travel ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_link_in_the_harness_is_refused_by_its_name_and_nothing_is_written()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        var target = machine.PlantHarness("projects/acme/AGENTS.md", "# Acme");
        if (!ManagedLayouts.TryLink(Path.Combine(machine.Harness, "projects", "acme", "linked.md"), target))
        {
            Assert.Skip(ManagedLayouts.NoLinkPrivilege);
        }

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains("linked.md", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    [Fact]
    public async Task A_name_another_system_cannot_hold_is_refused_by_its_name_and_nothing_is_written()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows cannot make a file whose name holds a colon.");
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantHarness("projects/acme/notes:1.md");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains("notes:1.md", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    // ---- links at the top of a carried place -------------------------------------------------------------------------

    [Fact]
    public async Task A_ui_custom_that_is_a_junction_to_a_folder_outside_both_places_is_carried_as_the_tree_it_leads_to()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        var built = Path.Combine(machine.Root, "checkout", "ui", "dist");
        MovingMachine.Plant(built, "index.html", "<html>a build of the person's own</html>");
        Directory.CreateDirectory(Path.Combine(machine.Data, "ui"));
        DataDirectoryBeltTests.Link(Path.Combine(machine.Data, "ui", "custom"), built);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("<html>a build of the person's own</html>", Read(Path.Combine(machine.Here, DefaultName), "data/ui/custom/index.html"));
        Assert.Contains("data/ui/custom/", Names(Path.Combine(machine.Here, DefaultName)));
    }

    [Fact]
    public async Task A_harness_root_that_is_a_junction_is_carried_as_the_tree_it_leads_to_under_the_root_s_own_name()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        var real = Path.Combine(machine.Root, "synced", "Jason");
        MovingMachine.Plant(real, "projects/acme/AGENTS.md", "# Acme");
        DataDirectoryBeltTests.Link(machine.Harness, real);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("# Acme", Read(Path.Combine(machine.Here, DefaultName), "harness/projects/acme/AGENTS.md"));
        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.Equal(machine.Harness, answer.RootElement.GetProperty("harness_root").GetString());
    }

    [Fact]
    public async Task A_junction_inside_the_harness_is_refused_by_its_name_and_nothing_is_written()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantHarness("projects/acme/AGENTS.md", "# Acme");
        var elsewhere = MovingMachine.Plant(machine.Root, "elsewhere/notes.md", "outside both");
        var link = Path.Combine(machine.Harness, "projects", "acme", "linked");
        DataDirectoryBeltTests.Link(link, Path.GetDirectoryName(elsewhere)!);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains(link, error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    [Fact]
    public async Task A_harness_root_that_is_a_junction_into_the_data_directory_is_refused_where_it_leads_and_nothing_is_written()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        DataDirectoryBeltTests.Link(machine.Harness, machine.Data);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains("must not be the data directory", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("plugins")]
    [InlineData("data")]
    [InlineData("harness")]
    [InlineData("root")]
    public async Task A_ui_custom_that_leads_into_another_place_of_the_data_directory_or_the_harness_is_refused_naming_the_link_and_where_it_leads(string into)
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantData("plugins/reply/plugin.yaml", "id: reply");
        machine.PlantHarness("projects/acme/AGENTS.md", "# Acme");
        var target = into switch
        {
            "state" => Path.Combine(machine.Data, "state"),
            "plugins" => Path.Combine(machine.Data, "plugins"),
            "data" => machine.Data,
            "harness" => Path.Combine(machine.Harness, "projects"),
            _ => machine.Root,
        };
        Directory.CreateDirectory(Path.Combine(machine.Data, "ui"));
        var link = Path.Combine(machine.Data, "ui", "custom");
        DataDirectoryBeltTests.Link(link, target);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains(link, error.Message, StringComparison.Ordinal);
        Assert.Contains(target, error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    // ---- the runtime, held ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_runtime_that_does_not_stop_is_runtime_still_running_with_nothing_written_and_no_launch()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.StartRuntime();
        machine.StopsOnShutdown = false;

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true, StopTimeout = RuntimeVerbs.ShortTimeout }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("runtime_still_running", RuntimeVerbs.Envelope(machine.Out.ToString()).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
        Assert.Empty(machine.Processes.Launches);
        Assert.True(machine.Running);
    }

    [Fact]
    public async Task A_held_instance_lock_is_a_running_runtime_and_is_refused_with_nothing_written()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        Directory.CreateDirectory(machine.Paths.RunDirectory);
        using var held = new FileStream(machine.Paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("runtime_running", error.Code);
        Assert.Contains("'jason runtime stop'", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    /// <summary>
    /// A lock held by something that is no runtime by its descriptor, let go once the stop was asked: the stop stopped
    /// nothing, so the export starts nothing, and does not say it started one again.
    /// </summary>
    [Fact]
    public async Task A_lock_let_go_after_a_stop_that_stopped_nothing_starts_no_runtime()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.LeaveStaleDescriptor();
        var held = new FileStream(machine.Paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        machine.OnCall = operation =>
        {
            if (operation == "system.shutdown")
            {
                held.Dispose();
            }
        };
        try
        {
            var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

            Assert.Equal(ExitCodes.Success, exit);
            Assert.Contains(machine.Calls, call => call.Operation == "system.shutdown");
            Assert.Single(Directory.EnumerateFiles(machine.Here, "*.zip"));
            Assert.Empty(machine.Processes.Launches);
            Assert.False(machine.Running);
            using var answer = JsonDocument.Parse(machine.Out.ToString());
            Assert.False(answer.RootElement.GetProperty("stopped").GetBoolean());
            Assert.DoesNotContain("started again", machine.Out.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            held.Dispose();
        }
    }

    [Fact]
    public async Task The_instance_lock_is_let_go_before_the_runtime_starts_again()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.StartRuntime();
        bool? freeAtLaunch = null;
        var launch = machine.Processes.OnLaunch!;
        machine.Processes.OnLaunch = paths =>
        {
            freeAtLaunch = CanTake(machine.Paths.LockFile);
            return launch(paths);
        };

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(freeAtLaunch);
        Assert.True(CanTake(machine.Paths.LockFile));
    }

    [Fact]
    public async Task A_failure_in_the_middle_of_the_write_leaves_nothing_behind_names_the_file_and_starts_the_runtime_again()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        var busy = Path.GetFullPath(machine.PlantData("logs/jason-20261108.log", "a log another program holds"));
        machine.StartRuntime();
        using var held = new FileStream(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains(busy, error.Message, StringComparison.Ordinal);
        Assert.Contains("started again", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
        Assert.Single(machine.Processes.Launches);
        Assert.True(machine.Running);
    }

    // ---- the partial file ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_partial_file_already_there_is_refused_by_name_as_the_person_s_to_remove()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        var partial = MovingMachine.Plant(machine.Here, DefaultName + ".partial", "an export that did not finish");
        machine.StartRuntime();

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain with { Stop = true }, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_output_exists", error.Code);
        Assert.Contains(partial, error.Message, StringComparison.Ordinal);
        Assert.Contains("yours to remove", error.Message, StringComparison.Ordinal);
        Assert.Equal("an export that did not finish", File.ReadAllText(partial));
        Assert.False(File.Exists(Path.Combine(machine.Here, DefaultName)));
        Assert.True(machine.Running);
        Assert.Empty(machine.Processes.Launches);
    }

    [Fact]
    public async Task No_partial_file_remains_after_a_success()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([DefaultName], Directory.EnumerateFileSystemEntries(machine.Here).Select(Path.GetFileName));
    }

    // ---- a root that is not there, names apart by case --------------------------------------------------------------

    [Fact]
    public async Task A_harness_root_that_is_not_there_still_gives_the_harness_entry_and_a_warning_naming_it()
    {
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("harness/", Names(Path.Combine(machine.Here, DefaultName)));
        using var answer = JsonDocument.Parse(machine.Out.ToString());
        Assert.Contains(answer.RootElement.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains(machine.Harness, StringComparison.Ordinal));
        Assert.Contains(machine.Harness, machine.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_names_apart_only_by_case_are_refused_and_nothing_is_written()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux holds two names apart only by case in one folder by default.");
        using var machine = new MovingMachine();
        machine.PlantData("state/jason.db");
        machine.PlantHarness("projects/acme/Notes.md", "one");
        var other = Path.GetFullPath(machine.PlantHarness("projects/acme/notes.md", "another"));

        var exit = await ExportCommand.RunAsync(machine.Env(), Plain, new StoppedClock(Now), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(machine.Out.ToString());
        Assert.Equal("export_refused", error.Code);
        Assert.Contains("case", error.Message, StringComparison.Ordinal);
        Assert.Contains(other, error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.Here));
    }

    private static bool CanTake(string lockFile)
    {
        try
        {
            using var taken = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static List<string> Names(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return [.. archive.Entries.Select(entry => entry.FullName)];
    }

    private static string Read(string archivePath, string name)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static JsonDocument Manifest(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var stream = archive.GetEntry(ExportManifest.FileName)!.Open();
        return JsonDocument.Parse(stream);
    }
}
