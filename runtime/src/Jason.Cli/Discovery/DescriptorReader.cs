using System.Text.Json;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Discovery;

/// <summary>Reads the runtime's endpoint descriptor. Missing or unreadable both mean "no runtime to talk to".</summary>
public sealed class DescriptorReader(JasonPaths paths)
{
    public RuntimeDescriptor? Read() => Look().Descriptor;

    /// <summary>
    /// The descriptor, or none; and, where there is none, whether a file is there that this account may not read. That is
    /// still no runtime this account can talk to, and is told apart so that the answer does not send a person to start a
    /// runtime that may be running.
    /// </summary>
    public DescriptorLook Look()
    {
        if (!File.Exists(paths.DescriptorFile))
        {
            return DescriptorLook.None;
        }

        try
        {
            return new DescriptorLook(JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllBytes(paths.DescriptorFile), JasonJson.Options), Denied: false);
        }
        catch (JsonException)
        {
            return DescriptorLook.None;
        }
        catch (IOException)
        {
            return DescriptorLook.None;
        }
        catch (UnauthorizedAccessException)
        {
            // There, and closed to this account: no runtime this account can talk to.
            return new DescriptorLook(null, Denied: true);
        }
    }

    /// <summary>What a command says when there is no descriptor it can read, for each of the two reasons it tells apart.</summary>
    public string NoRuntime(DescriptorLook look)
    {
        ArgumentNullException.ThrowIfNull(look);

        return look.Denied
            ? $"A descriptor at '{paths.DescriptorFile}' exists but could not be read by this account."
            : $"No runtime endpoint descriptor at '{paths.DescriptorFile}'. Is the runtime running?";
    }
}

/// <summary>What reading the endpoint descriptor found.</summary>
/// <param name="Descriptor">The descriptor, or null where there is none this account can read.</param>
/// <param name="Denied">Whether a file is there that this account may not read.</param>
public sealed record DescriptorLook(RuntimeDescriptor? Descriptor, bool Denied)
{
    /// <summary>No descriptor: none there, or one that cannot be read as one.</summary>
    public static DescriptorLook None { get; } = new(null, Denied: false);
}
