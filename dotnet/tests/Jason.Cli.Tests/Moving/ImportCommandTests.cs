using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Moving;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Moving;

/// <summary>
/// <c>jason import</c>: an archive <c>jason export</c> wrote, refused before anything is written when it cannot be read,
/// escapes, does not match its listing, was written by a newer Jason or would land on an installation that is here; placed
/// otherwise, with what is here moved aside and never deleted, the settings made this machine's, and the runtime started
/// and asked to link each project folder.
/// </summary>
public sealed class ImportCommandTests
{
    /// <summary>Noon two hours east of Greenwich is ten in UTC: what is moved aside is named in UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 5, TimeSpan.FromHours(2));

    private const string Stamp = "20261109T100005Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ImportOptions Options(string file) => new(file, Replace: false, Root: null, Stop: false, Human: false)
    {
        StopTimeout = RuntimeVerbs.Timeout,
        StartTimeout = RuntimeVerbs.Timeout,
        Poll = RuntimeVerbs.Poll,
    };

    private static Task<int> Import(MovingMachine to, ImportOptions options) => ImportCommand.RunAsync(to.Env(), options, new StoppedClock(Now), Ct);

    // ---- an archive that cannot be read ------------------------------------------------------------------------------

    [Fact]
    public async Task A_file_that_is_not_an_archive_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = MovingMachine.Plant(from.Here, "jason-export.zip", "not an archive at all");

        var exit = await Import(to, Options(file));

        Refused(to, exit, "import_unreadable");
    }

    [Fact]
    public async Task An_archive_without_export_json_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = Path.Combine(from.Here, "other.zip");
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("data/state/jason.db").Open();
            stream.Write("made up"u8);
        }

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_unreadable");
        Assert.Contains("export.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_export_json_of_another_format_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from) { Listing = manifest => manifest with { Format = 2 } }.Installation().Write();

        var exit = await Import(to, Options(file));

        Refused(to, exit, "import_unreadable");
    }

    // ---- an archive that escapes -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("data/../escaped.md", "a parent segment under data")]
    [InlineData("../escaped.md", "a parent segment at the top")]
    [InlineData("/rooted.md", "a name from the root")]
    [InlineData("C:/rooted.md", "a name with a drive")]
    [InlineData("harness\\projects\\back.md", "a backslash")]
    [InlineData("harness//empty.md", "an empty segment")]
    [InlineData("harness/./dot.md", "a dot segment")]
    [InlineData("harness/dotted./notes.md", "a segment ending in a dot")]
    [InlineData("outside/notes.md", "a name outside data and harness")]
    [InlineData("notes.md", "a name at the top that is not export.json")]
    [InlineData("data/run/runtime.lock", "a part of the data directory no export carries")]
    [InlineData("data/state/backups/jason-20261101T000000Z.db", "a backup no export carries")]
    [InlineData("data/ui/default/index.html", "the shipped UI no export carries")]
    public async Task An_entry_that_escapes_is_refused_by_its_name_and_nothing_is_written(string name, string why)
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from) { Beside = archive => Entry(archive, name) }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_refused");
        Assert.True(error.Message.Contains(name, StringComparison.Ordinal), $"{why}: the refusal does not name '{name}': {error.Message}");
    }

    [Fact]
    public async Task A_name_export_json_lists_that_escapes_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from)
        {
            Listing = manifest => manifest with { Entries = [.. manifest.Entries, new ExportEntry("harness/../../escaped.md", 1, new string('0', 64))] },
        }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_refused");
        Assert.Contains("harness/../../escaped.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_in_the_archive_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from)
        {
            Beside = archive => archive.CreateEntry("harness/projects/acme/linked.md").ExternalAttributes = unchecked((int)0xA1FF0000),
        }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_refused");
        Assert.Contains("harness/projects/acme/linked.md", error.Message, StringComparison.Ordinal);
        Assert.Contains("link", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_names_apart_only_by_case_are_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from)
            .Installation()
            .With("harness/projects/acme/Notes.md")
            .With("harness/projects/acme/notes.md")
            .Write();

        var exit = await Import(to, Options(file));

        Refused(to, exit, "import_refused");
    }

    // ---- an archive that is not what export.json lists ---------------------------------------------------------------

    [Fact]
    public async Task A_file_whose_digest_is_not_the_listed_one_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from)
        {
            Listing = manifest => manifest with
            {
                Entries = [.. manifest.Entries.Select(entry => entry.Path == "harness/projects/acme/AGENTS.md" ? entry with { Sha256 = new string('a', 64) } : entry)],
            },
        }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_digest_mismatch");
        Assert.Contains("harness/projects/acme/AGENTS.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_whose_size_is_not_the_listed_one_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from)
        {
            Listing = manifest => manifest with
            {
                Entries = [.. manifest.Entries.Select(entry => entry.Path == "data/state/jason.db" ? entry with { Bytes = entry.Bytes + 1 } : entry)],
            },
        }.Installation().Write();

        var exit = await Import(to, Options(file));

        Refused(to, exit, "import_digest_mismatch");
    }

    [Fact]
    public async Task A_file_export_json_lists_and_the_archive_does_not_hold_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from)
        {
            Listing = manifest => manifest with { Entries = [.. manifest.Entries, new ExportEntry("harness/projects/acme/gone.md", 4, new string('b', 64))] },
        }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_digest_mismatch");
        Assert.Contains("harness/projects/acme/gone.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_export_json_does_not_list_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from) { Beside = archive => Entry(archive, "harness/projects/acme/slipped-in.md") }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_refused");
        Assert.Contains("harness/projects/acme/slipped-in.md", error.Message, StringComparison.Ordinal);
    }

    // ---- versions ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_archive_a_newer_Jason_wrote_is_refused_naming_the_version_to_install_first()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from) { Version = "0.34.0" }.Installation().Write();

        var exit = await Import(to, Options(file));

        var error = Refused(to, exit, "import_newer_version");
        Assert.Contains("Install Jason 0.34.0 or later first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_archive_an_older_Jason_wrote_is_placed()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from) { Version = "0.32.0" }.Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("a database, made up", File.ReadAllText(Path.Combine(to.Data, "state", "jason.db")));
    }

    // ---- an installation that is here --------------------------------------------------------------------------------

    [Fact]
    public async Task A_database_here_without_replace_is_refused_naming_it_the_settings_the_project_folders_and_the_flag()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var db = Path.GetFullPath(to.PlantData("state/jason.db", "the database here"));
        var settings = Path.GetFullPath(to.PlantData("config/settings.json", "{}"));
        to.Mark(string.Empty, HarnessMarker.Root, null);
        to.Mark("projects/one", HarnessMarker.Project, "prj_01ONE");
        to.Mark("projects/two", HarnessMarker.Project, "prj_01TWO");
        var file = new MovingArchive(from).Installation().Write();
        var before = Tree(to.Root);

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(to.Out.ToString());
        Assert.Equal("import_not_empty", error.Code);
        Assert.Contains(db, error.Message, StringComparison.Ordinal);
        Assert.Contains(settings, error.Message, StringComparison.Ordinal);
        Assert.Contains("2 project folders", error.Message, StringComparison.Ordinal);
        Assert.Contains("--replace", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Tree(to.Root));
    }

    [Fact]
    public async Task A_harness_root_with_a_marker_and_no_database_without_replace_is_refused()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.Mark(string.Empty, HarnessMarker.Root, null);
        var file = new MovingArchive(from).Installation().Write();
        var before = Tree(to.Root);

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("import_not_empty", RuntimeVerbs.Envelope(to.Out.ToString()).Code);
        Assert.Equal(before, Tree(to.Root));
    }

    [Fact]
    public async Task With_replace_what_is_here_is_moved_aside_named_and_nothing_is_deleted()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.PlantData("state/jason.db", "the database here");
        to.PlantData("config/settings.json", "{\"Here\": true}");
        to.PlantData("logs/jason-20261001.log", "a log here");
        to.PlantData("state/backups/jason-20261001T000000Z.db", "a backup, not a carried part");
        to.Mark(string.Empty, HarnessMarker.Root, null);
        to.Mark("projects/old", HarnessMarker.Project, "prj_01OLD");
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Replace = true });

        Assert.Equal(ExitCodes.Success, exit);
        var dataAside = Path.Combine(to.Data, "replaced-" + Stamp);
        var rootAside = to.Harness + ".replaced-" + Stamp;
        Assert.Equal("the database here", File.ReadAllText(Path.Combine(dataAside, "state", "jason.db")));
        Assert.Equal("{\"Here\": true}", File.ReadAllText(Path.Combine(dataAside, "config", "settings.json")));
        Assert.Equal("a log here", File.ReadAllText(Path.Combine(dataAside, "logs", "jason-20261001.log")));
        Assert.Equal("prj_01OLD", HarnessMarker.In(Path.Combine(rootAside, "projects", "old"))!.Id);
        Assert.Equal("a backup, not a carried part", File.ReadAllText(Path.Combine(to.Data, "state", "backups", "jason-20261001T000000Z.db")));
        Assert.Equal("a database, made up", File.ReadAllText(Path.Combine(to.Data, "state", "jason.db")));
        Assert.Equal("prj_01ACME", HarnessMarker.In(Path.Combine(to.Harness, "projects", "acme"))!.Id);
        using var answer = JsonDocument.Parse(to.Out.ToString());
        Assert.Equal([dataAside, rootAside], answer.RootElement.GetProperty("moved_aside").EnumerateArray().Select(path => path.GetString()));
    }

    [Fact]
    public async Task A_name_already_taken_aside_is_followed_by_the_next_number()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.PlantData("state/jason.db", "the database here");
        to.PlantData("replaced-" + Stamp + "/state/jason.db", "moved aside before");
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Replace = true });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("moved aside before", File.ReadAllText(Path.Combine(to.Data, "replaced-" + Stamp, "state", "jason.db")));
        Assert.Equal("the database here", File.ReadAllText(Path.Combine(to.Data, "replaced-" + Stamp + "-2", "state", "jason.db")));
    }

    [Fact]
    public async Task Parts_here_with_no_database_and_no_marker_need_no_replace_and_are_moved_aside_not_written_over()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.PlantData("logs/jason-20261001.log", "a log here");
        to.PlantHarness("notes.md", "a note, no marker");
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("a log here", File.ReadAllText(Path.Combine(to.Data, "replaced-" + Stamp, "logs", "jason-20261001.log")));
        Assert.Equal("a note, no marker", File.ReadAllText(Path.Combine(to.Harness + ".replaced-" + Stamp, "notes.md")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(to.Data, "logs", "jason-20261108.log")));
    }

    // ---- the settings ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_path_under_the_other_machine_s_places_is_rewritten_and_said_the_root_first_and_the_rest_left_as_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var gone = Path.Combine(from.Root, "gone", "nowhere");
        var file = new MovingArchive(from).Installation().With("data/config/settings.json", SettingsText(from.Home, from.Data, from.Harness, gone)).Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(SettingsText(to.Home, to.Data, to.Harness, gone), File.ReadAllText(to.Paths.UserSettingsFile));
        using var answer = JsonDocument.Parse(to.Out.ToString());
        var rewritten = answer.RootElement.GetProperty("rewritten").EnumerateArray().ToArray();
        Assert.Equal(
            ["Harness:Root", "Plugins:Config:reply:exports", "Logs:Copy", "Elsewhere:0"],
            rewritten.Select(rewrite => rewrite.GetProperty("setting").GetString()));
        Assert.Equal(from.Harness, rewritten[0].GetProperty("from").GetString());
        Assert.Equal(to.Harness, rewritten[0].GetProperty("to").GetString());
        var notHere = Assert.Single(answer.RootElement.GetProperty("not_here").EnumerateArray());
        Assert.Equal("Elsewhere:1", notHere.GetProperty("setting").GetString());
        Assert.Equal(gone, notHere.GetProperty("path").GetString());
        Assert.Contains(gone, to.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("left as written", to.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_root_no_setting_or_variable_would_find_is_written_into_the_settings_file()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.Variables[InstallationPlaces.RootVariable] = string.Empty;
        var root = Path.Combine(to.Root, "elsewhere-root");
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Root = root });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(root, InstallationPlaces.SettingsRoot(File.ReadAllText(to.Paths.UserSettingsFile)));
        Assert.Equal("prj_01ACME", HarnessMarker.In(Path.Combine(root, "projects", "acme"))!.Id);
        using var answer = JsonDocument.Parse(to.Out.ToString());
        var first = answer.RootElement.GetProperty("rewritten")[0];
        Assert.Equal("Harness:Root", first.GetProperty("setting").GetString());
        Assert.Equal(root, first.GetProperty("to").GetString());
    }

    [Fact]
    public async Task A_role_skills_pack_from_a_directory_not_here_is_listed_with_skills_install()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var source = Path.Combine(from.Root, "my-skills");
        var record = new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [
                new SkillsDeployment("house-style", source, SkillsSource.DirectoryRef, false, null, MovingArchive.ExportedAt, []),
                new SkillsDeployment("sdr-roles", "reply-team/jason-ai", "v0.33.0", false, null, MovingArchive.ExportedAt, []),
            ]);
        var file = new MovingArchive(from)
            .Installation()
            .With("data/skills/roles/" + SkillsRecord.FileName, JsonSerializer.Serialize(record, JasonJson.Options))
            .Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        using var answer = JsonDocument.Parse(to.Out.ToString());
        var listed = Assert.Single(answer.RootElement.GetProperty("skills_sources_not_here").EnumerateArray());
        Assert.Equal("house-style", listed.GetProperty("pack").GetString());
        Assert.Equal(source, listed.GetProperty("source").GetString());
        Assert.Contains("'jason skills install'", to.Error.ToString(), StringComparison.Ordinal);
    }

    // ---- the logon registration --------------------------------------------------------------------------------------

    [Fact]
    public async Task It_registers_at_logon_for_this_data_directory_when_the_other_machine_did()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from) { Registered = true }.Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        var applied = Assert.Single(to.Registrar.Applied);
        Assert.Equal(FolderPaths.Normal(to.Data), FolderPaths.Normal(RuntimeAutostartCommands.DataDirectoryIn(applied.Run)!));
        using var answer = JsonDocument.Parse(to.Out.ToString());
        Assert.True(answer.RootElement.GetProperty("autostart").GetProperty("registered").GetBoolean());
    }

    [Fact]
    public async Task It_registers_nothing_when_the_other_machine_did_not()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(to.Registrar.Applied);
        Assert.True(File.Exists(Path.Combine(to.Data, "state", "jason.db")));
        using var answer = JsonDocument.Parse(to.Out.ToString());
        Assert.False(answer.RootElement.GetProperty("autostart").GetProperty("exported").GetBoolean());
    }

    [Fact]
    public async Task A_machine_that_cannot_register_says_so_in_the_sentence_autostart_status_says()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine { Registrar = new RecordingRegistrar(AutostartPlatform.Unsupported) };
        var file = new MovingArchive(from) { Registered = true }.Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(to.Registrar.Applied);
        Assert.Contains("No logon registration, and this machine has no way to register one.", to.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registration_refused_here_is_said_and_the_import_goes_on()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.Registrar.Refuses = new AutostartException(AutostartCodes.Refused, "The scheduler said no, made up.");
        var file = new MovingArchive(from) { Registered = true }.Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("The scheduler said no, made up.", to.Error.ToString(), StringComparison.Ordinal);
        Assert.True(to.Running);
    }

    // ---- the runtime -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Refuses_while_the_runtime_runs_naming_runtime_stop_and_writes_nothing()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.StartRuntime();
        var file = new MovingArchive(from).Installation().Write();
        var before = Tree(to.Root);

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(to.Out.ToString());
        Assert.Equal("runtime_running", error.Code);
        Assert.Contains("'jason runtime stop'", error.Message, StringComparison.Ordinal);
        Assert.Contains("--stop", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Tree(to.Root));
        Assert.DoesNotContain(to.Calls, call => call.Operation == "system.shutdown");
    }

    [Fact]
    public async Task With_stop_it_stops_the_runtime_places_the_archive_and_starts_it_on_it()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.StartRuntime();
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Stop = true });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(to.Calls, call => call.Operation == "system.shutdown");
        Assert.Single(to.Processes.Launches);
        Assert.True(to.Running);
        using var answer = JsonDocument.Parse(to.Out.ToString());
        Assert.True(answer.RootElement.GetProperty("stopped").GetBoolean());
    }

    [Fact]
    public async Task It_starts_the_runtime_links_each_project_folder_by_its_marker_and_leaves_it_running()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine { Answers = Linked };
        var file = new MovingArchive(from).Installation().Mark("harness/projects/beta", HarnessMarker.Project, "prj_01BETA").Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(to.Running);
        var links = to.Calls.Where(call => call.Operation == "project.link").Select(call => JsonDocument.Parse(call.Body).RootElement).ToArray();
        Assert.Equal(["prj_01ACME", "prj_01BETA"], links.Select(link => link.GetProperty("project_id").GetString()));
        Assert.Equal(
            [Path.Combine(to.Harness, "projects", "acme"), Path.Combine(to.Harness, "projects", "beta")],
            links.Select(link => link.GetProperty("path").GetString()));
        using var answer = JsonDocument.Parse(to.Out.ToString());
        Assert.Equal(2, answer.RootElement.GetProperty("linked").GetArrayLength());
        Assert.Equal(2, answer.RootElement.GetProperty("projects").GetInt32());
        Assert.Equal(1, answer.RootElement.GetProperty("campaigns").GetInt32());
        Assert.True(answer.RootElement.GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task A_link_the_runtime_refuses_is_listed_with_its_code_and_is_not_fatal()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine
        {
            Answers = (operation, _) => operation == "project.link"
                ? RuntimeVerbs.Response(HttpStatusCode.Conflict, "{\"error\":{\"code\":\"project_folder_mismatch\",\"message\":\"The marker names another project, made up.\",\"retryable\":false}}")
                : null,
        };
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        using var answer = JsonDocument.Parse(to.Out.ToString());
        var refused = Assert.Single(answer.RootElement.GetProperty("link_refused").EnumerateArray());
        Assert.Equal("prj_01ACME", refused.GetProperty("project_id").GetString());
        Assert.Equal("project_folder_mismatch", refused.GetProperty("code").GetString());
        Assert.Contains("project_folder_mismatch", to.Error.ToString(), StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(to.Harness, "projects", "acme")));
    }

    [Fact]
    public async Task A_runtime_that_does_not_start_is_exit_1_with_the_files_in_place()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine { StartsOnLaunch = false };
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(to.Out.ToString());
        Assert.Equal("runtime_start_failed", error.Code);
        Assert.Contains("'jason runtime start'", error.Message, StringComparison.Ordinal);
        Assert.Equal("a database, made up", File.ReadAllText(Path.Combine(to.Data, "state", "jason.db")));
        Assert.DoesNotContain(to.Calls, call => call.Operation == "project.link");
    }

    [Fact]
    public async Task The_answer_leaves_skills_install_autostart_enable_and_status_to_the_person()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        using var answer = JsonDocument.Parse(to.Out.ToString());
        var next = answer.RootElement.GetProperty("next").EnumerateArray().Select(line => line.GetString()!).ToArray();
        Assert.Equal(3, next.Length);
        Assert.StartsWith("jason skills install", next[0], StringComparison.Ordinal);
        Assert.StartsWith("jason runtime autostart enable", next[1], StringComparison.Ordinal);
        Assert.StartsWith("jason status", next[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task For_people_it_says_what_was_placed_and_what_is_left_to_them()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Human = true });

        Assert.Equal(ExitCodes.Success, exit);
        var said = to.Out.ToString();
        Assert.Contains("Imported the installation Jason 0.33.0 exported at 2026-11-08 09:30:00Z", said, StringComparison.Ordinal);
        Assert.Contains("Left to you:", said, StringComparison.Ordinal);
        Assert.Contains("jason status", said, StringComparison.Ordinal);
    }

    // ---- the harness root --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_root_named_relative_is_the_folder_under_the_one_the_command_runs_in()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.Variables[InstallationPlaces.RootVariable] = string.Empty;
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Root = "moved-root" });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("prj_01ACME", HarnessMarker.In(Path.Combine(to.Here, "moved-root", "projects", "acme"))!.Id);
    }

    [Fact]
    public async Task The_root_is_the_one_this_machine_s_settings_file_names_when_no_variable_does()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine { RootVariable = false };
        var named = Path.Combine(to.Root, "named-root");
        to.PlantData("config/settings.json", "{\"Harness\": {\"Root\": " + JsonSerializer.Serialize(named) + "}}");
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("prj_01ACME", HarnessMarker.In(Path.Combine(named, "projects", "acme"))!.Id);
        Assert.Equal(named, InstallationPlaces.SettingsRoot(File.ReadAllText(to.Paths.UserSettingsFile)));
    }

    [Fact]
    public async Task A_root_option_the_variable_contradicts_is_refused_and_nothing_is_written()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Root = Path.Combine(to.Root, "another-root") });

        var error = Refused(to, exit, "import_refused");
        Assert.Contains(InstallationPlaces.RootVariable, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("home", "is the home directory")]
    [InlineData("data", "is the data directory")]
    [InlineData("inside-data", "is inside the data directory")]
    [InlineData("above-data", "holds the home directory")]
    public async Task A_root_the_runtime_would_refuse_is_refused_and_nothing_is_written(string which, string said)
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine();
        to.Variables[InstallationPlaces.RootVariable] = string.Empty;
        var root = which switch
        {
            "home" => to.Home,
            "data" => to.Data,
            "inside-data" => Path.Combine(to.Data, "harness"),
            _ => to.Root,
        };
        var file = new MovingArchive(from).Installation().Write();

        var exit = await Import(to, Options(file) with { Root = root });

        var error = Refused(to, exit, "import_refused");
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
    }

    // ---- there and back ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task What_export_wrote_import_places_byte_for_byte_but_the_settings_file_which_is_the_rewrite()
    {
        using var from = new MovingMachine();
        using var to = new MovingMachine { Answers = Linked };
        var gone = Path.Combine(from.Root, "gone", "nowhere");
        from.PlantData("state/jason.db", "the database, made up, cold");
        from.PlantData("config/settings.json", SettingsText(from.Home, from.Data, from.Harness, gone));
        from.PlantData("plugins/reply/plugin.yaml", "id: reply");
        from.PlantData("plugins/reply/main.js", "export default {};");
        from.PlantData("skills/roles/manager/SKILL.md", "# Manager");
        from.PlantData("ui/custom/index.html", "<html></html>");
        from.PlantData("logs/jason-20261108.log", "{\"level\":\"info\"}");
        from.Mark(string.Empty, HarnessMarker.Root, null);
        from.Mark("projects/acme", HarnessMarker.Project, "prj_01ACME");
        from.PlantHarness("projects/acme/AGENTS.md", "# Acme, a person's words");
        from.Mark("projects/acme/campaigns/q4", HarnessMarker.Campaign, "cmp_01Q4");
        Directory.CreateDirectory(Path.Combine(from.Harness, "projects", "acme", "empty"));
        var file = Path.Combine(from.Here, "there-and-back.zip");
        Assert.Equal(
            ExitCodes.Success,
            await ExportCommand.RunAsync(from.Env(), new ExportOptions(file, Stop: false, Human: false), new StoppedClock(Now), Ct));

        var exit = await Import(to, Options(file));

        Assert.Equal(ExitCodes.Success, exit);
        // The runtime the import started keeps its descriptor in run/, which no export carries.
        var placedData = Tree(to.Data).Where(file => !file.Key.StartsWith("run", StringComparison.Ordinal)).ToDictionary(StringComparer.Ordinal);
        var placedHarness = Tree(to.Harness);
        var settings = Path.Combine("config", "settings.json");
        Assert.Equal(SettingsText(to.Home, to.Data, to.Harness, gone), placedData[settings]);
        placedData.Remove(settings);
        var exportedData = Tree(from.Data);
        exportedData.Remove(settings);
        Assert.Equal(exportedData, placedData);
        Assert.Equal(Tree(from.Harness), placedHarness);
    }

    // ---- the help ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Its_help_publishes_what_is_refused_replaced_kept_rewritten_and_left_to_the_person()
    {
        using var to = new MovingMachine();

        var exit = await CliApp.RunAsync(["import", "--help"], to.Env(), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var help = to.Out.ToString().ReplaceLineEndings(" ");
        Assert.Contains("Refused before anything is written", help, StringComparison.Ordinal);
        Assert.Contains("an archive a newer Jason wrote", help, StringComparison.Ordinal);
        Assert.Contains("Nothing is deleted", help, StringComparison.Ordinal);
        Assert.Contains("<data>/replaced-<stamp>/", help, StringComparison.Ordinal);
        Assert.Contains("Harness:Root first", help, StringComparison.Ordinal);
        Assert.Contains("Left to you: jason skills install", help, StringComparison.Ordinal);
        Assert.Contains("Exit codes: 0 placed and the runtime answered", help, StringComparison.Ordinal);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------

    private static HttpResponseMessage? Linked(string operation, string body) =>
        operation == "project.link" ? RuntimeVerbs.Response(HttpStatusCode.OK, "{\"id\":\"prj_made_up\"}") : null;

    private static void Entry(ZipArchive archive, string name)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write("made up"u8);
    }

    /// <summary>A settings file naming each of the three places, a secret, a path in an array, a path nowhere, a comment.</summary>
    private static string SettingsText(string home, string data, string root, string gone) =>
        "{\n"
        + "  // the person's own comment, kept\n"
        + "  \"Harness\": { \"Root\": " + JsonSerializer.Serialize(root) + " },\n"
        + "  \"Plugins\": { \"Config\": { \"reply\": { \"token\": \"made-up-secret\", \"exports\": " + JsonSerializer.Serialize(Path.Combine(home, "exports")) + " } } },\n"
        + "  \"Logs\": { \"Copy\": " + JsonSerializer.Serialize(Path.Combine(data, "logs", "copy")) + " },\n"
        + "  \"Elsewhere\": [ " + JsonSerializer.Serialize(Path.Combine(root, "projects")) + ", " + JsonSerializer.Serialize(gone) + ", \"not a path\" ],\n"
        + "  \"Dispatcher\": { \"MaxParallel\": 2 }\n"
        + "}\n";

    /// <summary>Every file and directory under a folder, by its path relative to it, with a file's text and a directory's slash.</summary>
    private static Dictionary<string, string> Tree(string folder)
    {
        var tree = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return tree;
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            tree[Path.GetRelativePath(folder, path)] = Directory.Exists(path) ? "/" : Encoding.UTF8.GetString(File.ReadAllBytes(path));
        }

        return tree;
    }

    /// <summary>The import was refused with the code, and nothing was written: the data directory is empty, no root exists.</summary>
    private static Jason.Contracts.Api.ErrorBody Refused(MovingMachine to, int exit, string code)
    {
        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(to.Out.ToString());
        Assert.Equal(code, error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(to.Data));
        Assert.False(Directory.Exists(to.Harness), "The harness root was made.");
        Assert.Empty(to.Processes.Launches);
        Assert.Empty(to.Registrar.Applied);
        return error;
    }
}
