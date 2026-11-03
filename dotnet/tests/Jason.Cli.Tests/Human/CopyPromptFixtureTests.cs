namespace Jason.Cli.Tests.Human;

/// <summary>
/// The published text of what Copy prompt puts on the clipboard, <c>docs/prompts/approval.txt</c> and
/// <c>docs/prompts/question.txt</c>: what <c>jason approval get --prompt</c> and <c>jason decision get --prompt</c> print
/// for the exchanges recorded under <c>docs/api/fixtures/</c>, with the campaign's own folder. The UI's test reads the same
/// two files and holds its button to them, so the two renderings cannot drift. A text that changed fails here; only
/// <c>JASON_UPDATE_FIXTURES=1</c> rewrites the files.
/// </summary>
public sealed class CopyPromptFixtureTests
{
    private const string UpdateVariable = "JASON_UPDATE_FIXTURES";

    [Fact]
    public async Task The_approval_prompt_is_the_published_text()
    {
        var run = await CopyPromptTests.Run(
            ["approval", "get", "apr_1", "--prompt"],
            CopyPromptTests.Fixture("approval.get", "pending-with-preview"),
            campaign: CopyPromptTests.Fixture("campaign.get", "in-project-with-folder"));

        Held("approval.txt", run);
    }

    [Fact]
    public async Task The_question_prompt_is_the_published_text()
    {
        var run = await CopyPromptTests.Run(
            ["decision", "get", "dec_1", "--prompt"],
            CopyPromptTests.Fixture("decision.get", "pending-with-options"),
            campaign: CopyPromptTests.Fixture("campaign.get", "in-project-with-folder"));

        Held("question.txt", run);
    }

    [Fact]
    public void The_published_texts_are_the_only_files_of_the_folder()
    {
        var folder = Path.Combine(CopyPromptTests.RepositoryRoot(), "docs", "prompts");

        Assert.Equal(["approval.txt", "question.txt"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private static void Held(string file, CopyPromptTests.PromptRun run)
    {
        Assert.Equal(ExitCodes.Success, run.Exit);
        var path = Path.Combine(CopyPromptTests.RepositoryRoot(), "docs", "prompts", file);
        var text = run.Output.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return;
        }

        Assert.True(File.Exists(path), $"docs/prompts/{file} is not there. Rerun this test with {UpdateVariable}=1 and commit the file.");
        Assert.Equal(
            File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal),
            text);
    }
}
