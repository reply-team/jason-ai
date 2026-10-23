using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Harness;
using Jason.Runtime.Harness;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// The folder policy through the operations that use it: <c>project.create</c> and <c>campaign.create</c> make the folders
/// they need and answer them as <c>folder_path</c>; a rename and an archive leave a folder where it is; a campaign that
/// changes project takes its folder along by one rename; and every refusal leaves the database and the disk as they were.
/// </summary>
public sealed class HarnessFolderOperationsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_project_is_made_with_its_folder_and_answered_with_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var root = RootOf(api);

        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);

        Assert.Equal(Path.Combine(root, "projects", "acme-corp"), acme.FolderPath);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, acme.Id), HarnessMarker.In(acme.FolderPath!));
        Assert.Equal(new HarnessMarker(HarnessMarker.Root, null), HarnessMarker.In(root));
        Assert.Equal(acme, await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { project_id = acme.Id }, Ct));
        var listed = await api.PostOkAsync<Page<ProjectDto>>(Operations.ProjectList, new { limit = 10 }, Ct);
        Assert.Equal(new string?[] { null, acme.FolderPath }, listed.Items.Select(project => project.FolderPath));
    }

    /// <summary>A project whose folder cannot be made is not made: no row, no line in the chronicle, and the file in the way untouched.</summary>
    [Fact]
    public async Task A_project_whose_folder_cannot_be_made_is_refused_whole()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var root = RootOf(api);
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "in the way");

        var refused = await api.PostErrorAsync(Operations.ProjectCreate, new { name = "Acme Corp" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal(("harness_folder_unavailable", false), (refused.Code, refused.Retryable));
        Assert.Equal("project_not_found", (await api.PostErrorAsync(Operations.ProjectGet, new { slug = "acme-corp" }, HttpStatusCode.NotFound, Ct)).Code);
        Assert.DoesNotContain("project_created", await KindsAsync(api));
        Assert.Equal("in the way", File.ReadAllText(root));
    }

    /// <summary>
    /// A folder at the project's path whose marker names an id no project here holds — the folder a create leaves when its
    /// commit fails after its folders stand — is named at the next attempt, never walked around and never deleted.
    /// </summary>
    [Fact]
    public async Task A_project_whose_path_holds_a_folder_marked_for_an_unknown_id_is_refused_naming_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var orphan = Path.Combine(RootOf(api), "projects", "acme-corp");
        Directory.CreateDirectory(Path.Combine(orphan, ".jason"));
        File.WriteAllText(Path.Combine(orphan, ".jason", "marker.json"), """{"marker_version":1,"kind":"project","id":"prj_01K7QN000000000000000GHOST"}""");

        var refused = await api.PostErrorAsync(Operations.ProjectCreate, new { name = "Acme Corp" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal(("harness_folder_taken", false), (refused.Code, refused.Retryable));
        Assert.Contains(orphan, refused.Message, StringComparison.Ordinal);
        Assert.Equal("project_not_found", (await api.PostErrorAsync(Operations.ProjectGet, new { slug = "acme-corp" }, HttpStatusCode.NotFound, Ct)).Code);
        Assert.DoesNotContain("project_created", await KindsAsync(api));
        Assert.Equal(new string?[] { ".jason" }, Directory.GetFileSystemEntries(orphan).Select(Path.GetFileName));
    }

    /// <summary>
    /// A campaign whose folder step fails is taken back whole: the row written first in the create's transaction is rolled
    /// back with its line, so a folder that could not be made leaves no campaign behind it.
    /// </summary>
    [Fact]
    public async Task A_campaign_whose_folder_cannot_be_made_is_rolled_back_with_its_line()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        Directory.CreateDirectory(Path.Combine(acme.FolderPath!, "campaigns"));
        File.WriteAllText(Path.Combine(acme.FolderPath!, "campaigns", "q3-leads"), "in the way");

        var refused = await api.PostErrorAsync(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Empty((await api.PostOkAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, new { project_id = acme.Id }, Ct)).Items);
        Assert.DoesNotContain("campaign_created", await KindsAsync(api));
        Assert.Equal("in the way", File.ReadAllText(Path.Combine(acme.FolderPath!, "campaigns", "q3-leads")));
    }

    /// <summary>
    /// A campaign's leaf taken by a folder marked for a campaign nobody here holds is refused, naming it; one taken by
    /// another campaign this runtime holds is numbered, since campaign names are not unique.
    /// </summary>
    [Fact]
    public async Task A_campaign_leaf_is_numbered_for_a_held_campaign_and_refused_for_an_unknown_one()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var first = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var second = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var orphan = Path.Combine(acme.FolderPath!, "campaigns", "q4-leads");
        Directory.CreateDirectory(Path.Combine(orphan, ".jason"));
        File.WriteAllText(Path.Combine(orphan, ".jason", "marker.json"), """{"marker_version":1,"kind":"campaign","id":"cmp_01K7QN000000000000000GHOST"}""");

        var refused = await api.PostErrorAsync(Operations.CampaignCreate, new { name = "Q4 leads", project_id = acme.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal((Path.Combine(acme.FolderPath!, "campaigns", "q3-leads"), Path.Combine(acme.FolderPath!, "campaigns", "q3-leads-2")), (first.FolderPath, second.FolderPath));
        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Contains(orphan, refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await KindsAsync(api)).Count(kind => kind == "campaign_created"));
    }

    /// <summary>
    /// The first campaign of a project with no folder recorded — the default project, here — makes the root, the project's
    /// folder and its own, and every answer that carries the campaign carries the folder.
    /// </summary>
    [Fact]
    public async Task A_campaign_is_made_in_its_project_s_folder_and_the_project_s_first_where_it_has_none()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var root = RootOf(api);

        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 Marketing Leads" }, Ct);

        var standing = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        Assert.Equal(Path.Combine(root, "projects", "default"), standing.FolderPath);
        Assert.Equal(Path.Combine(root, "projects", "default", "campaigns", "q3-marketing-leads"), leads.FolderPath);
        Assert.Equal(new HarnessMarker(HarnessMarker.Campaign, leads.Id), HarnessMarker.In(leads.FolderPath!));
        Assert.Equal(leads.FolderPath, (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = leads.Id }, Ct)).FolderPath);
        var listed = await api.PostOkAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, new { limit = 10 }, Ct);
        Assert.Equal(new string?[] { leads.FolderPath }, listed.Items.Select(campaign => campaign.FolderPath));
    }

    /// <summary>
    /// A project's recorded folder that the person moved is not made again beside it: the campaign is refused, nothing is
    /// written, and the moved folder is as it was.
    /// </summary>
    [Fact]
    public async Task A_campaign_into_a_project_whose_folder_was_moved_is_refused_and_nothing_is_written()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var elsewhere = Path.Combine(Path.GetDirectoryName(RootOf(api))!, "acme-moved");
        Directory.Move(acme.FolderPath!, elsewhere);

        var refused = await api.PostErrorAsync(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_missing", refused.Code);
        Assert.Contains($"move it back to '{acme.FolderPath}', or record where it is now with jason project link.", refused.Message, StringComparison.Ordinal);
        Assert.Empty((await api.PostOkAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, new { project_id = acme.Id }, Ct)).Items);
        Assert.DoesNotContain("campaign_created", await KindsAsync(api));
        Assert.False(Directory.Exists(acme.FolderPath), "the moved project's folder was made again where it had been.");
        Assert.False(Directory.Exists(Path.Combine(elsewhere, "campaigns")), "a campaign's folder was made in the moved project's folder.");
    }

    [Fact]
    public async Task A_create_sent_again_under_its_key_makes_no_second_folder()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var first = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", idempotency_key = "launch-q3" }, Ct);

        var again = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", idempotency_key = "launch-q3" }, Ct);

        Assert.Equal(first.FolderPath, again.FolderPath);
        Assert.Equal(new string?[] { "q3-leads" }, Directory.GetDirectories(Path.GetDirectoryName(first.FolderPath!)!).Select(Path.GetFileName));
    }

    /// <summary>A rename changes the name and nothing else: the folder keeps its leaf; an archived campaign keeps its folder.</summary>
    [Fact]
    public async Task A_rename_and_an_archive_leave_the_folders_where_they_are()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);

        var renamedProject = await api.PostOkAsync<ProjectDto>(Operations.ProjectUpdate, new { project_id = acme.Id, name = "Acme Inc" }, Ct);
        var renamed = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = leads.Id, name = "Q4 leads" }, Ct);
        var archived = await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = leads.Id }, Ct);

        Assert.Equal(acme.FolderPath, renamedProject.FolderPath);
        Assert.Equal(leads.FolderPath, renamed.FolderPath);
        Assert.Equal(leads.FolderPath, archived.FolderPath);
        Assert.True(Directory.Exists(leads.FolderPath), "an archived campaign's folder was removed.");
    }

    /// <summary>
    /// A campaign that changes project takes its folder along, by one rename into the new project's <c>campaigns/</c>,
    /// with everything the person wrote in it; the line in the chronicle is the move's, as it was before folders.
    /// </summary>
    [Fact]
    public async Task A_campaign_moved_to_another_project_takes_its_folder_along()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        File.WriteAllText(Path.Combine(leads.FolderPath!, "AGENTS.md"), "Only clinics with two chairs or more.\n");

        var moved = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, Ct);

        Assert.Equal((beta.Id, Path.Combine(beta.FolderPath!, "campaigns", "q3-leads")), (moved.ProjectId, moved.FolderPath));
        Assert.False(Directory.Exists(leads.FolderPath), "the campaign's old folder is still there.");
        Assert.Equal("Only clinics with two chairs or more.\n", File.ReadAllText(Path.Combine(moved.FolderPath!, "AGENTS.md")));
        Assert.Equal(new HarnessMarker(HarnessMarker.Campaign, leads.Id), HarnessMarker.In(moved.FolderPath!));
        Assert.Equal(1, (await KindsAsync(api)).Count(kind => kind == "campaign_updated"));
    }

    /// <summary>The project a campaign moves to gets its folder first when it has none recorded, as it would at its first campaign.</summary>
    [Fact]
    public async Task A_move_to_a_project_with_no_folder_recorded_makes_that_project_s_folder()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var standing = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        Assert.Null(standing.FolderPath);

        var moved = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = standing.Id }, Ct);

        var made = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        Assert.Equal(Path.Combine(RootOf(api), "projects", "default"), made.FolderPath);
        Assert.Equal(Path.Combine(made.FolderPath!, "campaigns", "q3-leads"), moved.FolderPath);
    }

    /// <summary>A campaign with no folder recorded — one made before folders were — moves in the database alone, and no folder is made for it.</summary>
    [Fact]
    public async Task A_campaign_with_no_folder_moves_in_the_database_alone()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads" }, Ct);
        await WithDbAsync(api, async db =>
        {
            var row = await db.Campaigns.SingleAsync(campaign => campaign.PublicId == leads.Id, Ct);
            row.FolderPath = null;
            await db.SaveChangesAsync(Ct);
        });

        var moved = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = acme.Id }, Ct);

        Assert.Equal((acme.Id, (string?)null), (moved.ProjectId, moved.FolderPath));
        Assert.False(Directory.Exists(Path.Combine(acme.FolderPath!, "campaigns")), "a folder was made for a campaign that had none.");
        Assert.True(Directory.Exists(leads.FolderPath), "the folder no campaign records any more was moved or removed.");
    }

    [Fact]
    public async Task A_move_whose_folder_is_not_where_it_was_recorded_is_refused_with_nothing_changed()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (leads, beta) = await TwoProjectsAsync(api);
        Directory.Move(leads.FolderPath!, Path.Combine(Path.GetDirectoryName(RootOf(api))!, "q3-moved"));
        var disk = Snapshot(RootOf(api));

        var refused = await api.PostErrorAsync(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_missing", refused.Code);
        await AssertUnmovedAsync(api, leads, disk);
    }

    [Fact]
    [Critical(Family.X2, "a move the disk refuses leaves the database and the disk as they were")]
    public async Task A_move_into_a_project_whose_campaigns_are_blocked_by_a_file_is_refused_with_nothing_changed()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (leads, beta) = await TwoProjectsAsync(api, betaHasACampaign: false);
        File.WriteAllText(Path.Combine(beta.FolderPath!, "campaigns"), "in the way");
        var disk = Snapshot(RootOf(api));

        var refused = await api.PostErrorAsync(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_unavailable", refused.Code);
        await AssertUnmovedAsync(api, leads, disk);
    }

    /// <summary>On Windows a folder with a file held open inside it cannot be renamed: the move is refused whole, and the campaign stays where it was on the disk and in the database.</summary>
    [Fact]
    public async Task A_move_of_a_folder_held_open_is_refused_with_nothing_changed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a file held open does not stop a rename of its folder.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (leads, beta) = await TwoProjectsAsync(api);
        var disk = Snapshot(RootOf(api));

        ErrorBody refused;
        using (new FileStream(Path.Combine(leads.FolderPath!, "AGENTS.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await api.PostErrorAsync(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, HttpStatusCode.Conflict, Ct);
        }

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Contains(leads.FolderPath!, refused.Message, StringComparison.Ordinal);
        Assert.Contains("no program holds a file in it open or is working in it, such as an agent session started there", refused.Message, StringComparison.Ordinal);
        await AssertUnmovedAsync(api, leads, disk);
    }

    /// <summary>
    /// A move the rename refuses into a project that has no <c>campaigns/</c> yet leaves the campaign's folder and its row
    /// as they were; the project keeps the <c>campaigns/</c> made for the move, its own structure, and nothing else changes.
    /// </summary>
    [Fact]
    public async Task A_move_refused_by_its_rename_into_a_project_without_campaigns_changes_nothing_of_the_campaign_s()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a file held open does not stop a rename of its folder.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (leads, beta) = await TwoProjectsAsync(api, betaHasACampaign: false);
        var disk = Snapshot(RootOf(api));
        disk[Path.GetRelativePath(RootOf(api), Path.Combine(beta.FolderPath!, "campaigns")) + "/"] = [];

        ErrorBody refused;
        using (new FileStream(Path.Combine(leads.FolderPath!, "AGENTS.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await api.PostErrorAsync(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, HttpStatusCode.Conflict, Ct);
        }

        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Contains("the campaign and its folder stay where they were", refused.Message, StringComparison.Ordinal);
        await AssertUnmovedAsync(api, leads, disk);
    }

    /// <summary>
    /// A move into a project with no folder recorded — the default project of an installation that upgraded — whose
    /// campaign's folder is not where it was recorded is refused before anything is made: that project gets no folder,
    /// and nothing on the disk or in the database changes.
    /// </summary>
    [Fact]
    public async Task A_missing_folder_moved_into_the_default_project_is_refused_before_anything_is_made()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var standing = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        Directory.Move(leads.FolderPath!, Path.Combine(Path.GetDirectoryName(RootOf(api))!, "q3-moved"));
        var disk = Snapshot(RootOf(api));

        var refused = await api.PostErrorAsync(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = standing.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_missing", refused.Code);
        Assert.Contains($"move it back to '{leads.FolderPath}', or relink its project with jason project link, which finds a campaign's folder by its marker.", refused.Message, StringComparison.Ordinal);
        Assert.Null((await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct)).FolderPath);
        await AssertUnmovedAsync(api, leads, disk);
    }

    /// <summary>
    /// The project's first need is met in a step of its own, committed before the campaign's: a move the rename then
    /// refuses leaves the campaign as it was, and the project it was to move to keeps the folder made for it, recorded.
    /// </summary>
    [Fact]
    public async Task A_move_refused_by_its_rename_leaves_the_new_project_s_first_need_standing_and_recorded()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a file held open does not stop a rename of its folder.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var standing = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);

        ErrorBody refused;
        using (new FileStream(Path.Combine(leads.FolderPath!, "AGENTS.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await api.PostErrorAsync(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = standing.Id }, HttpStatusCode.Conflict, Ct);
        }

        var made = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        var after = await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = leads.Id }, Ct);
        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal(Path.Combine(RootOf(api), "projects", "default"), made.FolderPath);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, standing.Id), HarnessMarker.In(made.FolderPath!));
        Assert.True(Directory.Exists(Path.Combine(made.FolderPath!, "campaigns")), "the campaigns/ made for the move is gone.");
        Assert.Equal((acme.Id, leads.FolderPath), (after.ProjectId, after.FolderPath));
        Assert.True(Directory.Exists(leads.FolderPath), "the campaign's folder is not where it was.");
        Assert.DoesNotContain("campaign_updated", await KindsAsync(api));
    }

    /// <summary>
    /// And at a create: the default project's folder, made for the campaign as its first need, stands and is recorded when
    /// the campaign's own folder is then refused and the campaign is not made.
    /// </summary>
    [Fact]
    public async Task A_campaign_refused_after_its_project_s_first_need_leaves_that_folder_standing_and_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaigns = Path.Combine(RootOf(api), "projects", "default", "campaigns");
        Directory.CreateDirectory(campaigns);
        File.WriteAllText(Path.Combine(campaigns, "q3-leads"), "in the way");

        var refused = await api.PostErrorAsync(Operations.CampaignCreate, new { name = "Q3 leads" }, HttpStatusCode.Conflict, Ct);

        var made = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        Assert.Equal("harness_folder_unavailable", refused.Code);
        Assert.Equal(Path.Combine(RootOf(api), "projects", "default"), made.FolderPath);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, made.Id), HarnessMarker.In(made.FolderPath!));
        Assert.Empty((await api.PostOkAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, new { limit = 10 }, Ct)).Items);
        Assert.DoesNotContain("campaign_created", await KindsAsync(api));
    }

    /// <summary>
    /// The runtime writes structure only. A person's words in an <c>AGENTS.md</c> and their own <c>CLAUDE.md</c> are held
    /// open by the test with no sharing while the runtime adopts the folders they are in and makes folders below them, so
    /// any open of them — to read, to write over, to create one where it is — fails the request; the campaign folder it
    /// adopts keeps its <c>AGENTS.md</c> unheld, so a write over one shows in its bytes. Every one of them is byte for
    /// byte as it was afterwards, the moved campaign's included, and no level is ever given a repository's files.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "the runtime never opens a memory file, never writes over one, and writes no repository files")]
    public async Task The_runtime_never_reads_or_writes_a_word_of_a_memory_file()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var root = RootOf(api);
        var waiting = Path.Combine(root, "projects", "acme-corp");
        var leaf = Path.Combine(waiting, "campaigns", "q3-leads");
        Directory.CreateDirectory(leaf);
        var words = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine(root, "AGENTS.md")] = "We sell scheduling software to dental clinics.\n",
            [Path.Combine(root, "CLAUDE.md")] = "@AGENTS.md\nRead the pricing notes too.\n",
            [Path.Combine(waiting, "AGENTS.md")] = "Only clinics with two chairs or more.\n",
        };
        foreach (var (file, text) in words)
        {
            File.WriteAllText(file, text);
        }

        const string Unheld = "Write as Marta, first person.\n";
        File.WriteAllText(Path.Combine(leaf, "AGENTS.md"), Unheld);

        ProjectDto acme;
        using (Hold(words.Keys))
        {
            acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        }

        CampaignDto leads;
        using (Hold(words.Keys))
        {
            leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        }

        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var moved = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = leads.Id }, Ct);

        Assert.Equal((waiting, leaf), (acme.FolderPath, leads.FolderPath));
        Assert.All(words, word => Assert.Equal(word.Value, File.ReadAllText(word.Key)));
        Assert.Equal(Unheld, File.ReadAllText(Path.Combine(moved.FolderPath!, "AGENTS.md")));
        var names = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain(".git", names);
        Assert.DoesNotContain(".gitignore", names);
    }

    /// <summary>Holds every file open with no sharing at all, so nothing else can open one until the holder is disposed.</summary>
    private static Held Hold(IEnumerable<string> files) => new([.. files.Select(file => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))]);

    private sealed class Held(List<FileStream> streams) : IDisposable
    {
        public void Dispose() => streams.ForEach(stream => stream.Dispose());
    }

    /// <summary>The root the test runtime was given: <c>Jason</c> in the home beside its data directory.</summary>
    private static string RootOf(RuntimeApiFixture api) => Path.Combine(TempDataDir.HomeOf(api.Paths.Root), "Jason");

    /// <summary>
    /// A campaign in one project and a second project, which has a campaign of its own unless the test says otherwise, so
    /// its <c>campaigns/</c> is there before the move and a refused move has nothing to make.
    /// </summary>
    private static async Task<(CampaignDto Leads, ProjectDto Beta)> TwoProjectsAsync(RuntimeApiFixture api, bool betaHasACampaign = true)
    {
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        if (betaHasACampaign)
        {
            await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q4 leads", project_id = beta.Id }, Ct);
        }

        return (leads, beta);
    }

    /// <summary>The campaign as it was before a refused move — its project, its folder, no line about a move — and the disk byte for byte.</summary>
    private static async Task AssertUnmovedAsync(RuntimeApiFixture api, CampaignDto before, SortedDictionary<string, byte[]> disk)
    {
        var after = await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = before.Id }, Ct);
        Assert.Equal((before.ProjectId, before.FolderPath), (after.ProjectId, after.FolderPath));
        Assert.DoesNotContain("campaign_updated", await KindsAsync(api));
        Assert.Equal(disk, Snapshot(RootOf(api)));
    }

    private static async Task<List<string>> KindsAsync(RuntimeApiFixture api)
    {
        var kinds = new List<string>();
        await WithDbAsync(api, async db => kinds.AddRange(await db.Journal.AsNoTracking().OrderBy(entry => entry.Id).Select(entry => entry.Kind).ToListAsync(Ct)));
        return kinds;
    }

    private static async Task WithDbAsync(RuntimeApiFixture api, Func<JasonDbContext, Task> act)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        await act(scope.ServiceProvider.GetRequiredService<JasonDbContext>());
    }

    /// <summary>Every file and directory below <paramref name="root"/>, each file with its bytes.</summary>
    private static SortedDictionary<string, byte[]> Snapshot(string root)
    {
        var snapshot = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(root, directory) + "/"] = [];
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(root, file)] = File.ReadAllBytes(file);
        }

        return snapshot;
    }
}
