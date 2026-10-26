using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Folders;

/// <summary>
/// The project a verb is about, found from the folder the shell stands in: the CLI walks up to the first marker, and a
/// project's marker gives that project, a campaign's gives the campaign's project as the runtime records it, the root's or
/// none gives nothing. It fills <c>project_id</c> only where a verb takes one and none was given, says so on stderr in one
/// line, and never adopts a marker whose folder is not the one the runtime records. Every tree is one the test owns.
/// </summary>
public sealed class WorkingFolderTests : IDisposable
{
    private const string Acme = "prj_01K7QN0000000000000000ACME";
    private const string Beta = "prj_01K7QN0000000000000000BETA";
    private const string Leads = "cmp_01K7QN000000000000000000Q3";

    private readonly string _tree = Path.Combine(Path.GetTempPath(), "jason-cli-tests", "folders-" + Guid.NewGuid().ToString("N"));

    public WorkingFolderTests()
    {
        Directory.CreateDirectory(_tree);
        Mark(RootFolder, new HarnessMarker(HarnessMarker.Root, null));
        Mark(AcmeFolder, new HarnessMarker(HarnessMarker.Project, Acme));
        Mark(LeadsFolder, new HarnessMarker(HarnessMarker.Campaign, Leads));
        Directory.CreateDirectory(Path.Combine(LeadsFolder, "drafts"));
    }

    private string RootFolder => Path.Combine(_tree, "Jason");

    private string AcmeFolder => Path.Combine(RootFolder, "projects", "acme-corp");

    private string LeadsFolder => Path.Combine(AcmeFolder, "campaigns", "q3-leads");

    [Fact]
    [Critical(Family.X2, "a campaign made from inside a project's folder lands in that project, said on stderr")]
    public async Task A_campaign_created_in_a_project_s_folder_is_made_in_that_project_and_says_so()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        var exit = await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.ProjectGet, Operations.CampaignCreate }, cli.Operations);
        cli.AssertSent(0, $$"""{"project_id":"{{Acme}}"}""");
        cli.AssertSent(1, $$"""{"name":"Q4 leads","project_id":"{{Acme}}"}""");
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains(Acme, line, StringComparison.Ordinal);
        Assert.Contains("acme-corp", line, StringComparison.Ordinal);
        Assert.Contains("from the working directory", line, StringComparison.Ordinal);
        Assert.Contains(AcmeFolder, line, StringComparison.Ordinal);
        Assert.EndsWith("give --project to name another.", line, StringComparison.Ordinal);
    }

    /// <summary>Below a campaign's folder, the project is the one the runtime records for that campaign, read once: the marker names no parent.</summary>
    [Fact]
    public async Task Inside_a_campaign_s_folder_the_project_is_the_campaign_s_as_the_runtime_records_it()
    {
        using var cli = new FolderRun(Path.Combine(LeadsFolder, "drafts"), Answers(campaignProject: Beta));

        var exit = await cli.RunAsync("campaign", "list");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.CampaignGet, Operations.CampaignList }, cli.Operations);
        cli.AssertSent(0, $$"""{"campaign_id":"{{Leads}}"}""");
        cli.AssertSent(1, $$"""{"project_id":"{{Beta}}"}""");
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains(Beta, line, StringComparison.Ordinal);
        Assert.Contains("beta", line, StringComparison.Ordinal);
        Assert.Contains("from the working directory", line, StringComparison.Ordinal);
        Assert.Contains(LeadsFolder, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A listing the folder scoped says so: <c>--human</c> shows the project above its rows, from the project the fill read,
    /// so a person reading it knows why it holds one project's campaigns; nothing more is asked of the runtime.
    /// </summary>
    [Fact]
    public async Task A_listing_scoped_by_the_folder_says_its_project_in_human_mode()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        var exit = await cli.RunAsync("campaign", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.ProjectGet, Operations.CampaignList }, cli.Operations);
        Assert.StartsWith("Project: acme-corp, from the working directory", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_the_root_nothing_is_filled_and_nothing_is_asked()
    {
        using var cli = new FolderRun(RootFolder, Answers());

        await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        Assert.Equal(new[] { Operations.CampaignCreate }, cli.Operations);
        cli.AssertSent(0, """{"name":"Q4 leads"}""");
        Assert.Empty(cli.ErrorLines);
    }

    [Fact]
    public async Task Outside_every_marked_folder_nothing_is_filled_and_nothing_is_asked()
    {
        var outside = Path.Combine(_tree, "elsewhere");
        Directory.CreateDirectory(outside);
        using var cli = new FolderRun(outside, Answers());

        await cli.RunAsync("campaign", "list");

        Assert.Equal(new[] { Operations.CampaignList }, cli.Operations);
        cli.AssertSent(0, "{}");
        Assert.Empty(cli.ErrorLines);
    }

    [Fact]
    public async Task A_project_given_on_the_line_is_never_replaced_and_no_folder_is_asked_about()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        await cli.RunAsync("campaign", "create", "--name", "Q4 leads", "--project", Beta);

        Assert.Equal(new[] { Operations.CampaignCreate }, cli.Operations);
        cli.AssertSent(0, $$"""{"name":"Q4 leads","project_id":"{{Beta}}"}""");
        Assert.Empty(cli.ErrorLines);
    }

    [Fact]
    public async Task A_project_given_in_the_file_body_is_never_replaced()
    {
        using var file = new TempFile($$"""{"name":"Q4 leads","project_id":"{{Beta}}"}""");
        using var cli = new FolderRun(AcmeFolder, Answers());

        await cli.RunAsync("campaign", "create", "--file", file.Path);

        Assert.Equal(new[] { Operations.CampaignCreate }, cli.Operations);
        Assert.Empty(cli.ErrorLines);
    }

    /// <summary>
    /// A copied folder, or one moved and not relinked, carries a marker whose folder is not the one the runtime records: it
    /// is reported on stderr and never adopted, so the verb goes on as if there were no marker.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a copied folder's marker is reported and never adopted")]
    public async Task A_copied_project_folder_is_reported_and_never_adopted()
    {
        var recorded = Path.Combine(_tree, "Jason", "projects", "acme-original");
        using var cli = new FolderRun(AcmeFolder, Answers(projectFolder: recorded));

        var exit = await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.ProjectGet, Operations.CampaignCreate }, cli.Operations);
        cli.AssertSent(1, """{"name":"Q4 leads"}""");
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains(AcmeFolder, line, StringComparison.Ordinal);
        Assert.Contains(recorded, line, StringComparison.Ordinal);
        Assert.Contains("not used", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_campaign_folder_whose_campaign_records_another_folder_is_reported_and_never_adopted()
    {
        using var cli = new FolderRun(LeadsFolder, Answers(campaignFolder: Path.Combine(_tree, "q3-copy")));

        await cli.RunAsync("campaign", "list");

        Assert.Equal(new[] { Operations.CampaignGet, Operations.CampaignList }, cli.Operations);
        cli.AssertSent(1, "{}");
        Assert.Contains("not used", Assert.Single(cli.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_project_with_no_folder_recorded_does_not_adopt_a_folder_that_names_it()
    {
        using var cli = new FolderRun(AcmeFolder, Answers(projectFolder: null));

        await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        cli.AssertSent(1, """{"name":"Q4 leads"}""");
        Assert.Contains("not used", Assert.Single(cli.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_marker_naming_a_project_the_runtime_does_not_hold_is_reported_and_never_adopted()
    {
        using var cli = new FolderRun(AcmeFolder, Answers(projectKnown: false));

        var exit = await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertSent(1, """{"name":"Q4 leads"}""");
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains(Acme, line, StringComparison.Ordinal);
        Assert.Contains("not used", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_marker_that_cannot_be_read_is_reported_and_never_adopted()
    {
        File.WriteAllText(HarnessMarker.PathIn(AcmeFolder), "{ not a marker");
        using var cli = new FolderRun(AcmeFolder, Answers());

        await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        Assert.Equal(new[] { Operations.CampaignCreate }, cli.Operations);
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains(AcmeFolder, line, StringComparison.Ordinal);
        Assert.Contains("not used", line, StringComparison.Ordinal);
    }

    /// <summary>Windows and macOS ignore case in a folder's name, so a recorded path spelled in another case is the same folder there.</summary>
    [Fact]
    public async Task The_recorded_folder_is_compared_as_the_platform_compares_names()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "Linux tells names apart by case.");
        using var cli = new FolderRun(AcmeFolder, Answers(projectFolder: AcmeFolder.ToUpperInvariant()));

        await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        cli.AssertSent(1, $$"""{"name":"Q4 leads","project_id":"{{Acme}}"}""");
    }

    /// <summary>A runtime that cannot be asked which project a marker names is the verb's own answer: exit 3, and the verb is not sent without its project.</summary>
    [Fact]
    public async Task A_runtime_that_cannot_be_asked_about_the_marker_ends_the_verb_with_3()
    {
        using var cli = new FolderRun(AcmeFolder, Answers(), descriptor: false);

        var exit = await cli.RunAsync("campaign", "create", "--name", "Q4 leads");

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Empty(cli.Operations);
        Assert.Contains(CliErrors.NoDescriptor, cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A role note written from inside a project's folder without naming a project stays at the level it names: the folder
    /// never fills one, and nothing is asked about the marker.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a role note is never moved to a project by the folder a shell stands in")]
    public async Task A_role_note_is_never_given_the_folder_s_project()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        await cli.RunAsync("--actor", "role:researcher", "rolenote", "set", Leads, "researcher", "--note", """{"pass":1}""");
        await cli.RunAsync("rolenote", "get", Leads, "researcher");
        await cli.RunAsync("rolenote", "list", "--campaign", Leads);

        // The client's level, named by no scope at all, stays the client's in a project's folder.
        await cli.RunAsync("rolenote", "get", "--role", "researcher");
        await cli.RunAsync("rolenote", "list");

        Assert.Equal(
            new[] { Operations.RoleNoteSet, Operations.RoleNoteGet, Operations.RoleNoteList, Operations.RoleNoteGet, Operations.RoleNoteList },
            cli.Operations);
        Assert.All(cli.Bodies, body => Assert.False(body.ContainsKey("project_id"), $"a role note was sent with a project: {body.ToJsonString()}"));
        Assert.Empty(cli.ErrorLines);
    }

    /// <summary>A move is never addressed by where a shell stands: <c>campaign update</c> without <c>--project</c> sends none.</summary>
    [Fact]
    public async Task A_campaign_update_is_never_given_the_folder_s_project()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        await cli.RunAsync("campaign", "update", Leads, "--name", "Q3 renamed");

        Assert.Equal(new[] { Operations.CampaignUpdate }, cli.Operations);
        cli.AssertSent(0, $$"""{"campaign_id":"{{Leads}}","name":"Q3 renamed"}""");
        Assert.Empty(cli.ErrorLines);
    }

    /// <summary>
    /// A verb that names a campaign by its id is about that campaign, whatever folder the shell stands in: run inside a
    /// campaign's own folder, none of them is given the folder's project, none asks the runtime what the marker names, and
    /// nothing is said on stderr. The rows are the campaign-id verbs of the verb map, so a verb added to the map without
    /// being listed here is a verb nobody has shown to be safe from the fill.
    /// </summary>
    [Theory]
    [Critical(Family.X2, "a campaign-id verb is never filled from the campaign folder a shell stands in")]
    [InlineData("get", Operations.CampaignGet, "campaign get {id}")]
    [InlineData("update", Operations.CampaignUpdate, "campaign update {id} --name Renamed")]
    [InlineData("start", Operations.CampaignStart, "campaign start {id}")]
    [InlineData("pause", Operations.CampaignPause, "campaign pause {id}")]
    [InlineData("archive", Operations.CampaignArchive, "campaign archive {id}")]
    [InlineData("update-context", Operations.CampaignUpdateContext, "campaign update-context {id} --set {\"stage\":\"warm\"}")]
    [InlineData("add-contacts", Operations.CampaignAddContacts, "campaign add-contacts {id} --file {file}")]
    [InlineData("remove-contacts", Operations.CampaignRemoveContacts, "campaign remove-contacts {id} --contact cnt_A")]
    [InlineData("list-contacts", Operations.CampaignListContacts, "campaign list-contacts {id}")]
    public async Task A_campaign_id_verb_run_in_a_campaign_s_folder_is_never_given_the_folder_s_project(string verb, string operation, string line)
    {
        var contacts = Path.Combine(_tree, "contacts.json");
        File.WriteAllText(contacts, """[{"first_name":"Marta","channels":{"email":"marta@example.com"}}]""");
        using var cli = new FolderRun(LeadsFolder, Answers());

        // The template is split before anything is put into it, so a path holding a space stays one argument.
        var exit = await cli.RunAsync([.. line.Split(' ').Select(word => word.Replace("{id}", Leads, StringComparison.Ordinal).Replace("{file}", contacts, StringComparison.Ordinal))]);

        Assert.True(exit == ExitCodes.Success, $"'campaign {verb}' ended {exit}: {cli.Error}");
        Assert.True(new[] { operation }.SequenceEqual(cli.Operations), $"'campaign {verb}' asked the runtime for {string.Join(", ", cli.Operations)}, and not only for {operation}.");
        var body = Assert.Single(cli.Bodies);
        Assert.False(body.ContainsKey("project_id"), $"'campaign {verb}' was sent with a project: {body.ToJsonString()}");
        Assert.True(cli.ErrorLines.Count == 0, $"'campaign {verb}' said something on stderr: {cli.Error}");
    }

    /// <summary>
    /// <c>project update</c> names its project by the id it is given. Run inside a project's folder it sends exactly the id
    /// and the new name, asks nothing about the marker and says nothing on stderr. The body alone would not show a fill
    /// that ran first, since the id written afterwards replaces the filled one; what shows it is the single request and the
    /// empty stderr, which a fill would break with the line that says where the project came from.
    /// </summary>
    [Fact]
    public async Task A_project_update_in_a_project_s_folder_sends_the_id_it_was_given_and_asks_nothing_of_the_marker()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        var exit = await cli.RunAsync("project", "update", Beta, "--name", "Beta renamed");

        Assert.True(exit == ExitCodes.Success, $"'project update' ended {exit}: {cli.Error}");
        Assert.Equal(new[] { Operations.ProjectUpdate }, cli.Operations);
        cli.AssertSent(0, new JsonObject { ["project_id"] = Beta, ["name"] = "Beta renamed" }.ToJsonString());
        Assert.True(cli.ErrorLines.Count == 0, $"'project update' said something on stderr: {cli.Error}");
    }

    /// <summary>
    /// Which verbs fill a project from a folder is decided where each is built, and three places ask: the two campaign
    /// verbs that take an optional project (create and list), and <c>project get</c> with neither id nor slug. A fourth is a
    /// verb whose writes are now addressed by where a shell stands, so it has to come here by name, with its own row above.
    /// Read off the sources, since a call that is not made leaves nothing to observe. Only the qualified spelling is counted,
    /// which is how every caller outside <c>WorkingFolder</c> has to write it and what leaves the definition itself out of
    /// the count; a call written unqualified inside WorkingFolder.cs, or one made through <c>using static</c>, would not be
    /// seen.
    /// </summary>
    [Fact]
    public void Only_three_places_in_the_client_fill_a_project_from_a_folder()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var callers = Directory.EnumerateFiles(Path.Combine(root.FullName, "runtime", "src", "Jason.Cli"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (File: Path.GetFileName(file), Calls: System.Text.RegularExpressions.Regex.Count(File.ReadAllText(file), System.Text.RegularExpressions.Regex.Escape("WorkingFolder.FillProjectAsync("))))
            .Where(caller => caller.Calls > 0)
            .OrderBy(caller => caller.File, StringComparer.Ordinal)
            .Select(caller => $"{caller.File} x{caller.Calls}")
            .ToList();

        Assert.True(callers.Count > 0, "No source of the client was read.");
        Assert.True(
            new[] { "CampaignCommands.cs x2", "ProjectCommands.cs x1" }.SequenceEqual(callers),
            $"The calls to WorkingFolder.FillProjectAsync are in: {string.Join(", ", callers)}.");
    }

    /// <summary>
    /// <c>campaign list</c> inside a project's folder lists that project's campaigns; <c>--all-projects</c> lists every
    /// project's from there, and no marker is read for it.
    /// </summary>
    [Fact]
    public async Task Campaign_list_with_all_projects_lists_every_project_s_from_a_project_s_folder()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        var exit = await cli.RunAsync("campaign", "list", "--all-projects");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.CampaignList }, cli.Operations);
        cli.AssertSent(0, "{}");
        Assert.Empty(cli.ErrorLines);
    }

    /// <summary>
    /// In JSON mode nothing on stdout says the listing was scoped, so the line on stderr says it follows the working
    /// directory's project and names <c>--all-projects</c>, as the human header does; stdout is the API's answer as it came.
    /// </summary>
    [Fact]
    public async Task Campaign_list_scoped_by_the_folder_names_all_projects_on_stderr_and_leaves_stdout_alone()
    {
        string? listed = null;
        var answers = Answers();
        using var cli = new FolderRun(AcmeFolder, (operation, body) =>
        {
            var answer = answers(operation, body);
            if (operation == Operations.CampaignList)
            {
                listed = answer.Item2;
            }

            return answer;
        });

        var exit = await cli.RunAsync("campaign", "list");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.ProjectGet, Operations.CampaignList }, cli.Operations);
        Assert.Equal(listed, cli.Text.TrimEnd());
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains("from the working directory", line, StringComparison.Ordinal);
        Assert.EndsWith("give --project to name another, or --all-projects to list every project's campaigns.", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Campaign_list_with_all_projects_and_a_project_is_a_usage_error()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        var exit = await cli.RunAsync("campaign", "list", "--all-projects", "--project", Beta);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(cli.Operations);
    }

    [Fact]
    public async Task Project_get_with_neither_an_id_nor_a_slug_inside_a_project_s_folder_shows_that_project()
    {
        using var cli = new FolderRun(LeadsFolder, Answers());

        var exit = await cli.RunAsync("project", "get");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.CampaignGet, Operations.ProjectGet }, cli.Operations);
        cli.AssertSent(1, $$"""{"project_id":"{{Acme}}"}""");
        var line = Assert.Single(cli.ErrorLines);
        Assert.Contains($"acme-corp ({Acme})", line, StringComparison.Ordinal);

        // project get names a project by its id or its slug, and has no --project to tell it another.
        Assert.EndsWith("give a project id or --slug to name another.", line, StringComparison.Ordinal);
        Assert.DoesNotContain("--project", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_get_with_neither_outside_a_project_s_folder_is_still_a_usage_error()
    {
        using var cli = new FolderRun(RootFolder, Answers());

        var exit = await cli.RunAsync("project", "get");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(cli.Operations);
    }

    /// <summary>
    /// A link with neither a project nor a path, typed inside a project's folder or one of its campaigns', links the folder
    /// of the nearest project marker above by that marker, which is the case of a folder moved and not yet relinked: the
    /// runtime reads the marker, so the CLI asks it nothing first.
    /// </summary>
    [Fact]
    public async Task A_link_with_neither_inside_a_project_s_folder_links_that_folder_by_its_marker()
    {
        using var cli = new FolderRun(Path.Combine(LeadsFolder, "drafts"), Answers());

        var exit = await cli.RunAsync("project", "link");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(new[] { Operations.ProjectLink }, cli.Operations);
        cli.AssertSent(0, new JsonObject { ["path"] = AcmeFolder }.ToJsonString());
        Assert.Contains(AcmeFolder, Assert.Single(cli.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_with_neither_at_the_root_is_a_usage_error()
    {
        using var cli = new FolderRun(Path.Combine(RootFolder, "projects"), Answers());

        var exit = await cli.RunAsync("project", "link");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(cli.Operations);
    }

    /// <summary>A link that names its project is never given a folder by the one it is typed in: a project alone means its folder by the policy.</summary>
    [Fact]
    public async Task A_link_that_names_its_project_is_never_given_the_folder_it_is_typed_in()
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        await cli.RunAsync("project", "link", Acme);

        Assert.Equal(new[] { Operations.ProjectLink }, cli.Operations);
        cli.AssertSent(0, $$"""{"project_id":"{{Acme}}"}""");
        Assert.Empty(cli.ErrorLines);
    }

    /// <summary>A relative <c>--path</c> is read from the folder the command runs in, and sent absolute, as the runtime takes it.</summary>
    [Fact]
    public async Task A_relative_path_is_read_from_the_folder_the_command_runs_in()
    {
        using var cli = new FolderRun(Path.Combine(RootFolder, "projects"), Answers());

        await cli.RunAsync("project", "link", "--path", "acme-corp");

        cli.AssertSent(0, new JsonObject { ["path"] = AcmeFolder }.ToJsonString());
    }

    /// <summary>
    /// An empty <c>--path</c> — a script's unset variable, most often — names no folder, and is never read as the working
    /// directory, which the platform would make of it: a usage error, with nothing sent and nothing adopted.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_path_is_a_usage_error_and_never_the_working_directory(string given)
    {
        using var cli = new FolderRun(AcmeFolder, Answers());

        var exit = await cli.RunAsync("project", "link", Acme, "--path", given);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(cli.Operations);
    }

    /// <summary>
    /// A working directory the platform cannot name — on Linux and macOS, a folder deleted under the shell standing in it —
    /// is no working directory, which finds nothing, rather than an end to every verb before it starts.
    /// </summary>
    [Theory]
    [InlineData("deleted")]
    [InlineData("gone above")]
    [InlineData("not to be read")]
    public void A_working_directory_the_platform_cannot_name_is_none(string why)
    {
        Exception thrown = why switch
        {
            "deleted" => new FileNotFoundException("Unable to find the specified file."),
            "gone above" => new DirectoryNotFoundException(),
            _ => new UnauthorizedAccessException(),
        };

        Assert.Null(CliEnvironment.WorkingDirectoryOf(() => throw thrown));
        Assert.Equal(_tree, CliEnvironment.WorkingDirectoryOf(() => _tree));
    }

    /// <summary>
    /// With no working directory, the verbs bound to exit 0 or 1 still do; a verb that takes a project finds none and goes
    /// on as anywhere else; and a relative <c>--path</c>, with nothing to be read from, is a usage error, while an absolute
    /// one is sent as it is.
    /// </summary>
    [Fact]
    public async Task With_no_working_directory_every_verb_still_answers_and_a_relative_path_is_a_usage_error()
    {
        var none = CliEnvironment.WorkingDirectoryOf(() => throw new FileNotFoundException());
        using var dir = new TempPaths();
        string[][] verbs = [["status"], ["logs"], ["doctor", "--dry-run"]];
        foreach (var verb in verbs)
        {
            var output = new StringWriter();
            var machine = Status.StatusCommandTests.Machine(dir, output) with
            {
                WorkingDirectory = none,
                Identity = new Jason.Cli.Doctor.IdentityFacts(@"C:\Users\Quillon", "Quillon", "WORKSTATION-7", null, null),
                Variables = new Dictionary<string, string>(),
            };

            var exit = await CliApp.RunAsync(verb, machine, TestContext.Current.CancellationToken);

            Assert.True(exit is ExitCodes.Success or ExitCodes.ApiError, $"'jason {string.Join(' ', verb)}' with no working directory ended {exit}: {output}");
        }

        using var cli = new FolderRun(none, Answers());
        Assert.Equal(ExitCodes.Success, await cli.RunAsync("campaign", "create", "--name", "Q4 leads"));
        Assert.Equal(ExitCodes.Usage, await cli.RunAsync("project", "link", Acme, "--path", "acme-corp"));
        Assert.Equal(ExitCodes.Usage, await cli.RunAsync("project", "link"));
        Assert.Equal(ExitCodes.Success, await cli.RunAsync("project", "link", Acme, "--path", AcmeFolder));
        Assert.Equal(new[] { Operations.CampaignCreate, Operations.ProjectLink }, cli.Operations);
        cli.AssertSent(0, """{"name":"Q4 leads"}""");
        cli.AssertSent(1, new JsonObject { ["project_id"] = Acme, ["path"] = AcmeFolder }.ToJsonString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tree, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Mark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
    }

    /// <summary>
    /// What the runtime answers: the project with the folder it records, the campaign with its project and folder, and an
    /// empty object for anything else the verb sends.
    /// </summary>
    private Func<string, JsonObject, (HttpStatusCode, string)> Answers(
        string? projectFolder = "<acme>",
        bool projectKnown = true,
        string campaignProject = Acme,
        string? campaignFolder = "<leads>")
    {
        var project = projectFolder == "<acme>" ? AcmeFolder : projectFolder;
        var campaign = campaignFolder == "<leads>" ? LeadsFolder : campaignFolder;
        return (operation, body) => operation switch
        {
            Operations.ProjectGet when !projectKnown => (HttpStatusCode.NotFound, $$$"""{"error":{"code":"project_not_found","message":"No project with id '{{{Acme}}}'.","retryable":false}}"""),
            Operations.ProjectGet => (HttpStatusCode.OK, new JsonObject { ["id"] = Acme, ["name"] = "Acme Corp", ["slug"] = "acme-corp", ["folder_path"] = project }.ToJsonString()),
            Operations.CampaignGet => (HttpStatusCode.OK, new JsonObject { ["id"] = Leads, ["project_id"] = campaignProject, ["project_slug"] = campaignProject == Beta ? "beta" : "acme-corp", ["folder_path"] = campaign }.ToJsonString()),
            Operations.CampaignList => (HttpStatusCode.OK, """{"items":[],"next_cursor":null}"""),
            _ => (HttpStatusCode.OK, "{}"),
        };
    }
}

/// <summary>A CLI run from a working directory, against a runtime that answers each operation as the test says and keeps every request.</summary>
internal sealed class FolderRun : IDisposable
{
    private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private readonly TempPaths _paths = new();
    private readonly CliEnvironment _environment;
    private readonly List<(string Operation, JsonObject Body)> _requests = [];

    public FolderRun(string? workingDirectory, Func<string, JsonObject, (HttpStatusCode Status, string Body)> answer, bool descriptor = true)
    {
        if (descriptor)
        {
            _paths.WriteDescriptor(Live);
        }

        _environment = new CliEnvironment(
            Output,
            Error,
            _paths.Paths,
            new FakeHandler(request =>
            {
                var operation = request.RequestUri!.AbsolutePath["/v1/".Length..];
                var body = JsonNode.Parse(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult())!.AsObject();
                _requests.Add((operation, body));
                var (status, text) = answer(operation, body);
                return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            }),
            WorkingDirectory: workingDirectory);
    }

    public StringWriter Output { get; } = new();

    public StringWriter Error { get; } = new();

    public string Text => Output.ToString();

    public IReadOnlyList<string> Operations => [.. _requests.Select(request => request.Operation)];

    public IReadOnlyList<JsonObject> Bodies => [.. _requests.Select(request => request.Body)];

    public IReadOnlyList<string> ErrorLines => Error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    public Task<int> RunAsync(params string[] args) => CliApp.RunAsync(args, _environment, TestContext.Current.CancellationToken);

    /// <summary>The request at <paramref name="index"/> carried this body; property order is not part of the contract.</summary>
    public void AssertSent(int index, string expectedBody)
    {
        Assert.True(index < _requests.Count, $"only {_requests.Count} request(s) were sent: {string.Join(", ", Operations)}");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedBody), _requests[index].Body), $"request {index} ({_requests[index].Operation}) was {_requests[index].Body.ToJsonString()}");
    }

    public void Dispose() => _paths.Dispose();
}
