using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Jason.PluginHost.Sdk;
using Jason.Runtime.Tests;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// How a timed-out program is ended can be stood in for, because a kill that never comes back cannot be made any other
/// way — and only a test can stand in for it. The seam is internal, the plugin host lets only test assemblies see its
/// internals, and nothing in the plugin host sets it from what a process is given: the runner hands its own on to the
/// exec it builds, and nothing sets the runner's, so the shipped executable's plugin-host mode always kills for real,
/// whatever its arguments, its envelope or its environment say.
/// </summary>
/// <remarks>
/// Read from the plugin host's compiled code: every call of either seam's setter, and every store into either seam's
/// backing field, which is where a property initializer puts its value — inside the constructor, with no setter called.
/// Beyond what it reads: reflection, and code outside the plugin host, which cannot see the seam at all.
/// </remarks>
public sealed class KillSeamTests
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private const int FieldDefinitionTable = 0x04;

    private const int MethodDefinitionTable = 0x06;

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);
    [Fact]
    [Critical(Family.B1, "the test seam is unreachable in production")]
    public void The_kill_seam_is_set_by_nothing_the_executable_ships()
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var seam in new[] { typeof(ExecService).GetProperty("Kill", Any)!, typeof(InvocationRunner).GetProperty("Kill", Any)! })
        {
            Assert.True(
                seam.GetMethod!.IsAssembly && seam.SetMethod!.IsAssembly,
                $"{seam.DeclaringType!.Name}.Kill is reachable beyond its assembly: get {Access(seam.GetMethod)}, init {Access(seam.SetMethod!)}.");
        }

        // The assemblies that may see it are test scaffolding, and none of them is a project the executable is built from.
        var root = RepositoryRoot();
        var friends = typeof(InvocationRunner).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(friend => friend.AssemblyName)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["Jason.PluginHost.Tests", "Jason.StuckKillPluginHost"], friends);
        foreach (var friend in friends)
        {
            Assert.True(
                Directory.Exists(Path.Combine(root, "dotnet", "tests", friend)) && !Directory.Exists(Path.Combine(root, "dotnet", "src", friend)),
                $"{friend} sees the plugin host's internals and is not a test project under dotnet/tests.");
        }

        // And inside the plugin host: the exec's seam is set only where the runner hands its own on, the runner's nowhere.
        var setters = CompiledCalls.In(typeof(InvocationRunner).Assembly.Location)
            .Where(call => call.Target is { Member: "set_Kill", Type: "ExecService" or "InvocationRunner" })
            .Select(call => $"{call.Target.Type}.set_Kill in {call.Method}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            setters is ["ExecService.set_Kill in Jason.PluginHost.InvocationRunner.Run"],
            $"Expected the kill seam to be set only by InvocationRunner.Run handing on its own; it is set by: {string.Join("; ", setters)}");

        // Nor by an initializer: the only stores into the seams' backing fields are their own setters, and the exec's
        // constructor giving it the real kill. The runner's starts empty.
        var stores = Stores([BackingField(typeof(ExecService)), BackingField(typeof(InvocationRunner))]);
        Assert.True(
            stores is ["ExecService.Kill in ExecService..ctor from ExecService.TryKill", "ExecService.Kill in ExecService.set_Kill", "InvocationRunner.Kill in InvocationRunner.set_Kill"],
            $"Expected the seams' fields stored only by their setters and the exec's constructor handing it TryKill; they are stored by: {string.Join("; ", stores)}");
    }

    private static FieldInfo BackingField(Type type)
    {
        var field = type.GetField("<Kill>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null, $"{type.Name}.Kill is no longer an automatic property, so its stores are not where this test reads them.");
        return field;
    }

    /// <summary>
    /// Every store into the given fields anywhere in the plugin host's compiled code, compiler-made methods and constructors
    /// included: the field, the method it is in, and the method the nearest delegate made before the store points at.
    /// </summary>
    private static List<string> Stores(IReadOnlyList<FieldInfo> seams)
    {
        var stores = new List<string>();
        foreach (var type in typeof(InvocationRunner).Assembly.GetTypes())
        {
            IEnumerable<MethodBase> methods = [.. type.GetMethods(Declared), .. type.GetConstructors(Declared)];
            foreach (var method in methods)
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is null)
                {
                    continue;
                }

                string? delegated = null;
                foreach (var (code, token) in Instructions(il))
                {
                    if (code == OpCodes.Ldftn && token >>> 24 == MethodDefinitionTable)
                    {
                        var target = method.Module.ResolveMethod(token)!;
                        delegated = $"{target.DeclaringType!.Name}.{target.Name}";
                    }
                    else if ((code == OpCodes.Stfld || code == OpCodes.Stsfld) && token >>> 24 == FieldDefinitionTable)
                    {
                        var field = method.Module.ResolveField(token)!;
                        var seam = seams.FirstOrDefault(each => each.Module == field.Module && each.MetadataToken == field.MetadataToken);
                        if (seam is not null)
                        {
                            stores.Add($"{seam.DeclaringType!.Name}.Kill in {method.DeclaringType!.Name}.{method.Name}{(delegated is null ? string.Empty : " from " + delegated)}");
                        }
                    }
                }
            }
        }

        return [.. stores.Order(StringComparer.Ordinal)];
    }

    /// <summary>Each instruction of a method body with its four-byte token, for the instructions that carry one.</summary>
    private static IEnumerable<(OpCode Code, int Token)> Instructions(byte[] il)
    {
        var at = 0;
        while (at < il.Length)
        {
            var twoBytes = il[at] == 0xFE;
            var code = Codes[twoBytes ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += twoBytes ? 2 : 1;
            var token = code.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType
                ? BitConverter.ToInt32(il, at)
                : 0;
            yield return (code, token);

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };
        }
    }

    private static string Access(MethodInfo accessor) =>
        accessor.IsPublic ? "public" : accessor.IsFamilyOrAssembly ? "protected internal" : accessor.IsFamily ? "protected" : accessor.IsAssembly ? "internal" : "private";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
