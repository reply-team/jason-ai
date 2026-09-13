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
