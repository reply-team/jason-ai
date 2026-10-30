using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A provider that does not offer a capability yet is answered with one code beside every operation, and two pages say
/// so: the plugin guide, where a plugin author learns when to answer it, and the contracts README, which names it once
/// above the list of capabilities a provider may lack for a while. Both are held to the code the runtime admits, so the
/// word a page teaches is the word the runtime accepts, under the class it accepts it with.
/// </summary>
public sealed partial class CapabilityNotOfferedDocTests
{
    private static readonly string Code = "`" + UnjudgedOutcomeCodes.CapabilityNotOffered + "`";

    [Fact]
    public void The_plugin_guide_s_failure_classes_name_the_code_as_permanent()
    {
        var section = Section(Flat(Source("docs/plugins.md")), "## 8. Failure classes", "## 9. ");

        Assert.Contains($"**A capability the provider does not offer yet** is answered {Code}, `permanent`.", section, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/plugins.md", "declaring an operation in the manifest says that the package knows how to call it, not that the provider offers it today. How a package tells that the provider does not offer what was asked is stated once, in [the contracts README](contracts/README.md#a-capability-the-provider-does-not-offer-yet), which also lists the capabilities a provider may lack for a while.")]
    [InlineData("docs/plugins.md", "with a message that names what is missing: the operation, the argument by its name in the operation's document, or a member of the provider's own object. Quote nothing of the provider's text in the message beyond its own code;")]
    [InlineData("docs/plugins.md", "It is reserved: no operation declares it, and every one passes it as `permanent` and under no other class")]
    [InlineData("docs/plugins.md", "`capability_not_offered`, the code reserved for a capability the provider does not offer yet (below), passes the same way, under `permanent` alone.")]
    [InlineData("docs/plugins.md", "what to do instead is the agent's to choose, never a fallback the runtime makes.")]
    [InlineData("docs/plugins.md", "It is not `plugin_operation_unsupported`, where your package does not implement the operation; not `capability_not_granted`, where the operator granted your package less than its manifest asks for (§9); and not an operation's own `feature_unavailable`, where the provider offers the thing and the account's plan or role does not include it.")]
    [InlineData("docs/contracts/README.md", "Nothing is detected in advance. For a route the provider's API does not have, the provider's answer to the call is the detection.")]
    [InlineData("docs/contracts/README.md", "For an argument, a provider that refuses what it does not know answers, and that answer is the detection; a provider that ignores it gives nothing to read, so a package sends a new argument only once it knows the provider takes it, by a read it makes before the call.")]
    [InlineData("docs/contracts/README.md", "The runtime falls back to nothing: the code is final, and what to do instead is the agent's to choose, another way or telling the person.")]
    [InlineData("docs/contracts/README.md", "Each capability a provider may lack for a while is a line below: the operation or the argument, and what an agent does instead where it is not offered.")]
    [InlineData("docs/contracts/README.md", "The code of [a capability the provider does not offer yet](#a-capability-the-provider-does-not-offer-yet) passes the same way, under `permanent` alone, and an events read refuses it too.")]
    public void The_pages_say_when_the_code_is_answered_and_what_follows(string page, string sentence)
    {
        var text = Flat(Source(page));
        Assert.True(text.Contains(sentence, StringComparison.Ordinal), $"{page} no longer says: {sentence}");
    }

    /// <summary>
    /// How a package tells that the provider does not offer what was asked is written once, in the contracts README;
    /// the plugin guide points there rather than giving its own list of answers that would read as the code.
    /// </summary>
    [Fact]
    public void The_plugin_guide_points_to_the_one_account_of_how_a_package_tells()
    {
        var section = Section(Flat(Source("docs/plugins.md")), "## 8. Failure classes", "## 9. ");

        Assert.DoesNotContain("the only thing that tells", section, StringComparison.Ordinal);
        Assert.DoesNotContain("explains as not supported", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The README names the code once, in its own section, with the class the runtime admits it under; the later lines name
    /// a capability each and point back at it, so the rule is never written a second time where it could drift.
    /// </summary>
    [Fact]
    public void The_contracts_readme_names_the_code_once_in_its_own_section()
    {
        var readme = Flat(Source("docs/contracts/README.md"));

        Assert.Equal(1, Regex.Count(readme, Regex.Escape(Code)));
        var section = Section(readme, "## A capability the provider does not offer yet", "## ");
        Assert.Contains($"one code, {Code}, class `permanent`: a failure code reserved beside every operation and declared by none", section, StringComparison.Ordinal);
        Assert.Contains("Being reserved, the code clears no [provider block](#the-provider-block).", section, StringComparison.Ordinal);
    }

    private static string Section(string text, string heading, string next)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page has no '{heading}' section.");
        var end = text.IndexOf(next, start + heading.Length, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }

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
