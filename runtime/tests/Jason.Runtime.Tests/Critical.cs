using Xunit.v3;

namespace Jason.Runtime.Tests;

/// <summary>
/// A test of the critical tier: the one test that goes red first when the behaviour its family names breaks.
/// </summary>
/// <remarks>
/// <para>
/// The tier is the fast loop a commit runs before it is pushed:
/// <c>dotnet test --solution runtime/Jason.slnx -- --filter-trait "Category=Critical"</c>, a few minutes on a
/// workstation. It never stands for the whole suite, which runs once before a pull request is marked ready and again on
/// CI. What else a family holds stays in the suite; the tier takes the test that would fail first.
/// </para>
/// <para>
/// On a fact, or on a theory whose every row belongs to the tier, the attribute marks the method. A theory of which only
/// some rows belong marks those rows instead and carries no attribute: an <c>InlineData</c> row takes
/// <c>Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)]</c>, a member's row is marked through
/// <see cref="CriticalRows"/>, and the reason is a comment above the row, since a row takes no argument of its own.
/// </para>
/// <para>
/// This file is linked into every test project, so the tier is one vocabulary across the five assemblies.
/// <c>Category</c> is also the key of the manual live runs' trait; the two values never meet.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CriticalAttribute(Family family, string why) : Attribute, ITraitAttribute
{
    /// <summary>The trait every critical test carries, with <see cref="Value"/>.</summary>
    public const string Trait = "Category";

    /// <summary>The value of <see cref="Trait"/> that selects the tier.</summary>
    public const string Value = "Critical";

    /// <summary>The trait that names the family, so a family's critical tests can be run, or counted, alone.</summary>
    public const string FamilyTrait = "CriticalFamily";

    public Family Family { get; } = family;

    /// <summary>Why this test is the one of its family that goes red first.</summary>
    public string Why { get; } = why;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [KeyValuePair.Create(Trait, Value), KeyValuePair.Create(FamilyTrait, Family.ToString())];
}

/// <summary>
/// Marks the critical rows of a theory fed by a member. A member's rows are shared by every theory that names the
/// member, so a theory with critical rows names a member of its own that marks them, and the member it reads stays
/// unmarked.
/// </summary>
public static class CriticalRows
{
    /// <summary>
    /// Marks every row of <paramref name="rows"/> whose data <paramref name="chosen"/> accepts, where it stands, and
    /// returns the same rows. Pass rows made for this call: a member that returns a new collection each time.
    /// </summary>
    /// <exception cref="InvalidOperationException">No row is accepted: a chosen row has gone from the data. xUnit then
    /// replaces the theory with one failing case, so the whole suite goes red; a run filtered to the tier does not see
    /// it, since that case carries no trait, and the tier's count is held by its guard.</exception>
    public static TRows Mark<TRows>(TRows rows, Family family, Func<object?[], bool> chosen)
        where TRows : IEnumerable<TheoryDataRowBase>
    {
        var marked = 0;
        foreach (var row in rows)
        {
            if (chosen(((ITheoryDataRow)row).GetData()))
            {
                row.WithTrait(CriticalAttribute.Trait, CriticalAttribute.Value)
                    .WithTrait(CriticalAttribute.FamilyTrait, family.ToString());
                marked++;
            }
        }

        return marked > 0
            ? rows
            : throw new InvalidOperationException(
                $"No row of this theory's data is the critical row of {family} it was asked to mark; the row has gone from the data.");
    }
}

/// <summary>
/// The families of the critical tier: each names a behaviour whose breaking must show in the tier, and each has at
/// least one critical test. The UI's golden path is not here: it runs in the browser, under <c>ui/</c>.
/// </summary>
public enum Family
{
    // The boundaries the build enforces.

    /// <summary>The child-process door: every child made by <c>ChildProcess.StartInfo</c>, without a window or a shell, with an environment that is built rather than inherited.</summary>
    B1,

    /// <summary>The references: the CLI, the plugin host, the contracts and the runtime load only what they may, and one executable picks its mode from argv.</summary>
    B2,

    /// <summary>Nothing a person typed reaches a log or a refusal.</summary>
    B3,

    /// <summary>The open set's publication: the public tree is exactly the listed paths, and nothing published points outside it.</summary>
    B4,

    /// <summary>The third-party notices cover every package of the closure at its exact version.</summary>
    B5,

    /// <summary>The UI is one more client of the API: without <c>&lt;data&gt;/ui/</c> the runtime is unchanged, and the UI's fixtures are held one to one.</summary>
    B6,

    /// <summary>The pipeline and the rulesets agree on the checks a merge waits for, and a release is tested before it is packaged.</summary>
    B7,

    /// <summary>The dispatcher reads no meaning from a work item's context.</summary>
    B8,

    /// <summary>The suite never reaches the machine: no real agent host, notification, feed, registration or provider CLI, and no orphaned child.</summary>
    B9,

    // Work execution.

    /// <summary>The transition table is the only way a status moves, and parked work never reaches a dispatcher.</summary>
    W1,

    /// <summary>Attempt fencing: the attempt id is the fence, a write is one guarded UPDATE, one live attempt per item.</summary>
    W2,

    /// <summary>The claim's checks, in their published order, fail closed before a child exists, with no fallback to another plugin or profile.</summary>
    W3,

    /// <summary>
    /// Attempts end through <c>AttemptOutcomes</c>: retried below the limit, final at it, leases and restarts, and a run
    /// that ends waiting, which costs no attempt and is released by the scan, never lost.
    /// </summary>
    W4,

    /// <summary>Never repeated after an ambiguous end: a lost answer costs a recovery read or a person, never a second effect.</summary>
    W5,

    /// <summary>The dispatcher's claim: <c>BEGIN IMMEDIATE</c>, no more items than free slots, one per campaign per scan, one runtime per data directory.</summary>
    W6,

    /// <summary>The launch envelope goes on stdin and nothing on argv; a running attempt keeps the snapshot it was given.</summary>
    W7,

    /// <summary>A caller's idempotency key is the caller's, never derived, and a repeat answers the first.</summary>
    W8,

    /// <summary>The campaign lifecycle, and what archiving cancels.</summary>
    W9,

    /// <summary>Pins: a provider identifier is pinned once per plugin, a different value is recorded beside it, and only a plugin's answer pins.</summary>
    W10,

    /// <summary>Routing: resolved by exact masking over a frozen snapshot; a bad route rejects the whole reload; provenance is never rewritten.</summary>
    W11,

    // Persistence.

    /// <summary>The append-only stores refuse a change three ways, and the runtime's own kinds are reserved.</summary>
    P1,

    /// <summary>Migrations apply at start after the backup, from the oldest database to the head, and a rollback restores only when nothing was written since.</summary>
    P2,

    /// <summary>A finished item's <c>result_json</c> is frozen, on every terminal path.</summary>
    P3,

    // People and safety.

    /// <summary>The do-not-contact register and the channel keys hold at the claim, the pre-flight and the provider.</summary>
    S1,

    /// <summary>The approval gate: nothing that needs a person's word reaches a plugin host without it, and a changed input needs a new word.</summary>
    S2,

    /// <summary>The capability token and every secret are redacted wherever they could land.</summary>
    S3,

    /// <summary>A diagnostic bundle carries shapes, counts and marks, never a value, unless <c>--include-journal</c> says so.</summary>
    S4,

    /// <summary>The loopback API's door: the bearer, the origin and host rules, the descriptor's access, the UI's pairing and its revocation.</summary>
    S5,

    /// <summary>A launched agent session cannot widen its permissions through a profile.</summary>
    S6,

    // Protocols.

    /// <summary>The plugin protocol: the digest the child recomputes, the exit codes, exactly one outcome, the five failure classes, manifests and reloads.</summary>
    R1,

    /// <summary>The CLI's exit codes and JSON, its declared exceptions, and the API's dialect and error envelope.</summary>
    R2,

    /// <summary>The contract documents, the schema dialect's vocabulary, the executed fixtures and <c>context.input</c>.</summary>
    R3,

    /// <summary>The plugin sandbox: no module, shell, state or directory outside the package and the invocation.</summary>
    R4,

    /// <summary>Declaration is not permission: a capability runs only where a grant names it.</summary>
    R5,

    // Install and update.

    /// <summary>Checksums and the release's assets.</summary>
    I1,

    /// <summary>Applying an update, its rollback, and the update check.</summary>
    I2,

    /// <summary>Uninstalling by receipt, skills deployed by receipt, and the belt around purging the data directory.</summary>
    I3,

    /// <summary>The logon registration: idempotent, undone when refused, never another account's.</summary>
    I4,

    // The golden path of each surface, by its steadiest test.

    /// <summary>The runtime and the CLI.</summary>
    E1,

    /// <summary>The executable, the plugin host and the provider's stand-in.</summary>
    E2,

    // Fragile places.

    /// <summary>One LinkedIn key per profile, the same in the runtime, the plugin host, the register, the claim and the migration.</summary>
    F1,

    /// <summary>The register's lift order: the exact text first, the key only where no entry holds the text.</summary>
    F2,

    /// <summary>Invariant mode and the machine's language: a runtime in invariant mode refuses to start, the mode reaches every child, and no test leans on a localized sentence.</summary>
    F3,

    /// <summary>A child's streams: UTF-8, caps that drain, kills that end, grandchildren that hold a pipe.</summary>
    F4,

    /// <summary>Held files: the wait counts from the first refusal and ends on time with its cause kept.</summary>
    F5,

    /// <summary>Program resolution on Windows: npm shims and <c>PATHEXT</c>.</summary>
    F6,

    // Further behaviours the suite holds.

    /// <summary>Configuration: a refused live edit keeps the last good settings, said once; a refused start names the path, never the value.</summary>
    C1,

    /// <summary>The manager loop: its triggers, one open check-in, no loop that feeds itself.</summary>
    M1,

    /// <summary>Inbound sync.</summary>
    N1,

    /// <summary>The events store.</summary>
    N2,

    /// <summary>Notifications as pointers.</summary>
    N3,

    /// <summary>A printed command is a real command: in a page, a skill or a repair.</summary>
    D1,

    /// <summary>A skill's verified row is earned.</summary>
    D2,

    /// <summary>Edges and bounds: caps, cuts through a character, timeouts that are never verdicts, races decided once.</summary>
    X1,

    /// <summary>Containment: no path, link, archive entry or reference leaves its root or its campaign.</summary>
    X2,

    /// <summary>Injection: no value becomes a second terminal line, a script's argument or a provider's template.</summary>
    X3,
}
