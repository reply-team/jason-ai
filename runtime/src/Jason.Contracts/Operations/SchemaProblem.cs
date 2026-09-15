namespace Jason.Contracts.Operations;

/// <summary>
/// One thing wrong with one value: where it is, which rule refused it, and what a person should read. The
/// <c>Pointer</c> is a JSON pointer — the empty string for the whole document — and the <c>Reason</c> is one of the
/// dialect's fixed codes, because both travel outwards: the pointer becomes the field of an API error and the
/// reason becomes its code, so a plugin author reads the same words the validator used.
/// </summary>
public sealed record SchemaProblem(string Pointer, string Reason, string Message);
