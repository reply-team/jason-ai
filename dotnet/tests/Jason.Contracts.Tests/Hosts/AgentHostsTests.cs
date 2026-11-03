using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Contracts.Json;

namespace Jason.Contracts.Tests.Hosts;

/// <summary>
/// The one table of what both halves of Jason know about an agent host: the CLI that deploys skills into it and the
/// runtime that launches it. A host is a value, a row and a registration; these rows hold the value and the row together.
/// </summary>
public class AgentHostsTests
{
    public static TheoryData<AgentHostKind> Kinds() => [.. Enum.GetValues<AgentHostKind>()];

    [Fact]
    public void Every_kind_has_exactly_one_row_and_no_row_names_a_kind_twice()
    {
        Assert.Equal(
            Enum.GetValues<AgentHostKind>().Order(),
            AgentHosts.All.Select(facts => facts.Kind).Order());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_kinds_word_is_the_one_the_api_writes_and_it_parses_back(AgentHostKind kind)
    {
        var written = JsonSerializer.Serialize(kind, JasonJson.Options).Trim('"');

        Assert.Equal(written, AgentHosts.Word(kind));
        Assert.Equal(kind, AgentHosts.Parse(written));
        Assert.Equal(kind, AgentHosts.Parse($"  {written} "));
        Assert.Same(AgentHosts.Of(kind), AgentHosts.All.Single(facts => facts.Kind == kind));
    }

    [Theory]
    [InlineData("ClaudeCode")]
    [InlineData("claudecode")]
    [InlineData("CLAUDE_CODE")]
    [InlineData("claude-code")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Only_the_snake_case_word_parses(string? word)
    {
        Assert.Null(AgentHosts.Parse(word));
    }

    [Fact]
    public void The_vocabulary_names_every_word_in_the_order_of_the_enum()
    {
        Assert.Equal(
            string.Join(", ", Enum.GetValues<AgentHostKind>().Select(AgentHosts.Word)),
            AgentHosts.Vocabulary);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_path_is_relative_written_with_forward_slashes_and_stays_inside_where_it_is_read(AgentHostKind kind)
    {
        var facts = AgentHosts.Of(kind);
        foreach (var path in new[] { facts.HomeMarker, facts.HomeSkills, facts.WorkSkills })
        {
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.False(Path.IsPathRooted(path), $"'{path}' of {kind} is rooted.");
            Assert.DoesNotContain('\\', path);
            Assert.DoesNotContain("..", path.Split('/'));
        }

        Assert.False(string.IsNullOrWhiteSpace(facts.CommandWord));
        Assert.Equal(facts.CommandWord, Path.GetFileName(facts.CommandWord));
    }

    [Fact]
    public void Claude_code_reads_its_own_directory_and_imports_the_memory_file_through_claude_md()
    {
        var facts = AgentHosts.Of(AgentHostKind.ClaudeCode);

        Assert.Equal("claude", facts.CommandWord);
        Assert.Equal(".claude", facts.HomeMarker);
        Assert.Equal(HomeMarkerKind.Directory, facts.MarkerKind);
        Assert.Equal(".claude/skills", facts.HomeSkills);
        Assert.Equal(".claude/skills", facts.WorkSkills);
        Assert.Equal(new HostInstructionFile("CLAUDE.md", "@AGENTS.md"), facts.Instructions);
    }

    [Fact]
    public void Antigravity_cli_is_found_by_the_folder_only_it_keeps_and_reads_the_memory_file_itself()
    {
        var facts = AgentHosts.Of(AgentHostKind.AntigravityCli);

        Assert.Equal("antigravity_cli", AgentHosts.Word(AgentHostKind.AntigravityCli));
        Assert.Equal("agy", facts.CommandWord);
        Assert.Equal(".gemini/antigravity-cli", facts.HomeMarker);
        Assert.Equal(HomeMarkerKind.Directory, facts.MarkerKind);
        Assert.Equal(".gemini/antigravity-cli/skills", facts.HomeSkills);
        Assert.Equal(".agents/skills", facts.WorkSkills);
        Assert.Null(facts.Instructions);
    }

    [Fact]
    public void The_import_files_are_each_file_once_and_never_the_memory_file_itself()
    {
        var files = AgentHosts.ImportFiles;

        Assert.Equal(files.Count, files.Select(file => file.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(files, file => string.Equals(file.FileName, AgentHosts.MemoryFile, StringComparison.OrdinalIgnoreCase));
        Assert.All(files, file => Assert.Contains(AgentHosts.MemoryFile, file.ImportLine, StringComparison.Ordinal));
        Assert.Equal(
            AgentHosts.All.Where(facts => facts.Instructions is not null).Select(facts => facts.Instructions!.FileName).Distinct(StringComparer.OrdinalIgnoreCase),
            files.Select(file => file.FileName));
    }

    [Fact]
    public void Resolving_a_relative_path_under_a_folder_uses_the_platforms_separator()
    {
        var root = Path.Combine(Path.GetTempPath(), "home");

        Assert.Equal(Path.Combine(root, ".claude", "skills"), AgentHosts.Under(root, ".claude/skills"));
    }
}
