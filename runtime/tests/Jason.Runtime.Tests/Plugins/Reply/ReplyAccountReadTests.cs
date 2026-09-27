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

    [Fact]
    public async Task A_file_caught_between_its_truncation_and_its_last_byte_is_read_again()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var account = new ReplyAccount().WithEnrollment(sequence: 7, contact: 42);
        var file = Path.Combine(account.Root, ReplyAccount.EnrollmentsFile);
        var whole = await File.ReadAllBytesAsync(file, cancel);

        // The stand-in's rewrite, stopped after its truncation: the file is empty and held for writing until the
        // rest arrives a moment later.
        var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        var finishing = Task.Run(
            async () =>
            {
                await Task.Delay(200, cancel);
                await writer.WriteAsync(whole, cancel);
                await writer.DisposeAsync();
            },
            cancel);

        try
        {
            Assert.Equal([42], account.EnrolledIn(7));
        }
        finally
        {
            await finishing;
        }
    }
}
