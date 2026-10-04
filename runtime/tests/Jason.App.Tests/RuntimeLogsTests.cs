namespace Jason.App.Tests;

public class RuntimeLogsTests
{
    /// <summary>
    /// The log is held exactly as the runtime's sink holds it — open for writing, shared for reading only — and is
    /// still read. A reader that does not share writing would be refused here on Windows with "being used by another
    /// process", which is how a test reading the log of a runtime it had just stopped once failed.
    /// </summary>
    [Fact]
    public void A_log_a_writer_still_holds_is_read_rather_than_refused()
    {
        var file = Path.Combine(Path.GetTempPath(), "jason-log-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            using (var held = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(held))
            {
                writer.WriteLine("{\"@mt\":\"Update check is disabled by configuration\"}");
                writer.Flush();

                Assert.Contains("Update check is disabled by configuration", RuntimeLogs.ReadAllText(file), StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
