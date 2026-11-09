using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Moving;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
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
        Assert.Contains("Never carried: state/backups/", help, StringComparison.Ordinal);
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

    private static JsonDocument Manifest(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var stream = archive.GetEntry(ExportManifest.FileName)!.Open();
        return JsonDocument.Parse(stream);
    }
}
