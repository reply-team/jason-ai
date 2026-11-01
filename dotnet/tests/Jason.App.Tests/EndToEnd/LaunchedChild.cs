using System.Text.Json.Nodes;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// Which child an attempt's launch record lets a test end by its number, and by what moment it is proven to be
/// that child. A launch record is written after the child ran; the moment the attempt was claimed is taken before
/// the child exists, and the child can begin any time after it, so the claim proves nothing about when a process
/// began.
/// </summary>
internal static class LaunchedChild
{
    /// <summary>
    /// The pid the attempt's launch names, with the moment to record for it, or null when there is nothing to end.
    /// A launch with an exit code names a child that has exited, and whatever has that number now is somebody
    /// else's. Without one the child may still be running: it began before its number could be read, so the
    /// moment it was read is a moment it had begun by.
    /// </summary>
    public static (int Pid, DateTimeOffset Recorded)? Of(JsonNode? attempt, DateTimeOffset readAt)
    {
        var launch = attempt?["launch"];
        if ((int?)launch?["pid"] is not { } pid || (int?)launch["exit_code"] is not null)
        {
            return null;
        }

        return (pid, readAt);
    }
}
