using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class ProjectCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_sends_the_name_the_slug_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("project", "create", "--name", "Acme Corp", "--slug", "acme", "--reason", "the first product");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted("project.create", """{"name":"Acme Corp","slug":"acme","reason":"the first product"}""");
    }

    [Fact]
    public async Task Create_without_a_slug_leaves_it_to_the_runtime()
    {
        using var cli = new CliRun();

        await cli.RunAsync("project", "create", "--name", "Acme Corp");

        cli.AssertPosted("project.create", """{"name":"Acme Corp"}""");
    }

    [Fact]
    public async Task Get_names_the_project_by_its_id_or_by_its_slug()
    {
        using (var byId = new CliRun())
        {
            Assert.Equal(ExitCodes.Success, await byId.RunAsync("project", "get", "prj_A"));
            byId.AssertPosted("project.get", """{"project_id":"prj_A"}""");
        }

        using var bySlug = new CliRun();
        Assert.Equal(ExitCodes.Success, await bySlug.RunAsync("project", "get", "--slug", "acme"));
        bySlug.AssertPosted("project.get", """{"slug":"acme"}""");
    }

    [Theory]
    [InlineData("project", "get")]
    [InlineData("project", "get", "prj_A", "--slug", "acme")]
    public async Task Get_by_neither_or_by_both_is_a_usage_error(params string[] args)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(args);

        Assert.True(exit == ExitCodes.Usage, $"'{string.Join(' ', args)}' exited {exit}: {cli.Error}");
        Assert.Null(cli.Url);
        Assert.Contains("--slug", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_passes_the_query_and_the_page()
    {
        using var cli = new CliRun();

        await cli.RunAsync("project", "list", "--query", "acme", "--limit", "5", "--cursor", "abc");

        cli.AssertPosted("project.list", """{"query":"acme","limit":5,"cursor":"abc"}""");
    }

    [Fact]
    public async Task Update_sends_the_new_name_and_the_reason()
    {
        using var cli = new CliRun();

        await cli.RunAsync("project", "update", "prj_A", "--name", "Acme Inc", "--reason", "renamed");

        cli.AssertPosted("project.update", """{"project_id":"prj_A","name":"Acme Inc","reason":"renamed"}""");
    }

    [Fact]
    public async Task Human_mode_renders_one_project()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(new ProjectDto("prj_A", "Acme Corp", "acme-corp", false, Moment, Moment), JasonJson.Options));

        var exit = await cli.RunAsync("project", "get", "prj_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("prj_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Acme Corp", cli.Text, StringComparison.Ordinal);
        Assert.Contains("acme-corp", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Default:", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_project_listing_with_the_default_marked()
    {
        var page = new Page<ProjectDto>(
            [new ProjectDto("prj_D", "Default", "default", true, Moment, Moment), new ProjectDto("prj_A", "Acme Corp", "acme-corp", false, Moment, Moment)],
            "cHJqX0E");
        using var cli = new CliRun(JsonSerializer.Serialize(page, JasonJson.Options));

        var exit = await cli.RunAsync("project", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Matches("(?m)^ID +NAME +SLUG +DEFAULT +CREATED", cli.Text);
        Assert.Matches("(?m)^prj_D +Default +default +yes ", cli.Text);
        Assert.Matches("(?m)^prj_A +Acme Corp +acme-corp +no ", cli.Text);
        Assert.Contains("next cursor: cHJqX0E", cli.Text, StringComparison.Ordinal);
    }
}
