using System.Collections.Frozen;

namespace Jason.Contracts.Plugins;

/// <summary>
/// Every failure code a package answers without the provider having judged the work: the plugin host's own endings
/// (<see cref="HostOutcomeCodes"/>), which say what became of the plugin, and the codes reserved beside every operation,
/// which say that the work never reached the provider's judgment: refused by the package before any call, or answered
/// by a provider that does not offer what was asked yet. A provider block is cleared only by the provider's judgment of
/// the work, so none of these clears one, whatever its class.
/// </summary>
/// <remarks>
/// A reserved code is declared by no operation document and passes beside every one of them, but only under the class
/// it is reserved with (<see cref="Reserves"/>), as a host code passes only under the class the host writes it with.
/// The outcome check, the fixtures and the published documents all read <see cref="Reserved"/>, so a code added here
/// is admitted, shown and kept out of every document at once. <c>docs/contracts/README.md</c> states each one.
/// </remarks>
public static class UnjudgedOutcomeCodes
{
    /// <summary>
    /// A package that reads the provider's object (the <c>provider</c> member of an operation's arguments) and cannot
    /// make sense of it says so under this code, class <see cref="FailureClass.Validation"/>, before any call.
    /// </summary>
    public const string ProviderInputInvalid = "provider_input_invalid";

    /// <summary>
    /// A package whose provider does not offer, yet, what it was asked for — an operation whose route the provider's API
    /// does not have, or an argument it does not take — says so under this code, class <see cref="FailureClass.Permanent"/>:
    /// asking again with the same arguments meets the same provider. No operation's list can say in advance which of its
    /// parts a provider has not shipped, so the code is reserved rather than declared. It is not the host's
    /// <c>capability_not_granted</c>, where the operator granted the plugin less than it asked for.
    /// </summary>
    public const string CapabilityNotOffered = "capability_not_offered";

    /// <summary>The codes reserved beside every operation, declared by none, each with the one class it passes under.</summary>
    public static IReadOnlyDictionary<string, FailureClass> Reserved { get; } = new Dictionary<string, FailureClass>(StringComparer.Ordinal)
    {
        [ProviderInputInvalid] = FailureClass.Validation,
        [CapabilityNotOffered] = FailureClass.Permanent,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The host's endings and the reserved codes together.</summary>
    public static IReadOnlySet<string> All { get; } = HostOutcomeCodes.All.Concat(Reserved.Keys).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="code"/> under <paramref name="failureClass"/> is a reserved code under the class it is
    /// reserved with. Under another class it is the plugin's word for something no contract admits.
    /// </summary>
    public static bool Reserves(FailureClass failureClass, string code) =>
        Reserved.TryGetValue(code, out var reserved) && reserved == failureClass;
}
