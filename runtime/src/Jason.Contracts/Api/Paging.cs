namespace Jason.Contracts.Api;

/// <summary>
/// One page of a listing. <see cref="NextCursor"/> is opaque: pass it back verbatim as <c>cursor</c> to continue,
/// and a null means the listing is exhausted.
/// </summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
