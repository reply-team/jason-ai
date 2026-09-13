namespace Jason.Cli;

/// <summary>0 success · 1 the API answered with a business error (stdout carries it) · 2 usage · 3 runtime unreachable or unauthorized.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int ApiError = 1;
    public const int Usage = 2;
    public const int RuntimeUnavailable = 3;
}
