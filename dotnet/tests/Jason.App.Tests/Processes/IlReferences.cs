using System.Reflection;
using System.Reflection.Emit;

namespace Jason.App.Tests.Processes;

/// <summary>
/// Which methods a compiled method calls, read from its IL. A source scan can be spelled around — a target-typed
/// <c>new()</c>, a helper in another file, a string split in two — and the compiler cannot: every call, callvirt
/// and newobj names its target by a metadata token, and this resolves each one.
/// </summary>
internal static class IlReferences
{
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>
    /// Every method and constructor with a body in an assembly, compiler-made ones included: a lambda, a local
    /// function and an async method's state machine are methods of nested types the compiler wrote, and
    /// <see cref="Assembly.GetTypes"/> lists nested types already.
    /// </summary>
    public static IEnumerable<MethodBase> Methods(Assembly assembly) =>
        assembly.GetTypes().SelectMany(Declaring);

    /// <summary>Every method and constructor with a body in one type and in every type nested inside it.</summary>
    public static IEnumerable<MethodBase> Methods(Type type) =>
        Declaring(type).Concat(type.GetNestedTypes(Declared).SelectMany(Methods));

    /// <summary>Every method a body names through a method token. A token that does not resolve throws: never skipped.</summary>
    public static IReadOnlyList<MethodBase> Called(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return [];
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } generic ? generic.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var called = new List<MethodBase>();
        var at = 0;
        while (at < il.Length)
        {
            var twoBytes = il[at] == 0xFE;
            var code = Codes[twoBytes ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += twoBytes ? 2 : 1;

            if (code.OperandType == OperandType.InlineMethod)
            {
                called.Add(method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!);
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

        return called;
    }

    private static IEnumerable<MethodBase> Declaring(Type type) =>
        type.GetMethods(Declared).Cast<MethodBase>()
            .Concat(type.GetConstructors(Declared))
            .Where(method => method.GetMethodBody() is not null);
}
