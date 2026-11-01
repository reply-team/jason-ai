namespace Jason.Contracts.Operations;

/// <summary>
/// What a warm-up did, counted in distinct patterns: how many finished warming, and how many it gave up on after
/// timeout upon timeout — whose cold cost is then paid inside a request's timeout and covered by that request's own
/// second match. A warm-up that reported only that it ran would hide the second number.
/// </summary>
public sealed record WarmedPatterns(int Finished, int Unfinished);
