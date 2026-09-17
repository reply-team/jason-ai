using System.Text.Json.Nodes;
using Jason.Runtime.Json;

namespace Jason.Runtime.Routing;

/// <summary>
/// The identity of a binding: <c>sha256:</c> over its canonical JSON — keys sorted recursively, compact, UTF-8 —
/// using the same algorithm a package digest uses, so a person reading an attempt row meets one kind of hash.
/// <para>
/// The reason an attempt records a hash rather than a copy: the same identity across attempts is what makes
/// "was this retried against a different account?" answerable, the resolved value is already in the journal and
/// in <c>route.list</c>, and the attempt row stays small and free of anything a vendor might later call
/// sensitive.
/// </para>
/// <para>
/// The canonical form itself is <see cref="CanonicalJson"/>, which an approval's subject is hashed with too: one
/// writer, so two documents that are the same are never given two different names.
/// </para>
/// </summary>
public static class BindingIdentity
{
    /// <summary>The binding's identity, or null when there is no binding — which is not the identity of an empty one.</summary>
    public static string? Of(JsonObject? binding) => Measure(binding)?.Identity;

    /// <summary>
    /// The canonical form written once, and both facts about it that anybody needs: how many bytes it is, and
    /// what it is called. They are answered together because the caller that bounds a binding is the caller that
    /// records its identity, and serialising the same object twice to learn two things about it is how the two
    /// numbers would eventually come to disagree.
    /// </summary>
    public static BindingMeasure? Measure(JsonObject? binding) =>
        CanonicalJson.Measure(binding) is { } measured ? new BindingMeasure(measured.Bytes, measured.Hash) : null;
}

/// <summary>
/// One binding's canonical form, measured and named in the same pass: the bytes the protocol would have to carry
/// it in, and the <c>sha256:</c> an attempt records it as.
/// </summary>
public readonly record struct BindingMeasure(long Bytes, string Identity);
