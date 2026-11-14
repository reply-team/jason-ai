using System.Text.Json.Serialization;
using Jason.Contracts.Api;

namespace Jason.Cli.Status;

/// <summary>What one check came to.</summary>
/// <remarks>
/// <see cref="Absent"/> is never a failure. It is the answer for everything optional — a provider that is not
/// configured, a harness with nothing deployed into it — and calling that failed would teach a person that a
/// working installation is broken. <see cref="Unknown"/> is for a check that could not be made at all.
/// </remarks>
public enum CheckState
{
    Ok,
    Failed,
    Absent,
    Unknown,
}

/// <summary>
/// One check: what was asked, whether failing it means this installation cannot work, what was seen, the one
/// command that repairs it, and what was seen again as fields a program reads, where a check has any.
/// </summary>
/// <remarks>
/// A check may report a fact and the command that repairs it. It may not read a log or explain a failure — that
/// is a diagnostics verb, it is a story of its own, and the shape of this record is part of what keeps the two
/// apart. <see cref="Details"/> does not change that: it is the same fact, said as fields, so that a program
/// reading a version does not parse a sentence written for a person, and it carries nothing the fact could not.
/// </remarks>
/// <param name="Details">
/// Fields of the fact, by name, and null on every check that has none; omitted from the body when null. Two checks
/// carry them: <c>role_skills</c>, the deployment in the role root, and <c>send_modes</c>, each mode in force and its
/// source as the runtime's own words.
/// </param>
public sealed record StatusCheck(
    string Name,
    bool Required,
    CheckState State,
    string Fact,
    string? Fix,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Details = null);

/// <summary>
/// The answer to "can I start work?": one state per check, and the boolean an agent branches on.
/// </summary>
/// <param name="Ready">
/// True when every required check passed. It is here because the exit code of this verb is the one thing about
/// it that departs from the published table, and a caller reading a body should never have to know that.
/// </param>
/// <param name="Setup">
/// Where this installation's parts are, composed from this CLI's own data directory, so it is there whether or not a
/// runtime answers. A fact beside the checks rather than one of them: it never decides <see cref="Ready"/>.
/// </param>
public sealed record StatusReport(bool Ready, IReadOnlyList<StatusCheck> Checks, SetupInfo Setup);
