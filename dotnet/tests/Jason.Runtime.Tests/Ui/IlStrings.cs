using System.Reflection;
using System.Reflection.Emit;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// The string literals a compiled type loads, read from its IL: every <c>ldstr</c> of every method and constructor with a
/// body, of the type and of every type nested in it, the ones the compiler wrote for a lambda included. A constant of
/// another type that a body uses is loaded as a literal too, so this sees what the code says, wherever it was spelt.
/// </summary>
internal static class IlStrings
{
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    public static IReadOnlyList<string> Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));
        var literals = methods.SelectMany(Loaded).ToList();
        literals.AddRange(type.GetNestedTypes(Declared).SelectMany(Of));
        return literals;
    }

    private static IEnumerable<string> Loaded(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var at = 0;
        while (at < il.Length)
        {
            var twoBytes = il[at] == 0xFE;
            var code = Codes[twoBytes ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += twoBytes ? 2 : 1;

            if (code.OperandType == OperandType.InlineString)
            {
                yield return method.Module.ResolveString(BitConverter.ToInt32(il, at));
            }

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
}
