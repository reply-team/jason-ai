using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The plugin page names the codes the host writes into a failed outcome itself, and the runtime passes exactly those
/// past an operation's declared codes. The page is held to the one list both read, so a code the host starts writing
/// is a failure here until a plugin author can find it.
/// </summary>
public sealed partial class HostCodesDocTests
{
    [Fact]
    public void The_plugin_page_names_exactly_the_codes_the_host_writes()
    {
        var page = Flat(Source("docs/plugins.md"));
        var sentence = HostSentence().Match(page);
        Assert.True(sentence.Success, "docs/plugins.md no longer has its 'The other codes the host itself writes into a failed outcome … are …' sentence.");

        var listed = Code().Matches(sentence.Groups["list"].Value).Select(match => match.Groups["code"].Value)
            .Append(HostOutcomeCodes.PluginTimeout)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(HostOutcomeCodes.All.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("docs/plugins.md", "the class is what the runtime acts on; the code is held to the operation's list")]
    [InlineData("docs/plugins.md", "Unreachable by construction, not a pattern")]
    [InlineData("docs/plugins.md", "A read made on a person's request outside any work item is held to the list by the same check")]
    [InlineData("docs/plugins.md", "The events family is the one exception: its reads refuse every code the events contract does not declare, the host's included (§15).")]
    [InlineData("docs/plugins.md", "All of them are `permanent`, and every operation's check passes them but the events family's (§15).")]
    [InlineData("docs/plugins.md", "Any other failure, the host's own codes of §8 included, is answered `result_invalid`.")]
    [InlineData("docs/contracts/README.md", "pass under the class the host writes them with ([the plugin guide, section 8](../plugins.md#8-failure-classes)); the events family alone refuses those as well.")]
    public void The_pages_say_where_the_host_s_codes_pass_and_where_they_do_not(string page, string sentence)
    {
        var text = Flat(Source(page));
        Assert.True(text.Contains(sentence, StringComparison.Ordinal), $"{page} no longer says: {sentence}");
    }

    /// <summary>
    /// The code reserved for a provider's object a package cannot read passes beside every operation as the host's codes
    /// do, under one class. The contracts page states it once, beside the slot, with its limit; the plugin page names it
    /// where it says which codes pass beside the declared ones (the sentence that tells an author what to answer is held
    /// by <see cref="CanonicalReadsDocTests"/>). The contracts page says once that a reserved code is not the provider's
    /// judgment of the work, so it clears no block, and the provider-block rule lists it among the answers that say
    /// nothing about the block.
    /// </summary>
    [Theory]
    [InlineData("docs/contracts/README.md", $"fails the item before any call with **`{UnjudgedOutcomeCodes.ProviderInputInvalid}`**, class `validation`: a failure code reserved beside every operation and declared by none, which the runtime passes under that class alone")]
    [InlineData("docs/contracts/README.md", "the code its error carries is one the document declares, under the class the document declares it with, or a code reserved beside every operation, under the class it is reserved with.")]
    [InlineData("docs/contracts/README.md", "The code is for this key only: a package that cannot read an argument the operation's document declares has a defect of its own, or has found one in the document, and no code stands for either.")]
    [InlineData("docs/contracts/README.md", $"The reserved `{UnjudgedOutcomeCodes.ProviderInputInvalid}` passes the same way, under `validation` alone ([the input a plugin receives](#the-input-a-plugin-receives)), and an events read refuses it too.")]
    [InlineData("docs/contracts/README.md", "A reserved code is not the provider's judgment of the work, so it clears no [provider block](#the-provider-block). The code is for this key only")]
    [InlineData("docs/contracts/README.md", "and, where the provider says, when it clears by itself; an open block is cleared only by the provider's judgment of the work.")]
    [InlineData("docs/work-execution.md", $"**anything else** — `transient`, `ambiguous`, an answer outside the contract, the host's own ending, or a code reserved beside every operation, which no provider judgment of the work stands behind (`{UnjudgedOutcomeCodes.ProviderInputInvalid}`, which a package answers before any call, [the input a plugin receives](contracts/README.md#the-input-a-plugin-receives); `{UnjudgedOutcomeCodes.CapabilityNotOffered}`, the provider's answer that it does not offer what was asked yet, [a capability the provider does not offer yet](contracts/README.md#a-capability-the-provider-does-not-offer-yet)) — says nothing about the block: it stays open")]
    [InlineData("docs/plugins.md", $"`{UnjudgedOutcomeCodes.ProviderInputInvalid}`, the code reserved for a provider's object you cannot read (§7), passes the same way, under `validation` alone.")]
    public void The_pages_say_where_the_reserved_provider_input_code_passes(string page, string sentence)
    {
        var text = Flat(Source(page));
        Assert.True(text.Contains(sentence, StringComparison.Ordinal), $"{page} no longer says: {sentence}");
    }

    /// <summary>
    /// The provider-block rule is one list, and a reader who stops at it reads the whole rule: the reserved code is named
    /// once on that page, inside the list, and not again in a paragraph of its own below it.
    /// </summary>
    [Fact]
    public void The_provider_block_rule_names_the_reserved_code_once_inside_its_list()
    {
        var text = Flat(Source("docs/work-execution.md"));
        var named = text.Split($"`{UnjudgedOutcomeCodes.ProviderInputInvalid}`").Length - 1;

        Assert.True(named == 1, $"docs/work-execution.md names `{UnjudgedOutcomeCodes.ProviderInputInvalid}` {named} times.");
    }

    [GeneratedRegex(@"The other codes the host itself writes into a failed outcome — as opposed to your own vocabulary — are (?<list>.+?)\. All of them are `permanent`")]
    private static partial Regex HostSentence();

    [GeneratedRegex("`(?<code>[a-z_]+)`")]
    private static partial Regex Code();

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
