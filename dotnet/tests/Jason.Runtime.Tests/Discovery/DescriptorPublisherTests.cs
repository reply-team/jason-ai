using System.Text.Json;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

public class DescriptorPublisherTests
{
    [Fact]
    [Critical(Family.S5, "the descriptor is restricted")]
    public void Publishing_writes_a_restricted_snake_case_descriptor()
    {
        using var dir = new TempDataDir();
        var publisher = new DescriptorPublisher(dir.Paths);
        var info = RuntimeInfo.Create();

        var descriptor = publisher.Publish(info, new Uri("http://127.0.0.1:54321/"), "secret-token", TestContext.Current.CancellationToken);

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
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);

        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", TestContext.Current.CancellationToken);

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
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", TestContext.Current.CancellationToken);

        publisher.Remove();
        publisher.Remove();

        Assert.False(File.Exists(dir.Paths.DescriptorFile));
    }

    [Fact]
    [Critical(Family.S5, "the run directory ACL")]
    public void The_run_directory_is_restricted_to_the_current_user()
    {
        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", TestContext.Current.CancellationToken);
        Assert.True(FilePermissions.IsRestrictedToCurrentUser(dir.Paths.RunDirectory));
    }

    [Fact]
    public void A_single_file_image_is_written_as_the_installed_image()
    {
        using var dir = new TempDataDir();
        var image = Path.Combine(dir.Paths.Root, "bin", OperatingSystem.IsWindows() ? "jason.exe" : "jason");
        var publisher = new DescriptorPublisher(dir.Paths) { InstalledImage = () => image };

        var descriptor = publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", TestContext.Current.CancellationToken);

        Assert.Equal(image, descriptor.InstalledImage);
        var json = File.ReadAllText(dir.Paths.DescriptorFile);
        Assert.Contains($"\"installed_image\":{JsonSerializer.Serialize(image)}", json, StringComparison.Ordinal);
        Assert.Equal(descriptor, JsonSerializer.Deserialize<RuntimeDescriptor>(json, JasonJson.Options));
    }

    [Fact]
    public void A_runtime_that_is_no_single_file_image_writes_no_installed_image()
    {
        using var dir = new TempDataDir();
        var publisher = new DescriptorPublisher(dir.Paths) { InstalledImage = () => null };

        var descriptor = publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", TestContext.Current.CancellationToken);

        Assert.Null(descriptor.InstalledImage);
        var json = File.ReadAllText(dir.Paths.DescriptorFile);
        Assert.Contains("\"installed_image\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void By_default_the_test_host_which_is_no_single_file_image_writes_no_installed_image()
    {
        using var dir = new TempDataDir();

        var descriptor = new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "t", TestContext.Current.CancellationToken);

        // What this holds is the default's answer in a test only: a test host is no single-file image, so it is none.
        // That a single-file image writes its own path is the seam's row above, and SelfExecutable.Image's rows in
        // UninstallExecutableTests.
        Assert.False(SelfExecutable.IsSingleFile);
        Assert.Null(descriptor.InstalledImage);
    }
}
