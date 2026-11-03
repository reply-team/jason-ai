using Jason.Contracts.Harness;
using Jason.Contracts.Hosts;
using Jason.Runtime.Harness;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// The sweep at the runtime's start: a folder recorded before a host's import file existed gains that file, over nothing,
/// and a folder that is not there is skipped without failing the start.
/// </summary>
public sealed class ImportFilesSweepTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly TempTree _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Home => Path.Combine(_tree.Root, "home");

    public void Dispose()
    {
        _database.Dispose();
        _tree.Dispose();
    }

    [Fact]
    public async Task A_recorded_folder_that_lacks_an_import_file_gains_it_and_nothing_else()
    {
        var folders = TestHarness.Folders(Home);
        var projectFolder = MarkedFolder("acme", HarnessMarker.Project, "prj_one");
        var campaignFolder = MarkedFolder("acme-launch", HarnessMarker.Campaign, "cmp_one");
        Mark(folders.Root, new HarnessMarker(HarnessMarker.Root, null));
        Record(projectFolder: projectFolder, campaignFolder: campaignFolder);

        await StartAsync(folders, new RecordingLogs());

        foreach (var folder in new[] { folders.Root, projectFolder, campaignFolder })
        {
            foreach (var file in AgentHosts.ImportFiles)
            {
                Assert.Equal(file.ImportLine, await File.ReadAllTextAsync(Path.Combine(folder, file.FileName), Ct));
            }

            // Never a word of the person's memory file: the sweep does not make it, and does not open it.
            Assert.False(File.Exists(Path.Combine(folder, HarnessFolders.MemoryFile)));
        }
    }

    [Fact]
    public async Task A_file_that_is_there_is_kept_byte_for_byte()
    {
        var folders = TestHarness.Folders(Home);
        var projectFolder = MarkedFolder("acme", HarnessMarker.Project, "prj_one");
        var file = AgentHosts.ImportFiles[0].FileName;
        byte[] theirs = [0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A, 0x00, 0xFF];
        await File.WriteAllBytesAsync(Path.Combine(projectFolder, file), theirs, Ct);
        var memory = Path.Combine(projectFolder, HarnessFolders.MemoryFile);
        await File.WriteAllTextAsync(memory, "my own words", Ct);
        Record(projectFolder: projectFolder);

        await StartAsync(folders, new RecordingLogs());

        Assert.Equal(theirs, await File.ReadAllBytesAsync(Path.Combine(projectFolder, file), Ct));
        Assert.Equal("my own words", await File.ReadAllTextAsync(memory, Ct));
    }

    [Fact]
    public async Task A_folder_that_is_not_there_or_lost_its_marker_is_skipped_by_id_and_the_start_succeeds()
    {
        var folders = TestHarness.Folders(Home);
        var absent = Path.Combine(_tree.Root, "gone-" + "secret-name");
        var unmarked = _tree.NewDirectory("unmarked-secret-name");
        var kept = MarkedFolder("acme-launch", HarnessMarker.Campaign, "cmp_two");
        Record(projectFolder: absent, campaignFolder: unmarked, otherCampaignFolder: kept);
        var logs = new RecordingLogs();

        await StartAsync(folders, logs);

        Assert.False(Directory.Exists(absent));
        Assert.Empty(Directory.GetFiles(unmarked));
        Assert.True(File.Exists(Path.Combine(kept, AgentHosts.ImportFiles[0].FileName)));
        Assert.Contains(logs.All, line => line.Contains("prj_one", StringComparison.Ordinal));
        Assert.Contains(logs.All, line => line.Contains("cmp_one", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => Says(line, "secret-name") || Says(line, _tree.Root));
    }

    /// <summary>A recorded folder that now carries another entity's marker is not this one's: it gains nothing.</summary>
    [Fact]
    public async Task A_campaigns_folder_that_carries_another_entitys_marker_gains_nothing()
    {
        var folders = TestHarness.Folders(Home);
        var theirs = MarkedFolder("elsewhere", HarnessMarker.Campaign, "cmp_other");
        Record(projectFolder: null, campaignFolder: theirs);

        await StartAsync(folders, new RecordingLogs());

        Assert.Empty(Directory.GetFiles(theirs));
    }

    private static bool Says(RecordingLogs.Line line, string text) =>
        line.Message.Contains(text, StringComparison.Ordinal)
        || line.Scope.Values.Any(value => value?.ToString()?.Contains(text, StringComparison.Ordinal) == true)
        || line.Exception is not null;

    private async Task StartAsync(HarnessFolders folders, RecordingLogs logs)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.Open());
        await using var provider = services.BuildServiceProvider();
        var sweep = new ImportFilesSweep(provider.GetRequiredService<IServiceScopeFactory>(), folders, new Logger<ImportFilesSweep>(new LoggerFactory([logs])));
        await sweep.StartAsync(Ct);
    }

    /// <summary>One project row for <c>prj_one</c> and campaign rows for <c>cmp_one</c> and <c>cmp_two</c>, with the folders given.</summary>
    private void Record(string? projectFolder, string? campaignFolder = null, string? otherCampaignFolder = null)
    {
        using var db = _database.Open();
        var project = new Project { PublicId = "prj_one", Name = "One", Slug = "one", FolderPath = projectFolder };
        db.Projects.Add(project);
        db.SaveChanges();
        db.Campaigns.Add(new Campaign { PublicId = "cmp_one", Name = "One", ProjectId = project.Id, FolderPath = campaignFolder });
        db.Campaigns.Add(new Campaign { PublicId = "cmp_two", Name = "Two", ProjectId = project.Id, FolderPath = otherCampaignFolder });
        db.SaveChanges();
    }

    private string MarkedFolder(string name, string kind, string id)
    {
        var folder = _tree.NewDirectory(name);
        Mark(folder, new HarnessMarker(kind, id));
        return folder;
    }

    private static void Mark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HarnessMarker.PathIn(folder))!);
        File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
    }
}
