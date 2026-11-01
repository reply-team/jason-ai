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

    /// <summary>The root the test runtime was given: <c>Jason</c> in the home beside its data directory.</summary>
    private static string RootOf(RuntimeApiFixture api) => Path.Combine(TempDataDir.HomeOf(api.Paths.Root), "Jason");
}
