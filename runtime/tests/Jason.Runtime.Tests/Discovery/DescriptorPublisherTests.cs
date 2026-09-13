using System.Text.Json;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

public class DescriptorPublisherTests
{
    [Fact]
    public void Publishing_writes_a_restricted_snake_case_descriptor()
    {
        using var dir = new TempDataDir();
        var publisher = new DescriptorPublisher(dir.Paths);
        var info = RuntimeInfo.Create();

        var descriptor = publisher.Publish(info, new Uri("http://127.0.0.1:54321/"), "secret-token");

        Assert.True(File.Exists(dir.Paths.DescriptorFile));
        Assert.True(FilePermissions.IsRestrictedToCurrentUser(dir.Paths.DescriptorFile));
        var json = File.ReadAllText(dir.Paths.DescriptorFile);
        Assert.Contains("\"base_url\":\"http://127.0.0.1:54321\"", json, StringComparison.Ordinal);
        Assert.Contains("\"token\":\"secret-token\"", json, StringComparison.Ordinal);
        Assert.Contains("\"api_version\":\"v1\"", json, StringComparison.Ordinal);
        Assert.Equal(descriptor, JsonSerializer.Deserialize<RuntimeDescriptor>(json, JasonJson.Options));
        Assert.Equal(info.InstanceId, descriptor.InstanceId);
        Assert.Equal(info.Pid, descriptor.Pid);
    }

    [Fact]
    public void Publishing_again_replaces_the_descriptor_and_leaves_no_temporary_files()
    {
        using var dir = new TempDataDir();
        var publisher = new DescriptorPublisher(dir.Paths);
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first");

        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second");

        var json = File.ReadAllText(dir.Paths.DescriptorFile);
        Assert.Contains("\"token\":\"second\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("first", json, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFileSystemEntries(dir.Paths.RunDirectory));
    }

    [Fact]
    public void Remove_deletes_the_descriptor_and_tolerates_absence()
    {
        using var dir = new TempDataDir();
        var publisher = new DescriptorPublisher(dir.Paths);
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t");

        publisher.Remove();
        publisher.Remove();

        Assert.False(File.Exists(dir.Paths.DescriptorFile));
    }

    [Fact]
    public void The_run_directory_is_restricted_to_the_current_user()
    {
        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t");
        Assert.True(FilePermissions.IsRestrictedToCurrentUser(dir.Paths.RunDirectory));
    }
}
