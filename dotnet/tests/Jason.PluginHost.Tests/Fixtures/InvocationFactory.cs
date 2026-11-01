using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>Everything a test wants to change about an envelope, with a default that is already valid.</summary>
public sealed class InvocationBuilder
{
    public int ProtocolVersion { get; set; } = PluginProtocol.CurrentVersion;

    public string InvocationId { get; set; } = PublicId.New(PluginProtocol.InvocationIdPrefix);

    public string CorrelationId { get; set; } = "test";

    public string PluginId { get; set; } = "fake-provider";

    public string Version { get; set; } = "1.0.0";

    public PluginKind Kind { get; set; } = PluginKind.Provider;

    /// <summary>Null means the child runs unverified code and says so; a test sets it to provoke a mismatch.</summary>
    public string? Digest { get; set; }

    public PluginEntry Entry { get; set; } = new("main.js", "invoke");

    public int OperationContractVersion { get; set; } = PluginProtocol.OperationContractVersion;

    public InvocationContext Context { get; set; } = new(null, null, null, null, null, JasonVersion.Current);

    public InvocationGrants Grants { get; set; } = new(null, null, null);

    public InvocationLimits Limits { get; set; } = InvocationFactory.DefaultLimits;
}

/// <summary>A valid invocation envelope for a package on disk, and the smallest package to point it at.</summary>
public static class InvocationFactory
{
    public static InvocationLimits DefaultLimits => new(
        TimeoutMs: 10_000,
        MemoryBytes: 64L * 1024 * 1024,
        MaxStatements: 10_000_000,
        MaxRecursion: 64,
        new ExecLimits(4_194_304, 64),
        new HttpLimits(4_194_304, 1_048_576, 64, 5_000),
        new LogLimits(16_384, 4_194_304));

    public static PluginInvocation Create(string root, string operation, JsonObject? input = null, Action<InvocationBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var builder = new InvocationBuilder { Digest = PackageDigest.Compute(root).Digest };
        configure?.Invoke(builder);

        return new PluginInvocation(
            builder.ProtocolVersion,
            builder.InvocationId,
            builder.CorrelationId,
            new InvocationPlugin(builder.PluginId, builder.Version, builder.Kind, root, builder.Digest, builder.Entry),
            operation,
            builder.OperationContractVersion,
            input ?? [],
            builder.Context,
            builder.Grants,
            builder.Limits);
    }

    /// <summary>Writes a package that is enough to run: a manifest placeholder, an entry module and its modules.</summary>
    public static string WritePackage(string dir, string mainJs, IReadOnlyDictionary<string, string>? modules = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);
        Directory.CreateDirectory(dir);
        var encoding = new UTF8Encoding(false);

        // The child never reads a manifest — it is handed the resolved facts — but the file belongs to the
        // package, so it belongs to the digest.
        File.WriteAllText(Path.Combine(dir, "plugin.yaml"), "manifest_version: 1\nid: fake-provider\n", encoding);
        File.WriteAllText(Path.Combine(dir, "main.js"), mainJs, encoding);

        foreach (var (relativePath, source) in modules ?? new Dictionary<string, string>())
        {
            var file = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, source, encoding);
        }

        return dir;
    }
}
