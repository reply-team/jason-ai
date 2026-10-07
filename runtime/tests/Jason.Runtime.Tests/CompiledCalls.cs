using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Jason.Runtime.Tests;

/// <summary>What a call names: the namespace and the name of the type it is declared on, its own name, and whether it is an instance's.</summary>
/// <param name="Type">The type's name without its generic arity: <c>Logger</c> for <c>Logger`1</c>.</param>
/// <param name="Member">The method's name, <c>.ctor</c> for a constructor.</param>
internal sealed record CallTarget(string Namespace, string Type, string Member, bool IsInstance);

/// <summary>One call an assembly makes: the file and lines of its statement, or none where the symbols give none, what it names, and the method it is in.</summary>
internal sealed record CompiledCall(string? File, int StartLine, int EndLine, CallTarget Target, string Method);

/// <summary>
/// The calls an assembly's compiled code makes, read from its file alone: its metadata for the IL and for what each
/// call names, its symbols for the file and the line. Nothing it names is loaded, and neither is the assembly itself —
/// the guards that read the runtime this way would otherwise load whatever its code calls into, the plugin host's
/// engine among them, into the process a test that the runtime never loads that engine runs in.
/// </summary>
internal static class CompiledCalls
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>
    /// Every call, constructor call and method handed out as a delegate in the bodies of an assembly's methods, compiler-
    /// made ones included — a lambda, a local function and an async method's state machine are methods of nested types
    /// the compiler wrote — with the file and lines its symbols give the statement each is in. A file a source generator
    /// wrote is left out: no source file holds it.
    /// </summary>
    /// <param name="assemblyFile">The assembly, with its portable symbols beside it.</param>
    /// <param name="declaredIn">Which types' methods are read, by full name, a nested type's after its declaring type's and a <c>/</c>; every type's when null.</param>
    public static List<CompiledCall> In(string assemblyFile, Func<string, bool>? declaredIn = null)
    {
        using var image = new PEReader(File.OpenRead(assemblyFile));
        var metadata = image.GetMetadataReader();
        Assert.True(
            image.TryOpenAssociatedPortablePdb(assemblyFile, path => File.Exists(path) ? File.OpenRead(path) : null, out var provider, out _) && provider is not null,
            $"The symbols of {Path.GetFileName(assemblyFile)} were not found beside it, so its calls cannot be given a file and a line.");

        using (provider)
        {
            var symbols = provider.GetMetadataReader();
            var calls = new List<CompiledCall>();
            foreach (var typeHandle in metadata.TypeDefinitions)
            {
                var typeName = FullName(metadata, typeHandle);
                if (declaredIn is not null && !declaredIn(typeName))
                {
                    continue;
                }

                foreach (var methodHandle in metadata.GetTypeDefinition(typeHandle).GetMethods())
                {
                    var method = metadata.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0)
                    {
                        continue;
                    }

                    var name = $"{typeName}.{metadata.GetString(method.Name)}";
                    var points = symbols.GetMethodDebugInformation(methodHandle).GetSequencePoints().Where(point => !point.IsHidden).ToList();
                    foreach (var (offset, token) in MethodTokens(image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!))
                    {
                        var target = Target(metadata, MetadataTokens.EntityHandle(token));
                        var point = points.LastOrDefault(each => each.Offset <= offset);
                        if (point.Document.IsNil)
                        {
                            calls.Add(new CompiledCall(null, 0, 0, target, name));
                            continue;
                        }

                        var document = symbols.GetString(symbols.GetDocument(point.Document).Name);
                        if (!document.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                        {
                            calls.Add(new CompiledCall(Relative(document), point.StartLine, point.EndLine, target, name));
                        }
                    }
                }
            }

            return calls;
        }
    }

    /// <summary>
    /// The compiled assembly of every production project under <c>runtime/src</c>, beside this test: the one its project
    /// file names, or else the one named after the file. A project whose assembly is not there fails, because what its
    /// code does is what that assembly says.
    /// </summary>
    public static List<string> ProductionAssemblies(string root)
    {
        var assemblies = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "runtime", "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var named = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), @"<AssemblyName>(?<name>[^<]+)</AssemblyName>");
            var name = named.Success ? named.Groups["name"].Value.Trim() : Path.GetFileNameWithoutExtension(file);
            var assembly = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            Assert.True(File.Exists(assembly), $"The production project {Path.GetRelativePath(root, file)} builds {name}, which is not beside this test: reference the project from this test project.");
            assemblies.Add(assembly);
        }

        return assemblies;
    }

    /// <summary>
    /// A document the symbols name, as a guard names a source file. The symbols hold the path the build saw — the
    /// repository's own, or under the root a deterministic build maps it to — so it is read from the tree it is in.
    /// </summary>
    public static string Relative(string document)
    {
        var path = document.Replace('\\', '/');
        foreach (var tree in new[] { "/runtime/src/", "/runtime/tests/" })
        {
            var at = path.LastIndexOf(tree, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                return path[(at + 1)..];
            }
        }

        return path;
    }

    /// <summary>Every method token a body names, with the offset of the instruction that names it.</summary>
    private static IEnumerable<(int Offset, int Token)> MethodTokens(byte[] il)
    {
        var at = 0;
        while (at < il.Length)
        {
            var start = at;
            var twoBytes = il[at] == 0xFE;
            var code = Codes[twoBytes ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += twoBytes ? 2 : 1;

            if (code.OperandType == OperandType.InlineMethod)
            {
                yield return (start, BitConverter.ToInt32(il, at));
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

    /// <summary>What a method token names, read from the metadata: a method of this assembly, a member another declares, or an instantiation of either.</summary>
    private static CallTarget Target(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                var (space, type) = TypeName(metadata, method.GetDeclaringType());
                return new CallTarget(space, type, metadata.GetString(method.Name), (method.Attributes & MethodAttributes.Static) == 0);
            }

            case HandleKind.MemberReference:
            {
                var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                var (space, type) = TypeName(metadata, member.Parent);
                var instance = metadata.GetBlobReader(member.Signature).ReadSignatureHeader().IsInstance;
                return new CallTarget(space, type, metadata.GetString(member.Name), instance);
            }

            case HandleKind.MethodSpecification:
                return Target(metadata, metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);

            default:
                return new CallTarget(string.Empty, string.Empty, string.Empty, false);
        }
    }

    /// <summary>The namespace and the name of a type a member is declared on, a generic instantiation read as its definition and a nested type in its outermost type's namespace.</summary>
    private static (string Namespace, string Name) TypeName(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
                var space = type.ResolutionScope.Kind == HandleKind.TypeReference
                    ? TypeName(metadata, (TypeReferenceHandle)type.ResolutionScope).Namespace
                    : metadata.GetString(type.Namespace);
                return (space, Unarity(metadata.GetString(type.Name)));
            }

            case HandleKind.TypeDefinition:
            {
                var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                var declaring = type.GetDeclaringType();
                var space = declaring.IsNil ? metadata.GetString(type.Namespace) : TypeName(metadata, declaring).Namespace;
                return (space, Unarity(metadata.GetString(type.Name)));
            }

            case HandleKind.TypeSpecification:
            {
                var signature = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                if (signature.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance)
                {
                    signature.ReadSignatureTypeCode();
                    return TypeName(metadata, signature.ReadTypeHandle());
                }

                return (string.Empty, string.Empty);
            }

            default:
                return (string.Empty, string.Empty);
        }
    }

    /// <summary>A type's full name as the metadata spells it, a nested type's after its declaring type's and a <c>/</c>.</summary>
    private static string FullName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{FullName(metadata, declaring)}/{name}";
        }

        var space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    private static string Unarity(string name) => name.Split('`')[0];
}
