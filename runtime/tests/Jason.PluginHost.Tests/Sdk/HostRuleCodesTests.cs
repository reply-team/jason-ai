using System.Reflection;
using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// A rule the host enforces ends the invocation under a code of the host's own, and the runtime passes exactly the codes
/// <see cref="HostOutcomeCodes.All"/> lists beside an operation's declared ones. Every place the host throws a rule's
/// failure names its code by that list, so a code the host starts writing cannot reach a runtime that refuses it.
/// </summary>
public sealed partial class HostRuleCodesTests
{
    [Fact]
    public void Every_rule_the_host_throws_names_a_code_on_the_host_s_list()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(Root(), "runtime", "src", "Jason.PluginHost"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        var sites = sources
            .SelectMany(path => Construction().Matches(File.ReadAllText(path)).Select(match => (File: Path.GetFileName(path), Argument: match.Groups["code"].Value)))
            .ToList();

        Assert.NotEmpty(sites);
        Assert.All(sites, site =>
        {
            var named = Named().Match(site.Argument);
            Assert.True(named.Success, $"{site.File} throws a HostRuleException whose code is not named from HostOutcomeCodes: {site.Argument}");
            var field = typeof(HostOutcomeCodes).GetField(named.Groups["name"].Value, BindingFlags.Public | BindingFlags.Static);
            Assert.True(field is not null, $"{site.File} names HostOutcomeCodes.{named.Groups["name"].Value}, which does not exist.");
            var code = (string)field.GetValue(null)!;
            Assert.True(HostOutcomeCodes.All.Contains(code), $"{site.File} throws '{code}', which HostOutcomeCodes.All does not list.");
        });
    }

    [GeneratedRegex(@"new HostRuleException\(\s*(?<code>[^,]+),")]
    private static partial Regex Construction();

    [GeneratedRegex(@"^HostOutcomeCodes\.(?<name>[A-Za-z]+)$")]
    private static partial Regex Named();

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
