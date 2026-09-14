using System.Reflection;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The boundaries that make the isolation claim true rather than intended. Plugin JavaScript runs in a separate
/// process because the runtime cannot run it at all: the engine is not in this assembly's world, and the host
/// that has it knows nothing of databases, web servers or the runtime's own code.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void The_runtime_has_no_way_to_run_a_plugin_in_its_own_process()
    {
        var referenced = References(typeof(PluginInvoker).Assembly);

        Assert.DoesNotContain("Jint", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("Acornima", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("Jason.PluginHost", referenced, StringComparer.Ordinal);
    }

    [Fact]
    public void The_contracts_carry_the_protocol_and_no_dependency_of_either_side()
    {
        var referenced = References(typeof(PluginInvocation).Assembly);

        Assert.DoesNotContain("YamlDotNet", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("Jint", referenced, StringComparer.Ordinal);
    }

    [Fact]
    public void The_plugin_host_knows_nothing_of_the_runtime_it_is_started_by()
    {
        var host = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Jason.PluginHost.dll"));
        var referenced = References(host);

        Assert.DoesNotContain("Jason.Runtime", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("YamlDotNet", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void The_manifest_reader_is_the_one_place_the_yaml_dependency_is_allowed()
    {
        // Stated as a fact rather than a prohibition: a manifest is written by people, and exactly one assembly
        // is allowed to know that. The two tests above are what keeps it from spreading.
        Assert.Contains("YamlDotNet", References(typeof(PluginInvoker).Assembly), StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];
}
