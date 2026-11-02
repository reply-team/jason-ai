using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Jason.Contracts.Harness;
using Jason.Contracts.Hosts;
using Jason.Runtime.Domain;
using Jason.Runtime.Harness;
using Jason.Runtime.Projects;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// The folder policy on a disk the test owns: the root made at the first need or adopted, the levels below it with their
/// markers and their two files, the names a folder takes, and every refusal leaving the disk as it was. A folder already
/// at a level's path is never walked around: with no marker it becomes the level's, marked for anything else it is refused,
/// and only a campaign leaf another campaign of this runtime has is numbered.
/// </summary>
public sealed class HarnessFoldersTests : IDisposable
{
    private const string Project = "prj_01K7QN0000000000000000ACME";

    private const string Other = "prj_01K7QN000000000000000OTHER";

    private const string Third = "prj_01K7QN000000000000000THIRD";

    private const string Campaign = "cmp_01K7QN000000000000000000Q3";

    private const string Nobody = "cmp_01K7QN0000000000000NOBODY";

    private readonly TempTree _tree = new();

    /// <summary>The campaigns this test's runtime would hold: the ones it made folders for.</summary>
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Home => Path.Combine(_tree.Root, "home");

    private string Root => Path.Combine(Home, "Jason");

    public void Dispose() => _tree.Dispose();

    [Fact]
    public void The_root_is_made_at_the_first_need_with_its_marker_and_its_two_files()
    {
        var folder = TestHarness.Folders(Home).MakeProjectFolder(Project, "acme");

        Assert.Equal(Path.Combine(Root, "projects", "acme"), folder);
        Assert.Equal("""{"marker_version":1,"kind":"root"}""", Text(Root, ".jason", "marker.json"));
        Assert.Equal("@AGENTS.md", Text(Root, "CLAUDE.md"));
        Assert.Equal(string.Empty, Text(Root, "AGENTS.md"));
        Assert.Equal($$"""{"marker_version":1,"kind":"project","id":"{{Project}}"}""", Text(folder, ".jason", "marker.json"));
        Assert.Equal("@AGENTS.md", Text(folder, "CLAUDE.md"));
        Assert.Equal(string.Empty, Text(folder, "AGENTS.md"));
    }

    /// <summary>
    /// Every file a host reads in place of the memory file is written at each level where absent, holding exactly the line
    /// of the table of hosts that imports the memory file, and one the person wrote is never opened.
    /// </summary>
    [Theory]
    [MemberData(nameof(ImportFiles))]
    public async Task Every_host_s_import_file_is_written_at_each_level_where_absent_and_never_over_one(string file, string line)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, file), "Read the notes first.\n");
        var folders = TestHarness.Folders(Home);

        var project = folders.MakeProjectFolder(Project, "acme");
        var campaign = await MakeAsync(folders, project, Campaign, "Q3 Marketing Leads");

        Assert.Equal("Read the notes first.\n", Text(Root, file));
        Assert.Equal(line, Text(project, file));
        Assert.Equal(line, Text(campaign, file));
    }

    public static TheoryData<string, string> ImportFiles()
    {
        var data = new TheoryData<string, string>();
        foreach (var file in AgentHosts.ImportFiles)
        {
            data.Add(file.FileName, file.ImportLine);
        }

        return data;
    }

    /// <summary>
    /// The levels are the policy's three and nothing else: <c>projects/</c> and <c>campaigns/</c> are plain directories,
    /// with no marker and no files of their own.
    /// </summary>
    [Fact]
    public async Task A_campaign_s_folder_is_in_its_project_s_campaigns_and_nothing_else_is_written()
    {
        var folders = TestHarness.Folders(Home);
        var project = folders.MakeProjectFolder(Project, "acme");

        var campaign = await MakeAsync(folders, project, Campaign, "Q3 Marketing Leads");

        Assert.Equal(Path.Combine(project, "campaigns", "q3-marketing-leads"), campaign);
        Assert.Equal($$"""{"marker_version":1,"kind":"campaign","id":"{{Campaign}}"}""", Text(campaign, ".jason", "marker.json"));
        string[] expected =
        [
            "AGENTS.md", "CLAUDE.md", ".jason/marker.json",
            "projects/acme/AGENTS.md", "projects/acme/CLAUDE.md", "projects/acme/.jason/marker.json",
            "projects/acme/campaigns/q3-marketing-leads/AGENTS.md", "projects/acme/campaigns/q3-marketing-leads/CLAUDE.md",
            "projects/acme/campaigns/q3-marketing-leads/.jason/marker.json",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Files(Root));
    }

    /// <summary>
    /// A folder already at the root is adopted: its marker is written, the two files where absent, and not a byte of what
    /// it held changes — the person's own <c>CLAUDE.md</c> and <c>AGENTS.md</c> included.
    /// </summary>
    [Fact]
    public void An_existing_root_is_adopted_by_its_marker_and_never_a_word_of_its_files()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "CLAUDE.md"), "Read the notes first.\n");
        File.WriteAllText(Path.Combine(Root, "AGENTS.md"), "Sell to dental clinics in Ontario.\n");
        File.WriteAllText(Path.Combine(Root, "notes.txt"), "kept\n");
        var before = Snapshot(Root);

        TestHarness.Folders(Home).MakeProjectFolder(Project, "acme");

        Assert.Equal("""{"marker_version":1,"kind":"root"}""", Text(Root, ".jason", "marker.json"));
        Assert.All(before, file => Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(Root, file.Key))));
    }

    [Fact]
    public void An_adopted_root_without_the_two_files_is_given_them()
    {
        Directory.CreateDirectory(Root);

        TestHarness.Folders(Home).MakeProjectFolder(Project, "acme");

        Assert.Equal("@AGENTS.md", Text(Root, "CLAUDE.md"));
        Assert.Equal(string.Empty, Text(Root, "AGENTS.md"));
    }

    /// <summary>A root that has its marker is used as it is: nothing at the root is written again.</summary>
    [Fact]
    public void A_root_that_has_its_marker_is_not_written_again()
    {
        var folders = TestHarness.Folders(Home);
        folders.MakeProjectFolder(Project, "acme");
        File.Delete(Path.Combine(Root, "AGENTS.md"));
        var before = Snapshot(Root, recursive: false);

        folders.MakeProjectFolder(Other, "other");

        Assert.Equal(before, Snapshot(Root, recursive: false));
        Assert.False(File.Exists(Path.Combine(Root, "AGENTS.md")), "a memory file the person removed from the root was written again.");
    }

    [Theory]
    [InlineData("""{"marker_version":1,"kind":"project","id":"prj_01K7QN000000000000000OTHER"}""")]
    [InlineData("""{"marker_version":2,"kind":"root"}""")]
    [InlineData("not a marker")]
    public void A_root_marked_as_anything_but_a_root_is_refused_with_nothing_written(string marker)
    {
        Directory.CreateDirectory(Path.Combine(Root, ".jason"));
        File.WriteAllText(Path.Combine(Root, ".jason", "marker.json"), marker);
        var before = Snapshot(Root);

        var refused = Assert.Throws<DomainException>(() => TestHarness.Folders(Home).MakeProjectFolder(Project, "acme"));

        Assert.Equal("harness_root_taken", refused.Code);
        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public void A_file_where_the_root_should_be_is_refused_and_left_as_it_is()
    {
        Directory.CreateDirectory(Home);
        File.WriteAllText(Root, "a file named like the root");

        var refused = Assert.Throws<DomainException>(() => TestHarness.Folders(Home).MakeProjectFolder(Project, "acme"));

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Contains(Root, refused.Message, StringComparison.Ordinal);
        Assert.Equal("a file named like the root", File.ReadAllText(Root));
    }

    [Fact]
    public void A_file_where_projects_should_be_is_refused()
    {
        var folders = TestHarness.Folders(Home);
        folders.MakeProjectFolder(Project, "acme");
        Directory.Delete(Path.Combine(Root, "projects"), recursive: true);
        File.WriteAllText(Path.Combine(Root, "projects"), "in the way");

        var refused = Assert.Throws<DomainException>(() => folders.MakeProjectFolder(Other, "other"));

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal("in the way", File.ReadAllText(Path.Combine(Root, "projects")));
    }

    /// <summary>The root is wherever the setting says, absolute, and the default is never consulted then.</summary>
    [Fact]
    public void A_root_the_setting_names_is_where_the_folders_are_made()
    {
        var chosen = Path.Combine(_tree.Root, "clients", "harness");

        var folder = TestHarness.Folders(Home, chosen).MakeProjectFolder(Project, "acme");

        Assert.Equal(Path.Combine(chosen, "projects", "acme"), folder);
        Assert.False(Directory.Exists(Root), "the default root was made although the setting named another.");
    }

    /// <summary>
    /// A campaign's leaf is made from its name as a project's slug is: folded, lower case, runs of anything else one hyphen,
    /// at most forty characters; a name that leaves too little takes <c>campaign-</c> and six hexadecimal characters, a
    /// device name <c>-campaign</c>.
    /// </summary>
    [Theory]
    [InlineData("Q3 Marketing Leads", "q3-marketing-leads")]
    [InlineData("Álvaro's Café — Łódź", "alvaro-s-cafe-lodz")]
    [InlineData("A name long enough to be cut at forty characters exactly", "a-name-long-enough-to-be-cut-at-forty-ch")]
    [InlineData("CON", "con-campaign")]
    [InlineData("Кампания", "campaign-")]
    public void A_campaign_s_leaf_is_made_from_its_name_as_a_slug_is(string name, string expected)
    {
        var leaf = ProjectSlug.Leaf(name, "campaign");

        Assert.True(expected.EndsWith('-') ? leaf.StartsWith(expected, StringComparison.Ordinal) && leaf.Length == expected.Length + 6 : leaf == expected, $"'{name}' made the leaf '{leaf}'.");
        Assert.True(leaf.Length <= ProjectSlug.MaxLength, $"'{leaf}' is longer than {ProjectSlug.MaxLength} characters.");
    }

    /// <summary>
    /// A leaf another campaign this runtime holds has gets the next number, since campaign names are not unique, and the
    /// number never carries a leaf past forty characters.
    /// </summary>
    [Fact]
    public async Task A_leaf_another_campaign_has_gets_the_next_number()
    {
        var folders = TestHarness.Folders(Home);
        var project = folders.MakeProjectFolder(Project, "acme");
        var first = await MakeAsync(folders, project, "cmp_01K7QN0000000000000000FIRST", "Q3 leads");

        var second = await MakeAsync(folders, project, Campaign, "Q3 leads");
        var long1 = await MakeAsync(folders, project, "cmp_01K7QN00000000000000000LONG1", new string('a', 60));
        var long2 = await MakeAsync(folders, project, "cmp_01K7QN00000000000000000LONG2", new string('a', 60));

        Assert.Equal("q3-leads", Path.GetFileName(first));
        Assert.Equal("q3-leads-2", Path.GetFileName(second));
        Assert.Equal(new string('a', 40), Path.GetFileName(long1));
        Assert.Equal(new string('a', 38) + "-2", Path.GetFileName(long2));
    }

    /// <summary>A folder at the leaf with no marker becomes the campaign's, as the root does: its marker is written, the two files where absent, and nothing it held changes.</summary>
    [Fact]
    public async Task An_unmarked_folder_at_a_campaign_s_leaf_becomes_the_campaign_s()
    {
        var folders = TestHarness.Folders(Home);
        var project = folders.MakeProjectFolder(Project, "acme");
        var waiting = Path.Combine(project, "campaigns", "q3-leads");
        Directory.CreateDirectory(waiting);
        File.WriteAllText(Path.Combine(waiting, "AGENTS.md"), "Clinics in Ontario first.\n");

        var campaign = await MakeAsync(folders, project, Campaign, "Q3 leads");

        Assert.Equal(waiting, campaign);
        Assert.Equal(new HarnessMarker(HarnessMarker.Campaign, Campaign), HarnessMarker.In(campaign));
        Assert.Equal("Clinics in Ontario first.\n", File.ReadAllText(Path.Combine(campaign, "AGENTS.md")));
        Assert.Equal("@AGENTS.md", Text(campaign, "CLAUDE.md"));
    }

    /// <summary>
    /// A folder at the leaf marked as another level, or for a campaign no row of this runtime holds — the folder a create
    /// left when its commit failed, say — is not walked around: it is refused, named, and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("""{"marker_version":1,"kind":"campaign","id":"cmp_01K7QN0000000000000NOBODY"}""")]
    [InlineData("""{"marker_version":1,"kind":"project","id":"prj_01K7QN000000000000000OTHER"}""")]
    [InlineData("not a marker")]
    public async Task A_campaign_s_leaf_marked_for_anything_but_a_held_campaign_is_refused_with_nothing_written(string marker)
    {
        var folders = TestHarness.Folders(Home);
        var project = folders.MakeProjectFolder(Project, "acme");
        var taken = Path.Combine(project, "campaigns", "q3-leads");
        Directory.CreateDirectory(Path.Combine(taken, ".jason"));
        File.WriteAllText(Path.Combine(taken, ".jason", "marker.json"), marker);
        var before = Snapshot(Root);

        var refused = await Assert.ThrowsAsync<DomainException>(() => MakeAsync(folders, project, Campaign, "Q3 leads"));

        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Contains(taken, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(Root));
    }

    /// <summary>A file where a campaign's folder belongs is in the way: refused as such, never taken for nothing and never walked around.</summary>
    [Fact]
    public async Task A_file_at_a_campaign_s_leaf_is_refused_as_in_the_way()
    {
        var folders = TestHarness.Folders(Home);
        var project = folders.MakeProjectFolder(Project, "acme");
        Directory.CreateDirectory(Path.Combine(project, "campaigns"));
        File.WriteAllText(Path.Combine(project, "campaigns", "q3-leads"), "a file of the person's own");

        var refused = await Assert.ThrowsAsync<DomainException>(() => MakeAsync(folders, project, Campaign, "Q3 leads"));

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal("a file of the person's own", File.ReadAllText(Path.Combine(project, "campaigns", "q3-leads")));
    }

    /// <summary>
    /// A project's folder is named by its slug and nothing else. A folder there with no marker becomes the project's; one
    /// marked for another project, another level or an id nobody holds is refused as taken, with nothing written; the
    /// project's own is used again.
    /// </summary>
    [Fact]
    public void A_project_s_folder_is_its_slug_s_or_refused_and_its_own_is_used_again()
    {
        var folders = TestHarness.Folders(Home);
        var other = folders.MakeProjectFolder(Other, "acme");
        var before = Snapshot(Root);

        var refused = Assert.Throws<DomainException>(() => folders.MakeProjectFolder(Project, "acme"));
        var again = folders.MakeProjectFolder(Other, "acme");

        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Contains(other, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(Root));
        Assert.Equal(other, again);
        Assert.False(Directory.Exists(Path.Combine(Root, "projects", "acme-2")), "a project's folder was numbered rather than refused.");
    }

    /// <summary>
    /// A marker cut short — what a write that failed halfway leaves, an empty file included — is read as naming nothing,
    /// even where what is left of it begins as this very project's would: the folder is refused as taken, naming it, and
    /// the marker is never written over.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void A_folder_whose_marker_was_cut_short_is_refused_and_its_marker_never_written_over(int kept)
    {
        var whole = new HarnessMarker(HarnessMarker.Project, Project).Bytes();
        var waiting = Path.Combine(Root, "projects", "acme");
        Directory.CreateDirectory(Path.Combine(waiting, ".jason"));
        File.WriteAllBytes(HarnessMarker.PathIn(waiting), whole[..kept]);
        TestHarness.Folders(Home).MakeProjectFolder(Other, "other");
        var before = Snapshot(waiting);

        var refused = Assert.Throws<DomainException>(() => TestHarness.Folders(Home).MakeProjectFolder(Project, "acme"));

        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Contains(waiting, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(waiting));
    }

    [Fact]
    public void An_unmarked_folder_at_a_project_s_slug_becomes_the_project_s()
    {
        var waiting = Path.Combine(Root, "projects", "acme");
        Directory.CreateDirectory(waiting);
        File.WriteAllText(Path.Combine(waiting, "AGENTS.md"), "We sell to dental clinics.\n");
        File.WriteAllText(Path.Combine(waiting, "CLAUDE.md"), "Read the pricing notes.\n");

        var folder = TestHarness.Folders(Home).MakeProjectFolder(Project, "acme");

        Assert.Equal(waiting, folder);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, Project), HarnessMarker.In(folder));
        Assert.Equal("We sell to dental clinics.\n", File.ReadAllText(Path.Combine(folder, "AGENTS.md")));
        Assert.Equal("Read the pricing notes.\n", File.ReadAllText(Path.Combine(folder, "CLAUDE.md")));
    }

    /// <summary>
    /// A level's marker is the first thing written, so a folder another's marker reached first is refused with nothing
    /// written in it. A step that fails after the marker leaves the folder marked for the id it was made for: the same
    /// level's next attempt finishes it, and anything else's names it as taken, as it names a folder left by a create that
    /// was taken back.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void A_folder_whose_files_cannot_be_written_keeps_its_marker_and_is_named_at_another_s_attempt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows lets a folder refuse new files while it still takes new folders, which is what puts a failure between a level's marker and its files.");
        var folders = TestHarness.Folders(Home);
        folders.MakeProjectFolder(Other, "other");
        var waiting = Path.Combine(Root, "projects", "acme");
        Directory.CreateDirectory(waiting);

        DomainException refused;
        var allow = DenyNewFiles(waiting);
        try
        {
            refused = Assert.Throws<DomainException>(() => folders.MakeProjectFolder(Project, "acme"));
        }
        finally
        {
            allow();
        }

        var markedThen = HarnessMarker.In(waiting);
        var taken = Assert.Throws<DomainException>(() => folders.MakeProjectFolder(Third, "acme"));
        var folder = folders.MakeProjectFolder(Project, "acme");

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, Project), markedThen);
        Assert.Equal("harness_folder_taken", taken.Code);
        Assert.Equal((waiting, "@AGENTS.md"), (folder, Text(folder, "CLAUDE.md")));
    }

    [Fact]
    public void A_file_at_a_project_s_slug_is_refused_as_in_the_way()
    {
        Directory.CreateDirectory(Path.Combine(Root, "projects"));
        File.WriteAllText(Path.Combine(Root, "projects", "acme"), "in the way");

        var refused = Assert.Throws<DomainException>(() => TestHarness.Folders(Home).MakeProjectFolder(Project, "acme"));

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal("in the way", File.ReadAllText(Path.Combine(Root, "projects", "acme")));
    }

    /// <summary>A recorded folder that is gone, or no longer carries the project's marker, is missing; a file standing at its path is in the way.</summary>
    [Fact]
    public void A_recorded_project_folder_that_is_gone_or_marked_for_another_is_missing_and_a_file_there_is_in_the_way()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");

        Assert.Equal(acme, folders.RequireProjectFolder(Project, acme));
        var marked = Assert.Throws<DomainException>(() => folders.RequireProjectFolder(Project, other));
        Directory.Move(acme, Path.Combine(_tree.Root, "moved-away"));
        var gone = Assert.Throws<DomainException>(() => folders.RequireProjectFolder(Project, acme));
        File.WriteAllText(acme, "a file where the folder was");
        var file = Assert.Throws<DomainException>(() => folders.RequireProjectFolder(Project, acme));

        Assert.Equal("harness_folder_missing", marked.Code);
        Assert.Equal("harness_folder_missing", gone.Code);
        Assert.Equal("harness_folder_unavailable", file.Code);
        Assert.Equal(
            $"The folder recorded for '{Project}', '{acme}', is not there or no longer carries its marker, so the campaign was not made or moved; move it back to '{acme}', or record where it is now with jason project link.",
            gone.Message);
    }

    /// <summary>A move is one rename: the folder, with everything the person wrote in it, is in the other project's <c>campaigns/</c> under the same leaf.</summary>
    [Fact]
    public async Task A_campaign_s_folder_moves_by_one_rename_with_everything_in_it()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        var campaign = await MakeAsync(folders, acme, Campaign, "Q3 leads");
        File.WriteAllText(Path.Combine(campaign, "AGENTS.md"), "Only clinics with two chairs or more.\n");
        File.WriteAllText(Path.Combine(campaign, "draft.md"), "kept\n");
        var before = Snapshot(campaign);

        var moved = await MoveAsync(folders, campaign, Campaign, other);

        Assert.Equal(Path.Combine(other, "campaigns", "q3-leads"), moved);
        Assert.False(Directory.Exists(campaign), "the folder was copied rather than moved.");
        Assert.Equal(before, Snapshot(moved));
    }

    /// <summary>
    /// A folder recorded behind a Windows device prefix is on the volume its drive names: a move between it and a project
    /// spelled plainly is one rename, never refused as a move to another volume.
    /// </summary>
    [Fact]
    public async Task A_move_between_a_path_behind_a_device_prefix_and_a_plain_one_is_on_one_volume()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Device prefixes are Windows' own; elsewhere such a path is a relative one.");
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        var campaign = await MakeAsync(folders, @"\\?\" + acme, Campaign, "Q3 leads");

        var moved = await MoveAsync(folders, campaign, Campaign, other);

        Assert.Equal(Path.Combine(other, "campaigns", "q3-leads"), moved);
        Assert.Equal(new HarnessMarker(HarnessMarker.Campaign, Campaign), HarnessMarker.In(moved));
    }

    [Fact]
    public async Task A_move_into_a_leaf_another_campaign_has_takes_the_next_number()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        await MakeAsync(folders, other, "cmp_01K7QN000000000000000THERE", "Q3 leads");
        var campaign = await MakeAsync(folders, acme, Campaign, "Q3 leads");

        var moved = await MoveAsync(folders, campaign, Campaign, other);

        Assert.Equal(Path.Combine(other, "campaigns", "q3-leads-2"), moved);
    }

    /// <summary>
    /// A rename never lands on a folder that is there: one at the leaf that is not another held campaign's is refused as
    /// taken, said as a move's refusal rather than as a marker's, since the folder there may have none, and nothing moves.
    /// </summary>
    [Fact]
    public async Task A_move_into_a_leaf_held_by_a_folder_of_the_person_s_own_is_refused_and_nothing_moves()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        var campaign = await MakeAsync(folders, acme, Campaign, "Q3 leads");
        var there = Path.Combine(other, "campaigns", "q3-leads");
        Directory.CreateDirectory(there);
        var before = Snapshot(Root);

        var refused = await Assert.ThrowsAsync<DomainException>(() => MoveAsync(folders, campaign, Campaign, other));

        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Equal(
            $"The folder '{there}' is already there, where the campaign's folder would move, and a move never lands on a folder that is there, so nothing was moved; rename or move that folder away, then try again.",
            refused.Message);
        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public async Task A_move_whose_folder_is_not_where_it_was_recorded_is_refused_and_nothing_is_made()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        var campaign = await MakeAsync(folders, acme, Campaign, "Q3 leads");
        Directory.Move(campaign, Path.Combine(_tree.Root, "moved-away"));
        var before = Snapshot(Root);

        var refused = await Assert.ThrowsAsync<DomainException>(() => MoveAsync(folders, campaign, Campaign, other));

        Assert.Equal("harness_folder_missing", refused.Code);
        Assert.Equal(
            $"The folder recorded for '{Campaign}', '{campaign}', is not there or no longer carries its marker, so the campaign stays where it was; move it back to '{campaign}', or relink its project with jason project link, which finds a campaign's folder by its marker.",
            refused.Message);
        Assert.Equal(before, Snapshot(Root));
    }

    /// <summary>On Windows a folder with a file held open inside it cannot be renamed; the move is refused, and the folder stays whole where it was.</summary>
    [Fact]
    public async Task A_move_of_a_folder_held_open_is_refused_and_the_folder_stays_where_it_was()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a file held open does not stop a rename of its folder.");
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        await MakeAsync(folders, other, "cmp_01K7QN000000000000000THERE", "Q4 leads");
        var campaign = await MakeAsync(folders, acme, Campaign, "Q3 leads");
        var before = Snapshot(Root);

        DomainException refused;
        using (new FileStream(Path.Combine(campaign, "AGENTS.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await Assert.ThrowsAsync<DomainException>(() => MoveAsync(folders, campaign, Campaign, other));
        }

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal(before, Snapshot(Root));
    }

    /// <summary>
    /// A folder that is gone when the rename comes is a rename the disk refuses, on every platform and not only where an
    /// open file stops one: the move is refused <c>harness_folder_unavailable</c>, and nothing is made at the target.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a rename the disk refuses is refused, and makes nothing at the target")]
    public async Task A_move_whose_source_is_gone_at_the_rename_is_refused_and_nothing_is_made_at_the_target()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        await MakeAsync(folders, other, "cmp_01K7QN000000000000000THERE", "Q4 leads");
        var campaign = await MakeAsync(folders, acme, Campaign, "Q3 leads");
        var leaf = await folders.PlanCampaignMoveAsync(campaign, Campaign, other, HeldAsync);
        Directory.Delete(campaign, recursive: true);
        var before = Snapshot(Root);

        var refused = Assert.Throws<DomainException>(() => folders.MoveCampaignFolder(campaign, other, leaf));

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.False(Directory.Exists(Path.Combine(other, "campaigns", leaf)), $"a folder was made at the target '{leaf}'.");
        Assert.Equal(before, Snapshot(Root));
    }

    /// <summary>
    /// Putting a moved folder back is one rename: the folder is at the path the database records again, with what was in
    /// it. A folder that cannot be put back — the recorded path is taken — is left where the move put it, and nothing
    /// is thrown, since the refusal that follows says where it is.
    /// </summary>
    [Fact]
    public void A_moved_folder_is_put_back_and_one_that_cannot_be_is_left_where_it_is_without_a_throw()
    {
        var moved = Path.Combine(_tree.Root, "moved");
        var recorded = Path.Combine(_tree.Root, "recorded");
        Directory.CreateDirectory(moved);
        File.WriteAllText(Path.Combine(moved, "AGENTS.md"), "kept");

        HarnessFolders.MoveBack(moved, recorded);

        Assert.False(Directory.Exists(moved), $"'{moved}' is still there.");
        Assert.Equal("kept", Text(recorded, "AGENTS.md"));

        var stuck = Path.Combine(_tree.Root, "stuck");
        var taken = Path.Combine(_tree.Root, "taken");
        Directory.CreateDirectory(stuck);
        File.WriteAllText(Path.Combine(stuck, "AGENTS.md"), "left");
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "AGENTS.md"), "theirs");

        HarnessFolders.MoveBack(stuck, taken);

        Assert.Equal("left", Text(stuck, "AGENTS.md"));
        Assert.Equal("theirs", Text(taken, "AGENTS.md"));
        Assert.Equal(["AGENTS.md"], Directory.GetFileSystemEntries(taken).Select(Path.GetFileName));
    }

    /// <summary>Nothing the policy does writes a repository's files: no <c>.git</c>, no <c>.gitignore</c>, at any level.</summary>
    [Fact]
    public async Task No_level_is_ever_given_a_repository_s_files()
    {
        var folders = TestHarness.Folders(Home);
        var acme = folders.MakeProjectFolder(Project, "acme");
        var other = folders.MakeProjectFolder(Other, "other");
        await MoveAsync(folders, await MakeAsync(folders, acme, Campaign, "Q3 leads"), Campaign, other);

        var entries = Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();

        Assert.DoesNotContain(".git", entries);
        Assert.DoesNotContain(".gitignore", entries);
    }

    /// <summary>Makes a campaign's folder as the runtime would, holding the campaign from then on, as its row would be held.</summary>
    private async Task<string> MakeAsync(HarnessFolders folders, string project, string id, string name)
    {
        _held.Add(id);
        return await folders.MakeCampaignFolderAsync(project, id, name, HeldAsync);
    }

    /// <summary>Moves a campaign's folder as the runtime would: its plan, which writes nothing, and then the rename.</summary>
    private async Task<string> MoveAsync(HarnessFolders folders, string campaign, string id, string project)
    {
        var leaf = await folders.PlanCampaignMoveAsync(campaign, id, project, HeldAsync);
        return folders.MoveCampaignFolder(campaign, project, leaf);
    }

    private Task<bool> HeldAsync(string id)
    {
        Ct.ThrowIfCancellationRequested();
        return Task.FromResult(_held.Contains(id) && !string.Equals(id, Nobody, StringComparison.Ordinal));
    }

    private static string Text(params string[] parts) => Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(parts)));

    /// <summary>
    /// Denies this account new files in <paramref name="folder"/> itself, while it may still make folders there and
    /// anything in them, and returns the undoing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Action DenyNewFiles(string folder)
    {
        var directory = new DirectoryInfo(folder);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        var security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
        return () =>
        {
            var now = directory.GetAccessControl();
            now.RemoveAccessRule(rule);
            directory.SetAccessControl(now);
        };
    }

    /// <summary>Every file below <paramref name="root"/>, by its path from there written with <c>/</c>, in order.</summary>
    private static List<string> Files(string root) =>
        [.. Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>Every file and directory below <paramref name="root"/>, each file with its bytes, so a test can say nothing changed.</summary>
    private static SortedDictionary<string, byte[]> Snapshot(string root, bool recursive = true)
    {
        var snapshot = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var directory in Directory.GetDirectories(root, "*", option))
        {
            snapshot[Path.GetRelativePath(root, directory) + "/"] = [];
        }

        foreach (var file in Directory.GetFiles(root, "*", option))
        {
            snapshot[Path.GetRelativePath(root, file)] = File.ReadAllBytes(file);
        }

        return snapshot;
    }
}
