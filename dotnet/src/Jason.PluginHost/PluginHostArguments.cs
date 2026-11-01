using System.Globalization;

namespace Jason.PluginHost;

/// <summary>The command line was not one this build accepts.</summary>
public sealed class HostUsageException(string message) : Exception(message);

/// <summary>
/// The four values a plugin-host process is started with, and nothing else. They say what runs without revealing
/// anything: a process listing shows the plugin and the operation, never the input, the binding or a secret.
/// Every one of them is repeated inside the envelope, and the child refuses a disagreement.
/// </summary>
public sealed record PluginHostArguments(int Protocol, string PluginId, string Operation, string CorrelationId)
{
    private const string Usage = "--protocol N --plugin ID --operation OP --correlation ID";

    public static PluginHostArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        int? protocol = null;
        string? pluginId = null;
        string? operation = null;
        string? correlationId = null;

        for (var index = 0; index < args.Count; index += 2)
        {
            var flag = args[index];
            if (index + 1 >= args.Count)
            {
                throw new HostUsageException($"'{flag}' needs a value; expected {Usage}.");
            }

            var value = args[index + 1];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new HostUsageException($"'{flag}' needs a value; expected {Usage}.");
            }

            switch (flag)
            {
                case "--protocol" when protocol is null:
                    protocol = int.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : throw new HostUsageException($"--protocol must be a number; got '{value}'.");
                    break;
                case "--plugin" when pluginId is null:
                    pluginId = value;
                    break;
                case "--operation" when operation is null:
                    operation = value;
                    break;
                case "--correlation" when correlationId is null:
                    correlationId = value;
                    break;
                default:
                    throw new HostUsageException($"'{flag}' is not one of {Usage}, or it was given twice.");
            }
        }

        if (protocol is null || pluginId is null || operation is null || correlationId is null)
        {
            throw new HostUsageException($"all four of {Usage} are required.");
        }

        return new PluginHostArguments(protocol.Value, pluginId, operation, correlationId);
    }
}
