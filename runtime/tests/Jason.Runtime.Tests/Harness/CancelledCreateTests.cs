using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Harness;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// A caller that goes away in the middle of a create — a timeout, Ctrl-C, an agent host ending the command. Before the
/// folders stand, the create is taken back whole and its retry makes it; once they stand, the create is finished, so no
/// folder is ever left marked for a row that was rolled back, and the retry answers what the first request made.
/// </summary>
public sealed class CancelledCreateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_create_whose_caller_goes_away_once_its_folder_stands_is_finished_and_its_retry_answers_it()
    {
        using var database = new TestDatabase();
        var request = new CampaignCreateRequest("Q3 leads", null, null, null, "launch-q3");
        using var leaving = new Leaves(RecordsACampaignFolder);
        using var db = Open(database, leaving);

        var made = await Campaigns(db).CreateAsync(request, leaving.Token);
        var again = await Campaigns(db).CreateAsync(request, Ct);

        Assert.True(leaving.Left, "the caller never went away.");
        Assert.Equal((made.Id, made.FolderPath), (again.Id, again.FolderPath));
        Assert.Equal(new HarnessMarker(HarnessMarker.Campaign, made.Id), HarnessMarker.In(made.FolderPath!));
        Assert.Equal(1, await db.Campaigns.CountAsync(Ct));
        Assert.Equal(new string?[] { "q3-leads" }, Directory.GetDirectories(Path.GetDirectoryName(made.FolderPath!)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_campaign_create_whose_caller_goes_away_before_its_folder_leaves_nothing_and_its_retry_makes_it()
    {
        using var database = new TestDatabase();
        var request = new CampaignCreateRequest("Q3 leads", null, null, null, "launch-q3");
        using var leaving = new Leaves(InsertsACampaign);
        using var db = Open(database, leaving);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Campaigns(db).CreateAsync(request, leaving.Token));
        db.ChangeTracker.Clear();
        var campaigns = Path.Combine(TestHarness.RootFor(db), "projects", "default", "campaigns");
        var leftBehind = Directory.Exists(campaigns) ? Directory.GetDirectories(campaigns) : [];
        var made = await Campaigns(db).CreateAsync(request, Ct);

        Assert.Empty(leftBehind);
        Assert.Equal(Path.Combine(campaigns, "q3-leads"), made.FolderPath);
        Assert.Equal(1, await db.Campaigns.CountAsync(Ct));
    }

    /// <summary>A project's retry is the same create again, answered <c>project_exists</c> naming the project, never <c>harness_folder_taken</c>.</summary>
    [Fact]
    public async Task A_project_create_whose_caller_goes_away_once_its_folder_stands_is_finished_and_its_retry_names_it()
    {
        using var database = new TestDatabase();
        var request = new ProjectCreateRequest("Acme Corp", null, null, null);
        using var leaving = new Leaves(RecordsAProjectFolder);
        using var db = Open(database, leaving);

        var made = await Projects(db).CreateAsync(request, leaving.Token);
        var again = await Assert.ThrowsAsync<DomainException>(() => Projects(db).CreateAsync(request, Ct));

        Assert.True(leaving.Left, "the caller never went away.");
        Assert.Equal("project_exists", again.Code);
        Assert.Contains(made.Id, again.Message, StringComparison.Ordinal);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, made.Id), HarnessMarker.In(made.FolderPath!));
        Assert.Equal(made.FolderPath, (await db.Projects.AsNoTracking().SingleAsync(project => project.PublicId == made.Id, Ct)).FolderPath);
    }

    [Fact]
    public async Task A_project_create_whose_caller_goes_away_before_its_folder_leaves_nothing_and_its_retry_makes_it()
    {
        using var database = new TestDatabase();
        var request = new ProjectCreateRequest("Acme Corp", null, null, null);
        using var leaving = new Leaves(InsertsAProject);
        using var db = Open(database, leaving);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Projects(db).CreateAsync(request, leaving.Token));
        db.ChangeTracker.Clear();
        var folder = Path.Combine(TestHarness.RootFor(db), "projects", "acme-corp");
        var leftBehind = Directory.Exists(folder);
        var made = await Projects(db).CreateAsync(request, Ct);

        Assert.False(leftBehind, $"'{folder}' was left behind by a create that was taken back.");
        Assert.Equal(folder, made.FolderPath);
        Assert.Equal(new HarnessMarker(HarnessMarker.Project, made.Id), HarnessMarker.In(folder));
    }

    private static JasonDbContext Open(TestDatabase database, Leaves leaving) =>
        new(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(leaving).Options);

    private static CampaignService Campaigns(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
    }

    private static ProjectService Projects(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new(db, new JournalWriter(clock), clock, TestHarness.FoldersFor(db));
    }

    /// <summary>The save that records a campaign's folder once it stands: the one after the folders, before the commit.</summary>
    private static bool RecordsACampaignFolder(DbContext context) =>
        context.ChangeTracker.Entries<Campaign>().Any(entry => entry.State == EntityState.Modified && entry.Entity.FolderPath is not null);

    /// <summary>The save that writes the campaign's row, before any folder of the campaign's is made.</summary>
    private static bool InsertsACampaign(DbContext context) =>
        context.ChangeTracker.Entries<Campaign>().Any(entry => entry.State == EntityState.Added);

    /// <summary>The save that records a project's folder once it stands: the one after the folder, before the commit.</summary>
    private static bool RecordsAProjectFolder(DbContext context) =>
        context.ChangeTracker.Entries<Project>().Any(entry => entry.State == EntityState.Modified && entry.Entity.FolderPath is not null);

    /// <summary>The save that writes the project's row, before its folder is made.</summary>
    private static bool InsertsAProject(DbContext context) =>
        context.ChangeTracker.Entries<Project>().Any(entry => entry.State == EntityState.Added);

    /// <summary>
    /// The caller going away at a chosen save, by construction rather than by timing: as the context is about to make the
    /// first save <paramref name="at"/> picks out, the request's token is cancelled. Every other save passes untouched.
    /// </summary>
    private sealed class Leaves(Func<DbContext, bool> at) : SaveChangesInterceptor, IDisposable
    {
        private readonly CancellationTokenSource _caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        public CancellationToken Token => _caller.Token;

        public bool Left => _caller.IsCancellationRequested;

        public void Dispose() => _caller.Dispose();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            if (!Left && at(eventData.Context!))
            {
                await _caller.CancelAsync();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
