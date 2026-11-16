using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using Jason.Contracts.Api;
using Jason.Contracts.Harness;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// <c>system.info</c> says the harness root in force and what stands there, as the next project or campaign will meet
/// it, so a status line shows the root the runtime uses rather than working one out; asking makes and writes nothing.
/// </summary>
public sealed class HarnessRootReportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("absent", HarnessRootState.Absent)]
    [InlineData("marked", HarnessRootState.Marked)]
    [InlineData("unmarked", HarnessRootState.Unmarked)]
    [InlineData("taken", HarnessRootState.Taken)]
    [InlineData("a-file", HarnessRootState.NotAFolder)]
    public async Task The_runtime_reports_the_root_in_force_and_what_stands_there(string standing, HarnessRootState expected)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var root = Path.Combine(TempDataDir.HomeOf(api.Paths.Root), "Jason");
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        switch (standing)
        {
            case "marked":
                await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
                break;
            case "unmarked":
                Directory.CreateDirectory(root);
                break;
            case "taken":
                Directory.CreateDirectory(Path.Combine(root, HarnessMarker.Directory));
                File.WriteAllText(HarnessMarker.PathIn(root), """{"marker_version":1,"kind":"project","id":"prj_01K7QN000000000000000GHOST"}""");
                break;
            case "a-file":
                File.WriteAllText(root, "in the way");
                break;
        }

        var before = Directory.GetFileSystemEntries(Path.GetDirectoryName(root)!, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);

        Assert.Equal(new HarnessInfo(root, expected), info.Harness);
        Assert.Equal(before, Directory.GetFileSystemEntries(Path.GetDirectoryName(root)!, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A root whose marker cannot be read — another program holds it open with no sharing — is unreadable, and the next
    /// project is refused there as the report says.
    /// </summary>
    [Fact]
    public async Task A_root_whose_marker_cannot_be_read_is_reported_unreadable_and_refuses_the_next_create()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var root = RootOf(api);

        SystemInfoResponse info;
        ErrorBody refused;
        using (new FileStream(HarnessMarker.PathIn(root), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);
            refused = await api.PostErrorAsync(Operations.ProjectCreate, new { name = "Beta" }, HttpStatusCode.Conflict, Ct);
        }

        Assert.Equal(new HarnessInfo(root, HarnessRootState.Unreadable), info.Harness);
        Assert.Equal("harness_folder_unavailable", refused.Code);
    }

    /// <summary>
    /// A root this account may not list is unreadable, never unmarked: a marker looked for in a folder that cannot be
    /// listed is not found, and the next project, which cannot write its marker there either, is refused as the report says.
    /// </summary>
    [Fact]
    public async Task A_root_this_account_cannot_list_is_reported_unreadable_and_refuses_the_next_create()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The folder is closed to this account by an access rule only Windows is given here.");
            return;
        }

        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var root = RootOf(api);
        var folder = Directory.CreateDirectory(root);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory | FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
        try
        {
            var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);
            var refused = await api.PostErrorAsync(Operations.ProjectCreate, new { name = "Acme Corp" }, HttpStatusCode.Conflict, Ct);

            Assert.Equal(new HarnessInfo(root, HarnessRootState.Unreadable), info.Harness);
            Assert.Equal("harness_folder_unavailable", refused.Code);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            folder.SetAccessControl(security);
        }
    }

    /// <summary>
    /// A runtime whose settings name a harness root keeps its folders there: it reports that root, makes the project's
    /// folder under it, and makes nothing at the root a runtime on the same home would default to.
    /// </summary>
    [Fact]
    public async Task A_runtime_keeps_its_folders_under_the_root_its_settings_name_and_never_at_the_default()
    {
        string? named = null;
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            named = Path.Combine(TempDataDir.HomeOf(paths.Root), "Jason-elsewhere");
            File.WriteAllText(paths.UserSettingsFile, new System.Text.Json.Nodes.JsonObject
            {
                ["Dispatcher"] = new System.Text.Json.Nodes.JsonObject { ["Enabled"] = false },
                ["Harness"] = new System.Text.Json.Nodes.JsonObject { ["Root"] = named },
            }.ToJsonString());
        });
        var before = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);
        Assert.Equal(new HarnessInfo(named!, HarnessRootState.Absent), before.Harness);

        var project = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);

        var relative = Path.GetRelativePath(named!, project.FolderPath!);
        Assert.False(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative), $"{project.FolderPath} is under {named}.");
        var after = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);
        Assert.Equal(new HarnessInfo(named!, HarnessRootState.Marked), after.Harness);
        Assert.False(Directory.Exists(RootOf(api)), "Nothing is made at the default harness root.");
    }

    /// <summary>The root the test runtime was given: <c>Jason</c> in the home beside its data directory.</summary>
    private static string RootOf(RuntimeApiFixture api) => Path.Combine(TempDataDir.HomeOf(api.Paths.Root), "Jason");
}
