using System.Text.Json;
using System.Text.RegularExpressions;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// <c>THIRD-PARTY-NOTICES</c> travels in every archive beside the executable, so it has to cover what the executable
/// is made of: the runtimes a self-contained publish puts inside it, and every package of <c>Jason.App</c>'s closure.
/// The closure is read from the restore's own record, <c>project.assets.json</c>, rather than from a list typed here,
/// so a package added, removed or moved to another version turns this red until the file is written again.
/// </summary>
/// <remarks>
/// The file is written by <c>.github/scripts/Write-ThirdPartyNotices.ps1</c>, which a person runs after a package or SDK
/// change; no test runs it, because it reads the machine's NuGet cache. What is held here is the result.
/// </remarks>
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    [Critical(Family.B5, "every package is noticed")]
    public void Every_package_of_the_closure_has_an_entry_with_its_exact_version()
    {
        var closure = ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets));

        var problems = ThirdPartyNotices.Problems(Notices(), closure).Where(problem => problem.Contains("has no entry", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(closure.Packages);
        Assert.True(problems.Count == 0, ThirdPartyNotices.Report(problems));
    }

    [Fact]
    public void Every_licence_an_entry_names_has_its_text()
    {
        var closure = ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets));

        var problems = ThirdPartyNotices.Problems(Notices(), closure).Where(problem => problem.Contains("has no text", StringComparison.Ordinal)).ToList();

        Assert.True(problems.Count == 0, ThirdPartyNotices.Report(problems));
    }

    /// <summary>
    /// The .NET runtime is not a package of the closure: it enters the executable from a runtime pack at publish, and
    /// so does every shared framework a project of the closure references — ASP.NET Core's, for the runtime's API.
    /// </summary>
    [Fact]
    public void Every_runtime_the_executable_carries_has_its_section()
    {
        var closure = ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets));

        var problems = ThirdPartyNotices.Problems(Notices(), closure).Where(problem => problem.Contains("runtime pack", StringComparison.Ordinal)).ToList();

        Assert.Contains("Microsoft.NETCore.App", closure.Frameworks);
        Assert.Contains("Microsoft.AspNetCore.App", closure.Frameworks);
        Assert.True(problems.Count == 0, ThirdPartyNotices.Report(problems));
    }

    [Fact]
    [Critical(Family.B5, "THIRD-PARTY-NOTICES is current")]
    public void The_file_has_no_problem_at_all()
    {
        var problems = ThirdPartyNotices.Problems(Notices(), ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets)));

        Assert.True(problems.Count == 0, ThirdPartyNotices.Report(problems));
    }

    [Fact]
    public void A_file_without_the_NET_runtime_s_section_is_refused()
    {
        var notices = Notices();
        var doctored = Regex.Replace(notices, @"^Runtime pack: Microsoft\.NETCore\.App\.Runtime\.[^\n]*\n", "", RegexOptions.Multiline);
        Assert.NotEqual(notices, doctored);

        var problems = ThirdPartyNotices.Problems(doctored, ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets)));

        Assert.Contains(problems, problem => problem.Contains("Microsoft.NETCore.App.Runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void A_runtime_section_holding_no_licence_or_notices_is_refused()
    {
        var notices = Notices();
        var heading = Regex.Match(notices, @"^Runtime pack: Microsoft\.AspNetCore\.App\.Runtime\.[^\n]*\n", RegexOptions.Multiline);
        Assert.True(heading.Success);
        var next = notices.IndexOf("\nRuntime pack: ", heading.Index + heading.Length, StringComparison.Ordinal) is var at and >= 0
            ? at + 1
            : notices.IndexOf(ThirdPartyNotices.PackagesHeading, heading.Index, StringComparison.Ordinal);
        var doctored = notices[..(heading.Index + heading.Length)] + "\n" + notices[next..];

        var problems = ThirdPartyNotices.Problems(doctored, ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets)));

        Assert.Contains(problems, problem => problem.Contains("Microsoft.AspNetCore.App.Runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_whose_package_is_dropped_is_refused_naming_it()
    {
        var closure = ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets));
        var (name, version) = closure.Packages[closure.Packages.Count / 2];
        var notices = Notices();
        var doctored = Regex.Replace(notices, $@"^{Regex.Escape(name)} {Regex.Escape(version)}\n(  [^\n]*\n)*", "", RegexOptions.Multiline);
        Assert.NotEqual(notices, doctored);

        var problems = ThirdPartyNotices.Problems(doctored, closure);

        Assert.Contains($"{name} {version} has no entry in THIRD-PARTY-NOTICES.", problems);
    }

    [Fact]
    public void A_file_whose_entry_names_another_version_is_refused()
    {
        var closure = ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets));
        var (name, version) = closure.Packages[0];
        var notices = Notices();
        var doctored = Regex.Replace(notices, $@"^{Regex.Escape(name)} {Regex.Escape(version)}$", $"{name} 0.0.1", RegexOptions.Multiline);
        Assert.NotEqual(notices, doctored);

        var problems = ThirdPartyNotices.Problems(doctored, closure);

        Assert.Contains($"{name} {version} has no entry in THIRD-PARTY-NOTICES.", problems);
    }

    [Fact]
    public void A_file_whose_licence_text_is_missing_is_refused_naming_the_licence()
    {
        var notices = Notices();
        var doctored = Regex.Replace(notices, @"^Licence text: Apache-2\.0$", "Licence text: (removed)", RegexOptions.Multiline);
        Assert.NotEqual(notices, doctored);

        var problems = ThirdPartyNotices.Problems(doctored, ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets)));

        Assert.Contains(problems, problem => problem.StartsWith("Apache-2.0, which ", StringComparison.Ordinal) && problem.Contains("has no text", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entry_without_a_copyright_line_is_refused()
    {
        var closure = ThirdPartyNotices.Closure.Of(Read(ThirdPartyNotices.Assets));
        var (name, version) = closure.Packages[^1];
        var notices = Notices();
        var doctored = Regex.Replace(notices, $@"^({Regex.Escape(name)} {Regex.Escape(version)}\n  Licence: [^\n]*\n)  Copyright: [^\n]*\n", "$1", RegexOptions.Multiline);
        Assert.NotEqual(notices, doctored);

        var problems = ThirdPartyNotices.Problems(doctored, closure);

        Assert.Contains($"{name} {version} has no entry in THIRD-PARTY-NOTICES.", problems);
    }

    /// <summary>The embedded SQLite has no licence text to carry, so it is named where the library that embeds it is.</summary>
    [Fact]
    public void The_embedded_SQLite_is_named_under_the_package_that_carries_it()
    {
        var notices = Notices();
        var entry = Regex.Match(notices, @"^SQLitePCLRaw\.lib\.e_sqlite3 \S+\n(  [^\n]*\n)*", RegexOptions.Multiline);

        Assert.True(entry.Success, "SQLitePCLRaw.lib.e_sqlite3 has no entry.");
        Assert.Contains("  Embeds: SQLite — public domain (no licence text; see https://sqlite.org/copyright.html)\n", entry.Value, StringComparison.Ordinal);
    }

    private static string Notices()
    {
        var path = Path.Combine(RepositoryRoot(), ThirdPartyNotices.FileName);
        Assert.True(File.Exists(path), $"{ThirdPartyNotices.FileName} is not at the root.");
        return File.ReadAllText(path).ReplaceLineEndings("\n");
    }
}

/// <summary>What <c>THIRD-PARTY-NOTICES</c> must hold for a closure, as a list of problems.</summary>
internal static partial class ThirdPartyNotices
{
    internal const string FileName = "THIRD-PARTY-NOTICES";
    internal const string PackagesHeading = "\n2. The packages\n";
    internal static readonly string[] Assets = ["runtime", "src", "Jason.App", "obj", "project.assets.json"];

    /// <summary>
    /// The packages and shared frameworks of one restore. The target read is the one without a runtime identifier: a
    /// RID-specific restore adds <c>net10.0/&lt;rid&gt;</c> beside it with the same packages.
    /// </summary>
    internal sealed record Closure(int Major, int Minor, IReadOnlyList<(string Name, string Version)> Packages, IReadOnlyList<string> Frameworks)
    {
        internal static Closure Of(string assets)
        {
            using var document = JsonDocument.Parse(assets);
            var root = document.RootElement;
            var target = root.GetProperty("targets").EnumerateObject().Single(target => !target.Name.Contains('/', StringComparison.Ordinal));
            var framework = TargetFramework().Match(target.Name);
            Assert.True(framework.Success, $"The target {target.Name} is not a netX.Y framework.");

            var packages = target.Value.EnumerateObject()
                .Where(library => library.Value.GetProperty("type").GetString() == "package")
                .Select(library => library.Name.Split('/'))
                .Select(parts => (parts[0], parts[1]))
                .OrderBy(package => package.Item1, StringComparer.Ordinal)
                .ToList();

            var frameworks = root.GetProperty("project").GetProperty("frameworks").EnumerateObject()
                .SelectMany(each => each.Value.TryGetProperty("frameworkReferences", out var references) ? references.EnumerateObject().Select(reference => reference.Name) : [])
                .Concat(target.Value.EnumerateObject()
                    .SelectMany(library => library.Value.TryGetProperty("frameworkReferences", out var references) ? references.EnumerateArray().Select(reference => reference.GetString()!) : []))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            return new Closure(int.Parse(framework.Groups[1].Value), int.Parse(framework.Groups[2].Value), packages, frameworks);
        }
    }

    internal static List<string> Problems(string notices, Closure closure)
    {
        var problems = new List<string>();

        foreach (var framework in closure.Frameworks)
        {
            var heading = new Regex($@"^Runtime pack: {Regex.Escape(framework)}\.Runtime\.[a-z0-9-]+ {closure.Major}\.{closure.Minor}\.\d+$", RegexOptions.Multiline).Match(notices);
            if (!heading.Success)
            {
                problems.Add($"No section for the runtime pack {framework}.Runtime of {closure.Major}.{closure.Minor}, which the executable carries.");
                continue;
            }

            var section = SectionAfter(notices, heading);
            if (!section.Contains("Permission is hereby granted", StringComparison.Ordinal) || !section.Contains("third-party", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"The section for the runtime pack {framework}.Runtime holds no licence text or no third-party notices.");
            }
        }

        var entries = Entry().Matches(notices)
            .Select(entry => (Name: entry.Groups["name"].Value, Version: entry.Groups["version"].Value, Licence: entry.Groups["licence"].Value))
            .ToList();
        var texts = LicenceText().Matches(notices).Select(text => text.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, version) in closure.Packages)
        {
            var entry = entries.FirstOrDefault(entry => entry.Name == name && entry.Version == version);
            if (entry.Name is null)
            {
                problems.Add($"{name} {version} has no entry in THIRD-PARTY-NOTICES.");
                continue;
            }

            foreach (var licence in Licences(entry.Licence).Where(licence => !texts.Contains(licence)))
            {
                problems.Add($"{licence}, which {name} {version} is used under, has no text in THIRD-PARTY-NOTICES.");
            }
        }

        return problems;
    }

    /// <summary>The problems, and what makes them go: the file is written, never edited by hand.</summary>
    internal static string Report(IReadOnlyList<string> problems) =>
        string.Join('\n', [.. problems, "Write the file again: pwsh .github/scripts/Write-ThirdPartyNotices.ps1"]);

    /// <summary>The licence identifiers of an SPDX expression: its operators and parentheses are not licences.</summary>
    private static IEnumerable<string> Licences(string expression) =>
        expression.Split([' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Where(token => token is not ("AND" or "OR" or "WITH"));

    /// <summary>From a runtime pack's heading to the next heading of the file, whichever it is.</summary>
    private static string SectionAfter(string notices, Match heading)
    {
        var start = heading.Index + heading.Length;
        var ends = new[] { notices.IndexOf("\nRuntime pack: ", start, StringComparison.Ordinal), notices.IndexOf(PackagesHeading, start, StringComparison.Ordinal) }
            .Where(at => at >= 0)
            .ToList();
        return notices[start..(ends.Count == 0 ? notices.Length : ends.Min())];
    }

    [GeneratedRegex(@"^net(\d+)\.(\d+)$")]
    private static partial Regex TargetFramework();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.-]+) (?<version>\d[^\s]*)\n  Licence: (?<licence>[^\n]+)\n  Copyright: [^\n]+$", RegexOptions.Multiline)]
    private static partial Regex Entry();

    [GeneratedRegex(@"^Licence text: (?<id>\S+)\n-+\n(?:Used by:\n(?:  [^\n]+\n)+)?\n[^\n]+", RegexOptions.Multiline)]
    private static partial Regex LicenceText();
}
