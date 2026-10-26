using Jason.Runtime.Tests.Documentation;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The runtime pulls its settings (<c>IOptionsMonitor</c> each tick, the reload's own read) and never registers a
/// listener on a change, because the settings' lock is held while the sections are told. A listener runs inside that
/// lock, on whichever thread applies the edit. One that read the settings file on that thread would enter the lock again,
/// since the thread already holds it, and return with the sections not yet told of what it read — the very loss the lock
/// is there to close; one that waited for another thread to read it, or for anything such a read waits behind, would
/// never return. The settings file's provider registers its own watch, and nothing else does.
/// </summary>
/// <remarks>
/// Read from every production assembly, compiled, compiler-made methods included, so a listener registered in a lambda,
/// a local function or an async method is found where its call is: <c>IOptionsMonitor&lt;T&gt;.OnChange</c>, the
/// <c>OnChange</c> extension that takes an action of the options alone, <c>OptionsMonitor&lt;T&gt;.OnChange</c>,
/// <c>ChangeToken.OnChange</c> and <c>IChangeToken.RegisterChangeCallback</c>, which a listener put straight on the
/// configuration's change token, or a section's, calls.
/// </remarks>
public sealed class SettingsListenerGuardTests
{
    private const string SettingsFileProvider = "Jason.Runtime.Configuration.JasonConfiguration/SettingsFileProvider";

    [Fact]
    public void No_method_outside_the_settings_file_provider_registers_a_listener_on_a_change()
    {
        var listeners = CompiledCalls.ProductionAssemblies(Publication.RepositoryRoot())
            .SelectMany(assembly => CompiledCalls.In(assembly))
            .Where(call => RegistersAListener(call.Target))
            .ToList();

        // The provider's own watch is found, so the guard is reading the calls it is meant to read.
        Assert.Contains(listeners, call => InTheProvider(call.Method) && call.Target is { Type: "ChangeToken", Member: "OnChange" });

        var elsewhere = listeners
            .Where(call => !InTheProvider(call.Method))
            .Select(call => $"{call.Target.Type}.{call.Target.Member} in {call.Method} ({call.File}:{call.StartLine})")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            elsewhere.Count == 0,
            $"A listener on a change runs while the settings' lock is held, so the runtime registers none and reads its settings when it needs them; registered by: {string.Join("; ", elsewhere)}");
    }

    private static bool RegistersAListener(CallTarget target) => target is
        { Namespace: "Microsoft.Extensions.Options", Type: "IOptionsMonitor" or "OptionsMonitorExtensions" or "OptionsMonitor", Member: "OnChange" }
        or { Namespace: "Microsoft.Extensions.Primitives", Type: "ChangeToken", Member: "OnChange" }
        or { Namespace: "Microsoft.Extensions.Primitives", Type: "IChangeToken", Member: "RegisterChangeCallback" };

    /// <summary>A method of the provider's own, or of a type the compiler wrote inside it.</summary>
    private static bool InTheProvider(string method) =>
        method.Length > SettingsFileProvider.Length
        && method.StartsWith(SettingsFileProvider, StringComparison.Ordinal)
        && method[SettingsFileProvider.Length] is '.' or '/';
}
