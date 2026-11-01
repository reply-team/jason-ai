using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Projects;

public class ProjectServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A project made from a name takes its slug from it, is not the default, and is said once in the chronicle: a line of
    /// the installation's rather than of any campaign's, keyed by the project's id, with the name and slug it was made with.
    /// </summary>
    [Fact]
    public async Task A_new_project_takes_its_slug_from_its_name_and_is_said_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var project = await NewService(db).CreateAsync(new ProjectCreateRequest("  Acme Corp  ", null, new ActorRef(ActorType.Human, "marta"), "the first product"), Ct);

        Assert.Matches("^prj_[0-9A-HJKMNP-TV-Z]{26}$", project.Id);
        Assert.Equal(new ProjectDto(project.Id, "Acme Corp", "acme-corp", false, Noon, Noon, Path.Combine(TestHarness.RootFor(db), "projects", "acme-corp")), project);
        var line = Assert.Single(await db.Journal.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(JournalKinds.ProjectCreated, line.Kind);
        Assert.Equal(project.Id, line.Key);
        Assert.Null(line.CampaignId);
        Assert.Null(line.Old);
        Assert.Equal("""{"name":"Acme Corp","slug":"acme-corp"}""", line.New!.ToJsonString());
        Assert.Equal("the first product", line.Reason);
        Assert.Equal((ActorType.Human, "marta"), (line.ActorType, line.ActorId));
    }

    [Fact]
    public async Task A_slug_given_is_the_slug_kept()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var project = await NewService(db).CreateAsync(new ProjectCreateRequest("Acme", "acme-eu", null, null), Ct);

        Assert.Equal("acme-eu", project.Slug);
        Assert.Equal("acme-eu", (await db.Projects.AsNoTracking().SingleAsync(p => p.PublicId == project.Id, Ct)).Slug);
    }

    /// <summary>
    /// The slug is unique, which is what makes a create safe to send again: the second is refused, naming the project the
    /// first one made, and nothing is written for it. The default project holds <c>default</c> in the same way.
    /// </summary>
    [Fact]
    public async Task A_slug_already_taken_is_refused_naming_the_project_that_holds_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var first = await service.CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var lines = await db.Journal.CountAsync(Ct);

        var again = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(new ProjectCreateRequest("ACME", null, null, null), Ct));

        Assert.Equal((409, "project_exists"), (again.StatusCode, again.Code));
        Assert.Equal([new ErrorDetail("project_id", "exists", first.Id)], again.Details);
        Assert.Contains(first.Id, again.Message, StringComparison.Ordinal);
        Assert.Equal(lines, await db.Journal.CountAsync(Ct));
        Assert.Equal(2, await db.Projects.CountAsync(Ct));

        var taken = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(new ProjectCreateRequest("Default", null, null, null), Ct));
        var standing = await db.Projects.AsNoTracking().SingleAsync(p => p.IsDefault, Ct);
        Assert.Equal([new ErrorDetail("project_id", "exists", standing.PublicId)], taken.Details);
    }

    /// <summary>
    /// A name that makes no slug of its own — in another script, or nothing but signs — is named by a hash of its fold, so
    /// the same create sent again is refused as <c>project_exists</c> naming the project the first one made, and there is
    /// still one project.
    /// </summary>
    [Theory]
    [InlineData("Новый продукт")]
    [InlineData("C++")]
    public async Task A_create_sent_again_for_a_name_that_makes_no_slug_finds_the_first_project(string name)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var first = await service.CreateAsync(new ProjectCreateRequest(name, null, null, null), Ct);

        var again = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(new ProjectCreateRequest(name, null, null, null), Ct));

        Assert.Equal((409, "project_exists"), (again.StatusCode, again.Code));
        Assert.Equal([new ErrorDetail("project_id", "exists", first.Id)], again.Details);
        Assert.Equal(1, await db.Projects.CountAsync(p => !p.IsDefault, Ct));
    }

    /// <summary>Extra white space inside or around a name does not make another slug, so the create sent again with it finds the first project.</summary>
    [Fact]
    public async Task A_create_sent_again_with_extra_white_space_finds_the_first_project()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var first = await service.CreateAsync(new ProjectCreateRequest("Новый продукт", null, null, null), Ct);

        var again = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(new ProjectCreateRequest("Новый  продукт ", null, null, null), Ct));

        Assert.Equal((409, "project_exists"), (again.StatusCode, again.Code));
        Assert.Equal([new ErrorDetail("project_id", "exists", first.Id)], again.Details);
        Assert.Equal(1, await db.Projects.CountAsync(p => !p.IsDefault, Ct));
    }

    [Theory]
    [InlineData(null, "name", "required")]
    [InlineData("   ", "name", "required")]
    public async Task A_project_without_a_name_is_refused(string? name, string field, string code)
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => NewService(db).CreateAsync(new ProjectCreateRequest(name, null, null, null), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.True((detail.Field, detail.Code) == (field, code), $"A name of '{name}' was refused as {detail.Field}/{detail.Code}.");
    }

    [Fact]
    public async Task A_name_too_long_and_a_slug_out_of_shape_are_both_said()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).CreateAsync(new ProjectCreateRequest(new string('n', 201), "Not A Slug", null, null), Ct));

        Assert.Equal(["name/too_long", "slug/invalid"], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}").Order(StringComparer.Ordinal));
        Assert.Equal(1, await db.Projects.CountAsync(Ct));
    }

    [Fact]
    public async Task A_project_is_found_by_its_id_or_by_its_slug_and_by_exactly_one()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var made = await service.CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);

        Assert.Equal(made, await service.GetAsync(new ProjectGetRequest(made.Id), Ct));
        Assert.Equal(made, await service.GetAsync(new ProjectGetRequest(null, "acme"), Ct));
        var standing = await service.GetAsync(new ProjectGetRequest(null, "default"), Ct);
        Assert.True(standing is { Default: true, Name: "Default" }, $"The default project read back as {standing}.");

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(new ProjectGetRequest("prj_01K7QN0000000000000000NONE"), Ct));
        Assert.Equal("project_not_found", unknown.Code);
        var noSlug = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(new ProjectGetRequest(null, "nothing-here"), Ct));
        Assert.Equal("project_not_found", noSlug.Code);

        var neither = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(new ProjectGetRequest(null), Ct));
        Assert.Equal(["project_id/required"], neither.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        var both = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(new ProjectGetRequest(made.Id, "acme"), Ct));
        Assert.Equal(["slug/not_allowed"], both.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    /// <summary>A listing is by id — the order projects were made in — pages by cursor, and finds a name whatever its case or accents.</summary>
    [Fact]
    public async Task Projects_are_listed_in_the_order_they_were_made_and_found_by_their_names()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var acme = await service.CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var cafe = await service.CreateAsync(new ProjectCreateRequest("Café Ölz", null, null, null), Ct);

        var all = await service.ListAsync(new ProjectListRequest(null, null, null), Ct);
        Assert.Equal(["default", "acme", "cafe-olz"], all.Items.Select(p => p.Slug));
        Assert.Null(all.NextCursor);

        var first = await service.ListAsync(new ProjectListRequest(null, 2, null), Ct);
        Assert.Equal(["default", "acme"], first.Items.Select(p => p.Slug));
        var rest = await service.ListAsync(new ProjectListRequest(null, 2, first.NextCursor), Ct);
        Assert.Equal([cafe], rest.Items);
        Assert.Null(rest.NextCursor);

        Assert.Equal([cafe], (await service.ListAsync(new ProjectListRequest("CAFE OL", null, null), Ct)).Items);
        Assert.Equal([acme], (await service.ListAsync(new ProjectListRequest("cm", null, null), Ct)).Items);
        Assert.Empty((await service.ListAsync(new ProjectListRequest("100%", null, null), Ct)).Items);
    }

    /// <summary>
    /// A rename changes the name and nothing else: the slug a person types and the folder it names stay. It is said once,
    /// with the old name and the new; asking for the name a project already has says nothing.
    /// </summary>
    [Fact]
    public async Task A_rename_keeps_the_slug_and_is_said_with_the_old_name_and_the_new()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var made = await service.CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        var renamed = await service.UpdateAsync(new ProjectUpdateRequest(made.Id, Optional<string?>.Of("Acme Europe"), null, "the name the client uses"), Ct);

        Assert.Equal(made with { Name = "Acme Europe", UpdatedAt = Noon.AddMinutes(5) }, renamed);
        var line = await db.Journal.AsNoTracking().OrderBy(e => e.Id).LastAsync(Ct);
        Assert.Equal((JournalKinds.ProjectUpdated, made.Id), (line.Kind, line.Key));
        Assert.Equal("""{"name":"Acme"}""", line.Old!.ToJsonString());
        Assert.Equal("""{"name":"Acme Europe"}""", line.New!.ToJsonString());
        Assert.Equal("the name the client uses", line.Reason);

        var lines = await db.Journal.CountAsync(Ct);
        Assert.Equal(renamed, await service.UpdateAsync(new ProjectUpdateRequest(made.Id, Optional<string?>.Of("Acme Europe"), null, null), Ct));
        Assert.Equal(renamed, await service.UpdateAsync(new ProjectUpdateRequest(made.Id, default, null, null), Ct));
        Assert.Equal(lines, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task The_default_project_can_be_renamed_and_stays_the_default()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var standing = await service.GetAsync(new ProjectGetRequest(null, "default"), Ct);

        var renamed = await service.UpdateAsync(new ProjectUpdateRequest(standing.Id, Optional<string?>.Of("ClickUp"), null, null), Ct);

        Assert.Equal(("ClickUp", "default", true), (renamed.Name, renamed.Slug, renamed.Default));
    }

    [Fact]
    public async Task A_rename_to_nothing_or_of_nothing_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var made = await service.CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);

        var blank = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(new ProjectUpdateRequest(made.Id, Optional<string?>.Of(null), null, null), Ct));
        Assert.Equal(["name/required"], blank.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(new ProjectUpdateRequest("prj_01K7QN0000000000000000NONE", Optional<string?>.Of("x"), null, null), Ct));
        Assert.Equal("project_not_found", unknown.Code);
        var nobody = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(new ProjectUpdateRequest(null, Optional<string?>.Of("x"), null, null), Ct));
        Assert.Equal(["project_id/required"], nobody.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    private static ProjectService NewService(JasonDbContext db, TimeProvider? clock = null)
    {
        clock ??= new FixedClock(Noon);
        return new ProjectService(db, new JournalWriter(clock), clock, TestHarness.FoldersFor(db));
    }
}
