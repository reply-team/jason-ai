using Jason.Contracts.Programs;

namespace Jason.Contracts.Tests.Programs;

/// <summary>
/// Where Cursor CLI's program is on a Windows machine: the word on the search path is a launcher script, a script is
/// never started through a shell, so the program is found by the rule the launcher script itself uses. Read by both
/// halves, the runtime that launches the host for a role and the CLI that starts it for a person.
/// </summary>
public class CursorCliLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public CursorCliLauncherTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }


    [Theory]
    [InlineData("agent.cmd")]
    [InlineData("cursor-agent.cmd")]
    [InlineData("AGENT.CMD")]
    [InlineData("Cursor-Agent.Cmd")]
    public void The_two_launcher_names_the_host_installs_are_its_own(string fileName)
    {
        Assert.True(new CursorCliLauncher(windows: true).NamesItsLauncher(fileName));
    }

    [Theory]
    [InlineData("agent.ps1")]
    [InlineData("claude.cmd")]
    [InlineData("agent.cmd.exe")]
    [InlineData("promote-staged.cmd")]
    [InlineData("agent")]
    [InlineData("")]
    public void No_other_script_is_the_hosts_launcher(string fileName)
    {
        Assert.False(new CursorCliLauncher(windows: true).NamesItsLauncher(fileName));
    }

    [Fact]
    public void Off_Windows_no_file_is_a_launcher()
    {
        Assert.False(new CursorCliLauncher(windows: false).NamesItsLauncher("agent.cmd"));
    }

    [Fact]
    public void The_newest_version_directory_by_its_date_is_the_one_the_launcher_starts()
    {
        var root = InstallRoot("2025.12.31-aaaaaaa", "2026.9.5-bbbbbbb", "2026.10.22-27dcf5f");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(
            [Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), Path.Combine(root, "versions", "2026.10.22-27dcf5f", "index.js")],
            layout);
    }

    [Fact]
    public void A_date_is_compared_as_a_number_so_within_one_year_a_month_written_with_one_digit_is_older_than_one_written_with_two()
    {
        var root = InstallRoot("2026.9.5-bbbbbbb", "2026.10.1-ccccccc");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "cursor-agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.1-ccccccc", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_staged_or_previous_directory_and_a_name_that_is_no_version_are_never_chosen()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.10.23-ccccccc.new", "2026.10.24.previous", "2026.10.25", "latest");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), layout![0]);
    }

    /// <summary>
    /// The launcher script matches a name by letters and digits of its own alphabet, where a digit of another script would
    /// read as a digit to a looser rule and then fail to parse as a number: such a name is no version.
    /// </summary>
    [Fact]
    public void A_directory_named_with_digits_of_another_script_is_no_version()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "٢٠٢٦.10.23-ccccccc");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_hash_in_capital_letters_is_still_a_version_as_the_launcher_script_reads_it()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.11.01-ABC");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.11.01-ABC", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_launcher_that_is_not_there_has_no_layout_though_its_script_and_versions_are()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.Delete(Path.Combine(root, "agent.cmd"));

        Assert.Null(new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd")));
        Assert.NotNull(new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "cursor-agent.cmd")));
    }

    [Fact]
    public void A_version_with_a_time_in_its_name_is_one_the_launcher_reads()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.10.23-11-22-33-ddddddd");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.23-11-22-33-ddddddd", "node.exe"), layout![0]);
    }

    [Fact]
    public void Two_versions_of_one_day_are_ordered_by_their_names_so_the_answer_does_not_depend_on_the_listing()
    {
        var root = InstallRoot("2026.10.22-aaaaaaa", "2026.10.22-fffffff", "2026.10.22-bbbbbbb");

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-fffffff", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_node_and_an_entry_script_beside_the_launcher_are_what_the_launcher_starts_first()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.WriteAllText(Path.Combine(root, "node.exe"), string.Empty);
        File.WriteAllText(Path.Combine(root, "index.js"), string.Empty);

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal([Path.Combine(root, "node.exe"), Path.Combine(root, "index.js")], layout);
    }

    [Fact]
    public void A_node_beside_the_launcher_without_an_entry_script_is_not_the_flat_form()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.WriteAllText(Path.Combine(root, "node.exe"), string.Empty);

        var layout = new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), layout![0]);
    }

    [Fact]
    public void The_newest_directory_is_the_only_one_tried_and_without_an_entry_script_there_is_no_answer()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.10.23-ccccccc");
        File.Delete(Path.Combine(root, "versions", "2026.10.23-ccccccc", "index.js"));

        Assert.Null(new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd")));
    }

    [Fact]
    public void Without_the_launchers_own_script_beside_it_there_is_no_answer()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.Delete(Path.Combine(root, "cursor-agent.ps1"));

        Assert.Null(new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd")));
    }

    [Fact]
    public void A_script_the_host_did_not_install_has_no_layout()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.WriteAllText(Path.Combine(root, "claude.cmd"), string.Empty);

        Assert.Null(new CursorCliLauncher(windows: true).ProgramLayout(Path.Combine(root, "claude.cmd")));
    }

    [Fact]
    public void A_version_installed_after_the_first_answer_is_the_answer_the_next_time()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        var host = new CursorCliLauncher(windows: true);
        var shim = Path.Combine(root, "agent.cmd");
        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), host.ProgramLayout(shim)![0]);

        AddVersion(root, "2026.11.3-eeeeeee");

        Assert.Equal(Path.Combine(root, "versions", "2026.11.3-eeeeeee", "node.exe"), host.ProgramLayout(shim)![0]);
    }

    [Fact]
    public void Off_Windows_there_is_no_layout()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");

        Assert.Null(new CursorCliLauncher(windows: false).ProgramLayout(Path.Combine(root, "agent.cmd")));
    }

    /// <summary>An install root as the Windows installer makes one: both launchers, the script they run, and versions.</summary>
    private string InstallRoot(params string[] versions)
    {
        var root = Path.Combine(_directory, "cursor-agent");
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        foreach (var file in new[] { "agent.cmd", "cursor-agent.cmd", "cursor-agent.ps1" })
        {
            File.WriteAllText(Path.Combine(root, file), string.Empty);
        }

        foreach (var version in versions)
        {
            AddVersion(root, version);
        }

        return root;
    }

    private static void AddVersion(string root, string version)
    {
        var directory = Path.Combine(root, "versions", version);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "node.exe"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "index.js"), string.Empty);
    }
}
