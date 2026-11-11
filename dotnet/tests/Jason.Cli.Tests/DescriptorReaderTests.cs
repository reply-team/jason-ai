using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests;

public class DescriptorReaderTests
{
    [Fact]
    public void Missing_descriptor_reads_as_null()
    {
        using var dir = new TempPaths();
        Assert.Null(new DescriptorReader(dir.Paths).Read());
    }

    [Fact]
    public void Descriptor_is_read_back()
    {
        using var dir = new TempPaths();
        var descriptor = new RuntimeDescriptor("v1", "0.1.0-dev", "rt_X", 1, "http://127.0.0.1:5", "tok", DateTimeOffset.UnixEpoch);
        dir.WriteDescriptor(descriptor);

        Assert.Equal(descriptor, new DescriptorReader(dir.Paths).Read());
    }

    [Fact]
    public void A_descriptor_without_an_installed_image_is_read_and_the_image_is_unknown()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.RunDirectory);
        // As a runtime before the field wrote it.
        File.WriteAllText(dir.Paths.DescriptorFile, """{"api_version":"v1","runtime_version":"0.33.0","instance_id":"rt_X","pid":1,"base_url":"http://127.0.0.1:5","token":"tok","started_at":"1970-01-01T00:00:00Z"}""");

        var read = new DescriptorReader(dir.Paths).Read();

        Assert.NotNull(read);
        Assert.Equal("rt_X", read.InstanceId);
        Assert.Null(read.InstalledImage);
    }

    [Fact]
    public void A_descriptor_with_an_installed_image_round_trips()
    {
        using var dir = new TempPaths();
        var descriptor = new RuntimeDescriptor("v1", "0.34.0", "rt_X", 1, "http://127.0.0.1:5", "tok", DateTimeOffset.UnixEpoch, Path.Combine(dir.Paths.Root, "bin", "jason.exe"));
        dir.WriteDescriptor(descriptor);

        Assert.Contains("\"installed_image\":", File.ReadAllText(dir.Paths.DescriptorFile), StringComparison.Ordinal);
        Assert.Equal(descriptor, new DescriptorReader(dir.Paths).Read());
    }

    [Fact]
    public void Corrupt_descriptor_reads_as_null()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.RunDirectory);
        File.WriteAllText(dir.Paths.DescriptorFile, "{ not json");

        Assert.Null(new DescriptorReader(dir.Paths).Read());
    }
}

/// <summary>Isolated data directory for CLI tests, with a helper that plants a descriptor the way the runtime would.</summary>
public sealed class TempPaths : IDisposable
{
    public TempPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Paths = new JasonPaths(root);
    }

    public JasonPaths Paths { get; }

    public void WriteDescriptor(RuntimeDescriptor descriptor)
    {
        Directory.CreateDirectory(Paths.RunDirectory);
        File.WriteAllText(Paths.DescriptorFile, JsonSerializer.Serialize(descriptor, JasonJson.Options));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Paths.Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
