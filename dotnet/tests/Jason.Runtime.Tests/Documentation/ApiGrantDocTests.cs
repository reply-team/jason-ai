using System.Text.RegularExpressions;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The plugin page tells an author what a handler's <c>host.call</c> may reach and how a call fails, and the API page
/// tells every caller what the handler capability is. Both are held to the one classification the runtime grants by,
/// so a family, a write or an operation no grant opens cannot change without the page an author reads changing too.
/// </summary>
public sealed partial class ApiGrantDocTests
{
    [Fact]
    public void The_plugin_page_lists_every_family_with_exactly_its_writes()
    {
        var page = Source("docs/plugins.md");
        foreach (var family in OperationFamilies.All)
        {
            var row = Row(page, family);
            Assert.True(row is not null, $"docs/plugins.md has no row for the family '{family}'.");
            if (OperationFamilies.Writes.TryGetValue(family, out var writes))
            {
                var named = Named().Matches(row).Select(match => match.Groups["name"].Value).Skip(1).ToHashSet(StringComparer.Ordinal);
                Assert.Equal(writes.Order(StringComparer.Ordinal), named.Order(StringComparer.Ordinal));
            }
        }
    }

    [Fact]
    public void The_plugin_page_names_every_read_verb_and_every_closed_noun()
    {
        var row = Row(Source("docs/plugins.md"), OperationFamilies.Read);
        Assert.NotNull(row);
        foreach (var name in OperationFamilies.ReadVerbs.Concat(OperationFamilies.ClosedNouns))
        {
            Assert.True(row.Contains($"`{name}`", StringComparison.Ordinal), $"The read row of docs/plugins.md does not name '{name}'.");
        }
    }

    [Fact]
    public void The_plugin_page_names_every_operation_no_grant_opens()
    {
        var page = Flat(Source("docs/plugins.md"));
        var start = page.IndexOf("No grant opens the rest:", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/plugins.md no longer says which operations no grant opens.");
        var paragraph = page[start..page.IndexOf("which a test holds.", start, StringComparison.Ordinal)];

        foreach (var operation in OperationFamilies.Never)
        {
            var noun = operation[..operation.IndexOf('.', StringComparison.Ordinal)];
            var covered = paragraph.Contains($"`{operation}`", StringComparison.Ordinal)
                || (OperationFamilies.ClosedNouns.Contains(noun) && paragraph.Contains($"`{noun}.`", StringComparison.Ordinal))
                || (noun == "profile" && paragraph.Contains("the profiles' writes", StringComparison.Ordinal));
            Assert.True(covered, $"docs/plugins.md does not say that no grant opens '{operation}'.");
        }
    }

    [Theory]
    [InlineData("docs/plugins.md", "A subscriber granted `workitem` writes can feed itself through a role: an item it creates may run a role, and what that role does afterwards is ordinary work.")]
    [InlineData("docs/plugins.md", "a person's decisions (`approval.approve`, `approval.reject`, `approval.set_standing`, `approval.unset_standing`, `decision.raise`, `decision.answer`, `block.clear`, `reply.record`, `workitem.resume`, `suppression.remove`, `campaign.set_send_mode`)")]
    [InlineData("docs/plugins.md", "`host.call` refuses the same way, with `details: { capability: \"api\", operation }`.")]
    [InlineData("docs/plugins.md", "`actor` or `reason` among the arguments is a `TypeError`; they are the host's to set.")]
    [InlineData("docs/plugins.md", "Every call is bounded by the smaller of 30 seconds and what remains of the invocation, and reads at most 1 MiB of answer.")]
    [InlineData("docs/plugins.md", "| `runtime_unreachable` | `transient`, `retryable` | nothing accepted the connection, so the request never left |")]
    [InlineData("docs/plugins.md", "| `runtime_answer_lost` | `ambiguous` | the connection broke after the request left; the call may have acted |")]
    [InlineData("docs/plugins.md", "| `runtime_timeout` | `ambiguous` |")]
    [InlineData("docs/plugins.md", "| `response_too_large` | `permanent` |")]
    [InlineData("docs/plugins.md", "| `runtime_answer_invalid` | `permanent` |")]
    [InlineData("docs/plugins.md", "Two things in the envelope are secret and nothing else is. `config`, absent where the person wrote nothing,")]
    [InlineData("docs/plugins.md", "And a handler's `door`, where the invocation has one, carries the capability the runtime minted for that invocation")]
    [InlineData("docs/plugins.md", "It travels on stdin only, never on argv, in the environment or in a file")]
    [InlineData("docs/plugins.md", "at the latest at the invocation's deadline plus the kill grace and one call's bound of 30 seconds, which covers the start of the child and a call in flight at the deadline,")]
    [InlineData("docs/runtime-api.md", "One credential is narrower than the token, and never a person's: a **handler capability**.")]
    [InlineData("docs/runtime-api.md", "any other operation is answered `403` with the code `operation_not_granted`.")]
    [InlineData("docs/runtime-api.md", "The type `plugin` is refused the same way to every caller but a handler capability, which must name its own plugin as the id.")]
    [InlineData("docs/work-execution.md", "A plugin handler's narrower capability follows its own rule")]
    public void The_pages_say_what_a_handler_s_door_is(string page, string sentence)
    {
        var text = Flat(Source(page));
        Assert.True(text.Contains(sentence, StringComparison.Ordinal), $"{page} no longer says: {sentence}");
    }

    private static string? Row(string page, string family) =>
        page.Split('\n').FirstOrDefault(line => line.StartsWith($"| `{family}` |", StringComparison.Ordinal));

    [GeneratedRegex("`(?<name>[a-z_.]+)`")]
    private static partial Regex Named();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
