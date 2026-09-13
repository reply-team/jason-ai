using System.Text.Json;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Discovery;

/// <summary>Reads the runtime's endpoint descriptor. Missing or unreadable both mean "no runtime to talk to".</summary>
public sealed class DescriptorReader(JasonPaths paths)
{
    public RuntimeDescriptor? Read()
    {
        if (!File.Exists(paths.DescriptorFile))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllBytes(paths.DescriptorFile), JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
