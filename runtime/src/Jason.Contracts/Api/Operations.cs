namespace Jason.Contracts.Api;

/// <summary>
/// Operation names shared by the runtime, the CLI and the skills. One vocabulary at three levels:
/// canonical operation <c>system.info</c> ↔ <c>POST /v1/system.info</c> ↔ <c>jason system info</c>.
/// </summary>
public static class Operations
{
    public const string SystemInfo = "system.info";

    public static string Route(string operation) => $"/{ApiVersion.Current}/{operation}";
}
