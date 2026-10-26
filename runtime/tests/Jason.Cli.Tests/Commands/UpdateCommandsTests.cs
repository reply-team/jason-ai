using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Update;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>jason update check</c> asks the release feed itself and never the runtime: the question has to be
/// answerable on a machine whose runtime will not start, and the answer is the same rule the runtime's own
/// unattended check runs. The feed here is the one handler every CLI request goes through, dispatching on the
/// address — which is how a test sees both what was asked and what was not.
/// </summary>
public class UpdateCommandsTests
{
    /// <summary>
    /// The release after this build, worked out from the build's own version. A number written here stops being
    /// newer at the release it names: a pull request builds <c>-dev</c>, which is older than that release, but the
    /// release itself is built with the bare version, and its tests are the ones that must pass.
    /// </summary>
    private static readonly string Newer = new SemanticVersion(SemanticVersion.Current.Major, SemanticVersion.Current.Minor + 1, 0, null).ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The two verbs that change the files beside the executable say so in their own descriptions: an update puts the new
    /// executable in place with its licence files, and a rollback puts the old one back with its own.
    /// </summary>
    [Theory]
    [InlineData("apply", "Install the newest release: download it, stop this machine's runtime, put the new executable and its licence files in place, start it again, redeploy the skills this machine recorded at the new release's tag, and place the release's UI. If the skills or the UI cannot be placed the update still stands; the step's line says why, and 'jason status' names the repair.")]
    [InlineData("rollback", "Put the version this machine updated from back: the executable it replaced with its licence files, and - if that update migrated the database - the backup it wrote.")]
    public async Task The_update_verbs_say_that_the_licence_files_move_with_the_executable(string verb, string description)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["update", verb, "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Contains(description, System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "), StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.I2, "update is outside the verb map")]
    public async Task Check_prints_what_the_feed_says_and_never_calls_the_runtime()
    {
        using var dir = new TempPaths();

        // A runtime is right there to be asked, and is not.
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var asked = new List<Uri>();
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest(Newer))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.True(answer.Available);
        Assert.Equal(Newer, answer.Latest);
        Assert.Equal(SemanticVersion.Current.ToString(), answer.Current);
        Assert.Equal("https://example.test/notes", answer.ReleaseNotesUrl);
        Assert.Equal([UpdateFeed.Default], asked);

        // One line of compact snake_case JSON, like every other verb's answer.
        Assert.DoesNotContain('\n', stdout.ToString().TrimEnd());
        Assert.Contains("\"checked_at\":", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"release_notes_url\":", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The default feed is on the product's own address, which answers every request with a redirect to where the
    /// release is stored. So the client a real CLI sends through, with no handler of a test's own, follows it: the
    /// address is asked first, then the place it named, and the answer is the release found there.
    /// </summary>
    [Fact]
    public async Task The_check_follows_the_address_to_the_release_it_redirects_to()
    {
        using var dir = new TempPaths();
        using var tree = new TempTree();
        var store = tree.NewDirectory("store");
        File.WriteAllText(Path.Combine(store, ReleaseAssets.Manifest), Manifest(Newer));
        using var address = new LoopbackReleases(store);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["update", "check", "--feed", $"{address.Releases}/latest/download/{ReleaseAssets.Manifest}"],
            new CliEnvironment(output, error, dir.Paths),
            Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + error.ToString());
        Assert.Equal(Newer, JsonSerializer.Deserialize<UpdateCheckResponse>(output.ToString(), JasonJson.Options)!.Latest);
        Assert.Equal(
            ["GET /releases/latest/download/manifest.json", "GET /store/releases/latest/download/manifest.json"],
            address.Asked);
    }

    [Fact]
    public async Task A_version_no_newer_than_this_one_is_not_available()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(SemanticVersion.Current.ToString()))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.False(answer.Available);
        Assert.Equal(answer.Current, answer.Latest);
    }

    [Fact]
    public async Task A_feed_that_cannot_be_reached_is_an_error_with_a_code()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, new FakeHandler(_ => throw new HttpRequestException("connection refused")), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(stdout.ToString());
        Assert.Equal("update_feed_unreachable", error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task A_feed_that_answers_something_other_than_a_manifest_is_an_error_with_a_code()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok("this is not a manifest")), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(stdout.ToString());
        Assert.Equal("update_feed_invalid", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    [Critical(Family.I2, "no plain-http feed off loopback")]
    public async Task Another_feed_is_asked_when_named_and_refused_when_it_is_plain_http_on_a_network()
    {
        using var dir = new TempPaths();
        var asked = new List<Uri>();
        var local = new Uri("http://127.0.0.1:9/manifest.json");
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest(Newer)), local), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--feed", local.ToString()], env, Ct));
        Assert.Equal([local], asked);

        // Refused before anything is sent: the address the handler would have seen never arrives.
        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["update", "check", "--feed", "http://example.com/manifest.json"], env, Ct));
        Assert.Equal("update_feed_insecure", RuntimeVerbs.Envelope(stdout.ToString()).Code);
        Assert.Equal([local], asked);
    }

    [Fact]
    public async Task A_feed_that_is_not_an_address_is_a_usage_error()
    {
        using var dir = new TempPaths();
        var (env, _, stderr) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--feed", "not an address"], env, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--feed", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_in_a_sentence_that_something_newer_exists()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--human"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = stdout.ToString();
        Assert.Contains(Newer, text, StringComparison.Ordinal);
        Assert.Contains(SemanticVersion.Current.ToString(), text, StringComparison.Ordinal);
        Assert.Contains("available", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.test/notes", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_in_a_sentence_that_this_is_the_newest()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(SemanticVersion.Current.ToString()))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--human"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = stdout.ToString();
        Assert.Contains("up to date", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SemanticVersion.Current.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer names the command that updates this installation. On Jason's own that is <c>jason update apply</c>, and
    /// a person reading the sentence is told so.
    /// </summary>
    [Fact]
    public async Task Check_names_jason_update_apply_for_jasons_own_installation()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));
        Assert.Equal("jason update apply", (string?)JsonNode.Parse(stdout.ToString())?["upgrade_command"]);

        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], env, Ct));
        Assert.Contains("`jason update apply`", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Under a package manager the answer names the manager's own upgrade, as one command: the field is what an agent
    /// runs between stopping the runtime and starting it, which the operating skill says, not the field.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Check_names_the_managers_upgrade_as_one_command(string manager, string upgrade)
    {
        using var dir = new TempPaths();
        using var layouts = new ManagedLayouts();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], Managed(env, layouts, manager), Ct));

        Assert.Equal(upgrade, (string?)JsonNode.Parse(stdout.ToString())?["upgrade_command"]);
    }

    /// <summary>
    /// And a person is given the three lines to type, never one: the runtime stopped, the manager's upgrade, the runtime
    /// started. Never <c>jason update apply</c>, which refuses such an installation.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Human_mode_gives_the_managers_upgrade_as_three_lines(string manager, string upgrade)
    {
        using var dir = new TempPaths();
        using var layouts = new ManagedLayouts();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], Managed(env, layouts, manager), Ct));

        var text = stdout.ToString();
        Assert.Contains(string.Join(Environment.NewLine, "  jason runtime stop", $"  {upgrade}", "  jason runtime start"), text, StringComparison.Ordinal);
        Assert.Contains(manager, text, StringComparison.Ordinal);
        Assert.DoesNotContain("jason update apply", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Below the newest release's floor, what updates this installation is first the apply of the floor itself: the
    /// answer names that version as <c>install_first</c> and its apply as the one command, while the release stays
    /// available and the check still exits 0.
    /// </summary>
    [Fact]
    public async Task Below_the_release_s_floor_check_names_the_version_to_install_first()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, floor: "9.0.0"))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        var answer = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.True((bool?)answer["available"]);
        Assert.Equal("9.0.0", (string?)answer["install_first"]);
        Assert.Equal("jason update apply --version 9.0.0", (string?)answer["upgrade_command"]);
    }

    /// <summary>
    /// And a person reading the sentence is given the apply's own words: the line under it is the refusal
    /// <c>jason update apply</c> prints for the same release, taken from the applier rather than written here again.
    /// </summary>
    [Fact]
    public async Task Human_mode_below_the_floor_prints_the_refusal_the_apply_would()
    {
        using var installation = new FakeInstallation();
        installation.Release.Says(UpdateRefusalTests.Floored(installation, "9.0.0"));

        var exit = await CliApp.RunAsync(["update", "check", "--human", "--feed", installation.Feed.ToString()], installation.Env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var lines = installation.Out.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Equal($"A newer version is available: {installation.To} (this is {SemanticVersion.Current}).", lines[0]);

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct));
        Assert.Equal(refused.Message, lines[1]);
    }

    /// <summary>
    /// At the floor the release is one apply away, so nothing is named to install first: the field is there, and null.
    /// </summary>
    [Fact]
    public async Task At_the_release_s_floor_check_names_the_apply_alone()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, floor: SemanticVersion.Current.ToString()))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        var answer = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.True(answer.ContainsKey("install_first"), stdout.ToString());
        Assert.Null(answer["install_first"]);
        Assert.Equal("jason update apply", (string?)answer["upgrade_command"]);
    }

    /// <summary>
    /// A floor on a release that is not newer names nothing to install first: the release is not available, so the
    /// field is there, and null, and the command stays the plain apply.
    /// </summary>
    [Fact]
    public async Task A_floor_on_a_release_that_is_not_newer_names_nothing_to_install_first()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(SemanticVersion.Current.ToString(), floor: "9.0.0"))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        var answer = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.False((bool?)answer["available"]);
        Assert.True(answer.ContainsKey("install_first"), stdout.ToString());
        Assert.Null(answer["install_first"]);
        Assert.Equal("jason update apply", (string?)answer["upgrade_command"]);
    }

    /// <summary>
    /// A package manager replaces the file whole, with no applier of this build's in between, so a floor says nothing
    /// about it: the manager's own upgrade is the command, and nothing is named to install first.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Under_a_manager_the_floor_names_nothing_to_install_first(string manager, string upgrade)
    {
        using var dir = new TempPaths();
        using var layouts = new ManagedLayouts();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, floor: "9.0.0"))), new FakeProcessControl());
        var managed = Managed(env, layouts, manager);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], managed, Ct));
        var answer = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.True(answer.ContainsKey("install_first"), stdout.ToString());
        Assert.Null(answer["install_first"]);
        Assert.Equal(upgrade, (string?)answer["upgrade_command"]);

        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], managed, Ct));
        Assert.DoesNotContain("9.0.0", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("jason update apply", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A plugin package installed here that the newer release publishes a later version of is named: its id, the version
    /// installed, read from the release's record of each version's digest, and the version the release publishes. The
    /// package is read from the plugins directory, and still nothing but the feed is asked.
    /// </summary>
    [Fact]
    public async Task Check_names_a_plugin_package_the_newer_release_publishes_a_later_version_of()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var installed = Install(dir.Paths, "reply");
        var asked = new List<Uri>();
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest(Newer, plugins: Reply(("0.8.0", installed), ("0.9.0", Later))))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Equal([UpdateFeed.Default], asked);
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.Equal(new PluginUpdate("reply", "0.8.0", "0.9.0"), Assert.Single(answer.PluginUpdates!));
        Assert.Contains("\"plugin_updates\":[{\"id\":\"reply\",\"installed\":\"0.8.0\",\"latest\":\"0.9.0\"}]", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing installed here that the record holds is behind, so the list is there, and empty: the package installed is
    /// the one the release publishes, or one the release's record does not hold — a package the list does not know: built
    /// or edited on that machine, or a version before the list's first line, 0.3.2, which <c>jason status</c> judges by
    /// its version — or none is installed at all.
    /// </summary>
    [Theory]
    [InlineData("the package the release publishes")]
    [InlineData("a package edited here")]
    [InlineData("no plugins directory")]
    [InlineData("a directory with no manifest")]
    public async Task Check_names_no_plugin_package_when_nothing_installed_is_behind(string installed)
    {
        using var dir = new TempPaths();
        var plugins = installed switch
        {
            "the package the release publishes" => Reply(("0.8.0", Earlier), ("0.9.0", Install(dir.Paths, "reply"))),
            "a package edited here" => Reply(("0.8.0", Edited(Install(dir.Paths, "reply"), dir.Paths)), ("0.9.0", Later)),
            "no plugins directory" => Reply(("0.8.0", Earlier), ("0.9.0", Later)),
            _ => Reply(("0.8.0", Install(dir.Paths, "reply", manifest: false)), ("0.9.0", Later)),
        };
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, plugins: plugins))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        Assert.Contains("\"plugin_updates\":[]", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A release that is not newer, or that names no package, says nothing about plugins: the field is there, and null.
    /// Null and empty are two answers — no package named or nothing newer, and nothing installed here that the record
    /// holds behind.
    /// </summary>
    [Theory]
    [InlineData("not newer")]
    [InlineData("no package named")]
    public async Task Check_says_nothing_about_plugins_when_the_release_is_not_newer_or_names_no_package(string release)
    {
        using var dir = new TempPaths();
        var installed = Install(dir.Paths, "reply");
        var manifest = release == "not newer"
            ? Manifest(SemanticVersion.Current.ToString(), plugins: Reply(("0.8.0", installed), ("0.9.0", Later)))
            : Manifest(Newer);
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(manifest)), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        var answer = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.True(answer.ContainsKey("plugin_updates"), stdout.ToString());
        Assert.Null(answer["plugin_updates"]);
    }

    /// <summary>
    /// A package manager places no plugin, and its upgrade leaves the plugins directory as the apply does, so the package
    /// is named under one as well — and a person reads it after the three lines they type.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Under_a_manager_a_plugin_package_behind_is_named_after_the_three_lines(string manager, string upgrade)
    {
        using var dir = new TempPaths();
        using var layouts = new ManagedLayouts();
        var installed = Install(dir.Paths, "reply");
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, plugins: Reply(("0.8.0", installed), ("0.9.0", Later))))), new FakeProcessControl());
        var managed = Managed(env, layouts, manager);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], managed, Ct));
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.Equal(new PluginUpdate("reply", "0.8.0", "0.9.0"), Assert.Single(answer.PluginUpdates!));

        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], managed, Ct));
        var lines = stdout.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal(["  jason runtime stop", $"  {upgrade}", "  jason runtime start"], lines[^4..^1]);
        Assert.Equal($"Plugin package reply 0.8.0 is installed, and Jason {Newer} publishes reply 0.9.0: after the update, step 3 of the wiring check places it once the package there is moved away, then `jason plugin reload`.", lines[^1]);
    }

    /// <summary>
    /// A person is told in one line per package, after every line about Jason itself: below the release's floor, after the
    /// two applies the refusal names.
    /// </summary>
    [Fact]
    public async Task Human_mode_names_a_plugin_package_behind_after_the_lines_about_jason()
    {
        using var installation = new FakeInstallation();
        var installed = Install(installation.Paths, "reply");
        var floored = UpdateRefusalTests.Floored(installation, "9.0.0");
        installation.Release.Says(floored[..floored.LastIndexOf('}')] + ", \"plugins\": " + Reply(("0.8.0", installed), ("0.9.0", Later)) + "}");

        var exit = await CliApp.RunAsync(["update", "check", "--human", "--feed", installation.Feed.ToString()], installation.Env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var lines = installation.Out.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith($"{installation.To} cannot be applied to {SemanticVersion.Current} directly", lines[1], StringComparison.Ordinal);
        Assert.Equal($"Plugin package reply 0.8.0 is installed, and Jason {installation.To} publishes reply 0.9.0: after the update, step 3 of the wiring check places it once the package there is moved away, then `jason plugin reload`.", lines[2]);
    }

    /// <summary>
    /// A package too large to be a plugin is not read, and the check answers as if none were installed: what is wrong on
    /// the installed side is <c>jason status</c>'s to say, and nothing reaches stderr.
    /// </summary>
    [Fact]
    public async Task A_package_too_large_to_read_is_passed_over_in_silence()
    {
        using var dir = new TempPaths();
        var installed = Install(dir.Paths, "reply");
        for (var file = 0; file < PackageDigest.MaxFiles; file++)
        {
            File.WriteAllText(Path.Combine(dir.Paths.PluginPackageDirectory("reply"), $"extra-{file}.js"), string.Empty);
        }

        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, plugins: Reply(("0.8.0", installed), ("0.9.0", Later))))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));

        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("\"plugin_updates\":[]", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A package with a file that cannot be read while the check runs is passed over in silence, as one too large is. The
    /// file is held open with no sharing: Windows refuses every other open of it, and .NET on Linux and macOS refuses one
    /// through the advisory lock it takes for that sharing. The package is one the release calls behind once it is read.
    /// </summary>
    [Fact]
    public async Task A_package_that_cannot_be_read_is_passed_over_in_silence()
    {
        using var dir = new TempPaths();
        var installed = Install(dir.Paths, "reply");
        var main = Path.Combine(dir.Paths.PluginPackageDirectory("reply"), "main.js");
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer, plugins: Reply(("0.8.0", installed), ("0.9.0", Later))))), new FakeProcessControl());

        using (new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => File.OpenRead(main).Dispose());
            Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));
        }

        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("\"plugin_updates\":[]", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// When nothing installed here that the record holds is behind, a person reads the lines about Jason and nothing else:
    /// the words a release that names no package gets.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_only_the_lines_about_jason_when_no_package_is_behind()
    {
        using var dir = new TempPaths();
        var release = Manifest(Newer, plugins: Reply(("0.8.0", Earlier), ("0.9.0", Install(dir.Paths, "reply"))));
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(release)), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));
        Assert.Contains("\"plugin_updates\":[]", stdout.ToString(), StringComparison.Ordinal);

        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], env, Ct));
        var none = stdout.ToString();

        release = Manifest(Newer);
        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], env, Ct));

        Assert.Equal(stdout.ToString(), none);
    }

    private static readonly string Earlier = "sha256:" + new string('a', 64);

    private static readonly string Later = "sha256:" + new string('b', 64);

    /// <summary>
    /// A package installed in <paramref name="paths"/>' plugins directory, with its manifest unless told otherwise, and the
    /// digest the runtime computes for it.
    /// </summary>
    private static string Install(JasonPaths paths, string id, bool manifest = true)
    {
        var directory = paths.PluginPackageDirectory(id);
        Directory.CreateDirectory(directory);
        if (manifest)
        {
            File.WriteAllText(Path.Combine(directory, "plugin.yaml"), $"id: {id}\nversion: 0.8.0\n");
        }

        File.WriteAllText(Path.Combine(directory, "main.js"), "export default {};\n");
        return PackageDigest.Compute(directory).Digest;
    }

    /// <summary>The digest a package had before somebody edited a file of it here.</summary>
    private static string Edited(string digest, JasonPaths paths)
    {
        File.AppendAllText(Path.Combine(paths.PluginPackageDirectory("reply"), "main.js"), "// edited here\n");
        return digest;
    }

    /// <summary>The release's Reply package, 0.9.0, and its record of each version's digest.</summary>
    private static string Reply(params (string Version, string Digest)[] recorded) =>
        "{\"reply\":{\"version\":\"0.9.0\",\"asset\":\"jason-plugin-reply.zip\",\"digest\":\""
        + recorded.Single(line => line.Version == "0.9.0").Digest
        + "\",\"recorded\":{"
        + string.Join(",", recorded.Select(line => $"\"{line.Version}\":\"{line.Digest}\""))
        + "}}}";

    /// <summary>The environment, naming a planted installation that <paramref name="manager"/> owns, with its record.</summary>
    private static CliEnvironment Managed(CliEnvironment env, ManagedLayouts layouts, string manager)
    {
        if (manager == "Homebrew")
        {
            return env with { InstallPath = layouts.Homebrew().Executable, Managers = new PlantedRecords() };
        }

        var winget = layouts.Winget();
        return env with { InstallPath = winget.Executable, Managers = new PlantedRecords(winget.Registration()) };
    }

    /// <summary>
    /// A handler that answers the feed and fails the test for anything else, the runtime included: an
    /// exception nothing in the verb catches reaches the top of the CLI as a non-zero exit and a line on stderr.
    /// </summary>
    private static HttpMessageHandler FeedOnly(List<Uri> asked, Func<HttpResponseMessage> answer, Uri? feed = null) =>
        new FakeHandler(request =>
        {
            if (request.RequestUri != (feed ?? UpdateFeed.Default))
            {
                throw new InvalidOperationException($"nothing but the feed may be asked here, and {request.RequestUri} was");
            }

            asked.Add(request.RequestUri!);
            return answer();
        });

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Manifest(string version, string? floor = null, string? plugins = null) =>
        $$$"""
        {"schema":1,"version":"{{{version}}}","published_at":"2026-09-19T08:00:00Z",
         "artifacts":{"win-x64":{"asset":"jason-win-x64.zip","sha256":"{{{new string('a', 64)}}}","size":1}},
         "release_notes_url":"https://example.test/notes",
         "min_upgrade_from":{{{(floor is null ? "null" : $"\"{floor}\"")}}},
         "plugins":{{{plugins ?? "null"}}}}
        """;
}
