namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The account is read back by a test while the stand-in CLI, another process, may be rewriting the same file. The
/// stand-in rewrites a file whole — truncate, then write — and holds it open for writing while it does.
/// </summary>
/// <remarks>
/// Windows CI met the first half: a golden-path test polling who is enrolled read the file while the stand-in was
/// writing it, and a plain read, which refuses a file another process holds open for writing, threw out of the
/// wait. The race is staged here by holding the file exactly as the stand-in's write holds it.
/// </remarks>
public class ReplyAccountReadTests
{
    [Fact]
    public void The_account_is_read_while_the_stand_in_holds_a_file_open_to_write_it()
    {
        using var account = new ReplyAccount().WithEnrollment(sequence: 7, contact: 42);
        var file = Path.Combine(account.Root, ReplyAccount.EnrollmentsFile);

        // What File.WriteAllText holds while it writes: write access, others may only read.
        using (new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            Assert.Equal([42], account.EnrolledIn(7));
        }
    }

    /// <summary>
    /// The read that lands between the rewrite's truncation and its last byte finds an empty file, and reads again
    /// until the whole of it has arrived.
    /// </summary>
    /// <remarks>
    /// The rest of the rewrite is written from a thread of its own. The read waits by blocking, as reads elsewhere
    /// in the suite do, and a writer that needed the thread pool to finish could be starved of it for longer than
    /// the read waits: on a loaded Windows runner it was, and the read gave up on a file that was still empty. In
    /// the golden path the writer is another process, which no pool in this one can starve.
    /// </remarks>
    [Fact]
    public void A_file_caught_between_its_truncation_and_its_last_byte_is_read_again()
    {
        using var account = new ReplyAccount().WithEnrollment(sequence: 7, contact: 42);
        var file = Path.Combine(account.Root, ReplyAccount.EnrollmentsFile);
        var whole = File.ReadAllBytes(file);

        // The stand-in's rewrite, stopped after its truncation: the file is empty and held for writing until the
        // rest arrives a moment later.
        var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        Exception? failed = null;
        var finishing = new Thread(() =>
        {
            try
            {
                using (writer)
                {
                    Thread.Sleep(200);
                    writer.Write(whole);
                }
            }
            catch (Exception exception)
            {
                // Kept for the test to report: thrown here, it would end the test run rather than this test.
                failed = exception;
            }
        });
        finishing.Start();

        try
        {
            Assert.Equal([42], account.EnrolledIn(7));
        }
        finally
        {
            finishing.Join();
        }

        Assert.Null(failed);
    }
}
