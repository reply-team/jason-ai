using System.Reflection;
using System.Text.RegularExpressions;
using Jason.Contracts.Channels;
using Jason.Runtime.Contacts;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Profiles;
using Jason.Runtime.Roles;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Patterns;

/// <summary>
/// A pattern that ends in <c>$</c> also matches just before a final line break, so a value followed by one would be
/// taken for the value itself. The runtime's own patterns end at the very end of the string. Where a pattern is
/// reached by a caller that hands it the raw string, the behaviour is held beside that caller's other tests; the
/// patterns here are the ones every caller shields — it trims the value, splits it on line breaks or strips every
/// separator before the pattern sees it — so no request can show the difference and the pattern is held on its own.
/// A pattern's own claim should not depend on how careful its callers are today.
/// </summary>
public sealed class PatternEndTests
{
    private const string NpmShimLastLine =
        "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\"  \"%dp0%\\node_modules\\vendor-cli\\dist\\index.js\" %*";

    [Theory]
    // Critical: the trailing-newline trap.
    [InlineData("WorkItemValidation.RoleName", typeof(WorkItemValidation), "RoleName", "campaign-manager", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.R3)])]
    [InlineData("WorkItemValidation.OperationName", typeof(WorkItemValidation), "OperationName", "campaign.get")]
    [InlineData("ProfileValidation.ProfileName", typeof(ProfileValidation), "ProfileName", "house")]
    [InlineData("RoleValidation.RoleName", typeof(RoleValidation), "RoleName", "campaign-manager")]
    [InlineData("ChannelRules.ChannelName", typeof(ChannelRules), "ChannelName", "email")]
    [InlineData("ChannelRules.E164", typeof(ChannelRules), "E164", "+14155550123")]
    [InlineData("ChannelRules.SchemeName", typeof(ChannelRules), "SchemeName", "https")]
    [InlineData("ChannelRules.HostName", typeof(ChannelRules), "HostName", "www.linkedin.com")]
    [InlineData("MatchKey.CustomFieldName", typeof(MatchKey), "CustomFieldName", "crm_id")]
    [InlineData("ExecutorService.ErrorCode", typeof(ExecutorService), "ErrorCode", "rate_limited")]
    [InlineData("ExecutableResolver.NpmShimCall", typeof(ExecutableResolver), "NpmShimCall", NpmShimLastLine)]
    [InlineData("ExecutableResolver.PackageName", typeof(ExecutableResolver), "PackageName", "vendor-cli")]
    [InlineData("YamlToJson.Decimal", typeof(YamlToJson), "Decimal", "42")]
    [InlineData("YamlToJson.Hexadecimal", typeof(YamlToJson), "Hexadecimal", "0x1F")]
    [InlineData("YamlToJson.Octal", typeof(YamlToJson), "Octal", "0o17")]
    [InlineData("YamlToJson.Float", typeof(YamlToJson), "Float", "1.5e3")]
    public void A_value_followed_by_a_line_break_is_not_the_value(string site, Type owner, string member, string value)
    {
        var (pattern, where) = PatternOf(site, owner, member);

        Assert.True(pattern.IsMatch(value), $"{where}: {pattern} ({site}) refuses the value itself: {value}");
        Assert.False(pattern.IsMatch(value + "\n"), $"{where}: {pattern} ({site}) takes the value followed by a line break for the value.");
    }

    /// <summary>
    /// The pattern a type holds under a name — a public property where the type shares it, and otherwise the private
    /// method the regular expression generator wrote for it — and the file and line that declare it, so a failure names
    /// the file, the line and the pattern's text. Where there is neither, the row is named with what it looked for, so a
    /// pattern renamed or moved says which row has to follow it.
    /// </summary>
    private static (Regex Pattern, string Where) PatternOf(string site, Type owner, string member)
    {
        const BindingFlags Shared = BindingFlags.Public | BindingFlags.Static;
        const BindingFlags Generated = BindingFlags.NonPublic | BindingFlags.Static;

        var pattern = owner.GetProperty(member, Shared)?.GetValue(null) ?? owner.GetMethod(member, Generated, Type.EmptyTypes)?.Invoke(null, null);
        Assert.True(
            pattern is Regex,
            $"{site}: {owner.FullName} has no public static property and no generated method named {member} that holds a pattern; it was renamed, moved or removed, and this row has to follow it.");
        return ((Regex)pattern, Declaration(owner, member));
    }

    /// <summary>The file and line of the source that declares a type's pattern under a name, read from the production tree.</summary>
    private static string Declaration(Type owner, string member)
    {
        var root = RepositoryRoot();
        var type = new Regex($@"\b(class|record|struct)\s+{Regex.Escape(owner.Name)}\b");
        var declaration = new Regex($@"\bRegex\??\s+{Regex.Escape(member)}\s*(\(|\{{|=>)");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "runtime", "src"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (type.IsMatch(text) && declaration.Match(text) is { Success: true } found)
            {
                return $"{Path.GetRelativePath(root, file).Replace('\\', '/')}({CSharpSource.LineOf(text, found.Index)})";
            }
        }

        return $"{owner.Name}.{member}, whose declaration was not found in the source";
    }

    private static string RepositoryRoot()
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
