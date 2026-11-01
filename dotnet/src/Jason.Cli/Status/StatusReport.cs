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
/// One check: what was asked, whether failing it means this installation cannot work, what was seen, and the
/// one command that repairs it.
/// </summary>
/// <remarks>
/// There is no fifth field on purpose. A check may report a fact and the command that repairs it. It may not
/// read a log or explain a failure — that is a diagnostics verb, it is a story of its own, and the shape of
/// this record is part of what keeps the two apart.
/// </remarks>
public sealed record StatusCheck(string Name, bool Required, CheckState State, string Fact, string? Fix);

/// <summary>
/// The answer to "can I start work?": one state per check, and the boolean an agent branches on.
/// </summary>
/// <param name="Ready">
/// True when every required check passed. It is here because the exit code of this verb is the one thing about
/// it that departs from the published table, and a caller reading a body should never have to know that.
/// </param>
public sealed record StatusReport(bool Ready, IReadOnlyList<StatusCheck> Checks);
