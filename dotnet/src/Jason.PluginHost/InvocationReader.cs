using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost;

/// <summary>The invocation was refused before any of the plugin's code ran.</summary>
public sealed class InvocationRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Why an invocation was refused, in one vocabulary the runtime classifies and the guide documents.</summary>
public static class RejectionCodes
{
    public const string EnvelopeUnreadable = "envelope_unreadable";
    public const string EnvelopeInvalid = "envelope_invalid";
    public const string ArgvMismatch = "argv_mismatch";
    public const string ProtocolUnsupported = "protocol_unsupported";
    public const string PackageRootMissing = "package_root_missing";
    public const string PackageUnreadable = "package_unreadable";
    public const string DigestMismatch = "digest_mismatch";
    public const string EntryModuleMissing = "entry_module_missing";
}

/// <summary>
/// Reads the one invocation from stdin and refuses everything that is not exactly it. The digest check is the
/// point of this type: the child recomputes the identity of the package on disk and will not run code the
/// runtime did not validate. A hand-written envelope may omit the digest — that is the development path — and
/// the host says on stderr that it ran unverified code.
/// </summary>
public static class InvocationReader
{
    public static async Task<PluginInvocation> ReadAsync(
        TextReader stdin,
        PluginHostArguments args,
        HostDiagnostics diagnostics,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string raw;
        try
        {
            raw = await stdin.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new InvocationRejectedException(RejectionCodes.EnvelopeUnreadable, $"stdin could not be read: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvocationRejectedException(RejectionCodes.EnvelopeUnreadable, "stdin carried no invocation envelope.");
        }

        PluginInvocation? invocation;
        try
        {
            invocation = JsonSerializer.Deserialize<PluginInvocation>(raw, JasonJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvocationRejectedException(RejectionCodes.EnvelopeInvalid, $"the invocation envelope is not valid JSON: {ex.Message}");
        }

        if (invocation is null || invocation.Plugin is null || invocation.Limits is null || invocation.Context is null || invocation.Grants is null)
        {
            throw new InvocationRejectedException(RejectionCodes.EnvelopeInvalid, "the invocation envelope is missing parts every invocation has.");
        }

        if (invocation.ProtocolVersion != PluginProtocol.CurrentVersion)
        {
            throw new InvocationRejectedException(
                RejectionCodes.ProtocolUnsupported,
                $"the envelope speaks protocol {invocation.ProtocolVersion}; this host speaks {PluginProtocol.CurrentVersion}.");
        }

        if (!string.Equals(invocation.Plugin.Id, args.PluginId, StringComparison.Ordinal)
            || !string.Equals(invocation.Operation, args.Operation, StringComparison.Ordinal)
            || !string.Equals(invocation.CorrelationId, args.CorrelationId, StringComparison.Ordinal)
            || invocation.ProtocolVersion != args.Protocol)
        {
            throw new InvocationRejectedException(
                RejectionCodes.ArgvMismatch,
                "the command line and the envelope disagree about what is being invoked.");
        }

        var root = invocation.Plugin.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new InvocationRejectedException(RejectionCodes.PackageRootMissing, "the package directory named by the envelope does not exist.");
        }

        VerifyDigest(invocation, diagnostics, root);

        var entry = invocation.Plugin.Entry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Module) || !File.Exists(Path.Combine(root, entry.Module)))
        {
            throw new InvocationRejectedException(RejectionCodes.EntryModuleMissing, "the entry module named by the envelope is not in the package.");
        }

        return invocation;
    }

    private static void VerifyDigest(PluginInvocation invocation, HostDiagnostics diagnostics, string root)
    {
        if (invocation.Plugin.Digest is null)
        {
            diagnostics.Host(
                "warn",
                "digest_unverified",
                new JsonObject { ["detail"] = "the envelope carries no digest, so the package was not verified before it ran" });
            return;
        }

        string digest;
        try
        {
            digest = PackageDigest.Compute(root).Digest;
        }
        catch (Exception ex) when (ex is PackageTooLargeException or IOException or UnauthorizedAccessException)
        {
            throw new InvocationRejectedException(RejectionCodes.PackageUnreadable, $"the package could not be hashed: {ex.Message}");
        }

        if (!string.Equals(digest, invocation.Plugin.Digest, StringComparison.Ordinal))
        {
            throw new InvocationRejectedException(
                RejectionCodes.DigestMismatch,
                "the package on disk is not the one the runtime validated.");
        }
    }
}
