using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Runtime.Harness;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// <c>project.link</c>: a folder the person moved is recorded again by its marker, a folder of their own becomes a
/// project's, and a project with none gets its folder by the policy; below the project's folder, each
/// <c>campaigns/*/</c> marker re-records its campaign's path, two fixed levels and never a search. The database decides a
/// campaign's project, never a folder, and every refusal leaves the database and the disk as they were.
/// </summary>
public sealed class ProjectLinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The person moved a project's folder, its campaigns' folders inside it: the link reads the marker at the new path,
    /// records that path for the project, re-records each campaign found below it by its marker, and says it in one line.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a moved project's folder and its campaigns' are recorded again by their markers")]
    public async Task A_moved_project_folder_is_relinked_by_its_marker_with_its_campaigns()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var moved = Path.Combine(Outside(api), "acme-moved");
        Directory.Move(acme.FolderPath!, moved);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { path = moved, reason = "moved to the shared drive" }, Ct);

        var leadsNow = Path.Combine(moved, "campaigns", "q3-leads");
        Assert.Equal((acme.Id, moved, LinkFolderOutcome.Relinked), (linked.Project.Id, linked.Project.FolderPath, linked.Folder));
        Assert.Equal([new LinkedCampaign(leads.Id, leadsNow, LinkCampaignOutcome.Relinked)], linked.Campaigns);
        Assert.Empty(linked.Reported);
        Assert.Equal(moved, (await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { project_id = acme.Id }, Ct)).FolderPath);
        Assert.Equal(leadsNow, (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = leads.Id }, Ct)).FolderPath);

        var line = await LastLineAsync(api);
        Assert.Equal((JournalKinds.ProjectLinked, acme.Id, (int?)null), (line.Kind, line.Key, line.CampaignId));
        Assert.True(
            JsonNode.DeepEquals(new JsonObject { ["folder_path"] = acme.FolderPath, ["campaigns"] = new JsonObject { [leads.Id] = leads.FolderPath } }, line.Old),
            $"the line's old value was {line.Old?.ToJsonString()}");
        Assert.True(
            JsonNode.DeepEquals(new JsonObject { ["folder_path"] = moved, ["campaigns"] = new JsonObject { [leads.Id] = leadsNow } }, line.New),
            $"the line's new value was {line.New?.ToJsonString()}");
        Assert.Equal("moved to the shared drive", line.Reason);
    }

    /// <summary>
    /// A path given behind a Windows device prefix names the folder behind it, and is recorded as the platform writes it,
    /// without the prefix: a campaign made there is too, and its move to another project is one rename on one volume.
    /// </summary>
    [Fact]
    public async Task A_path_behind_a_device_prefix_is_recorded_without_it_and_a_later_move_goes_ahead()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Device prefixes are Windows' own; elsewhere such a path is a relative one, refused as one.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var moved = Path.Combine(Outside(api), "acme-moved");
        Directory.Move(acme.FolderPath!, moved);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { path = @"\\?\" + moved }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var after = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = leads.Id, project_id = beta.Id }, Ct);

        Assert.Equal((moved, LinkFolderOutcome.Relinked), (linked.Project.FolderPath, linked.Folder));
        Assert.Equal(Path.Combine(moved, "campaigns", "q3-leads"), leads.FolderPath);
        Assert.Equal(Path.Combine(beta.FolderPath!, "campaigns", "q3-leads"), after.FolderPath);
    }

    /// <summary>
    /// A folder of the person's own, with no marker, becomes the project's: its marker is written, and a <c>CLAUDE.md</c>
    /// and an <c>AGENTS.md</c> only where there are none. The person's own files are held open with no sharing while the
    /// link runs, so any open of them fails the request, and each is byte for byte as it was.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a folder of the person's own is adopted without a word of their files read or written")]
    public async Task A_folder_of_the_person_s_own_is_adopted_and_none_of_its_files_is_opened()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-gone"));
        var own = Path.Combine(Outside(api), "work", "acme");
        Directory.CreateDirectory(own);
        var agents = Path.Combine(own, "AGENTS.md");
        File.WriteAllText(agents, "We sell to dental clinics with two chairs or more.\n");

        ProjectLinkResult linked;
        using (new FileStream(agents, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id, path = own }, Ct);
        }

        Assert.Equal((own, LinkFolderOutcome.Adopted), (linked.Project.FolderPath, linked.Folder));
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, acme.Id), HarnessMarker.In(own));
        Assert.Equal("We sell to dental clinics with two chairs or more.\n", File.ReadAllText(agents));
        Assert.Equal("@AGENTS.md", File.ReadAllText(Path.Combine(own, "CLAUDE.md")));
    }

    /// <summary>
    /// The upgrade path: a project with no folder recorded — the default, with campaigns made before folders were — gets
    /// its folder by the policy under the root, and so does each campaign of it that has none, except an archived one.
    /// </summary>
    [Fact]
    public async Task A_project_with_no_folder_gets_one_and_so_do_its_campaigns_but_an_archived_one()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads" }, Ct);
        var old = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring push" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = old.Id }, Ct);
        var standing = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { slug = "default" }, Ct);
        await ForgetFoldersAsync(api);
        Directory.Delete(standing.FolderPath!, recursive: true);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = standing.Id }, Ct);

        var folder = Path.Combine(RootOf(api), "projects", "default");
        Assert.Equal((folder, LinkFolderOutcome.Made), (linked.Project.FolderPath, linked.Folder));
        Assert.Equal(
            new[]
            {
                new LinkedCampaign(leads.Id, Path.Combine(folder, "campaigns", "q3-leads"), LinkCampaignOutcome.Made),
                new LinkedCampaign(old.Id, null, LinkCampaignOutcome.Archived),
            }.OrderBy(campaign => campaign.CampaignId, StringComparer.Ordinal),
            linked.Campaigns);
        Assert.Equal(new HarnessMarker(HarnessMarker.Campaign, leads.Id), HarnessMarker.In(Path.Combine(folder, "campaigns", "q3-leads")));
        Assert.False(Directory.Exists(Path.Combine(folder, "campaigns", "spring-push")), "a folder was made for an archived campaign.");
    }

    /// <summary>A project whose folder holds already makes the folders its campaigns lack, the case of an installation that upgraded and has made a campaign since.</summary>
    [Fact]
    public async Task A_project_with_its_folder_in_place_makes_only_what_its_campaigns_lack()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var older = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q2 leads", project_id = acme.Id }, Ct);
        await WithDbAsync(api, async db =>
        {
            (await db.Campaigns.SingleAsync(campaign => campaign.PublicId == older.Id, Ct)).FolderPath = null;
            await db.SaveChangesAsync(Ct);
        });
        Directory.Delete(older.FolderPath!, recursive: true);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.Equal(LinkFolderOutcome.Kept, linked.Folder);
        Assert.Equal(
            [new LinkedCampaign(leads.Id, leads.FolderPath, LinkCampaignOutcome.Kept), new LinkedCampaign(older.Id, older.FolderPath, LinkCampaignOutcome.Made)],
            linked.Campaigns.OrderBy(campaign => campaign.CampaignId == leads.Id ? 0 : 1));

        // The project's own path did not change, and the campaign's is saved all the same, said in one line.
        Assert.Equal(older.FolderPath, (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = older.Id }, Ct)).FolderPath);
        Assert.Single(await KindsAsync(api), kind => kind == JournalKinds.ProjectLinked);
    }

    [Fact]
    public async Task A_link_that_finds_everything_where_it_is_recorded_writes_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var lines = (await KindsAsync(api)).Count;

        var byPath = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { path = acme.FolderPath }, Ct);
        var byId = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.All([byPath, byId], linked =>
        {
            Assert.Equal(LinkFolderOutcome.Kept, linked.Folder);
            Assert.Equal([new LinkedCampaign(leads.Id, leads.FolderPath, LinkCampaignOutcome.Kept)], linked.Campaigns);
        });
        Assert.Equal(lines, (await KindsAsync(api)).Count);
    }

    /// <summary>
    /// Two fixed levels: the project's folder, then each folder directly in its <c>campaigns/</c>. A campaign's marker one
    /// level deeper is never seen, so that campaign keeps the path it had and is said as not found; the disk is never searched.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "the link reads two fixed levels and never searches the disk")]
    public async Task A_campaign_marker_deeper_than_campaigns_star_is_never_seen()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var deeper = Path.Combine(acme.FolderPath!, "campaigns", "2026", "q3-leads");
        Directory.CreateDirectory(Path.GetDirectoryName(deeper)!);
        Directory.Move(leads.FolderPath!, deeper);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.Equal([new LinkedCampaign(leads.Id, leads.FolderPath, LinkCampaignOutcome.NotFound)], linked.Campaigns);
        Assert.Empty(linked.Reported);
        Assert.Equal(leads.FolderPath, (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = leads.Id }, Ct)).FolderPath);
        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
    }

    /// <summary>
    /// A campaign folder found under the wrong project — its marker names a campaign the database puts in another project
    /// — is reported and never acted on: that campaign keeps its project and its recorded path.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "the database decides a campaign's project, never the folder it is found in")]
    public async Task A_campaign_folder_under_the_wrong_project_is_reported_and_never_acted_on()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var theirs = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Beta launch", project_id = beta.Id }, Ct);
        var stray = Path.Combine(acme.FolderPath!, "campaigns", "beta-launch");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        Directory.Move(theirs.FolderPath!, stray);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.Empty(linked.Campaigns);
        Assert.Equal([new LinkReport(stray, LinkReportReason.AnotherProject, theirs.Id, beta.Id)], linked.Reported);
        var after = await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = theirs.Id }, Ct);
        Assert.Equal((beta.Id, theirs.FolderPath), (after.ProjectId, after.FolderPath));
        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
    }

    /// <summary>
    /// What the scan finds and does not act on is reported, never acted on: a marker naming a campaign nobody holds, one of
    /// another level, one that cannot be read, and a second folder carrying one campaign's marker; a folder with no marker
    /// is the person's and is not mentioned.
    /// </summary>
    [Fact]
    public async Task Markers_below_campaigns_that_name_nothing_of_this_project_s_are_reported()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var campaigns = Path.Combine(acme.FolderPath!, "campaigns");
        var ghost = Mark(Path.Combine(campaigns, "ghost"), """{"marker_version":1,"kind":"campaign","id":"cmp_01K7QN000000000000000GHOST"}""");
        var level = Mark(Path.Combine(campaigns, "a-project"), $$"""{"marker_version":1,"kind":"project","id":"{{acme.Id}}"}""");
        var broken = Mark(Path.Combine(campaigns, "broken"), "{ cut short");
        var copy = Path.Combine(campaigns, "q3-leads-copy");
        CopyFolder(leads.FolderPath!, copy);
        Directory.CreateDirectory(Path.Combine(campaigns, "notes"));

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.Equal([new LinkedCampaign(leads.Id, leads.FolderPath, LinkCampaignOutcome.Kept)], linked.Campaigns);
        Assert.Equal(
            new[]
            {
                new LinkReport(level, LinkReportReason.Unknown),
                new LinkReport(broken, LinkReportReason.Unknown),
                new LinkReport(ghost, LinkReportReason.Unknown, "cmp_01K7QN000000000000000GHOST"),
                new LinkReport(copy, LinkReportReason.Duplicate, leads.Id, null),
            }.OrderBy(report => report.FolderPath, StringComparer.Ordinal),
            linked.Reported);
    }

    /// <summary>Two folders carry one campaign's marker and neither is the one recorded: which is the campaign's is the person's to say, so the recorded path stays.</summary>
    [Fact]
    public async Task Two_folders_of_one_campaign_neither_recorded_leave_its_path_as_it_was()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var campaigns = Path.Combine(acme.FolderPath!, "campaigns");
        CopyFolder(leads.FolderPath!, Path.Combine(campaigns, "q3-a"));
        Directory.Move(leads.FolderPath!, Path.Combine(campaigns, "q3-b"));

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.Equal([new LinkedCampaign(leads.Id, leads.FolderPath, LinkCampaignOutcome.Duplicate)], linked.Campaigns);
        Assert.Equal(
            [Path.Combine(campaigns, "q3-a"), Path.Combine(campaigns, "q3-b")],
            linked.Reported.Select(report => (report.Reason, report.FolderPath)).Where(report => report.Reason == LinkReportReason.Duplicate).Select(report => report.FolderPath));
        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
    }

    /// <summary>An archived campaign whose folder is found below the project's is relinked like any other; none is ever made for one.</summary>
    [Fact]
    public async Task An_archived_campaign_found_by_its_marker_is_relinked()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = leads.Id }, Ct);
        var renamed = Path.Combine(acme.FolderPath!, "campaigns", "q3-leads-done");
        Directory.Move(leads.FolderPath!, renamed);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id }, Ct);

        Assert.Equal([new LinkedCampaign(leads.Id, renamed, LinkCampaignOutcome.Relinked)], linked.Campaigns);
        Assert.Equal(renamed, (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = leads.Id }, Ct)).FolderPath);
        Assert.Single(await KindsAsync(api), kind => kind == JournalKinds.ProjectLinked);
    }

    /// <summary>
    /// The project's folder is still where it is recorded, with its marker, and another path is given — a copy, most often:
    /// a link records where a folder went and never moves or copies one, so it is refused, naming both, with nothing written.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a link never takes a second folder for a project whose own is still there")]
    public async Task Another_path_for_a_project_whose_folder_is_still_there_is_refused_with_nothing_written()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        var copy = Path.Combine(Outside(api), "acme-copy");
        CopyFolder(acme.FolderPath!, copy);
        var other = Path.Combine(Outside(api), "unmarked");
        Directory.CreateDirectory(other);
        var disk = Snapshot(Outside(api));

        var byMarker = await api.PostErrorAsync(Operations.ProjectLink, new { path = copy }, HttpStatusCode.Conflict, Ct);
        var byId = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = acme.Id, path = other }, HttpStatusCode.Conflict, Ct);

        Assert.All([byMarker, byId], refused =>
        {
            Assert.Equal(("harness_folder_present", false), (refused.Code, refused.Retryable));
            Assert.Contains(acme.FolderPath!, refused.Message, StringComparison.Ordinal);
        });
        Assert.Contains(copy, byMarker.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(api, acme, disk);
    }

    /// <summary>
    /// A recorded folder that is there but no longer carries the project's marker is missing, by the folders' own rule: a
    /// link to another folder goes ahead, and the old folder stays the person's, byte for byte, with no marker written in it.
    /// </summary>
    [Fact]
    public async Task A_recorded_folder_without_its_marker_is_missing_and_a_link_elsewhere_goes_ahead()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        File.Delete(HarnessMarker.PathIn(acme.FolderPath!));
        File.WriteAllText(Path.Combine(acme.FolderPath!, "AGENTS.md"), "Kept by the person as it is.\n");
        var old = Snapshot(acme.FolderPath!);
        var own = Path.Combine(Outside(api), "work", "acme");
        Directory.CreateDirectory(own);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id, path = own }, Ct);

        Assert.Equal((own, LinkFolderOutcome.Adopted), (linked.Project.FolderPath, linked.Folder));
        Assert.Null(HarnessMarker.In(acme.FolderPath!));
        Assert.Equal(old, Snapshot(acme.FolderPath!));
    }

    /// <summary>
    /// A marker naming another project, another level, an id this runtime does not hold, or nothing it can read is refused
    /// as taken, and nothing is written: not in the folder, not in the database, not in the chronicle.
    /// </summary>
    [Theory]
    [InlineData("another-project")]
    [InlineData("a-campaign")]
    [InlineData("the-root")]
    [InlineData("unknown-id")]
    [InlineData("unreadable")]
    [Critical(Family.X2, "a folder marked for anything else is refused with nothing written")]
    public async Task A_path_marked_for_anything_else_is_refused_and_nothing_is_written(string marked)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = beta.Id }, Ct);
        Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-gone"));
        var folder = Mark(Path.Combine(Outside(api), "marked"), marked switch
        {
            "another-project" => $$"""{"marker_version":1,"kind":"project","id":"{{beta.Id}}"}""",
            "a-campaign" => $$"""{"marker_version":1,"kind":"campaign","id":"{{leads.Id}}"}""",
            "the-root" => """{"marker_version":1,"kind":"root"}""",
            "unknown-id" => """{"marker_version":1,"kind":"project","id":"prj_01K7QN000000000000000GHOST"}""",
            _ => """{"marker_version":1,"kind":"proj""",
        });
        var disk = Snapshot(Outside(api));

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = acme.Id, path = folder }, HttpStatusCode.Conflict, Ct);

        Assert.True(refused.Code == "harness_folder_taken", $"a folder marked '{marked}' was answered {refused.Code}: {refused.Message}");
        Assert.Contains(folder, refused.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(api, acme, disk);
    }

    [Fact]
    public async Task A_path_whose_marker_names_an_unknown_project_is_refused_without_an_id_too()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var folder = Mark(Path.Combine(Outside(api), "marked"), """{"marker_version":1,"kind":"project","id":"prj_01K7QN000000000000000GHOST"}""");

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { path = folder }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Equal([".jason"], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
    }

    /// <summary>A folder another project records is that project's, marked or not: it never becomes a second project's.</summary>
    [Fact]
    public async Task A_folder_another_project_records_is_refused_even_without_its_marker()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-gone"));
        File.Delete(HarnessMarker.PathIn(beta.FolderPath!));

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = acme.Id, path = beta.FolderPath }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("harness_folder_taken", refused.Code);
        Assert.Null(HarnessMarker.In(beta.FolderPath!));
    }

    /// <summary>
    /// A project alone whose recorded folder is gone, or is there without the project's marker, is refused as missing: the
    /// folder is not made again, and one that is there is neither kept nor marked, by the folders' own rule.
    /// </summary>
    [Theory]
    [InlineData("moved")]
    [InlineData("unmarked")]
    public async Task A_project_alone_whose_recorded_folder_is_gone_is_refused_saying_to_name_where_it_went(string gone)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        if (gone == "moved")
        {
            Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-moved"));
        }
        else
        {
            File.Delete(HarnessMarker.PathIn(acme.FolderPath!));
        }

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = acme.Id }, HttpStatusCode.Conflict, Ct);

        Assert.True(refused.Code == "harness_folder_missing", $"a recorded folder {gone} was answered {refused.Code}: {refused.Message}");
        Assert.Contains(acme.FolderPath!, refused.Message, StringComparison.Ordinal);
        Assert.Contains("--path", refused.Message, StringComparison.Ordinal);
        if (gone == "moved")
        {
            Assert.False(Directory.Exists(acme.FolderPath), "the moved folder was made again where it had been.");
        }
        else
        {
            Assert.Null(HarnessMarker.In(acme.FolderPath!));
        }

        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
    }

    /// <summary>What a link is asked is checked before anything is read from the disk: something to link, an absolute path, a folder that is there, and never a place the root itself could not be.</summary>
    [Theory]
    [InlineData("nothing", "project_id")]
    [InlineData("relative", "path")]
    [InlineData("absent", "path")]
    [InlineData("home", "path")]
    [InlineData("top", "path")]
    [InlineData("holds-home", "path")]
    [InlineData("data", "path")]
    [InlineData("inside-data", "path")]
    public async Task A_link_with_nothing_to_link_or_a_path_it_may_not_use_is_refused_by_the_field(string asked, string field)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        Directory.CreateDirectory(Path.Combine(api.Paths.Root, "plugins"));
        var home = TempDataDir.HomeOf(api.Paths.Root);
        object body = asked switch
        {
            "nothing" => new { },
            "relative" => new { project_id = acme.Id, path = Path.Combine("work", "acme") },
            "absent" => new { project_id = acme.Id, path = Path.Combine(Outside(api), "not-there") },
            "home" => new { project_id = acme.Id, path = home },
            "top" => new { project_id = acme.Id, path = Path.GetPathRoot(home)! },

            // The test's home and its data directory are side by side, so the folder holding the one holds the other too.
            "holds-home" => new { project_id = acme.Id, path = Path.GetDirectoryName(home)! },
            "data" => new { project_id = acme.Id, path = api.Paths.Root },
            _ => new { project_id = acme.Id, path = Path.Combine(api.Paths.Root, "plugins") },
        };

        var refused = await api.PostErrorAsync(Operations.ProjectLink, body, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", refused.Code);
        Assert.True(refused.Details?.Any(detail => detail.Field == field) == true, $"'{asked}' was refused without a detail on {field}: {refused.Message}");
        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
    }

    /// <summary>
    /// The root is the client's level, a folder holding it would put every project's folder inside one project's, and the
    /// root's <c>projects/</c> holds every project's folder and is no level's: each is refused by the field, as the home and
    /// the data directory are, with nothing written. The root here is a folder below the home, so that a folder can hold it
    /// and not the home.
    /// </summary>
    [Theory]
    [InlineData("root")]
    [InlineData("holds-root")]
    [InlineData("projects")]
    public async Task The_root_a_folder_holding_it_and_its_projects_directory_are_refused_by_the_field(string place)
    {
        string? root = null;
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            root = Path.Combine(TempDataDir.HomeOf(paths.Root), "clients", "Jason");
            File.WriteAllText(paths.UserSettingsFile, new JsonObject
            {
                ["Dispatcher"] = new JsonObject { ["Enabled"] = false },
                ["Harness"] = new JsonObject { ["Root"] = root },
            }.ToJsonString());
        });
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-gone"));
        var path = place switch
        {
            "root" => root!,
            "holds-root" => Path.GetDirectoryName(root)!,
            _ => Path.Combine(root!, "projects"),
        };
        var disk = Snapshot(Outside(api));

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = acme.Id, path }, HttpStatusCode.BadRequest, Ct);

        Assert.True(refused.Code == "validation_failed", $"the {place} was answered {refused.Code}: {refused.Message}");
        Assert.Contains(refused.Details ?? [], detail => detail is { Field: "path", Code: "not_allowed" });
        Assert.Equal(disk, Snapshot(Outside(api)));
        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
    }

    /// <summary>
    /// A link never makes a project's folder of one inside another level's or around one: a session started in either
    /// would read the other's memory files, another client's work included. A path at, inside or holding a folder the
    /// runtime records for another project or for any campaign — this project's own included, since a project's folder is
    /// never inside its own campaign's — and a path below a folder carrying a project's or a campaign's marker, which a
    /// walk up from it reads, are refused as taken, naming the other folder, with nothing written.
    /// </summary>
    [Theory]
    [InlineData("inside-a-project")]
    [InlineData("inside-a-campaign")]
    [InlineData("holding-a-project")]
    [InlineData("inside-its-own-campaign")]
    [InlineData("below-a-project-marker")]
    [InlineData("below-a-campaign-marker")]
    public async Task A_path_inside_or_holding_another_level_s_folder_is_taken_naming_that_folder(string where)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var spring = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring push", project_id = acme.Id }, Ct);
        var beta = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Beta" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = beta.Id }, Ct);
        if (where == "inside-its-own-campaign")
        {
            // Acme's folder stays where it is recorded, without its marker, so the link is not refused as a second folder.
            File.Delete(HarnessMarker.PathIn(acme.FolderPath!));
        }
        else
        {
            Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-gone"));
        }

        var work = Path.Combine(Outside(api), "work");
        if (where == "holding-a-project")
        {
            Directory.CreateDirectory(work);
            Directory.Move(beta.FolderPath!, Path.Combine(work, "beta"));
            await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { path = Path.Combine(work, "beta") }, Ct);
        }

        var (path, other) = where switch
        {
            "inside-a-project" => (Path.Combine(beta.FolderPath!, "notes"), beta.FolderPath!),
            "inside-a-campaign" => (Path.Combine(leads.FolderPath!, "notes"), leads.FolderPath!),
            "holding-a-project" => (work, Path.Combine(work, "beta")),
            "inside-its-own-campaign" => (Path.Combine(spring.FolderPath!, "notes"), spring.FolderPath!),
            "below-a-project-marker" => (Path.Combine(Outside(api), "beta-copy", "notes"), Path.Combine(Outside(api), "beta-copy")),
            _ => (Path.Combine(Outside(api), "leads-copy", "notes"), Path.Combine(Outside(api), "leads-copy")),
        };
        if (where.StartsWith("below-", StringComparison.Ordinal))
        {
            // A copy carries the marker of what it was copied from, and the runtime records no copy.
            CopyFolder(where == "below-a-project-marker" ? beta.FolderPath! : leads.FolderPath!, other);
        }

        Directory.CreateDirectory(path);
        var before = await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { project_id = acme.Id }, Ct);
        var lines = (await KindsAsync(api)).Count;
        var disk = Snapshot(Outside(api));

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = acme.Id, path }, HttpStatusCode.Conflict, Ct);

        Assert.True(refused.Code == "harness_folder_taken", $"a path {where} was answered {refused.Code}: {refused.Message}");
        Assert.Contains($"'{other}'", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before.FolderPath, (await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { project_id = acme.Id }, Ct)).FolderPath);
        Assert.Equal(lines, (await KindsAsync(api)).Count);
        Assert.Equal(disk, Snapshot(Outside(api)));
    }

    /// <summary>
    /// A folder of the person's own named <c>campaigns</c>, anywhere but in a project's folder, is a folder like any other:
    /// no rule goes by a name, and the one in a project's folder is refused because the folder holding it is a project's.
    /// </summary>
    [Fact]
    public async Task A_folder_of_the_person_s_own_named_campaigns_elsewhere_is_adopted_as_any_folder_is()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        Directory.Move(acme.FolderPath!, Path.Combine(Outside(api), "acme-gone"));
        var own = Path.Combine(Outside(api), "work", "campaigns");
        Directory.CreateDirectory(own);

        var linked = await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { project_id = acme.Id, path = own }, Ct);

        Assert.Equal((own, LinkFolderOutcome.Adopted), (linked.Project.FolderPath, linked.Folder));
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, acme.Id), HarnessMarker.In(own));
    }

    /// <summary>
    /// Each place no level may be is refused in its own words, compared where it leads: the home, the top of a drive, a
    /// folder holding the home, the data directory, a folder inside it and one holding it, here with the data directory
    /// apart from the home so that a folder can hold it alone; a folder beside them is not refused. Nothing is made.
    /// </summary>
    [Theory]
    [InlineData("home", "path must not be the home directory:")]
    [InlineData("top", "path must not be the top of a drive, a share or the file system:")]
    [InlineData("holds-home", "path must not hold the home directory:")]
    [InlineData("data", "path must not be the data directory,")]
    [InlineData("inside-data", "path must not be inside the data directory,")]
    [InlineData("holds-data", "path must not hold the data directory,")]
    [InlineData("apart", null)]
    public void Each_place_no_level_may_be_is_refused_in_its_own_words(string place, string? refusal)
    {
        var tree = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(tree, "home");
        var data = new JasonPaths(Path.Combine(tree, "data", ".jason"));
        var folder = place switch
        {
            "home" => home,
            "top" => Path.GetPathRoot(home)!,
            "holds-home" => tree,
            "data" => data.Root,
            "inside-data" => Path.Combine(data.Root, "plugins"),
            "holds-data" => Path.Combine(tree, "data"),
            _ => Path.Combine(tree, "work", "acme"),
        };

        var said = HarnessRoot.FolderRefusal(folder, data, home);

        if (refusal is null)
        {
            Assert.True(said is null, $"a folder apart from the home and the data directory was refused: {said}");
        }
        else
        {
            Assert.True(said?.StartsWith(refusal, StringComparison.Ordinal) == true, $"'{place}' was said as: {said ?? "<accepted>"}");
        }

        Assert.False(Directory.Exists(tree), "a place was made to be compared.");
    }

    [Fact]
    public async Task A_project_nobody_made_is_not_found()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var refused = await api.PostErrorAsync(Operations.ProjectLink, new { project_id = "prj_01K7QN000000000000000GHOST" }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("project_not_found", refused.Code);
    }

    /// <summary>
    /// The chronicle's line names the project and the paths it recorded, and nothing else: no memory file is ever opened,
    /// so none of its words can reach it, and no name is in it but the folders' own.
    /// </summary>
    [Fact]
    public async Task The_line_names_paths_and_ids_and_never_a_word_of_a_memory_file()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        File.WriteAllText(Path.Combine(acme.FolderPath!, "AGENTS.md"), "Never mention pricing before the second call.\n");
        File.WriteAllText(Path.Combine(leads.FolderPath!, "AGENTS.md"), "Write as Marta, first person.\n");
        var moved = Path.Combine(Outside(api), "acme-moved");
        Directory.Move(acme.FolderPath!, moved);

        await api.PostOkAsync<ProjectLinkResult>(Operations.ProjectLink, new { path = moved }, Ct);

        var line = await LastLineAsync(api);
        var text = line.Old!.ToJsonString() + line.New!.ToJsonString();
        Assert.Equal(["campaigns", "folder_path"], line.New!.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("pricing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Marta", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme Corp", text, StringComparison.Ordinal);
    }

    /// <summary>The root the test runtime was given: <c>Jason</c> in the home beside its data directory.</summary>
    private static string RootOf(RuntimeApiFixture api) => Path.Combine(TempDataDir.HomeOf(api.Paths.Root), "Jason");

    /// <summary>A folder in the test's home and outside the root, where a person keeps folders of their own.</summary>
    private static string Outside(RuntimeApiFixture api) => TempDataDir.HomeOf(api.Paths.Root);

    /// <summary>Makes <paramref name="folder"/> with a marker holding exactly <paramref name="marker"/>, and answers it.</summary>
    private static string Mark(string folder, string marker)
    {
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        File.WriteAllText(HarnessMarker.PathIn(folder), marker);
        return folder;
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories).Prepend(from))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    /// <summary>Every campaign and project of the test as one made before folders were: no path recorded.</summary>
    private static Task ForgetFoldersAsync(RuntimeApiFixture api) => WithDbAsync(api, async db =>
    {
        await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.FolderPath, (string?)null), Ct);
        await db.Projects.ExecuteUpdateAsync(set => set.SetProperty(project => project.FolderPath, (string?)null), Ct);
    });

    /// <summary>The project as it was before a refused link — its folder and no line about a link — and the disk byte for byte.</summary>
    private static async Task AssertUnchangedAsync(RuntimeApiFixture api, ProjectDto before, SortedDictionary<string, byte[]> disk)
    {
        Assert.Equal(before.FolderPath, (await api.PostOkAsync<ProjectDto>(Operations.ProjectGet, new { project_id = before.Id }, Ct)).FolderPath);
        Assert.DoesNotContain(JournalKinds.ProjectLinked, await KindsAsync(api));
        Assert.Equal(disk, Snapshot(Outside(api)));
    }

    private static async Task<JournalEntry> LastLineAsync(RuntimeApiFixture api)
    {
        JournalEntry? line = null;
        await WithDbAsync(api, async db => line = await db.Journal.AsNoTracking().OrderBy(entry => entry.Id).LastAsync(Ct));
        return line!;
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
