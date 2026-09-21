namespace Jason.Cli;

/// <summary>
/// 0 success · 1 understood and refused · 2 usage · 3 runtime unreachable or unauthorized.
/// </summary>
/// <remarks>
/// <para>
/// <b>1 is "understood and refused"</b>, which is the API answering with a business error — stdout carries it —
/// or a local act this CLI refused to perform. The second half is not a widening for its own sake: a verb that
/// declines to write a deployment the runtime would refuse has understood the command perfectly and refused
/// it, which is neither a usage error nor an unreachable runtime.
/// </para>
/// <para>
/// <b><c>jason status</c> never answers 3.</b> It exits 0 when every required check passed and 1 when one did
/// not, because "I could not ask the runtime" is the answer that verb exists to give rather than a reason it
/// has none. It is the one declared exception, it is published in <c>jason status --help</c> beside the list of
/// which checks are required, and its body carries <c>ready</c> so that a caller reading the answer never has
/// to know about any of this.
/// </para>
/// </remarks>
public static class ExitCodes
{
    public const int Success = 0;
    public const int ApiError = 1;
    public const int Usage = 2;
    public const int RuntimeUnavailable = 3;
}
