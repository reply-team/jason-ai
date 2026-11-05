using Jason.Cli.Doctor;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A problem is reported through one issue form, with the file <c>jason doctor</c> writes attached. The form's shape is
/// held here: its fields by the ids <c>jason doctor</c> fills in through the address it prints, the one required
/// checkbox that says the file was made without the journal's entries standing above the attachment it is about, and no
/// blank issue beside it, so nothing reaches the issues without that box.
/// </summary>
public sealed class IssueFormTests
{
    private static string Form(string name) => File.ReadAllText(Path.Combine(RepositoryTree.Root(), ".github", "ISSUE_TEMPLATE", name));

    private static List<YamlMappingNode> Body(YamlMappingNode form) =>
        [.. Assert.IsType<YamlSequenceNode>(Node(form, "body")).Children.Cast<YamlMappingNode>()];

    private static YamlMappingNode Attributes(YamlMappingNode field) => Mapping(field, "attributes");

    private static bool Required(YamlMappingNode node) =>
        Node(node, "validations") is YamlMappingNode validations && Scalar(validations, "required") == "true";

    [Fact]
    public void The_problem_form_asks_in_order_for_what_was_run_what_it_answered_the_version_the_platform_and_what_was_expected()
    {
        var form = Parse(Form(ProblemReport.Template));

        Assert.False(string.IsNullOrWhiteSpace(Scalar(form, "name")));
        Assert.False(string.IsNullOrWhiteSpace(Scalar(form, "description")));
        Assert.Equal("[Problem]: ", Scalar(form, "title"));

        var ids = Body(form).Select(field => Scalar(field, "id")).OfType<string>().ToList();
        Assert.Equal(["ran", "answered", ProblemReport.VersionField, ProblemReport.PlatformField, "expected", "without_journal", "doctor_file"], ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        // The two the command fills in are single-line inputs, which is what a query parameter fills.
        foreach (var id in (string[])[ProblemReport.VersionField, ProblemReport.PlatformField])
        {
            var field = Body(form).Single(item => Scalar(item, "id") == id);
            Assert.Equal("input", Scalar(field, "type"));
            Assert.True(Required(field), $"'{id}' is required.");
        }

        Assert.Contains("jason --version", Scalar(Attributes(Body(form).Single(item => Scalar(item, "id") == ProblemReport.VersionField)), "description"), StringComparison.Ordinal);
    }

    [Fact]
    public void One_required_checkbox_says_the_file_was_made_without_the_journal_and_stands_above_the_attachment()
    {
        var body = Body(Parse(Form(ProblemReport.Template)));
        var boxes = body.Where(field => Scalar(field, "type") == "checkboxes").ToList();
        var box = Assert.Single(boxes);
        var options = Assert.IsType<YamlSequenceNode>(Node(Attributes(box), "options")).Children.Cast<YamlMappingNode>().ToList();
        var option = Assert.Single(options);
        Assert.Equal("This file was made by `jason doctor` without `--include-journal`.", Scalar(option, "label"));
        Assert.Equal("true", Scalar(option, "required"));

        var upload = Assert.Single(body, field => Scalar(field, "type") == "upload");
        Assert.Equal("doctor_file", Scalar(upload, "id"));
        Assert.Equal(body.IndexOf(box) + 1, body.IndexOf(upload));
        var description = Scalar(Attributes(upload), "description")!;
        Assert.Contains(".json", description, StringComparison.Ordinal);
        Assert.Contains("zip", description, StringComparison.Ordinal);
        Assert.Contains("--include-journal", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages that tell a person where to write send a problem to this form, not to a blank issue, and say that a file
    /// made with the journal's entries never goes to an issue.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("docs/INSTALL.md")]
    public void The_pages_send_a_problem_to_the_form_and_a_journal_file_never_to_an_issue(string page)
    {
        var text = string.Join(' ', File.ReadAllText(Path.Combine(RepositoryTree.Root(), page)).Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains($"({Jason.Cli.PublicRepository.Url}/issues/new?template={ProblemReport.Template})", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"({Jason.Cli.PublicRepository.Url}/issues)", text, StringComparison.Ordinal);
        Assert.Matches("--include-journal.{0,120}?never (goes )?(to )?an issue", text);
    }

    [Fact]
    public void Blank_issues_are_off_and_a_question_and_a_vulnerability_are_pointed_elsewhere()
    {
        var config = Parse(Form("config.yml"));

        Assert.Equal("false", Scalar(config, "blank_issues_enabled"));
        var links = Assert.IsType<YamlSequenceNode>(Node(config, "contact_links")).Children.Cast<YamlMappingNode>().Select(link => Scalar(link, "url")).ToList();
        Assert.Equal([$"{Jason.Cli.PublicRepository.Url}/discussions", $"{Jason.Cli.PublicRepository.Url}/blob/main/SECURITY.md"], links);
    }
}
