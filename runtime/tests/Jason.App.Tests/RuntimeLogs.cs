namespace Jason.App.Tests;

/// <summary>
/// Reads a runtime log the way a writer still holding it allows. The runtime's log sink keeps its file open for
/// writing and shares it for reading only, so a reader that refuses to share writing with it — as
/// <see cref="File.ReadAllText(string)"/> does — is turned away on Windows for as long as any handle with write
/// access is still open: the runtime's own just after it is stopped, or another process's.
/// </summary>
internal static class RuntimeLogs
{
    public static string ReadAllText(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
