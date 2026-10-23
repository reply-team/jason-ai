using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Harness;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The harness root's one setting: where the visible folder is, absent meaning <c>Jason</c> in the home directory, and the
/// places it may never be — the home directory itself, the data directory, anywhere inside it, and anywhere a relative path
/// would make it depend on where the runtime was started.
/// </summary>
public class HarnessOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A home, a data directory beside it and a folder of the person's own, none of them ever made.</summary>
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    private static readonly string Home = Path.Combine(Base, "home");

    private static readonly JasonPaths Data = new(Path.Combine(Home, ".jason"));

    [Fact]
    public void Absent_the_root_is_Jason_in_the_home_directory()
    {
        var options = new HarnessOptions();

        Assert.Null(options.Root);
        Assert.Equal(Path.Combine(Home, "Jason"), HarnessRoot.Of(options, Home));
        Assert.True(new HarnessOptionsValidator(Data, Home).Validate(null, options).Succeeded);
    }

    /// <summary>Nothing written, or only white space, is the default, as a setting left out is.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_root_written_as_nothing_is_the_default(string written)
    {
        var options = new HarnessOptions { Root = written };

        Assert.Equal(Path.Combine(Home, "Jason"), HarnessRoot.Of(options, Home));
        Assert.True(new HarnessOptionsValidator(Data, Home).Validate(null, options).Succeeded);
    }

    [Fact]
    public void A_full_path_of_the_person_s_choosing_is_the_root_as_the_platform_spells_it()
    {
        var chosen = Path.Combine(Base, "clients", "Jason") + Path.DirectorySeparatorChar;
        var options = new HarnessOptions { Root = chosen };

        Assert.Equal(Path.Combine(Base, "clients", "Jason"), HarnessRoot.Of(options, Home));
        Assert.True(new HarnessOptionsValidator(Data, Home).Validate(null, options).Succeeded);
    }

    /// <summary>
    /// A relative path would name wherever the runtime happened to start, and no existing setting has a rule for one, so it
    /// is refused; <c>~</c> is a shell's word and is not expanded. The sentence describes what was written and never says
    /// it back.
    /// </summary>
    [Theory]
    [InlineData("Clients")]
    [InlineData("~/Jason")]
    [InlineData("./Jason")]
    public void A_relative_path_is_refused_by_its_rule(string written)
    {
        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = written });

        Assert.True(result.Failed, $"'{written}' was accepted as a root.");
        Assert.Equal(
            [$"Harness:Root must be an absolute path, one that starts at a drive, a share or /, and ~ is not expanded; the value written is {written.Length} characters long and is not one."],
            result.Failures);
    }

    [Fact]
    public void The_home_directory_itself_is_refused()
    {
        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Home + Path.DirectorySeparatorChar });

        Assert.True(result.Failed, "the home directory was accepted as a root.");
        Assert.Equal(
            ["Harness:Root must not be the home directory itself: every agent session started anywhere in it would read the memory files kept at the root; the value written is the home directory."],
            result.Failures);
    }

    [Fact]
    public void The_data_directory_is_refused()
    {
        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Data.Root });

        Assert.True(result.Failed, "the data directory was accepted as a root.");
        Assert.Equal(
            ["Harness:Root must not be the data directory, which is the runtime's own; the value written is the data directory."],
            result.Failures);
    }

    [Fact]
    public void A_folder_inside_the_data_directory_is_refused()
    {
        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Path.Combine(Data.Root, "harness", "Jason") });

        Assert.True(result.Failed, "a folder inside the data directory was accepted as a root.");
        Assert.Equal(
            ["Harness:Root must not be inside the data directory, which is the runtime's own; the value written is inside it."],
            result.Failures);
    }

    /// <summary>
    /// A <c>CLAUDE.md</c> at the root is read by every agent session started anywhere below it, so the root is never the
    /// top of a drive or of the file system, nor a folder that holds the home directory.
    /// </summary>
    [Fact]
    public void The_top_of_a_drive_or_of_the_file_system_is_refused()
    {
        var top = Path.GetPathRoot(Base)!;

        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = top });

        Assert.True(result.Failed, $"'{top}' was accepted as a root.");
        Assert.Equal(
            ["Harness:Root must not be the top of a drive, a share or the file system: every agent session started anywhere below it would read the memory files kept at the root; the value written is such a top."],
            result.Failures);
    }

    [Fact]
    public void A_folder_that_holds_the_home_directory_is_refused()
    {
        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Base });

        Assert.True(result.Failed, "a folder holding the home directory was accepted as a root.");
        Assert.Equal(
            ["Harness:Root must not hold the home directory: every agent session started anywhere in the home would read the memory files kept at the root; the value written holds it."],
            result.Failures);
    }

    [Fact]
    public void A_folder_that_holds_the_data_directory_is_refused()
    {
        var elsewhere = Path.Combine(Base, "elsewhere");
        var data = new JasonPaths(Path.Combine(elsewhere, "data"));

        var result = new HarnessOptionsValidator(data, Home).Validate(null, new HarnessOptions { Root = elsewhere });

        Assert.True(result.Failed, "a folder holding the data directory was accepted as a root.");
        Assert.Equal(
            ["Harness:Root must not hold the data directory, which is the runtime's own; the value written holds it."],
            result.Failures);
    }

    [Fact]
    public void A_default_that_holds_the_data_directory_is_refused_saying_what_to_set()
    {
        var data = new JasonPaths(Path.Combine(Home, "Jason", "data"));

        var result = new HarnessOptionsValidator(data, Home).Validate(null, new HarnessOptions());

        Assert.True(result.Failed, "a default root holding the data directory was accepted.");
        Assert.Equal(
            ["Harness:Root is not set, and its default, Jason in the home directory, holds the data directory, which is the runtime's own; set Harness:Root to an absolute path apart from it."],
            result.Failures);
    }

    /// <summary>A folder whose name begins with the data directory's is beside it, not inside it.</summary>
    [Fact]
    public void A_folder_beside_the_data_directory_whose_name_begins_with_it_is_accepted()
    {
        var options = new HarnessOptions { Root = Data.Root + "-harness" };

        Assert.True(new HarnessOptionsValidator(Data, Home).Validate(null, options).Succeeded, $"'{options.Root}' was refused as a root.");
    }

    /// <summary>Paths are compared as the platform's file systems compare them by default: Windows and macOS ignore case, Linux does not.</summary>
    [Fact]
    public void The_data_directory_spelled_in_another_case_is_the_data_directory_where_the_platform_ignores_case()
    {
        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Data.Root.ToUpperInvariant() });

        Assert.True(
            result.Failed == IgnoresCase,
            $"the data directory in upper case was {(result.Failed ? "refused" : "accepted")} on {Environment.OSVersion.Platform}.");
    }

    [Fact]
    public void The_home_directory_spelled_in_another_case_is_the_home_directory_where_the_platform_ignores_case()
    {
        string[] expected = IgnoresCase ? [HomeRefused] : [];

        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Home.ToUpperInvariant() });

        Assert.Equal(expected, result.Failures ?? []);
    }

    /// <summary>
    /// The refusals compare where a path leads, not how it is spelled: a prefix that turns off Windows' own reading of a
    /// path names the folder behind it, so the home, a folder holding it and the data directory are refused in that
    /// spelling too.
    /// </summary>
    [Theory]
    [InlineData(@"\\?\", "home")]
    [InlineData(@"\\.\", "home")]
    [InlineData(@"\\?\", "holds the home")]
    [InlineData(@"\\?\", "data")]
    public void A_place_refused_in_its_own_spelling_is_refused_behind_a_device_prefix(string prefix, string place)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The prefixes are Windows' own; elsewhere such a value is a relative path, refused as one.");
        var (path, refusal) = place switch
        {
            "home" => (Home, HomeRefused),
            "holds the home" => (Base, HoldsHomeRefused),
            _ => (Data.Root, DataRefused),
        };

        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = prefix + path });

        Assert.Equal([refusal], result.Failures ?? []);
    }

    /// <summary>A device path that names no drive and no share cannot be compared with the home or the data directory, and is not the absolute path the rule asks for.</summary>
    [Fact]
    public void A_device_path_that_names_no_drive_or_share_is_refused_by_the_absolute_path_rule()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Device paths are Windows' own.");
        const string Volume = @"\\?\Volume{00000000-0000-0000-0000-000000000000}\Jason";

        var result = new HarnessOptionsValidator(Data, Home).Validate(null, new HarnessOptions { Root = Volume });

        Assert.Equal(
            [$"Harness:Root must be an absolute path, one that starts at a drive, a share or /, and ~ is not expanded; the value written is {Volume.Length} characters long and is not one."],
            result.Failures ?? []);
    }

    /// <summary>
    /// And through a link — a junction on Windows, which any account may make, a symbolic link elsewhere — the root is
    /// where the link leads: a link to the home is the home, one to a folder holding it holds it, one to the data
    /// directory is the data directory, and a folder below a link to the data directory is inside it.
    /// </summary>
    [Theory]
    [InlineData("home", "")]
    [InlineData("base", "")]
    [InlineData("data", "")]
    [InlineData("data", "harness")]
    public void A_root_through_a_link_is_where_the_link_leads(string target, string below)
    {
        using var tree = new TempTree();
        var home = Path.Combine(tree.Root, "base", "home");
        var data = new JasonPaths(Path.Combine(home, ".jason"));
        Directory.CreateDirectory(data.Root);
        var (to, refusal) = target switch
        {
            "home" => (home, HomeRefused),
            "base" => (Path.GetDirectoryName(home)!, HoldsHomeRefused),
            _ => (data.Root, below.Length == 0 ? DataRefused : InsideDataRefused),
        };
        var link = Path.Combine(tree.Root, "link");
        Link(link, to);

        var result = new HarnessOptionsValidator(data, home).Validate(null, new HarnessOptions { Root = Path.Combine(link, below) });

        Assert.Equal([refusal], result.Failures ?? []);
    }

    /// <summary>
    /// The default is held to the same rule: a data directory named <c>Jason</c> in the home directory is where the default
    /// would be, and the sentence says the setting is not written and what to write.
    /// </summary>
    [Fact]
    public void A_default_that_lands_on_the_data_directory_is_refused_saying_what_to_set()
    {
        var data = new JasonPaths(Path.Combine(Home, "Jason"));

        var result = new HarnessOptionsValidator(data, Home).Validate(null, new HarnessOptions());

        Assert.True(result.Failed, "a default root that is the data directory was accepted.");
        Assert.Equal(
            ["Harness:Root is not set, and its default, Jason in the home directory, is the data directory, which is the runtime's own; set Harness:Root to an absolute path apart from it."],
            result.Failures);
    }

    [Fact]
    public void A_default_that_lands_inside_the_data_directory_is_refused_saying_what_to_set()
    {
        var data = new JasonPaths(Home);

        var result = new HarnessOptionsValidator(data, Home).Validate(null, new HarnessOptions());

        Assert.True(result.Failed, "a default root inside the data directory was accepted.");
        Assert.Equal(
            ["Harness:Root is not set, and its default, Jason in the home directory, is inside the data directory, which is the runtime's own; set Harness:Root to an absolute path apart from it."],
            result.Failures);
    }

    /// <summary>The section is bound and validated with the others: a refused root stops the start, naming the setting and never its value.</summary>
    [Fact]
    [Critical(Family.C1, "a refused harness root stops the start by its path, never its value")]
    public async Task A_refused_root_stops_the_start_naming_the_setting_and_not_the_value()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        var inside = Path.Combine(dir.Paths.Root, "SENTINEL_ROOT");
        File.WriteAllText(dir.Paths.UserSettingsFile, new JsonObject
        {
            ["Dispatcher"] = new JsonObject { ["Enabled"] = false },
            ["Harness"] = new JsonObject { ["Root"] = inside },
        }.ToJsonString());

        var refused = await Assert.ThrowsAsync<InvalidSettingsException>(async () =>
        {
            var started = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
            await started.DisposeAsync();
        });

        Assert.Equal(
            "Invalid Jason configuration: Harness:Root must not be inside the data directory, which is the runtime's own; the value written is inside it.",
            refused.Message);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The root is read through its seam at each create and move, so an edit applies to the next one; an edit the validator
    /// refuses leaves the last root that validated in force, and the log names the section and the setting.
    /// </summary>
    [Fact]
    public async Task A_refused_live_edit_keeps_the_last_good_root()
    {
        var first = Path.Combine(Base, "first", "Jason");
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, Settings(first)));
        await using (fixture)
        {
            var harness = fixture.Resolve<LiveSettings<HarnessOptions>>();
            Assert.Equal(first, harness.Current.Root);

            File.WriteAllText(fixture.Paths.UserSettingsFile, Settings("SENTINEL_RELATIVE"));
            Assert.True(await TestOptions.RefusedAsync(harness, Ct));

            Assert.Equal(first, harness.Current.Root);
            await fixture.Runtime.StopAsync();

            var log = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText));
            var refused = log
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == HarnessOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal(
                ["Harness:Root must be an absolute path, one that starts at a drive, a share or /, and ~ is not expanded; the value written is 17 characters long and is not one."],
                refused);
            Assert.DoesNotContain("SENTINEL", log, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_root_written_as_an_object_is_refused_by_its_path_and_its_type()
    {
        using var provider = Provider("""{"Harness":{"Root":{"Path":"SENTINEL_OBJECT"}}}""");

        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<HarnessOptions>>().CurrentValue);

        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
        Assert.Equal(["Harness:Root could not be read as System.String."], refused.Failures);
    }

    [Fact]
    public void The_vocabulary_names_the_section_and_its_setting()
    {
        Assert.Contains("Harness", SettingsVocabulary.Names);
        Assert.Contains("Root", SettingsVocabulary.Names);
    }

    private const string HomeRefused =
        "Harness:Root must not be the home directory itself: every agent session started anywhere in it would read the memory files kept at the root; the value written is the home directory.";

    private const string HoldsHomeRefused =
        "Harness:Root must not hold the home directory: every agent session started anywhere in the home would read the memory files kept at the root; the value written holds it.";

    private const string DataRefused = "Harness:Root must not be the data directory, which is the runtime's own; the value written is the data directory.";

    private const string InsideDataRefused = "Harness:Root must not be inside the data directory, which is the runtime's own; the value written is inside it.";

    /// <summary>Whether this platform's file systems tell two names apart by case only by default: Windows' and macOS' do not.</summary>
    private static bool IgnoresCase => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>A link at <paramref name="link"/> to the directory <paramref name="target"/>, of the kind this account may make.</summary>
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        // A junction: a symbolic link needs a privilege an ordinary Windows account does not hold, and a junction does not.
        // Started through the door, as every child is, so no console window opens on the desktop.
        var start = ChildProcess.StartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ChildKind.SystemTool);
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = ChildProcess.Start(start)!;
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), $"mklink did not end within 30 seconds making '{link}'.");
        Assert.True(Directory.Exists(link), $"the junction at '{link}' was not made: {process.StandardError.ReadToEnd()}");
    }

    private static string Settings(string root) => new JsonObject
    {
        ["Dispatcher"] = new JsonObject { ["Enabled"] = false },
        ["Harness"] = new JsonObject { ["Root"] = root },
    }.ToJsonString();

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build(),
            Data,
            Home);
        return services.BuildServiceProvider();
    }
}
