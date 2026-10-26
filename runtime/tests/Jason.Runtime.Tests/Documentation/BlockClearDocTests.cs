using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// What the philosophy page says about who clears a provider block, held to the refusal the runtime makes: a clear is a
/// person's word that the cause is gone, so a clear from a role or from an attempt is refused with the code the
/// work-execution contract names, and a clear that names no person is refused with another. A page that said only that
/// "an agent cannot" would leave a reader to guess what is refused, and to whom.
/// </summary>
public sealed partial class BlockClearDocTests
{
    [Fact]
    public void The_philosophy_page_says_a_clear_from_a_role_or_a_run_is_refused_and_so_is_one_naming_no_person()
    {
        var page = Whitespace().Replace(File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "docs", "philosophy.md")), " ");

        Assert.Contains(
            "A person who has removed the cause clears it under their name. A clear that comes from a role or from a run is refused, and so is one that names no person.",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_says_a_clear_from_a_role_or_a_run_is_refused_and_no_longer_that_an_agent_cannot_clear()
    {
        var readme = Whitespace().Replace(File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "README.md")), " ");

        Assert.Contains(
            "A person who has removed the cause says so under their name, and the next scan lets the work go; a clear that comes from a role or from a run is refused, and so is one that names no person:",
            readme,
            StringComparison.Ordinal);
        Assert.DoesNotContain("an agent cannot clear a block", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActorType.Role)]
    [InlineData(ActorType.Attempt)]
    public void The_contract_names_the_code_the_runtime_refuses_a_role_or_an_attempt_with(ActorType type)
    {
        var contract = Whitespace().Replace(File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "docs", "work-execution.md")), " ");
        var code = DomainErrors.BlockNotHuman(type).Code;

        Assert.Contains($"a role or an attempt is refused with `{code}`", contract, StringComparison.Ordinal);
        Assert.Contains($"| `{code}` | 400 | a block is cleared on a person's word; a role or an attempt cannot clear one |", contract, StringComparison.Ordinal);
        Assert.Contains($"and an absent actor, or one with no id, with `{DomainErrors.BlockActorRequired().Code}`", contract, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
